using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Consoles;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;

namespace Content.Server._SW._Senate.Empire;

public sealed class SWEmpireCouncilConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SWStrategicVisibilitySystem _visibility = default!;
    [Dependency] private readonly SWEmpireCouncilBudgetSystem _budget = default!;
    [Dependency] private readonly SWEmpireEconomySystem _economy = default!;
    [Dependency] private readonly SWEmpireIndustrySystem _industry = default!;
    [Dependency] private readonly SWEmpireMilitarySystem _military = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    private readonly Dictionary<(EntityUid Actor, SWEmpireCouncilSphere Sphere), (string Id, string Result, SWEmpireIndustryQuote Quote)> _results = new();
    private readonly Dictionary<(EntityUid Actor, SWEmpireCouncilSphere Sphere), SWEmpireMilitaryQuote> _militaryQuotes = new();
    private readonly HashSet<string> _militaryRequests = new();
    private float _elapsed;
    private object? _viewSnapshot;
    private bool _viewPaused;
    private SWEconomyView? _cachedView;
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), int> _sent = new();
    private const SWEmpireCouncilConsoleUiKey Key = SWEmpireCouncilConsoleUiKey.Key;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireIndustryQuoteMessage>(OnQuote);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireIndustryCommandMessage>(OnCommand);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireMaterialQuoteMessage>(OnMaterialQuote);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireMaterialCommandMessage>(OnMaterialCommand);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireMaterialRejectMessage>(OnMaterialReject);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireBudgetTransferMessage>(OnTransfer);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireMilitaryQuoteMessage>(OnMilitaryQuote);
        SubscribeLocalEvent<SWEmpireCouncilConsoleComponent, SWEmpireMilitaryCommandMessage>(OnMilitaryCommand);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _results.Clear(); _militaryQuotes.Clear(); _militaryRequests.Clear(); _sent.Clear(); _cachedView = null; _viewSnapshot = null; _elapsed = 0; });
    }

    private void OnMilitaryQuote(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireMilitaryQuoteMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere != SWEmpireCouncilSphere.MilitaryOffense || args.Request == null) return;
        _military.TryQuote(args.Request, out var quote);
        _militaryQuotes[(args.Actor, sphere)] = quote;
        _results[(args.Actor, sphere)] = (args.Request.RequestId, quote.Reason, new());
        Send(ent.Owner, args.Actor);
    }

    private void OnMilitaryCommand(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireMilitaryCommandMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere != SWEmpireCouncilSphere.MilitaryOffense || args.Request == null) return;
        var request = args.Request;
        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128 || !_militaryRequests.Add(request.RequestId)) return;
        if (!_military.TryQuote(request, out var quote) || !quote.Allowed)
        { _results[(args.Actor, sphere)] = (request.RequestId, quote.Reason, new()); _militaryQuotes[(args.Actor, sphere)] = quote; Send(ent.Owner, args.Actor); return; }
        if (quote.Cost < 0 || quote.Cost > 0 && !_budget.TryReserve(sphere, quote.Cost, request.RequestId, out _))
        { _results[(args.Actor, sphere)] = (request.RequestId, "Недостаточно средств сферы", new()); Send(ent.Owner, args.Actor); return; }
        bool ok;
        string reason;
        try { ok = _military.TrySubmit(request, out reason); }
        catch { if (quote.Cost > 0) _budget.Refund(sphere, request.RequestId); throw; }
        if (quote.Cost > 0) { if (ok) _budget.Commit(sphere, request.RequestId); else _budget.Refund(sphere, request.RequestId); }
        _results[(args.Actor, sphere)] = (request.RequestId, ok ? "Приказ принят" : reason, new());
        _militaryQuotes[(args.Actor, sphere)] = new();
        Send(ent.Owner, args.Actor);
    }

    private void OnOpen(Entity<SWEmpireCouncilConsoleComponent> ent, ref BoundUIOpenedEvent args) => Send(ent.Owner, args.Actor);
    private bool Authorized(EntityUid console, EntityUid actor, out SWEmpireCouncilSphere sphere)
    {
        sphere = _visibility.CouncilSphere(actor);
        return sphere != SWEmpireCouncilSphere.None && _visibility.CanRead(console, actor)
            && _ui.GetActors((console, null), Key).Contains(actor);
    }
    private void OnQuote(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireIndustryQuoteMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere != SWEmpireCouncilSphere.ProductionLogistics || args.Request == null) return;
        _industry.TryQuote(args.Request, out var quote);
        _results[(args.Actor, sphere)] = (args.Request.RequestId, quote.Reason, quote);
        Send(ent.Owner, args.Actor);
    }
    private void OnCommand(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireIndustryCommandMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere != SWEmpireCouncilSphere.ProductionLogistics || args.Request == null) return;
        var request = args.Request;
        if (!_industry.TryQuote(request, out var quote) || !quote.Allowed)
        { _results[(args.Actor, sphere)] = (request.RequestId, quote.Reason, quote); Send(ent.Owner, args.Actor); return; }
        if (request.Action == SWEmpireIndustryAction.AcceptMaterial && quote.Cost == 0)
        {
            var pending = _industry.BuildProjects().FirstOrDefault(p => p.Id == request.World && p.AwaitingSource);
            if (pending == null || !_budget.HasReservation(pending.SourceSphere, pending.Id))
            { _results[(args.Actor, sphere)] = (request.RequestId, "Заявка не обеспечена средствами", quote); Send(ent.Owner, args.Actor); return; }
            var accepted = _industry.TrySubmit(request, out var acceptReason);
            if (accepted) _budget.Commit(pending.SourceSphere, pending.Id);
            _results[(args.Actor, sphere)] = (request.RequestId, accepted ? "Заявка передана в доставку" : acceptReason, quote);
            Send(ent.Owner, args.Actor); return;
        }
        if (!_budget.TryReserve(sphere, quote.Cost, request.RequestId, out var reason))
        { _results[(args.Actor, sphere)] = (request.RequestId, reason, quote); Send(ent.Owner, args.Actor); return; }
        try
        {
            if (!_industry.TrySubmit(request, out reason))
            { _budget.Refund(sphere, request.RequestId); _results[(args.Actor, sphere)] = (request.RequestId, reason, quote); }
            else
            { _budget.Commit(sphere, request.RequestId); _results[(args.Actor, sphere)] = (request.RequestId, "Приказ принят", quote); }
        }
        catch
        {
            _budget.Refund(sphere, request.RequestId); throw;
        }
        Send(ent.Owner, args.Actor);
    }
    private void OnTransfer(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireBudgetTransferMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere)) return;
        var ok = _budget.Transfer(sphere, args.Target, args.Amount, args.RequestId);
        _results[(args.Actor, sphere)] = (args.RequestId, ok ? "Средства переведены" : "Перевод отклонён", new());
        Send(ent.Owner, args.Actor);
    }
    private void OnMaterialQuote(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireMaterialQuoteMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere == SWEmpireCouncilSphere.ProductionLogistics || args.Request == null) return;
        _industry.TryQuoteMaterial(sphere, args.Request, out var quote);
        _results[(args.Actor, sphere)] = (args.Request.RequestId, quote.Reason, quote);
        Send(ent.Owner, args.Actor);
    }
    private void OnMaterialCommand(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireMaterialCommandMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere == SWEmpireCouncilSphere.ProductionLogistics || args.Request == null) return;
        var request = args.Request;
        if (!_industry.TryQuoteMaterial(sphere, request, out var quote) || !quote.Allowed)
        { _results[(args.Actor, sphere)] = (request.RequestId, quote.Reason, quote); Send(ent.Owner, args.Actor); return; }
        if (!_budget.TryReserve(sphere, quote.Cost, request.RequestId, out var reason))
        { _results[(args.Actor, sphere)] = (request.RequestId, reason, quote); Send(ent.Owner, args.Actor); return; }
        try
        {
            if (!_industry.TrySubmitMaterial(sphere, request, out reason))
            { _budget.Refund(sphere, request.RequestId); _results[(args.Actor, sphere)] = (request.RequestId, reason, quote); }
            else
            { _results[(args.Actor, sphere)] = (request.RequestId, "Средства зарезервированы; ожидается выбор источника", quote); }
        }
        catch { _budget.Refund(sphere, request.RequestId); throw; }
        Send(ent.Owner, args.Actor);
    }
    private void OnMaterialReject(Entity<SWEmpireCouncilConsoleComponent> ent, ref SWEmpireMaterialRejectMessage args)
    {
        if (!Authorized(ent.Owner, args.Actor, out var sphere) || sphere != SWEmpireCouncilSphere.ProductionLogistics || string.IsNullOrWhiteSpace(args.RequestId)) return;
        var requestId = args.RequestId;
        var pending = _industry.BuildProjects().FirstOrDefault(p => p.Id == requestId && p.AwaitingSource);
        if (pending == null || !_budget.HasReservation(pending.SourceSphere, pending.Id)) return;
        var ok = _industry.TryRejectMaterial(pending.Id, out var reason);
        if (ok) _budget.Refund(pending.SourceSphere, pending.Id);
        _results[(args.Actor, sphere)] = (args.RequestId, ok ? "Заявка отклонена, средства возвращены" : reason, new());
        Send(ent.Owner, args.Actor);
    }
    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 2) return;
        _elapsed = 0;
        foreach (var pending in _industry.BuildProjects().Where(p => p.ExpiredUnaccepted || p.RejectedUnaccepted))
            _budget.Refund(pending.SourceSphere, pending.Id);
        var query = EntityQueryEnumerator<SWEmpireCouncilConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
            foreach (var actor in _ui.GetActors((uid, ui), Key).ToArray()) Send(uid, actor, false);
    }
    private void Send(EntityUid console, EntityUid actor, bool force = true)
    {
        if (!Authorized(console, actor, out var sphere)) return;
        var result = _results.GetValueOrDefault((actor, sphere));
        var snapshot = _economy.Snapshot;
        var paused = !_time.ModeActive || !_time.DayActive;
        if (_cachedView == null || !ReferenceEquals(_viewSnapshot, snapshot) || _viewPaused != paused)
        {
            _cachedView = SWRepublicEconomyPublicationSystem.BuildView(snapshot, 0, paused, _economy.GoodNames);
            _cachedView.ViewerFaction = "empire";
            _viewSnapshot = snapshot; _viewPaused = paused;
        }
        var projects = _industry.BuildProjects();
        var military = _military.BuildState();
        var hash = new HashCode();
        hash.Add(snapshot); hash.Add(paused); hash.Add(sphere); hash.Add(_budget.Treasury); hash.Add(_budget.NextAllocationDay);
        foreach (var entry in _budget.Balances) { hash.Add(entry.Key); hash.Add(entry.Value); }
        foreach (var entry in _budget.Spent) { hash.Add(entry.Key); hash.Add(entry.Value); }
        foreach (var entry in _budget.Received) { hash.Add(entry.Key); hash.Add(entry.Value); }
        foreach (var p in projects) { hash.Add(p.Id); hash.Add(p.Status); hash.Add(p.Reason); hash.Add(p.Source); hash.Add(p.Delivered); hash.Add(p.Sent); hash.Add(p.InTransit); hash.Add(p.WorkedHours); hash.Add(p.AwaitingSource); hash.Add(p.ExpiredUnaccepted); hash.Add(p.RejectedUnaccepted); }
        hash.Add(military.ClockRunning); if (military.ClockRunning) hash.Add(military.NowHours);
        foreach (var f in military.Fleets) { hash.Add(f.Id); hash.Add(f.WorldId); hash.Add(f.NextWorldId); hash.Add(f.Target); hash.Add(f.Status); hash.Add(f.Phase); hash.Add(f.Order); hash.Add(f.Strength); hash.Add(f.Damage); hash.Add(f.Supply); hash.Add(f.Training); hash.Add(f.EtaHours); hash.Add(f.LegStartedAt); hash.Add(f.LegEndsAt); hash.Add(f.CargoGood); hash.Add(f.CargoGoodName); hash.Add(f.CargoStatus); hash.Add(f.CargoAmount); hash.Add(f.CargoCapacity); hash.Add(f.CargoUsedSlots); hash.Add(f.CargoReadyAt); foreach (var point in f.Route) hash.Add(point); }
        foreach (var o in military.Options) { hash.Add(o.FleetId); hash.Add(o.Action); hash.Add(o.Target); hash.Add(o.Available); hash.Add(o.Reason); hash.Add(o.Risk); hash.Add(o.Cost); hash.Add(o.EtaHours); }
        foreach (var o in military.Observations) { hash.Add(o.SectorId); hash.Add(o.Summary); hash.Add(o.SeenHours); hash.Add(o.Current); }
        foreach (var b in military.Borders) { hash.Add(b.Id); hash.Add(b.Name); hash.Add(b.OwnWorldId); hash.Add(b.ForeignSectorId); hash.Add(b.Status); hash.Add(b.Warning); hash.Add(b.ReadyAt); }
        hash.Add(military.SupplyHour);
        foreach (var flow in military.SupplyFlows) hash.Add(flow);
        var signature = hash.ToHashCode();
        if (!force && _sent.TryGetValue((console, actor), out var previous) && previous == signature) return;
        _sent[(console, actor)] = signature;
        var state = new SWEmpireCouncilConsoleState
        {
            Sphere = sphere, Treasury = _budget.Treasury, Allocated = _budget.Allocated, TotalFunds = _budget.TotalFunds,
            DailyAllocation = SWEmpireCouncilBudgetSystem.DailySphereAllocation, NextAllocationDay = _budget.NextAllocationDay,
            Budgets = _budget.Balances.ToDictionary(p => p.Key, p => p.Value),
            Spent = _budget.Spent.ToDictionary(p => p.Key, p => p.Value), Received = _budget.Received.ToDictionary(p => p.Key, p => p.Value),
            IncomePerHour = _budget.IncomePerHour, Economy = _cachedView, Projects = projects, Military = military,
            MilitaryQuote = _militaryQuotes.GetValueOrDefault((actor, sphere)) ?? new(),
            ResultRequestId = result.Id ?? string.Empty, Result = result.Result ?? string.Empty,
            Quote = result.Quote ?? new(), QuoteAllowed = result.Quote?.Allowed ?? false, QuoteCost = result.Quote?.Cost ?? 0,
        };
        _ui.ServerSendUiMessage(console, Key, new SWEmpireCouncilUpdateMessage(state), actor);
    }
}
