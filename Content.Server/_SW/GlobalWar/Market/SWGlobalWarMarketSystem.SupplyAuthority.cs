using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly Dictionary<(NetUserId User, Guid Request), (Guid Assignment, SWGlobalWarSupplyCommand Command,
        SWGlobalWarSupplyResult Result)> _supplyRequests = new();
    private readonly List<SWGlobalWarSupplyJournalEntry> _supplyJournal = new();
    internal IReadOnlyList<SWGlobalWarSupplyJournalEntry> SupplyJournal => _supplyJournal;

    private bool HasSupplyAuthority(SWGlobalWarAssignment actor, bool production, bool headDecision)
    {
        var role = production ? SWGlobalWarRole.IndustryMinister : SWGlobalWarRole.WarMinister;
        var authority = production ? SWGlobalWarAuthority.IndustryBudget : SWGlobalWarAuthority.WarBudget;
        return actor.Role == role || actor.Role == SWGlobalWarRole.Head &&
            (headDecision || (_assignments.GetAuthority(actor) & authority) != 0);
    }

    /// <summary>Revalidates the current office and exact subject before any supply mutation or replay.</summary>
    public bool TryConfigureSupply(ICommonSession session, SWGlobalWarSupplyCommand command,
        out SWGlobalWarSupplyResult result)
    {
        result = new(command?.Request ?? Guid.Empty, false, SWGlobalWarSupplyFailure.Denied);
        if (command == null || command.Request == Guid.Empty || !Enum.IsDefined(command.Action) ||
            !Enum.IsDefined(command.Kind) || !CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor))
            return false;
        var production = command.Action is SWGlobalWarSupplyAction.ConfigureLine or SWGlobalWarSupplyAction.CancelProduction;
        if (!HasSupplyAuthority(actor, production, command.HeadDecision))
            return false;
        var key = (actor.User, command.Request);
        if (_supplyRequests.TryGetValue(key, out var previous))
        {
            if (previous.Assignment != actor.Id || previous.Command != command)
                return false;
            // A pending physical return is retryable with the same exact operation after its point is restored.
            if (previous.Result.Failure != SWGlobalWarSupplyFailure.ReturnPending)
            {
                result = previous.Result;
                return result.Success;
            }
        }
        Update(0);
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            !HasSupplyAuthority(actor, production, command.HeadDecision))
            return false;
        if (command.Action == SWGlobalWarSupplyAction.PreviewPurchase)
        {
            if (!TryQuoteSupplyPurchase(actor, command, out var quote))
            {
                result = new(command.Request, false, SWGlobalWarSupplyFailure.Unavailable);
                return false;
            }
            result = new(command.Request, true, SWGlobalWarSupplyFailure.None, Quote: quote);
            _supplyRequests[key] = (actor.Id, command, result);
            return true;
        }
        var notifications = _ledger.DeferNotifications();
        try
        {
            return ExecuteSupplyCommand(actor, command, production, out result);
        }
        finally
        {
            var wasSettling = _settling;
            _settling = true;
            try
            {
                notifications.Dispose();
            }
            finally
            {
                _settling = wasSettling;
            }
        }
    }

    private bool ExecuteSupplyCommand(SWGlobalWarAssignment actor, SWGlobalWarSupplyCommand command,
        bool production, out SWGlobalWarSupplyResult result)
    {
        var key = (actor.User, command.Request);
        var program = Simulation!.SupplyPrograms[actor.Faction];
        var line = program.Lines[command.Kind];
        var before = SupplySubjectSnapshot(actor.Faction, command);
        var failure = SWGlobalWarSupplyFailure.Unavailable;
        var success = false;
        var target = command.Target;
        var continues = false;
        switch (command.Action)
        {
            case SWGlobalWarSupplyAction.ConfigureLine:
                failure = SWGlobalWarSupplyFailure.Revision;
                if (line.Revision != command.ExpectedRevision || line.Revision == long.MaxValue ||
                    command.LocalPercent is < 0 or > 100 || command.Bacta && command.Kind != SWGlobalWarSupplyKind.Medicine)
                    break;
                line.LocalPercent = command.LocalPercent;
                line.Bacta = command.Bacta;
                line.Revision++;
                success = true;
                break;
            case SWGlobalWarSupplyAction.Purchase:
                if (TryQuoteSupplyPurchase(actor, command, out var routeQuote) &&
                    routeQuote.Failure == SWGlobalWarSupplyFailure.TransportRoute)
                {
                    failure = SWGlobalWarSupplyFailure.TransportRoute;
                    break;
                }
                failure = SWGlobalWarSupplyFailure.Revision;
                if (!MatchesSupplyQuote(actor, command))
                    break;
                failure = SWGlobalWarSupplyFailure.Unavailable;
                if (command.Source is not { } source || command.Recipient is not { } recipient ||
                    !TryGetSupplyStore(command.Generation, GetEntity(source), actor.Faction, out var from, out _) ||
                    !TryGetSupplyStore(command.Generation, GetEntity(recipient), actor.Faction, out var to, out _) ||
                    !_ledger.TryGetLedger(out var purchaseLedger))
                    break;
                // The physical consumer validates the current route before reserving money, items or destination space.
                // The saved command retains its quote revision for exact replay. The internal reserve uses
                // the current revision after revalidating the displayed plan and available funds.
                success = TryReserveSupplyPurchase(new(command.Generation, command.Request, purchaseLedger.Comp.Revision,
                    actor.Faction, GetEntity(source), GetEntity(recipient), command.Kind, command.Quantity, command.Bacta),
                    out _, out failure);
                target = command.Request;
                break;
            case SWGlobalWarSupplyAction.CancelPurchase:
                if (!_supplyPurchases.TryGetValue(target, out var purchase) ||
                    purchase.Request.Faction != actor.Faction || purchase.Request.Kind != command.Kind)
                    break;
                success = TryCancelSupplyPurchase(command.Generation, target,
                    FindSupplyReturnStore(command.Generation, purchase.Request.Source));
                continues = purchase.Production is { } batch && _supplyProduction[batch].Stage == SWGlobalWarSupplyStage.Producing;
                if (purchase.ReturnPending)
                    failure = SWGlobalWarSupplyFailure.ReturnPending;
                break;
            case SWGlobalWarSupplyAction.ResumeIntent:
                if (!_supplyIntents.TryGetValue((actor.Faction, program.Window, command.Kind), out var intent) ||
                    intent.Id != target)
                    break;
                intent.Suspended = false;
                success = true;
                break;
            case SWGlobalWarSupplyAction.ApproveArsenal:
                if (command.Recipient is not { } approved ||
                    !_supplyPurchases.TryGetValue(target, out var approving) || approving.Request.Kind != command.Kind)
                    break;
                success = TryApproveSupplyArsenal(command.Generation, target, actor.Faction, GetEntity(approved),
                    command.Quantity, command.ExpectedRevision, out failure);
                break;
            case SWGlobalWarSupplyAction.CancelProduction:
                if (!_supplyProduction.TryGetValue(target, out var work) || work.Request.Faction != actor.Faction ||
                    work.Request.Kind != command.Kind)
                    break;
                success = TryCancelSupplyProduction(command.Generation, target,
                    FindSupplyReturnStore(command.Generation, work.Request.Store));
                if (work.ReturnPending)
                    failure = SWGlobalWarSupplyFailure.ReturnPending;
                break;
        }
        result = new(command.Request, success, success ? SWGlobalWarSupplyFailure.None : failure, target,
            line.Revision, continues);
        _supplyRequests[key] = (actor.Id, command, result);
        var after = SupplySubjectSnapshot(actor.Faction, command);
        if (!success && before == after)
            return false;
        _supplyJournal.Add(new(command.Generation, command.Request, actor.User, actor.Id, actor.Faction, actor.Role,
            command.HeadDecision, command.Action, command.Kind, target, before, after, Simulation.Time));
        if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision && _assignments.TryGetRegistry(out var registry))
        {
            var responsible = production ? SWGlobalWarRole.IndustryMinister : SWGlobalWarRole.WarMinister;
            foreach (var minister in registry.Comp.Assignments.Values.Where(a =>
                         a.Faction == actor.Faction && a.Role == responsible))
            {
                if (TryComp<ActorComponent>(minister.Body, out var player))
                    _economyChat.DispatchServerMessage(player.PlayerSession,
                        Loc.GetString("sw-global-war-supply-head-decision"));
            }
        }
        return success;
    }

    private EntityUid? FindSupplyReturnStore(Guid generation, EntityUid originalStore)
    {
        if (!_supplyStores.TryGetValue(originalStore, out var original))
            return null;
        var owner = Simulation!.Markets[original.Planet].Owner;
        foreach (var candidate in _supplyStores.Keys)
            if (TryGetSupplyStore(generation, candidate, owner, out var binding, out _) &&
                binding.Planet == original.Planet && binding.Coordinates == original.Coordinates &&
                binding.Arsenal == original.Arsenal)
                return candidate;
        return null;
    }

    private SWGlobalWarSupplySubjectSnapshot SupplySubjectSnapshot(SWGlobalWarFaction faction,
        SWGlobalWarSupplyCommand command)
    {
        if (command.Action == SWGlobalWarSupplyAction.ConfigureLine)
        {
            var line = Simulation!.SupplyPrograms[faction].Lines[command.Kind];
            return new(true, Revision: line.Revision, LocalPercent: line.LocalPercent, Bacta: line.Bacta);
        }
        if (command.Action == SWGlobalWarSupplyAction.ResumeIntent)
        {
            var intent = _supplyIntents.Values.FirstOrDefault(i => i.Id == command.Target && i.Faction == faction);
            return intent == null ? new(false) : new(true, Suspended: intent.Suspended);
        }
        if (command.Action == SWGlobalWarSupplyAction.CancelProduction)
        {
            if (!_supplyProduction.TryGetValue(command.Target, out var work) || work.Request.Faction != faction)
                return new(false);
            return new(true, work.Stage, work.Quantity, ReturnPending: work.ReturnPending,
                InputReservations: work.Stocks.Count(id => Simulation!.HasPendingStockReservation(command.Generation, id)));
        }
        var id = command.Action == SWGlobalWarSupplyAction.Purchase ? command.Request : command.Target;
        if (!_supplyPurchases.TryGetValue(id, out var purchase) || purchase.Request.Faction != faction)
            return new(false);
        var order = purchase.Obligation;
        var payment = _ledger.TryGetReservation(purchase.Reservation, out var reservation) ? reservation.Remaining : 0;
        return new(true, order.Stage, order.Quantity, order.Accepted, order.Outstanding, order.Cancelled, payment,
            purchase.ReturnPending, Revision: purchase.ArsenalRevision);
    }

    private bool IsCurrentSupplyViewer(SWGlobalWarAssignment actor)
    {
        return TryGetMarketObserver(actor.Body, out var current) && current.User == actor.User &&
            current.Id == actor.Id && current.Revision == actor.Revision && current.Generation == actor.Generation &&
            current.Faction == actor.Faction && current.Role == actor.Role;
    }

    internal SWGlobalWarSupplyView? GetSupplyView(SWGlobalWarAssignment actor, int page = 0,
        SWGlobalWarSupplyKind selectedKind = SWGlobalWarSupplyKind.T2)
    {
        // Subject work is projected separately. It never gives workers the program's financial overview.
        if (!IsCurrentSupplyViewer(actor) || Simulation is not { } model || actor.Generation != model.Generation ||
            actor.Role is not (SWGlobalWarRole.Head or SWGlobalWarRole.WarMinister or
                SWGlobalWarRole.IndustryMinister or SWGlobalWarRole.LogisticsMinister or
                SWGlobalWarRole.IndustryWorker or SWGlobalWarRole.LogisticsWorker or
                SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer))
            return null;
        var program = model.SupplyPrograms[actor.Faction];
        var lines = new List<SWGlobalWarSupplyLineView>();
        foreach (var (kind, line) in program.Lines)
        {
            _supplyIntents.TryGetValue((actor.Faction, program.Window, kind), out var intent);
            program.Windows.TryGetValue(program.Window, out var window);
            var consumer = _supplyConsumers.Any(c => c.Kind == kind &&
                TryGetSupplyStore(actor.Generation, c.Store, actor.Faction, out _, out _));
            lines.Add(new(kind, program.Participants, SWGlobalWarSupplyProgram.Target(kind, program.Participants),
                CountAcceptedSupplyStock(actor.Faction, kind), program.Orders.Values.Where(o => o.Kind == kind)
                    .Sum(o => (long) o.Outstanding), program.Window, window?.Participants ?? 0,
                window?.Budgets[kind] ?? 0, program.RemainingQuota(kind), line.Revision, line.LocalPercent, line.Bacta,
                model.EffectiveSupplyRate(actor.Faction, kind), intent?.Id ?? Guid.Empty, intent?.Suspended ?? false,
                intent?.Failure ?? SWGlobalWarSupplyFailure.None, consumer));
        }
        var war = HasSupplyAuthority(actor, false, true);
        if (!Enum.IsDefined(selectedKind))
            selectedKind = SWGlobalWarSupplyKind.T2;
        var purchases = _supplyPurchases.Values.Where(p => p.Request.Generation == actor.Generation &&
            p.Request.Faction == actor.Faction && p.Request.Kind == selectedKind)
            .OrderByDescending(p => p.Obligation.Outstanding > 0)
            .ThenByDescending(p => p.Obligation.Window).ThenBy(p => p.Request.Operation).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (purchases.Length - 1) / SWGlobalWarMarketConsoleState.ShipmentPageSize));
        var orders = purchases.Skip(page * SWGlobalWarMarketConsoleState.ShipmentPageSize)
            .Take(SWGlobalWarMarketConsoleState.ShipmentPageSize).Select(p =>
            {
                var order = p.Obligation;
                SWGlobalWarSupplyBatchView? batch = null;
                if (p.Production is { } id && _supplyProduction.TryGetValue(id, out var work))
                    batch = SupplyBatchView(work, p, actor.Faction);
                return new SWGlobalWarSupplyOrderView(order.Id, order.Kind, p.Request.Bacta, order.Window,
                    order.Quantity, order.Accepted, order.Outstanding, order.Cancelled, order.Stage,
                    _supplyStores[p.Request.Source].Planet, _supplyStores[p.Request.Recipient].Planet, p.ReturnPending,
                    war ? _ledger.TryGetReservation(p.Reservation, out var held) ? held.Remaining : 0 : null,
                    batch, p.InitialReady, p.InitialProduction, p.ArsenalRevision,
                    p.AllowedArsenals.Select(a => a.Name).ToList(), p.Number, p.TransportHold,
                    Simulation!.Shipments.Where(s => s.SupplyManifest?.Purchase == order.Id).Select(s => s.Id).ToList());
            }).ToList();
        return new(lines, war, HasSupplyAuthority(actor, true, true),
            actor.Role == SWGlobalWarRole.Head && !HasSupplyAuthority(actor, false, false),
            actor.Role == SWGlobalWarRole.Head && !HasSupplyAuthority(actor, true, false),
            war ? _ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Available : null,
            war && _ledger.TryGetLedger(out var ledger) ? ledger.Comp.Revision : null,
            orders, purchases.Length, page, selectedKind,
            war ? _supplyStores.Keys.Where(e => TryGetSupplyStore(actor.Generation, e, actor.Faction, out _, out _))
                .Select(e => new SWGlobalWarSupplyStoreView(GetNetEntity(e), _supplyStores[e].Planet, Name(e),
                    _supplyStores[e].Arsenal)).ToList() : []);
    }

    private SWGlobalWarSupplyBatchView SupplyBatchView(SWGlobalWarSupplyProduction work,
        SWGlobalWarSupplyPurchase purchase, SWGlobalWarFaction viewer)
    {
        var planned = work.Stage is SWGlobalWarSupplyStage.Reserved or SWGlobalWarSupplyStage.Producing;
        var assigned = planned ? work.Purchase == purchase.Request.Operation ? purchase.ProductionQuantity : 0 :
            work.Output.Count(e => !TerminatingOrDeleted(e) && _supplyItems.TryGetValue(e, out var item) &&
                item.Order == purchase.Request.Operation && item.Owner == viewer);
        int? market = 0;
        int? local = 0;
        if (planned)
        {
            var allocation = work.Purchase is { } id ? _supplyPurchases[id].ProductionQuantity : 0;
            market = Math.Max(0, work.MarketQuantity - allocation);
            local = work.Quantity - work.MarketQuantity - Math.Max(0, allocation - work.MarketQuantity);
        }
        else if (work.Stage == SWGlobalWarSupplyStage.Complete)
        {
            if (!_supplyStores.TryGetValue(work.Request.Store, out var source) ||
                Simulation!.Markets[source.Planet].Owner != viewer)
                market = local = null;
            else
            {
                var output = work.Output.ToHashSet();
                foreach (var store in _supplyStores.Keys)
                {
                    if (!TryGetSupplyStore(work.Request.Generation, store, viewer, out var binding, out var inventory) || binding.Arsenal)
                        continue;
                    foreach (var entity in inventory.ContainedEntities)
                    {
                        if (!output.Contains(entity) || !_supplyItems.TryGetValue(entity, out var item) ||
                            item.Order != null || item.Payment != null || item.Accepted || item.Owner != viewer ||
                            item.Origin != SWGlobalWarSupplyOrigin.Industrial ||
                            !IsSuitableSupplyItem(entity, item.Kind, viewer, item.Bacta))
                            continue;
                        if (item.Market)
                            market++;
                        else
                            local++;
                    }
                }
            }
        }
        return new(work.Request.Operation, work.Quantity, work.MarketQuantity, work.Quantity - work.MarketQuantity,
            work.Duration, work.Stage, work.Purchase is { } linked && _supplyPurchases[linked].Obligation.Outstanding > 0
                ? linked : null, work.ReturnPending, assigned, market, local, planned);
    }

    internal List<SWGlobalWarSupplyResult> GetSupplyResults(SWGlobalWarAssignment actor)
    {
        if (!IsCurrentSupplyViewer(actor))
            return [];
        return _supplyRequests.Where(p => p.Key.User == actor.User && p.Value.Assignment == actor.Id)
            .Select(p => p.Value.Result).TakeLast(8).ToList();
    }

    private void ResetSupplyAuthority()
    {
        ResetSupplyWork();
        _supplyRequests.Clear();
        _supplyJournal.Clear();
    }
}

internal sealed record SWGlobalWarSupplyJournalEntry(Guid Generation, Guid Request, NetUserId Author,
    Guid Assignment, SWGlobalWarFaction Faction, SWGlobalWarRole Role, bool HeadDecision, SWGlobalWarSupplyAction Action,
    SWGlobalWarSupplyKind Kind, Guid Target, SWGlobalWarSupplySubjectSnapshot Before,
    SWGlobalWarSupplySubjectSnapshot After, TimeSpan At);

internal sealed record SWGlobalWarSupplySubjectSnapshot(bool Exists, SWGlobalWarSupplyStage? Stage = null,
    int Quantity = 0, int Accepted = 0, int Outstanding = 0, int Cancelled = 0, long ReservedPayment = 0,
    bool ReturnPending = false, bool Suspended = false, long Revision = 0, int LocalPercent = 0, bool Bacta = false,
    int InputReservations = 0);
