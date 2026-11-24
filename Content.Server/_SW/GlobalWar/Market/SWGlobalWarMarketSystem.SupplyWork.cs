using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SharedHandsSystem _supplyHands = default!;
    [Dependency] private readonly PullingSystem _supplyPulling = default!;
    private readonly Dictionary<Guid, SWGlobalWarSupplyWork> _supplyWork = new();
    private readonly Dictionary<(NetUserId, Guid), (Guid Assignment, SWGlobalWarSupplyWorkCommand Command,
        SWGlobalWarSupplyWorkResult Result)> _supplyWorkRequests = new();
    private readonly List<SWGlobalWarSupplyWorkJournalEntry> _supplyWorkJournal = new();
    internal IReadOnlyList<SWGlobalWarSupplyWorkJournalEntry> SupplyWorkJournal => _supplyWorkJournal;

    /// <summary>Executes a published delivery without granting account or production authority to the worker.</summary>
    public bool TryExecuteSupplyWork(ICommonSession session, SWGlobalWarSupplyWorkCommand command,
        out SWGlobalWarSupplyWorkResult result)
    {
        result = new(command?.Request ?? Guid.Empty, false, SWGlobalWarSupplyFailure.Denied);
        if (command == null || command.Request == Guid.Empty || !Enum.IsDefined(command.Action) ||
            !CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out var actor))
            return false;
        RefreshSupplyWorkAuthority();
        if (!CanExecuteSupplyWork(actor, command))
            return false;
        var key = (actor.User, command.Request);
        if (_supplyWorkRequests.TryGetValue(key, out var old))
        {
            if (old.Assignment != actor.Id || old.Command != command || old.Result.Success &&
                _supplyWork.TryGetValue(old.Result.Work, out var oldWork) && oldWork.Status == SWGlobalWarSupplyWorkStatus.Revoked)
                return false;
            result = old.Result;
            return result.Success;
        }
        Update(0);
        RefreshSupplyWorkAuthority();
        if (!CanMutateCampaign(command.Generation) ||
            !_assignments.TryGetActor(session, command.Generation, command.AssignmentRevision, out actor) ||
            !CanExecuteSupplyWork(actor, command))
            return false;
        var notifications = _ledger.DeferNotifications();
        try
        {
            result = ExecuteSupplyWork(actor, command);
            _supplyWorkRequests.Add(key, (actor.Id, command, result));
            return result.Success;
        }
        finally
        {
            var wasSettling = _settling;
            _settling = true;
            try { notifications.Dispose(); }
            finally { _settling = wasSettling; }
        }
    }

    private bool CanExecuteSupplyWork(SWGlobalWarAssignment actor, SWGlobalWarSupplyWorkCommand command)
    {
        if (command.Action == SWGlobalWarSupplyWorkAction.Claim)
            return _supplyPurchases.TryGetValue(command.Target, out var purchase) && purchase.Request.Faction == actor.Faction &&
                (!IsInterplanetSupplyPurchase(purchase) || HasPhysicalSupplyCarrier(purchase));
        if (!_supplyWork.TryGetValue(command.Target, out var work) ||
            work.Generation != actor.Generation || work.Faction != actor.Faction)
            return false;
        if (command.Action == SWGlobalWarSupplyWorkAction.Revoke)
            return HasLogisticsAuthority(actor, command.HeadDecision);
        return work.User == actor.User && work.Assignment == actor.Id && work.Status != SWGlobalWarSupplyWorkStatus.Revoked;
    }

    private SWGlobalWarSupplyWorkResult ExecuteSupplyWork(SWGlobalWarAssignment actor, SWGlobalWarSupplyWorkCommand command)
    {
        var failed = new SWGlobalWarSupplyWorkResult(command.Request, false, SWGlobalWarSupplyFailure.Unavailable);
        if (command.Action == SWGlobalWarSupplyWorkAction.Claim)
        {
            if (command.Quantity is < 1 or > 120)
                return failed with { Failure = SWGlobalWarSupplyFailure.InvalidQuantity };
            if (_supplyWork.ContainsKey(command.Request))
                return failed;
            if (_supplyWork.Values.Any(w => w.Assignment == actor.Id && ActiveSupplyWork(w)))
                return failed with { Failure = SWGlobalWarSupplyFailure.WorkBusy };
            var purchase = _supplyPurchases[command.Target];
            if (purchase.ReturnPending)
                return failed with { Failure = SWGlobalWarSupplyFailure.ReturnPending };
            if (command.Carrier == null && !TryGetSupplyStore(actor.Generation, purchase.Request.Source, actor.Faction, out _, out _) ||
                !purchase.AwaitingPhysical && !TryGetSupplyStore(actor.Generation, purchase.Request.Recipient, actor.Faction, out _, out _))
                return failed;
            var carrier = command.Carrier is { } net ? GetEntity(net) : (EntityUid?) null;
            var goods = AvailableDeliveryGoods(actor, purchase, carrier).Take(command.Quantity).ToArray();
            if (goods.Length != command.Quantity)
                return failed with { Failure = SWGlobalWarSupplyFailure.QuantityChanged };
            if (PhysicalFreightDestination(purchase, goods) is not { } destination ||
                !TryGetSupplyStore(actor.Generation, destination, actor.Faction, out _, out _))
                return failed with { Failure = SWGlobalWarSupplyFailure.QuantityChanged };
            var work = new SWGlobalWarSupplyWork(command.Request, actor, purchase.Request.Operation, goods, carrier, Name(actor.Body));
            // A new claim may adopt the same order items from their actual carrier after a real transfer.
            // Eligibility above excludes paid items and items protected by another active claim.
            if (carrier is { } actualCarrier)
                foreach (var item in goods)
                    purchase.Carriers[item] = actualCarrier;
            _supplyWork.Add(work.Id, work);
            RecordSupplyWork(actor, command, work, null);
            return new(command.Request, true, SWGlobalWarSupplyFailure.None, work.Id);
        }
        var current = _supplyWork[command.Target];
        if (!ActiveSupplyWork(current))
            return failed;
        var before = current.Status;
        if (command.Action is SWGlobalWarSupplyWorkAction.Release or SWGlobalWarSupplyWorkAction.Revoke)
        {
            // Releasing authority does not cancel the state purchase or move cargo out of a real carrier.
            current.Status = command.Action == SWGlobalWarSupplyWorkAction.Release ?
                SWGlobalWarSupplyWorkStatus.Released : SWGlobalWarSupplyWorkStatus.Revoked;
        }
        else
        {
            if (_supplyPurchases.TryGetValue(current.Purchase, out var pendingReturn) && pendingReturn.ReturnPending)
                return failed with { Failure = SWGlobalWarSupplyFailure.ReturnPending };
            if (!_supplyPurchases.TryGetValue(current.Purchase, out var purchase) ||
                purchase.Obligation.Outstanding == 0 || purchase.ReturnPending ||
                command.Carrier is not { } carrierId || !IsSupplyCarrier(actor.Body, GetEntity(carrierId)))
                return failed with { Failure = SWGlobalWarSupplyFailure.Transfer };
            var held = GetEntity(carrierId);
            if (command.Action == SWGlobalWarSupplyWorkAction.Collect)
            {
                if (current.Status != SWGlobalWarSupplyWorkStatus.Assigned ||
                    !CanReachSupplyStore(actor.Body, purchase.Request.Source) ||
                    !TryCollectSupplyPurchase(command.Generation, current.Purchase, held, current.Goods))
                    return failed with { Failure = SWGlobalWarSupplyFailure.Transfer };
                current.Carrier = held;
                current.Status = SWGlobalWarSupplyWorkStatus.Collected;
            }
            else if (command.Action == SWGlobalWarSupplyWorkAction.Accept)
            {
                if (current.Status != SWGlobalWarSupplyWorkStatus.Collected || current.Carrier != held ||
                    PhysicalFreightDestination(purchase, current.Goods) is not { } destination ||
                    !CanReachSupplyStore(actor.Body, destination) || !_ledger.TryGetLedger(out var ledger) ||
                    !TryAcceptSupplyPurchase(command.Generation, command.Request, current.Purchase,
                        ledger.Comp.Revision, current.Goods))
                    return failed with { Failure = SWGlobalWarSupplyFailure.Transfer };
                current.Status = SWGlobalWarSupplyWorkStatus.Completed;
                var delivered = new SWGlobalWarDeliveryCompletedEvent(actor.User);
                RaiseLocalEvent(ref delivered);
            }
            else
                return failed;
        }
        RecordSupplyWork(actor, command, current, before);
        return new(command.Request, true, SWGlobalWarSupplyFailure.None, current.Id);
    }

    private bool CanReachSupplyStore(EntityUid body, EntityUid store)
    {
        return SupplyAtStore(body, store) && _hubBlocker.CanInteract(body, store) &&
            _hubInteraction.InRangeUnobstructed(body, store, range: 2f);
    }

    private EntityUid[] AvailableDeliveryGoods(SWGlobalWarAssignment actor, SWGlobalWarSupplyPurchase purchase,
        EntityUid? carrier)
    {
        if (purchase.Request.Generation != actor.Generation || purchase.Request.Faction != actor.Faction ||
            IsInterplanetSupplyPurchase(purchase) && !HasPhysicalSupplyCarrier(purchase) ||
            carrier == null && purchase.AwaitingPhysical && !HasSupplyPurchaseRoute(purchase.Request) ||
            purchase.Obligation.Outstanding == 0 || purchase.ReturnPending ||
            carrier == null && !TryGetSupplyStore(actor.Generation, purchase.Request.Source, actor.Faction, out _, out _) ||
            !purchase.AwaitingPhysical && !TryGetSupplyStore(actor.Generation, purchase.Request.Recipient, actor.Faction, out _, out _) ||
            carrier == null && !_supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out _))
            return [];
        _supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out var escrow);
        if (carrier is { } held && !IsSupplyCarrier(actor.Body, held))
            return [];
        return purchase.Goods.Where(e => !TerminatingOrDeleted(e) && _supplyItems.TryGetValue(e, out var item) &&
            item.Order == purchase.Request.Operation && item.Owner == actor.Faction && !item.Accepted && item.Payment == null &&
            item.Origin == SWGlobalWarSupplyOrigin.Industrial &&
            !_supplyWork.Values.Any(w => ActiveSupplyWork(w) && w.Goods.Contains(e)) &&
            (carrier == null ? escrow!.Contains(e) : SupplyCarrierHolds(carrier.Value, e)))
            .OrderBy(e => e.Id).ToArray();
    }

    private bool IsSupplyCarrier(EntityUid body, EntityUid carrier)
    {
        return !TerminatingOrDeleted(carrier) && IsSupplyCarrierContainer(carrier) &&
            (_supplyHands.GetActiveItem(body) == carrier ||
                _supplyPulling.GetPulling(body) == carrier && _supplyPulling.GetPuller(carrier) == body) &&
            _hubBlocker.CanInteract(body, carrier) && _hubInteraction.InRangeUnobstructed(body, carrier, range: 2f);
    }

    private EntityUid[] SupplyCarriers(EntityUid body)
    {
        return new[] { _supplyHands.GetActiveItem(body), _supplyPulling.GetPulling(body) }
            .Where(e => e != null && IsSupplyCarrier(body, e.Value)).Select(e => e!.Value).Distinct().ToArray();
    }

    private static bool ActiveSupplyWork(SWGlobalWarSupplyWork work)
    {
        return work.Status is SWGlobalWarSupplyWorkStatus.Assigned or SWGlobalWarSupplyWorkStatus.Collected;
    }

    private void RefreshSupplyWorkAuthority()
    {
        foreach (var work in _supplyWork.Values.Where(ActiveSupplyWork))
            if (!_assignments.TryGetAssignment(work.User, out var current) || current.Id != work.Assignment ||
                !IsCurrentSupplyViewer(current) || !_supplyPurchases.TryGetValue(work.Purchase, out var purchase) ||
                purchase.Obligation.Outstanding == 0)
                work.Status = SWGlobalWarSupplyWorkStatus.Revoked;
    }

    private void RecordSupplyWork(SWGlobalWarAssignment actor, SWGlobalWarSupplyWorkCommand command,
        SWGlobalWarSupplyWork work, SWGlobalWarSupplyWorkStatus? before)
    {
        _supplyWorkJournal.Add(new(command, actor.User, actor.Id, actor.Role, before, work.Status, Simulation!.Time));
        if (actor.Role == SWGlobalWarRole.Head && command.HeadDecision &&
            _assignments.TryGetRegistry(out var registry))
            foreach (var minister in registry.Comp.Assignments.Values.Where(a =>
                         a.Faction == actor.Faction && a.Role == SWGlobalWarRole.LogisticsMinister))
                if (TryComp<ActorComponent>(minister.Body, out var player))
                    _economyChat.DispatchServerMessage(player.PlayerSession, Loc.GetString("sw-global-war-supply-work-head-notice"));
    }

    internal SWGlobalWarSupplyWorkBoard? GetSupplyWorkBoard(SWGlobalWarAssignment actor, int page = 0)
    {
        if (!IsCurrentSupplyViewer(actor))
            return null;
        RefreshSupplyWorkAuthority();
        var deliveries = new List<SWGlobalWarSupplyDeliveryView>();
        var carriers = SupplyCarriers(actor.Body);
        foreach (var purchase in _supplyPurchases.Values.OrderBy(p => p.Request.Operation))
        foreach (var carrier in new EntityUid?[] { null }.Concat(carriers.Select(e => (EntityUid?) e)))
        {
            var available = AvailableDeliveryGoods(actor, purchase, carrier);
            var count = available.Length;
            var destination = PhysicalFreightDestination(purchase, available) ?? purchase.Request.Recipient;
            if (count > 0)
                deliveries.Add(new(purchase.Request.Operation, purchase.Request.Kind, purchase.Request.Bacta, count,
                    _supplyStores[purchase.Request.Source].Planet, _supplyStores[destination].Planet,
                    carrier is { } cargo ? GetNetEntity(cargo) : null, purchase.Number,
                    SupplyWorkPointName(purchase.Request.Source), SupplyWorkPointName(destination)));
        }
        var manage = HasLogisticsAuthority(actor, true);
        var own = _supplyWork.Values.Where(w => w.Generation == actor.Generation && w.Assignment == actor.Id).Reverse();
        var lastResult = _supplyWorkRequests.Where(p => p.Key.Item1 == actor.User && p.Value.Assignment == actor.Id &&
            p.Value.Result.Success).Select(p => p.Value.Result.Work).LastOrDefault();
        var visibleWork = own.Where(w => ActiveSupplyWork(w) || w.Id == lastResult)
            .Concat(own.Where(w => !ActiveSupplyWork(w)).Take(8)).DistinctBy(w => w.Id);
        if (manage)
        {
            if (_supplyWork.TryGetValue(lastResult, out var addressed) && addressed.Generation == actor.Generation &&
                addressed.Faction == actor.Faction && addressed.Assignment != actor.Id)
                visibleWork = visibleWork.Append(addressed);
            visibleWork = visibleWork.Concat(_supplyWork.Values.Where(w => w.Generation == actor.Generation &&
                w.Faction == actor.Faction && w.Assignment != actor.Id && ActiveSupplyWork(w)).Take(40));
        }
        var visible = visibleWork.DistinctBy(w => w.Id)
            .Select(w =>
            {
                var p = _supplyPurchases[w.Purchase];
                var destination = PhysicalFreightDestination(p, w.Goods) ?? p.Request.Recipient;
                return new SWGlobalWarSupplyWorkView(w.Id, p.Request.Kind, p.Request.Bacta, w.Goods.Length,
                    _supplyStores[p.Request.Source].Planet, _supplyStores[destination].Planet, w.Status,
                    w.Assignment == actor.Id, w.Worker, p.Request.Operation, p.Number,
                    SupplyWorkPointName(p.Request.Source), SupplyWorkPointName(destination), p.ReturnPending);
            }).ToList();
        page = Math.Clamp(page, 0, Math.Max(0, (deliveries.Count - 1) / 40));
        return new(deliveries.Skip(page * 40).Take(40).ToList(), visible, manage,
            actor.Role == SWGlobalWarRole.Head && !HasLogisticsAuthority(actor, false), deliveries.Count, page,
            carriers.Select(e => new SWGlobalWarSupplyCarrierView(GetNetEntity(e), Name(e))).ToList());
    }

    internal List<SWGlobalWarSupplyWorkResult> GetSupplyWorkResults(SWGlobalWarAssignment actor)
    {
        return !IsCurrentSupplyViewer(actor) ? [] : _supplyWorkRequests.Where(p => p.Key.Item1 == actor.User &&
            p.Value.Assignment == actor.Id).Select(p => p.Value.Result).TakeLast(8).ToList();
    }

    private string SupplyWorkPointName(EntityUid point)
    {
        return TryComp<MetaDataComponent>(point, out var metadata) ? metadata.EntityName :
            _supplyStores.TryGetValue(point, out var store) ? store.Name : Loc.GetString("sw-global-war-supply-work-point-lost");
    }

    private void ResetSupplyWork()
    {
        _supplyWork.Clear();
        _supplyWorkRequests.Clear();
        _supplyWorkJournal.Clear();
    }
}

internal sealed class SWGlobalWarSupplyWork(Guid id, SWGlobalWarAssignment actor, Guid purchase,
    EntityUid[] goods, EntityUid? carrier, string worker)
{
    public readonly Guid Id = id;
    public readonly Guid Generation = actor.Generation;
    public readonly NetUserId User = actor.User;
    public readonly Guid Assignment = actor.Id;
    public readonly SWGlobalWarFaction Faction = actor.Faction;
    public readonly Guid Purchase = purchase;
    public readonly EntityUid[] Goods = goods;
    public readonly string Worker = worker;
    public EntityUid? Carrier = carrier;
    public SWGlobalWarSupplyWorkStatus Status = carrier == null ?
        SWGlobalWarSupplyWorkStatus.Assigned : SWGlobalWarSupplyWorkStatus.Collected;
}

internal sealed record SWGlobalWarSupplyWorkJournalEntry(SWGlobalWarSupplyWorkCommand Command, NetUserId Author,
    Guid Assignment, SWGlobalWarRole Role, SWGlobalWarSupplyWorkStatus? Before, SWGlobalWarSupplyWorkStatus After, TimeSpan At);
