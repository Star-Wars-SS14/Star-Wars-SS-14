using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SharedTransformSystem _supplyTransforms = default!;
    [Dependency] private readonly SharedStorageSystem _supplyStorage = default!;
    private readonly Dictionary<Guid, SWGlobalWarSupplyPurchase> _supplyPurchases = new();
    private readonly Dictionary<Guid, SWGlobalWarSupplyAcceptance> _supplyAcceptances = new();

    /// <summary>
    /// Server consumer seam. The caller owns actor authorization; this boundary verifies real goods, reserves actual
    /// inputs for only the missing quantity, and binds both to one fixed-window obligation backed by War money.
    /// </summary>
    internal bool TryReserveSupplyPurchase(SWGlobalWarSupplyPurchaseRequest request, out SWGlobalWarSupplyPurchase purchase)
    {
        return TryReserveSupplyPurchase(request, out purchase, out _);
    }

    internal bool TryReserveSupplyPurchase(SWGlobalWarSupplyPurchaseRequest request, out SWGlobalWarSupplyPurchase purchase,
        out SWGlobalWarSupplyFailure failure)
    {
        return ReserveSupplyPurchase(request, out purchase, out failure, automatic: false);
    }

    private bool ReserveSupplyPurchase(SWGlobalWarSupplyPurchaseRequest request, out SWGlobalWarSupplyPurchase purchase,
        out SWGlobalWarSupplyFailure failure, bool automatic)
    {
        purchase = default!;
        failure = SWGlobalWarSupplyFailure.Unavailable;
        if (!CanMutateCampaign(request.Generation, automatic && _ownershipCatchUp) || request.Operation == Guid.Empty || request.Quantity is < 1 or > 120 ||
            !Enum.IsDefined(request.Kind) || request.Kind == SWGlobalWarSupplyKind.AdvancedElectronics ||
            !TryGetSupplyStore(request.Generation, request.Source, request.Faction, out var sourceBinding, out var source) ||
            sourceBinding.Arsenal ||
            !TryGetSupplyStore(request.Generation, request.Recipient, request.Faction, out var recipientBinding, out var recipient) ||
            !recipientBinding.Arsenal || !_ledger.TryGetLedger(out var ledger))
            return false;
        if (_supplyPurchases.TryGetValue(request.Operation, out purchase!))
        {
            var success = purchase.Request == request && !purchase.ReturnPending;
            if (success)
                failure = SWGlobalWarSupplyFailure.None;
            return success;
        }
        failure = SWGlobalWarSupplyFailure.TransportRoute;
        if (!HasSupplyPurchaseRoute(request))
            return false;
        var program = Simulation!.SupplyPrograms[request.Faction];
        failure = SWGlobalWarSupplyFailure.Quota;
        if (program.RemainingQuota(request.Kind) < request.Quantity)
            return false;
        failure = SWGlobalWarSupplyFailure.Revision;
        if (request.Bacta && request.Kind != SWGlobalWarSupplyKind.Medicine ||
            ledger.Comp.Revision != request.ObservedRevision)
            return false;
        failure = SWGlobalWarSupplyFailure.ReceiveSpace;
        if (!HasSupplyPlace(request.Recipient, request.Kind, request.Quantity, recipient))
            return false;
        var goods = source.ContainedEntities.Where(e => IsUnpaidSupplyItem(e, request)).Take(request.Quantity).ToArray();
        var missing = request.Quantity - goods.Length;
        var cost = (long) request.Quantity * SWGlobalWarSupplyProgram.UnitPrice(request.Kind, request.Bacta);
        failure = SWGlobalWarSupplyFailure.Funds;
        if (_ledger.GetBalance(request.Faction, SWGlobalWarAccount.War).Available < cost)
            return false;
        _settling = true;
        EntityUid? escrow = null;
        SWGlobalWarSupplyProduction? production = null;
        var completed = false;
        var notifications = _ledger.DeferNotifications();
        try
        {
            if (missing > 0)
            {
                var recipe = SupplyRecipe(request.Kind, request.Bacta);
                var line = program.Lines[request.Kind];
                failure = SWGlobalWarSupplyFailure.Revision;
                if (request.Bacta != (request.Kind == SWGlobalWarSupplyKind.Medicine && line.Bacta))
                    return false;
                failure = SWGlobalWarSupplyFailure.LocalOnly;
                if (!request.AllowLocal && line.LocalPercent == 100)
                    return false;
                var batches = request.AllowLocal ? (missing + recipe.Output - 1) / recipe.Output :
                    (int) decimal.Ceiling((missing - line.MarketRemainder) /
                        ((100m - line.LocalPercent) / 100m * recipe.Output));
                if (!ReserveSupplyProduction(new(request.Generation, Guid.NewGuid(), request.Source, request.Faction,
                        request.Kind, batches, request.Bacta), out production, out failure))
                    return false;
            }
            escrow = CreateSupplyEscrow();
            var held = _supplyContainers.EnsureContainer<Container>(escrow.Value, SupplyContainer);
            failure = SWGlobalWarSupplyFailure.Transfer;
            foreach (var item in goods)
                if (!_supplyContainers.Insert(item, held))
                    return false;
            failure = SWGlobalWarSupplyFailure.Funds;
            if (!_ledger.TryExecuteSystem(new(request.Generation, request.Operation, request.ObservedRevision,
                    SWGlobalWarLedgerAction.Reserve, request.Faction, SWGlobalWarAccount.War, cost,
                    Purpose: "supply-purchase"), out var reservation))
                return false;
            var obligation = new SWGlobalWarSupplyObligation(request.Operation, program.Window, request.Kind, request.Quantity)
            {
                Stage = missing == 0 ? SWGlobalWarSupplyStage.Ready : SWGlobalWarSupplyStage.Reserved,
            };
            if (!Simulation.AddSupplyObligation(request.Faction, obligation))
                throw new InvalidOperationException("Prepared supply obligation could not commit.");
            purchase = new(request, reservation, escrow.Value, obligation, goods)
            {
                Number = _supplyPurchases.Count + 1,
                Production = production?.Request.Operation, ProductionQuantity = missing,
                AwaitingPhysical = sourceBinding.Planet != recipientBinding.Planet &&
                    HasComp<Content.Server._SW.GlobalWar.Production.SWGlobalWarFactoryComponent>(request.Source),
            };
            purchase.AllowedArsenals.AddRange(SupplyArsenalPoints(request).Select(p => p.Value));
            if (production != null)
                production.Purchase = request.Operation;
            foreach (var item in goods)
                _supplyItems[item] = _supplyItems[item] with { Order = request.Operation };
            _supplyPurchases.Add(request.Operation, purchase);
            _supplyPlaces[(request.Recipient, request.Kind)] =
                _supplyPlaces.GetValueOrDefault((request.Recipient, request.Kind)) + request.Quantity;
            completed = true;
            failure = SWGlobalWarSupplyFailure.None;
            return true;
        }
        finally
        {
            try
            {
                if (!completed && production != null)
                    CancelSupplyProduction(production);
                if (!completed && escrow is { } failed)
                {
                    foreach (var item in goods)
                        if (!TerminatingOrDeleted(item) && !source.Contains(item))
                            _supplyContainers.Insert(item, source, force: true);
                    Del(failed);
                }
            }
            finally
            {
                try
                {
                    notifications.Dispose();
                }
                finally
                {
                    _settling = false;
                }
            }
        }
    }

    private bool IsUnpaidSupplyItem(EntityUid entity, SWGlobalWarSupplyPurchaseRequest request)
    {
        return _supplyItems.TryGetValue(entity, out var item) && item.Generation == request.Generation &&
            item.Kind == request.Kind && item.Bacta == request.Bacta && item.Owner == request.Faction &&
            (item.Market || request.AllowLocal) &&
            item.Origin == SWGlobalWarSupplyOrigin.Industrial && item.Payment == null && !item.Accepted && item.Order == null &&
            IsSuitableSupplyItem(entity, request.Kind, request.Faction, request.Bacta);
    }

    /// <summary>Hands only the specified reserved goods to a real storage container at the source.</summary>
    internal bool TryCollectSupplyPurchase(Guid generation, Guid operation, EntityUid carrier, IReadOnlyList<EntityUid> goods)
    {
        if (!CanMutateCampaign(generation) || !_supplyPurchases.TryGetValue(operation, out var purchase) ||
            IsInterplanetSupplyPurchase(purchase) && !HasPhysicalSupplyCarrier(purchase) ||
            purchase.Request.Generation != generation || purchase.ReturnPending || goods.Count == 0 ||
            goods.Distinct().Count() != goods.Count ||
            !TryGetSupplyStore(generation, purchase.Request.Source, purchase.Request.Faction, out _, out _) ||
            !IsSupplyCarrierContainer(carrier) || !SupplyAtStore(carrier, purchase.Request.Source) ||
            !_supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out var held) ||
            goods.Any(e => !purchase.Goods.Contains(e) || !_supplyItems.TryGetValue(e, out var item) ||
                item.Order != operation || (!held.Contains(e) &&
                    (!purchase.Carriers.TryGetValue(e, out var previous) || previous != carrier || !SupplyCarrierHolds(carrier, e)))) ||
            !TryCloseSupplyCarrier(carrier))
            return false;
        _settling = true;
        var moved = new List<EntityUid>();
        try
        {
            foreach (var item in goods.Where(held.Contains))
            {
                if (!TryLoadSupplyCarrier(carrier, item))
                {
                    foreach (var rollback in moved)
                        _supplyContainers.Insert(rollback, held, force: true);
                    return false;
                }
                moved.Add(item);
            }
            if (!RecordPhysicalCollection(purchase, carrier, goods))
            {
                foreach (var rollback in moved)
                    _supplyContainers.Insert(rollback, held, force: true);
                return false;
            }
            foreach (var item in goods)
                purchase.Carriers[item] = carrier;
            RefreshSupplyPurchaseStage(purchase);
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    /// <summary>Transfers War to Industry only after the exact physical tranche fits in its actual receiving arsenal.</summary>
    internal bool TryAcceptSupplyPurchase(Guid generation, Guid operation, Guid purchaseId, long observedRevision,
        IReadOnlyList<EntityUid> goods)
    {
        return AcceptSupplyPurchase(generation, operation, purchaseId, observedRevision, goods, null);
    }

    private bool AcceptSupplyPurchase(Guid generation, Guid operation, Guid purchaseId, long observedRevision,
        IReadOnlyList<EntityUid> goods, SWGlobalWarShipment? shipment)
    {
        if (!CanMutateCampaign(generation, shipment != null && _ownershipCatchUp) || operation == Guid.Empty ||
            !_supplyPurchases.TryGetValue(purchaseId, out var purchase) || purchase.Request.Generation != generation ||
            shipment == null && IsInterplanetSupplyPurchase(purchase) && !purchase.AwaitingPhysical ||
            purchase.ReturnPending)
            return false;
        if (_supplyAcceptances.TryGetValue(operation, out var receipt))
            return receipt.Generation == generation && receipt.Purchase == purchaseId &&
                receipt.ObservedRevision == observedRevision && receipt.Goods.SequenceEqual(goods);
        if ((shipment?.SupplyManifest?.Recipient ?? PhysicalFreightDestination(purchase, goods)) is not { } destination ||
            !TryGetSupplyStore(generation, destination, purchase.Request.Faction, out var binding, out var recipient) ||
            (shipment != null || purchase.AwaitingPhysical) && !AllowedSupplyArsenal(purchase, binding))
            return false;
        if (shipment == null && purchase.AwaitingPhysical && !CanAcceptPhysicalFreight(purchase, goods, destination))
            return false;
        if (goods.Count == 0 || goods.Count > purchase.Obligation.Outstanding || goods.Distinct().Count() != goods.Count ||
            !_ledger.TryGetLedger(out var ledger) || ledger.Comp.Revision != observedRevision ||
            goods.Any(e => !purchase.Goods.Contains(e) || !_supplyItems.TryGetValue(e, out var item) ||
                item.Order != purchaseId || item.Payment != null || item.Accepted || item.Owner != purchase.Request.Faction ||
                item.Origin != SWGlobalWarSupplyOrigin.Industrial ||
                !IsSuitableSupplyItem(e, purchase.Request.Kind, purchase.Request.Faction, purchase.Request.Bacta) ||
                (shipment != null ? !IsPhysicalSupplyInCustody(shipment, e) :
                !purchase.Carriers.TryGetValue(e, out var carrier) || !SupplyAtStore(carrier, destination) ||
                !SupplyCarrierHolds(carrier, e))))
            return false;
        var cost = (long) goods.Count * SWGlobalWarSupplyProgram.UnitPrice(purchase.Request.Kind, purchase.Request.Bacta);
        var positions = goods.Where(_ => shipment == null).ToDictionary(e => e, e =>
            (Carrier: purchase.Carriers[e], Location: SupplyCarrierPosition(purchase.Carriers[e], e)));
        _settling = true;
        var moved = new List<EntityUid>();
        var completed = false;
        var notifications = _ledger.DeferNotifications();
        try
        {
            foreach (var item in goods)
            {
                if (recipient.Contains(item))
                    continue;
                if (!_supplyContainers.Insert(item, recipient))
                    return false;
                moved.Add(item);
            }
            if (!_ledger.TryExecuteSystem(new(generation, operation, observedRevision,
                    SWGlobalWarLedgerAction.CommitTransfer, purchase.Request.Faction, SWGlobalWarAccount.War, cost,
                    SWGlobalWarAccount.Industry, purchase.Reservation, Purpose: "supply-accepted"), out _))
                return false;
            foreach (var item in goods)
            {
                _supplyItems[item] = _supplyItems[item] with { Accepted = true, Payment = operation, Order = null };
                purchase.Carriers.Remove(item);
            }
            purchase.Obligation.Accepted += goods.Count;
            RefreshSupplyPurchaseStage(purchase);
            if (shipment?.SupplyManifest is { } manifest)
            {
                ReleaseManifestPlace(manifest, purchase.Request.Kind);
                manifest.Stored = false;
            }
            else
                _supplyPlaces[(destination, purchase.Request.Kind)] -= goods.Count;
            _supplyAcceptances.Add(operation, new(generation, purchaseId, observedRevision, goods.ToArray(), cost));
            if (shipment == null && purchase.AwaitingPhysical)
                Simulation!.CompletePhysicalSupplyItems(generation, purchaseId, goods);
            completed = true;
            return true;
        }
        finally
        {
            try
            {
                if (!completed)
                    foreach (var item in moved)
                    {
                        if (shipment?.SupplyManifest?.Custody is { } custody)
                        {
                            if (!_supplyContainers.TryGetContainer(custody, SupplyContainer, out var held) ||
                                !_supplyContainers.Insert(item, held, force: true))
                                throw new InvalidOperationException("Physical supply custody could not be restored.");
                            continue;
                        }
                        var original = positions[item];
                        if (!TryRestoreSupplyCarrier(original.Carrier, item, original.Location))
                            throw new InvalidOperationException("Prepared supply storage position could not be restored.");
                    }
            }
            finally
            {
                try
                {
                    notifications.Dispose();
                }
                finally
                {
                    _settling = false;
                }
            }
        }
    }

    private bool SupplyAtStore(EntityUid carrier, EntityUid store)
    {
        return !TerminatingOrDeleted(carrier) && !TerminatingOrDeleted(store) &&
            Transform(carrier).MapUid == Transform(store).MapUid &&
            (_supplyTransforms.GetWorldPosition(carrier) - _supplyTransforms.GetWorldPosition(store)).LengthSquared() <= 4;
    }

    private void RefreshSupplyPurchaseStage(SWGlobalWarSupplyPurchase purchase)
    {
        purchase.Obligation.Stage = purchase.Obligation.Outstanding == 0 ? SWGlobalWarSupplyStage.Complete :
            purchase.Carriers.Count > 0 ? SWGlobalWarSupplyStage.InTransit :
            purchase.ProductionQuantity == 0 ? SWGlobalWarSupplyStage.Ready :
            purchase.Production is { } production && _supplyProduction[production].Stage == SWGlobalWarSupplyStage.Producing
                ? SWGlobalWarSupplyStage.Producing : SWGlobalWarSupplyStage.Reserved;
    }

    /// <summary>Cancels only the unaccepted remainder; in-transit physical goods remain where they actually are.</summary>
    internal bool TryCancelSupplyPurchase(Guid generation, Guid operation, EntityUid? returnStore = null)
    {
        if (!CanMutateCampaign(generation) || !_supplyPurchases.TryGetValue(operation, out var purchase) ||
            purchase.Request.Generation != generation || !_ledger.TryGetLedger(out _))
            return false;
        SuspendSupplyIntent(purchase);
        _settling = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            return CancelSupplyPurchase(purchase, returnStore);
        }
        finally
        {
            _settling = false;
        }
    }

    private bool CancelSupplyPurchase(SWGlobalWarSupplyPurchase purchase, EntityUid? returnStore = null)
    {
        var request = purchase.Request;
        if (purchase.Obligation.Outstanding == 0)
            return true;
        purchase.ReturnPending = true;
        var production = purchase.Production is { } productionId ? _supplyProduction[productionId] : null;
        if (production is { Stage: SWGlobalWarSupplyStage.Reserved } && !CancelSupplyProduction(production, returnStore))
            return false;
        if (_supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out var held) && held.ContainedEntities.Count > 0)
        {
            if (!_supplyStores.TryGetValue(request.Source, out var original) ||
                !TryGetSupplyStore(request.Generation, returnStore ?? request.Source, Simulation!.Markets[original.Planet].Owner,
                    out var restored, out var source) || restored.Planet != original.Planet || restored.Coordinates != original.Coordinates)
                return false;
            foreach (var item in held.ContainedEntities.ToArray())
                if (!_supplyContainers.Insert(item, source))
                    return false;
        }
        if (_ledger.TryGetReservation(purchase.Reservation, out _) &&
            !_ledger.TryExecuteSystem(new(request.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Release,
                request.Faction, SWGlobalWarAccount.War, Reservation: purchase.Reservation, Purpose: "supply-cancelled"), out _))
            return false;
        // An already consumed recipe still owns its output space and time. Cancelling its buyer only detaches demand;
        // explicit production cancellation, destruction, capture and round cleanup retain their separate meanings.
        if (production is { Stage: SWGlobalWarSupplyStage.Producing })
        {
            production.Purchase = null;
            purchase.ProductionQuantity = 0;
        }
        foreach (var entity in purchase.Goods)
            if (_supplyItems.TryGetValue(entity, out var item) && item.Order == request.Operation)
                _supplyItems[entity] = item with { Order = null };
        ReleasePurchasePlaces(purchase);
        Simulation!.CloseCancelledStoredSupply(request.Generation, request.Operation, request.Faction);
        purchase.Obligation.Cancelled += purchase.Obligation.Outstanding;
        purchase.Obligation.Stage = SWGlobalWarSupplyStage.Cancelled;
        purchase.ReturnPending = false;
        return true;
    }

    private void ResetSupplyPurchases()
    {
        _supplyPurchases.Clear();
        _supplyAcceptances.Clear();
    }
}

public sealed record SWGlobalWarSupplyPurchaseRequest(Guid Generation, Guid Operation, long ObservedRevision,
    SWGlobalWarFaction Faction, EntityUid Source, EntityUid Recipient, SWGlobalWarSupplyKind Kind, int Quantity,
    bool Bacta = false, bool AllowLocal = false, Guid Intent = default);

public sealed class SWGlobalWarSupplyPurchase(SWGlobalWarSupplyPurchaseRequest request, Guid reservation, EntityUid escrow,
    SWGlobalWarSupplyObligation obligation, IReadOnlyList<EntityUid> goods)
{
    public SWGlobalWarSupplyPurchaseRequest Request { get; } = request;
    public int Number { get; internal set; }
    public Guid Reservation { get; } = reservation;
    public EntityUid Escrow { get; } = escrow;
    public SWGlobalWarSupplyObligation Obligation { get; } = obligation;
    public List<EntityUid> Goods { get; } = goods.ToList();
    public int InitialReady { get; } = goods.Count;
    public int InitialProduction { get; } = request.Quantity - goods.Count;
    public Guid? Production { get; internal set; }
    public int ProductionQuantity { get; internal set; }
    public Dictionary<EntityUid, EntityUid> Carriers { get; } = new();
    public bool ReturnPending { get; internal set; }
    public List<SWGlobalWarSupplyStore> AllowedArsenals { get; } = new();
    public long ArsenalRevision { get; internal set; }
    public string TransportHold { get; internal set; } = string.Empty;
    /// <summary>Physical E cargo is excluded from virtual dispatch/arrival and requires verified flight and unloading.</summary>
    public bool AwaitingPhysical { get; internal set; }
}

public sealed record SWGlobalWarSupplyAcceptance(Guid Generation, Guid Purchase, long ObservedRevision,
    IReadOnlyList<EntityUid> Goods, long Paid);
