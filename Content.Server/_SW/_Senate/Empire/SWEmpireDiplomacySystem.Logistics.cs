using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Empire;

public sealed partial class SWEmpireDiplomacySystem
{
    [Dependency] private readonly SWRepublicEconomySystem _cargoRepublic = default!;
    [Dependency] private readonly SWEmpireEconomySystem _cargoEmpire = default!;
    [Dependency] private readonly SWSenateBudgetSystem _cargoTreasury = default!;
    private const double CargoEpsilon = 0.000001;
    private const double MaxCargoAmount = 100;
    private const double BaseCapacity = 100;
    private const int MaxCargo = 128;
    private readonly List<CargoLedger> _cargo = new();
    private readonly Dictionary<(string World, string Good), double> _baseStock = new();
    private int _cargoGeneration, _cargoSequence;

    private sealed class CargoLedger
    {
        public string Id = string.Empty, RequestId = string.Empty, Good = string.Empty, Source = string.Empty, Destination = string.Empty;
        public string Current = string.Empty, Next = string.Empty, Pending = string.Empty, Reason = string.Empty;
        public int AgreementId, RouteIndex, Sequence;
        public bool RepublicSource, Returning, Cancelled, Settled, BaseDestination;
        public double Requested, Withdrawn, Delivered, Refunded, LegStart, LegEnd, RetryAt;
        public List<string> Route = new();
        public SWEmpireDiplomacyCargoPhase Phase = SWEmpireDiplomacyCargoPhase.AwaitingApproval;
        public double Remaining => Math.Max(0, Withdrawn - Delivered - Refunded);
    }

    private string Faction(string id) => Map.Worlds.FirstOrDefault(w => w.Id == id) is { } world ? Map.GetWorldFaction(world) : string.Empty;
    private SWRepublicEconomyRunner? SourceRunner(CargoLedger c) => c.RepublicSource ? _cargoRepublic.Runner : _cargoEmpire.Runner;
    private string Receipt(CargoLedger c, string kind) => $"diplomacy:{_cargoGeneration}:{c.Id}:{kind}:{++c.Sequence}";
    private static bool ValidAmount(double amount) => double.IsFinite(amount) && amount >= .01 && amount <= MaxCargoAmount;
    private bool HasInventory(SWRepublicEconomyRunner? runner, string world, string good) => runner != null && runner.Fault == null &&
        runner.Definition.Worlds.Any(w => w.Id == world && w.Active) && runner.Definition.Inventory.Any(i => i.World == world && i.Good == good);

    // The existing closed border can carry an individually authorised civilian shipment.
    // Its zero scheduled capacity is not a live blockade and does not change the economy or military graph.
    private static bool CivilianPhysicalLink(SWTransitLinkPrototype link) =>
        double.IsFinite(link.TravelHours) && link.TravelHours > 0 &&
        (link.Accessible && link.CapacitySlotsPerHour > 0 || link.ID == "korriban--telos" && !link.Accessible);

    private bool Route(string from, string to, string good, bool supply, bool baseDestination, out List<string> route, bool ignoreRights = false, bool ignoreRestrictions = false, bool allowUnsupported = false)
    {
        route = new();
        if (from == to || Faction(from) == string.Empty || Faction(to) == string.Empty) return false;
        var previous = new Dictionary<string, string> { [from] = string.Empty };
        var queue = new Queue<string>(); queue.Enqueue(from);
        var links = _proto.EnumeratePrototypes<SWTransitLinkPrototype>().Where(CivilianPhysicalLink).OrderBy(l => l.ID).ToArray();
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            var current = queue.Dequeue();
            foreach (var link in links)
            {
                var next = link.From == current ? link.To : link.To == current ? link.From : string.Empty;
                if (next == string.Empty || Faction(next) == string.Empty || previous.ContainsKey(next)) continue;
                var currentFaction = Faction(current);
                var nextFaction = Faction(next);
                // A bilateral Empire agreement cannot grant passage between two other parties.
                if (!allowUnsupported && (currentFaction == "republic" && nextFaction == "neutral" ||
                    currentFaction == "neutral" && nextFaction == "republic")) continue;
                var foreign = currentFaction != "empire" || nextFaction != "empire";
                var baseLeg = baseDestination && (current == to || next == to) && AllowsCivilianBasing(to);
                var supplyLeg = supply && (Faction(current) is "republic" or "empire") && (Faction(next) is "republic" or "empire");
                if (!ignoreRights && foreign && !baseLeg && !supplyLeg && !AllowsCivilianTransit(link.ID)) continue;
                if (!ignoreRestrictions && (!(_cargoRepublic.Runner?.AllowsExternalTransit(link.ID, current, next, good) ?? false) ||
                    !(_cargoEmpire.Runner?.AllowsExternalTransit(link.ID, current, next, good) ?? false))) continue;
                previous.Add(next, current); queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) return false;
        for (var current = to; current != string.Empty; current = previous[current]) route.Add(current);
        route.Reverse(); return route.Count<=65;
    }
    private SWTransitLinkPrototype? Link(string from, string to) => _proto.EnumeratePrototypes<SWTransitLinkPrototype>()
        .Where(l => CivilianPhysicalLink(l) && ((l.From == from && l.To == to) || (l.From == to && l.To == from))).OrderBy(l=>l.ID).FirstOrDefault();
    private bool LegAllowed(CargoLedger c, string from, string to, out string reason)
    {
        var link = Link(from, to);
        if (link == null)
        { reason = "Переход недоступен"; return false; }
        if (!(_cargoRepublic.Runner?.AllowsExternalTransit(link.ID, from, to, c.Good) ?? false) ||
            !(_cargoEmpire.Runner?.AllowsExternalTransit(link.ID, from, to, c.Good) ?? false))
        { reason = "Переход закрыт ограничениями или карантином"; return false; }
        var foreign = Faction(from) != "empire" || Faction(to) != "empire";
        var baseLeg = c.BaseDestination && (from == c.Destination || to == c.Destination) && AllowsCivilianBasing(c.Destination);
        var agreement = c.RepublicSource ? _agreements.FirstOrDefault(a => a.Id == c.AgreementId && a.Terms.Term == SWEmpireDiplomacyTerm.Supply &&
            (Active(a) || c.Cancelled && c.Returning && (a.Status is SWEmpireDiplomacyStatus.Broken or SWEmpireDiplomacyStatus.Expired))) : null;
        var reverseOfApproved = !c.Cancelled || !c.Returning || c.RouteIndex>0 && c.Route[c.RouteIndex]==from && c.Route[c.RouteIndex-1]==to;
        var scopedSupply = agreement != null && agreement.Terms.RouteLinks.Contains(link.ID) &&
            reverseOfApproved && (Faction(from) is "republic" or "empire") && (Faction(to) is "republic" or "empire");
        if (foreign && !baseLeg && !scopedSupply && !AllowsCivilianTransit(link.ID))
        { reason = "Нет действующего разрешения на транзит"; return false; }
        reason = string.Empty; return true;
    }
    private bool ValidCargo(SWEmpireDiplomacyRequest r, bool supply, out List<string> route, out string reason, SWEmpireDiplomacyQuote? quote = null)
    {
        route = new(); reason = "Некорректные товар, объём или склады";
        if (!ValidAmount(r.Amount) || !_proto.TryIndex<SWCommodityPrototype>(r.Good, out _) || _cargo.Count >= MaxCargo) return false;
        if (supply)
        {
            if (r.Party != SWEmpireDiplomacyParty.Republic || r.TargetId != r.SourceWorld || Faction(r.SourceWorld) != "republic" || Faction(r.DestinationWorld) != "empire" ||
                !HasInventory(_cargoRepublic.Runner, r.SourceWorld, r.Good) || !HasInventory(_cargoEmpire.Runner, r.DestinationWorld, r.Good)) return false;
        }
        else if (Faction(r.SourceWorld) != "empire" || !HasInventory(_cargoEmpire.Runner, r.SourceWorld, r.Good) ||
            !(Faction(r.DestinationWorld) == "empire" && HasInventory(_cargoEmpire.Runner, r.DestinationWorld, r.Good) ||
              Faction(r.DestinationWorld) != "empire" && AllowsCivilianBasing(r.DestinationWorld))) return false;
        if (!Route(r.SourceWorld, r.DestinationWorld, r.Good, supply, !supply && Faction(r.DestinationWorld) != "empire", out route))
        {
            var baseDestination = !supply && Faction(r.DestinationWorld) != "empire";
            if (Route(r.SourceWorld, r.DestinationWorld, r.Good, supply, baseDestination, out var candidate, true))
            {
                if (quote != null)
                {
                    quote.Request.RouteWorlds = candidate.ToList();
                    quote.Request.RouteLinks = candidate.Zip(candidate.Skip(1), (a,b) => Link(a,b)!.ID).ToList();
                    foreach (var link in quote.Request.RouteLinks.Select(id => _proto.Index<SWTransitLinkPrototype>(id)))
                    {
                        if (AllowsCivilianTransit(link.ID)) continue;
                        var factions = new[] { Faction(link.From), Faction(link.To) };
                        if (factions.All(f => f == "empire")) continue;
                        if (baseDestination && (link.From == r.DestinationWorld || link.To == r.DestinationWorld) && AllowsCivilianBasing(r.DestinationWorld)) continue;
                        if (supply && factions.All(f => f is "empire" or "republic")) continue;
                        quote.MissingTransitLinks.Add(link.ID);
                    }
                    quote.NeedsTransit = quote.MissingTransitLinks.Count > 0;
                    if (quote.NeedsTransit)
                    {
                        var first = _proto.Index<SWTransitLinkPrototype>(quote.MissingTransitLinks[0]);
                        quote.RequiredParty = Faction(first.From) == "neutral" || Faction(first.To) == "neutral" ? SWEmpireDiplomacyParty.Neutral : SWEmpireDiplomacyParty.Republic;
                    }
                }
                var missing = candidate.Zip(candidate.Skip(1), (a,b) => (From:a, To:b, Link:Link(a,b)!))
                    .Where(x => (Faction(x.From) != "empire" || Faction(x.To) != "empire") && !AllowsCivilianTransit(x.Link.ID))
                    .Select(x => $"{WorldName(x.From)} — {WorldName(x.To)}");
                reason = "Нужно согласовать транзит: " + string.Join(", ", missing);
            }
            else if (Route(r.SourceWorld, r.DestinationWorld, r.Good, supply, baseDestination, out _, true, true))
                reason = "Маршрут закрыт блокадой или карантином";
            else if (Route(r.SourceWorld, r.DestinationWorld, r.Good, supply, baseDestination, out _, true, true, true))
            {
                if (quote != null) quote.NegotiationUnsupported = true;
                reason = "Маршрут требует соглашения Республики с нейтральной стороной";
            }
            else reason = "Нет физического маршрута между выбранными мирами";
            return false;
        }
        reason = string.Empty; return true;
    }
    private string WorldName(string id) => Map.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? "неизвестный мир";

    private bool QuoteSupply(SWEmpireDiplomacyRequest r, SWEmpireDiplomacyQuote quote)
    {
        if (!ValidCargo(r, true, out var route, out var reason, quote)) { quote.Reason = reason; return false; }
        quote.Request.RouteWorlds = route.ToList();
        quote.Request.RouteLinks = route.Zip(route.Skip(1),(from,to)=>Link(from,to)!.ID).ToList();
        var good = _proto.Index<SWCommodityPrototype>(r.Good);
        var unit = good.Category.Id switch { "resources" => 60, "food" => 90, "materials" => 120, "energy" => 108,
            "components" => 180, "civil" => 150, "health" => 180, "defense" => 210, "culture" => 150, "controlled" => 210, _ => 120 };
        quote.Cost = Math.Max(1, (int) Math.Ceiling(r.Amount * unit)); quote.Allowed = true; quote.Reason = "Требуется согласие Республики"; return true;
    }
    private bool QuoteCargo(SWEmpireDiplomacyRequest r, SWEmpireDiplomacyQuote quote)
    {
        if (!ValidCargo(r, false, out var route, out var reason, quote)) { quote.Reason = reason; return false; }
        quote.Request.RouteWorlds = route.ToList();
        quote.Request.RouteLinks = route.Zip(route.Skip(1),(from,to)=>Link(from,to)!.ID).ToList();
        var probe = NewCargo(r, 0, route, false, false);
        for (var i = 0; i + 1 < route.Count; i++) if (!LegAllowed(probe, route[i], route[i + 1], out reason)) { quote.Reason = reason; return false; }
        var hours = 0d;
        for (var i = 0; i + 1 < route.Count; i++) hours += Link(route[i], route[i + 1])!.TravelHours;
        quote.Allowed = true; quote.Cost = 0; quote.Reason = $"Время в пути: {hours:0.##} активных ч., без ожидания"; return true;
    }
    private CargoLedger NewCargo(SWEmpireDiplomacyRequest r, int agreement, List<string> route, bool republic, bool assignId = true) => new()
    {
        Id = assignId ? (++_cargoSequence).ToString() : string.Empty, RequestId = r.RequestId, AgreementId = agreement, Good = r.Good, Source = r.SourceWorld,
        Destination = r.DestinationWorld, Current = r.SourceWorld, Requested = r.Amount, RepublicSource = republic,
        BaseDestination = !republic && Faction(r.DestinationWorld) != "empire", Route = route
    };
    private bool SubmitCargo(SWEmpireDiplomacyRequest r, out string reason)
    {
        if (!ValidCargo(r, false, out var route, out reason)) return false;
        var c = NewCargo(r, 0, route, false);
        for (var i = 0; i + 1 < route.Count; i++) if (!LegAllowed(c, route[i], route[i + 1], out reason)) return false;
        _requests.Add(r.RequestId); _cargo.Add(c); c.Phase = SWEmpireDiplomacyCargoPhase.DebitPending; reason = string.Empty; return true;
    }
    private List<SWEmpireDiplomacyCargoView> BuildCargo() => _cargo.Select(c => new SWEmpireDiplomacyCargoView
    {
        Id = c.Id, AgreementId = c.AgreementId, Good = c.Good, SourceWorld = c.Source, DestinationWorld = c.Destination,
        CurrentWorld = c.Current, NextWorld = c.Next, Amount = c.Requested, Delivered = c.Delivered, Phase = c.Phase, Reason = c.Reason,
        LegStartedAt = c.LegStart, LegEndsAt = c.LegEnd, EtaHours = CargoEta(c), Route = c.Route.ToList()
    }).ToList();
    private List<SWEmpireDiplomacyCargoView> BuildRepublicCargo()
    {
        var allowed = _cargo.Where(c => c.RepublicSource).Select(c => c.Id).ToHashSet();
        return BuildCargo().Where(c => allowed.Contains(c.Id)).ToList();
    }
    private double CargoEta(CargoLedger c)
    {
        if (c.Phase is not (SWEmpireDiplomacyCargoPhase.InTransit or SWEmpireDiplomacyCargoPhase.Returning) || c.Next == string.Empty) return 0;
        var eta = Math.Max(0, c.LegEnd - _time.ActiveGameHours);
        var step = c.Returning ? -1 : 1;
        for (var i = c.RouteIndex + step; i + step >= 0 && i + step < c.Route.Count; i += step)
            eta += Link(c.Route[i], c.Route[i + step])?.TravelHours ?? 0;
        return eta;
    }
    public double ReadBaseStock(string world, string good) => _baseStock.GetValueOrDefault((world, good));
    public bool TryConsumeBaseSupplies(string world, IReadOnlyDictionary<string, double> requirements)
    {
        if (!_time.ModeActive || !_time.DayActive || !AllowsCivilianBasing(world) || requirements.Count == 0 || requirements.Any(p => !double.IsFinite(p.Value) || p.Value <= 0 || ReadBaseStock(world, p.Key) < p.Value)) return false;
        foreach (var (good, amount) in requirements) _baseStock[(world, good)] = Math.Max(0, ReadBaseStock(world, good) - amount);
        return true;
    }
    private void RefundSupply(SWEmpireDiplomacyAgreementView a)
    {
        if (a.Terms.Term != SWEmpireDiplomacyTerm.Supply) return;
        var c = _cargo.FirstOrDefault(x => x.AgreementId == a.Id);
        if (c == null) _budget.Refund(SWEmpireCouncilSphere.ExpansionDiplomacy, a.Terms.RequestId);
        else CancelCargo(c);
    }
    private void StopSupply(SWEmpireDiplomacyAgreementView a) => RefundSupply(a);
    private void CancelCargo(CargoLedger c)
    {
        c.Cancelled = true;
        if (c.Pending != string.Empty)
        {
            if (c.Phase == SWEmpireDiplomacyCargoPhase.DebitPending) SourceRunner(c)?.CancelExternalTransfer(c.Pending);
            if (c.Phase == SWEmpireDiplomacyCargoPhase.DepositPending) _cargoEmpire.Runner?.CancelExternalTransfer(c.Pending);
        }
    }
    private void ResetCargo()
    {
        foreach (var c in _cargo)
        {
            if (c.Pending == string.Empty) continue;
            SourceRunner(c)?.CancelExternalTransfer(c.Pending);
            _cargoEmpire.Runner?.CancelExternalTransfer(c.Pending);
        }
        _cargo.Clear(); _baseStock.Clear(); _cargoSequence = 0; _cargoGeneration++;
    }
    private bool Settle(CargoLedger c)
    {
        if (!c.RepublicSource || c.Settled) return true;
        var agreement = _agreements.FirstOrDefault(a => a.Id == c.AgreementId);
        if (agreement == null) return false;
        var spent = Math.Clamp((int)Math.Ceiling(agreement.Cost * c.Delivered / c.Requested), 0, agreement.Cost);
        if (!_cargoTreasury.CanCreditTreasury(spent)) { c.Reason = "Ожидается зачисление оплаты в казну"; return false; }
        if (!_budget.CompleteReservation(SWEmpireCouncilSphere.ExpansionDiplomacy, c.RequestId, spent))
        { c.Reason = "Ожидается расчёт по резерву"; return false; }
        _cargoTreasury.CreditTreasury(spent); c.Settled = true; return true;
    }
    private void Finish(CargoLedger c)
    {
        if (c.Remaining > CargoEpsilon) return;
        c.Phase = SWEmpireDiplomacyCargoPhase.Paused;
        if (!Settle(c)) return;
        c.Next = string.Empty;
        c.Phase = c.Delivered > CargoEpsilon ? SWEmpireDiplomacyCargoPhase.Completed : SWEmpireDiplomacyCargoPhase.Rejected;
        c.Reason = c.Delivered > CargoEpsilon ? "Доставка подтверждена" : "Груз не доставлен";
        if (c.RepublicSource && _agreements.FirstOrDefault(a => a.Id == c.AgreementId) is { } a && a.Status == SWEmpireDiplomacyStatus.Active)
        { a.Status = SWEmpireDiplomacyStatus.Completed; a.Reason = c.Reason; }
    }
    private bool Queue(CargoLedger c, SWRepublicEconomyRunner? runner, string world, double amount, bool deposit, string kind)
    {
        if (runner == null || runner.Fault != null || amount <= CargoEpsilon) return false;
        var id = Receipt(c, kind);
        if (!runner.TryQueueExternalTransfer(new EconomyExternalTransfer(id, world, c.Good, amount, deposit))) return false;
        c.Pending = id; return true;
    }
    private void TickCargo()
    {
        foreach (var a in _agreements.Where(a => a.Terms.Term == SWEmpireDiplomacyTerm.Supply && Active(a)).ToArray())
        {
            if (_cargo.Any(c => c.AgreementId == a.Id)) continue;
            var route=a.Terms.RouteWorlds.ToList();
            if (route.Count<2 || route.Count>65 || route[0]!=a.Terms.SourceWorld || route[^1]!=a.Terms.DestinationWorld ||
                a.Terms.RouteLinks.Count!=route.Count-1 || !HasInventory(_cargoRepublic.Runner,a.Terms.SourceWorld,a.Terms.Good) ||
                !HasInventory(_cargoEmpire.Runner,a.Terms.DestinationWorld,a.Terms.Good))
            { a.Status = SWEmpireDiplomacyStatus.Rejected; a.Reason = "Согласованный маршрут или склады недоступны"; RefundSupply(a); continue; }
            var c = NewCargo(a.Terms, a.Id, route, true);
            for (var i = 0; i + 1 < route.Count; i++)
                if (Link(route[i],route[i+1])?.ID != a.Terms.RouteLinks[i])
                { a.Status = SWEmpireDiplomacyStatus.Rejected; a.Reason = "Согласованный переход недоступен"; RefundSupply(a); c = null!; break; }
            if (c == null) continue;
            c.Phase = SWEmpireDiplomacyCargoPhase.DebitPending; _cargo.Add(c);
        }
        foreach (var c in _cargo.ToArray()) TickOneCargo(c);
    }
    private void TickOneCargo(CargoLedger c)
    {
        if (c.Phase is SWEmpireDiplomacyCargoPhase.Completed or SWEmpireDiplomacyCargoPhase.Rejected || _time.ActiveGameHours < c.RetryAt) return;
        if (c.Phase == SWEmpireDiplomacyCargoPhase.DebitPending)
        {
            if (c.Pending == string.Empty)
            {
                if (c.Cancelled) { Finish(c); return; }
                if (!LegAllowed(c, c.Route[0], c.Route[1], out var reason)) { c.Reason = reason; return; }
                if (!Queue(c, SourceRunner(c), c.Source, c.Requested, false, "withdraw")) c.Reason = "Ожидание исходного склада";
                return;
            }
            var receipt = SourceRunner(c)?.GetExternalTransferResult(c.Pending);
            if (receipt == null) return;
            c.Pending = string.Empty; c.Withdrawn = Math.Clamp(receipt.Amount, 0, c.Requested);
            if (c.Withdrawn <= CargoEpsilon) { c.Withdrawn = 0; Finish(c); return; }
            c.Returning = c.Cancelled; c.Phase = SWEmpireDiplomacyCargoPhase.Paused;
        }
        if (c.Phase == SWEmpireDiplomacyCargoPhase.DepositPending)
        {
            var receipt = _cargoEmpire.Runner?.GetExternalTransferResult(c.Pending);
            if (receipt == null) return;
            c.Pending = string.Empty; c.Delivered += Math.Clamp(receipt.Amount, 0, c.Remaining);
            if (c.Remaining <= CargoEpsilon) { Finish(c); return; }
            c.Returning = true; c.Phase = SWEmpireDiplomacyCargoPhase.Paused;
        }
        if (c.Phase == SWEmpireDiplomacyCargoPhase.Returning && c.Pending != string.Empty)
        {
            var receipt = SourceRunner(c)?.GetExternalTransferResult(c.Pending);
            if (receipt == null) return;
            c.Pending = string.Empty; c.Refunded += Math.Clamp(receipt.Amount, 0, c.Remaining);
            if (c.Remaining <= CargoEpsilon) { Finish(c); return; }
            c.Reason = "Исходный склад заполнен; ожидается возврат остатка";
            c.RetryAt = _time.ActiveGameHours + 1; c.Phase = SWEmpireDiplomacyCargoPhase.Paused; return;
        }
        if (c.Phase == SWEmpireDiplomacyCargoPhase.InTransit || c.Phase == SWEmpireDiplomacyCargoPhase.Returning && c.Next != string.Empty)
        {
            if (_time.ActiveGameHours < c.LegEnd) return;
            c.Current = c.Next; c.Next = string.Empty; c.RouteIndex += c.Returning ? -1 : 1;
            c.Phase = SWEmpireDiplomacyCargoPhase.Paused;
        }
        if (c.Cancelled) c.Returning = true;
        if (c.Remaining <= CargoEpsilon) { Finish(c); return; }
        if (c.Returning && c.Current == c.Source)
        {
            if (Queue(c, SourceRunner(c), c.Source, c.Remaining, true, "return")) c.Phase = SWEmpireDiplomacyCargoPhase.Returning;
            else c.Reason = "Ожидание исходного склада для возврата";
            return;
        }
        if (!c.Returning && c.Current == c.Destination)
        {
            if (c.BaseDestination)
            {
                if (!AllowsCivilianBasing(c.Destination)) { c.Reason = "Нет действующего договора базирования"; return; }
                var total = _baseStock.Where(p => p.Key.World == c.Destination).Sum(p => p.Value);
                var accepted = Math.Min(c.Remaining, Math.Max(0, BaseCapacity - total));
                _baseStock[(c.Destination, c.Good)] = ReadBaseStock(c.Destination, c.Good) + accepted; c.Delivered += accepted;
                if (c.Remaining <= CargoEpsilon) { Finish(c); return; }
                c.Returning = true; c.Reason = "Склад базы заполнен; остаток возвращается";
            }
            else
            {
                if (Queue(c, _cargoEmpire.Runner, c.Destination, c.Remaining, true, "deposit")) c.Phase = SWEmpireDiplomacyCargoPhase.DepositPending;
                else c.Reason = "Ожидание склада назначения";
                return;
            }
        }
        var nextIndex = c.RouteIndex + (c.Returning ? -1 : 1);
        if (nextIndex < 0 || nextIndex >= c.Route.Count) { c.Reason = "Маршрут недоступен"; return; }
        var next = c.Route[nextIndex];
        if (!LegAllowed(c, c.Current, next, out var blocked)) { c.Reason = blocked; return; }
        var link = Link(c.Current, next)!;
        c.Next = next; c.LegStart = _time.ActiveGameHours; c.LegEnd = c.LegStart + link.TravelHours;
        c.Phase = c.Returning ? SWEmpireDiplomacyCargoPhase.Returning : SWEmpireDiplomacyCargoPhase.InTransit;
        c.Reason = c.Returning ? "Возврат остатка" : "В пути";
    }
}
