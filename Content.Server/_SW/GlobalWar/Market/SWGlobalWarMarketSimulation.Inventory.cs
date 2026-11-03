using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly Dictionary<Guid, SWGlobalWarStockReservation> _stockReservations = new();
    private readonly Dictionary<Guid, SWGlobalWarStockReservation> _consumptionReservations = new();
    private readonly HashSet<(string Planet, Guid Operation)> _ownershipReceipts = new();

    public int InternalReservationCount => _consumptionReservations.Count;
    public int InternalPendingReservationCount => _consumptionReservations.Values.Count(r =>
        r.Status == SWGlobalWarStockReservationStatus.Pending);
    public int ExternalReservationCount => _stockReservations.Count;
    public int ExternalPendingReservationCount => _stockReservations.Values.Count(r =>
        r.Status == SWGlobalWarStockReservationStatus.Pending);

    internal bool HasPendingStockReservation(Guid generation, Guid operation)
    {
        return Generation == generation && _stockReservations.TryGetValue(operation, out var reservation) &&
               reservation.Status == SWGlobalWarStockReservationStatus.Pending;
    }

    /// <summary>
    /// Reserves existing quanta without making a second inventory. Retries must preserve the exact original request.
    /// The system owning this simulation additionally validates the campaign phase, actor and ownership closure.
    /// </summary>
    public bool TryReserveStock(SWGlobalWarStockRequest request, out SWGlobalWarStockReservation reservation)
    {
        return TryReserveStock(request, _stockReservations, out reservation);
    }

    private bool TryReserveStock(SWGlobalWarStockRequest request,
        Dictionary<Guid, SWGlobalWarStockReservation> reservations, out SWGlobalWarStockReservation reservation)
    {
        reservation = default!;
        if (Generation != request.Generation || request.Operation == Guid.Empty || request.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 200)
            return false;
        if (reservations.TryGetValue(request.Operation, out var previous))
        {
            reservation = previous;
            return previous.Request == request && previous.Status != SWGlobalWarStockReservationStatus.Cancelled;
        }
        if (!_markets.TryGetValue(request.Planet, out var market) || market.Owner != request.Owner ||
            !market.Goods.TryGetValue(request.Commodity, out var good) || good.MutableBatches == null ||
            good.Available < request.Quantity)
            return false;
        var remaining = request.Quantity;
        var parts = new List<SWGlobalWarStockPart>();
        foreach (var batch in good.MutableBatches.Values)
        {
            var quantity = Math.Min(remaining, batch.Quantity - batch.Reserved);
            if (quantity == 0)
                continue;
            parts.Add(new SWGlobalWarStockPart(batch.Id, quantity));
            remaining -= quantity;
            if (remaining == 0)
                break;
        }
        // The immutable batch records cannot change during this synchronous, callback-free commit.
        foreach (var part in parts)
        {
            var batch = good.MutableBatches[part.Batch];
            good.MutableBatches[part.Batch] = batch with { Reserved = batch.Reserved + part.Quantity };
        }
        reservation = new SWGlobalWarStockReservation(request, parts.AsReadOnly(),
            SWGlobalWarStockReservationStatus.Pending, []);
        reservations.Add(request.Operation, reservation);
        return true;
    }

    /// <summary>Commits a prepared withdrawal once and retains the exact withdrawn provenance for replay and D/E.</summary>
    public bool TryCommitStock(Guid generation, Guid operation, out SWGlobalWarStockReservation receipt)
    {
        return TryCommitStock(generation, operation, _stockReservations, true, out receipt);
    }

    private bool TryCommitStock(Guid generation, Guid operation,
        Dictionary<Guid, SWGlobalWarStockReservation> reservations, bool retainWithdrawn,
        out SWGlobalWarStockReservation receipt)
    {
        receipt = default!;
        if (Generation != generation || !reservations.TryGetValue(operation, out var pending) ||
            pending.Status == SWGlobalWarStockReservationStatus.Cancelled)
            return false;
        receipt = pending;
        if (pending.Status == SWGlobalWarStockReservationStatus.Committed)
            return true;
        var request = pending.Request;
        var good = _markets[request.Planet].Goods[request.Commodity];
        List<SWGlobalWarMarketBatch>? withdrawn = retainWithdrawn ? new() : null;
        foreach (var part in pending.Parts)
        {
            var batch = good.MutableBatches![part.Batch];
            // The new tranche has its own identity; a capture proof is narrowed to exactly that tranche.
            // Internal consumption destroys goods; no withdrawn tranche or new batch identity is needed.
            withdrawn?.Add(SliceBatch(batch, part.Quantity));
            var left = batch.Quantity - part.Quantity;
            if (left == 0)
                good.MutableBatches.Remove(batch.Id);
            else
                good.MutableBatches[batch.Id] = ResizeBatch(batch, left, batch.Reserved - part.Quantity);
        }
        receipt = pending with
        {
            Status = SWGlobalWarStockReservationStatus.Committed,
            Withdrawn = withdrawn == null ? [] : withdrawn.AsReadOnly(),
        };
        reservations[operation] = receipt;
        return true;
    }

    /// <summary>Cancels an unfinished reservation; completed transfers can never be released back into inventory.</summary>
    public bool TryCancelStock(Guid generation, Guid operation)
    {
        return TryCancelStock(generation, operation, _stockReservations);
    }

    private bool TryCancelStock(Guid generation, Guid operation,
        Dictionary<Guid, SWGlobalWarStockReservation> reservations)
    {
        if (Generation != generation || !reservations.TryGetValue(operation, out var pending) ||
            pending.Status == SWGlobalWarStockReservationStatus.Committed)
            return false;
        if (pending.Status == SWGlobalWarStockReservationStatus.Cancelled)
            return true;
        var good = _markets[pending.Request.Planet].Goods[pending.Request.Commodity];
        foreach (var part in pending.Parts)
        {
            var batch = good.MutableBatches![part.Batch];
            good.MutableBatches[part.Batch] = batch with { Reserved = batch.Reserved - part.Quantity };
        }
        reservations[operation] = pending with { Status = SWGlobalWarStockReservationStatus.Cancelled };
        return true;
    }

    /// <summary>
    /// Internal ownership closure. The caller has already committed the whole territory batch and must invoke this
    /// before arbitration or public callbacks. In-flight goods are never captured by changing their departure port.
    /// </summary>
    internal void CommitOwnershipBatch(Guid generation, IReadOnlyList<SWGlobalWarMarketOwnershipChange> changes,
        TimeSpan? at = null)
    {
        if (Generation != generation)
            return;
        if (_consumptionReservations.Count != 0)
            throw new InvalidOperationException("Ownership must close after the active consumption settlement.");
        foreach (var change in changes)
        {
            if (!_markets.TryGetValue(change.Planet, out var market) ||
                !_ownershipReceipts.Add((change.Planet, change.Operation)))
                continue;
            foreach (var pending in _stockReservations.Values.Where(r => r.Request.Planet == change.Planet &&
                         r.Status == SWGlobalWarStockReservationStatus.Pending).ToArray())
                TryCancelStock(generation, pending.Request.Operation);
            market.Owner = change.Owner;
            market.CapturedAt = at ?? Time;
            foreach (var good in market.Goods.Values)
            {
                if (good.MutableBatches == null)
                    continue;
                foreach (var batch in good.MutableBatches.Values.ToArray())
                    good.MutableBatches[batch.Id] = CaptureBatch(batch, change);
            }
            foreach (var shipment in _shipments)
            {
                if (shipment.ArrivesAt == null && shipment.Route[shipment.Leg] == change.Planet &&
                    shipment.SupplyManifest is { AwaitingPhysical: false } manifest && manifest.Owner != change.Owner)
                {
                    manifest.Capture = new(manifest.Generation, change.Operation, Guid.NewGuid(), change.Planet,
                        manifest.Id, manifest.Items.Count, manifest.Owner, change.Owner);
                    manifest.Owner = change.Owner;
                    shipment.DeliveryParticipant = null;
                    CaptureReturnPart(shipment);
                    shipment.ReturnCaptured = true;
                    CaptureSupplyManifest?.Invoke(shipment, change);
                }
                if (shipment.ArrivesAt == null && shipment.Route[shipment.Leg] == change.Planet &&
                    shipment.Batch is { } batch)
                {
                    shipment.Batch = CaptureBatch(batch, change);
                    if (batch.Owner != change.Owner)
                    {
                        shipment.DeliveryParticipant = null;
                        CaptureReturnPart(shipment);
                    }
                }
            }
        }
    }

    private static SWGlobalWarMarketBatch CaptureBatch(SWGlobalWarMarketBatch batch,
        SWGlobalWarMarketOwnershipChange change)
    {
        if (batch.Owner == change.Owner)
            return batch;
        return batch with
        {
            Owner = change.Owner,
            Capture = new SWGlobalWarTradeCaptureEvidence(batch.Generation, change.Operation, Guid.NewGuid(),
                change.Planet, batch.Id, batch.Quantity, batch.Owner, change.Owner),
        };
    }

    private static SWGlobalWarMarketBatch SliceBatch(SWGlobalWarMarketBatch batch, int quantity)
    {
        var id = Guid.NewGuid();
        return batch with
        {
            Id = id, Quantity = quantity, Reserved = 0,
            Capture = batch.Capture is { } proof ? proof with { Batch = id, Quantity = quantity } : null,
        };
    }

    private static SWGlobalWarMarketBatch ResizeBatch(SWGlobalWarMarketBatch batch, int quantity, int reserved = 0)
    {
        return batch with
        {
            Quantity = quantity, Reserved = reserved,
            Capture = batch.Capture is { } proof ? proof with { Quantity = quantity } : null,
        };
    }

    private static SWGlobalWarMarketBatch TakeBatch(SWGlobalWarMarketGoodState good,
        SWGlobalWarMarketBatch batch, int quantity)
    {
        var taken = SliceBatch(batch, quantity);
        if (quantity == batch.Quantity)
            good.MutableBatches!.Remove(batch.Id);
        else
            good.MutableBatches![batch.Id] = ResizeBatch(batch, batch.Quantity - quantity, batch.Reserved);
        return taken;
    }

    private static void AddBatch(SWGlobalWarMarketGoodState good, SWGlobalWarMarketBatch batch)
    {
        // Ordinary homogeneous tranches may coalesce. Captured tranches keep their separate quantity evidence.
        var merge = good.MutableBatches!.Values.FirstOrDefault(b => b.Origin == batch.Origin &&
            b.Owner == batch.Owner && b.Capture == null && batch.Capture == null && b.Reserved == 0);
        if (merge == null)
            good.MutableBatches.Add(batch.Id, batch);
        else
            good.MutableBatches[merge.Id] = merge with { Quantity = merge.Quantity + batch.Quantity };
    }

    private static SWGlobalWarShipment SplitShipment(SWGlobalWarShipment shipment, int quantity)
    {
        if (shipment.SupplyManifest != null &&
            (quantity <= 0 || quantity >= shipment.Quantity || quantity % SWGlobalWarMarketUnits.PerUnit != 0))
            throw new InvalidOperationException("Physical manifests can only split into nonempty whole-item parts.");
        var part = new SWGlobalWarShipment(shipment.Commodity, quantity, shipment.Route,
            shipment.DepartedAt, shipment.ArrivesAt ?? TimeSpan.Zero)
        {
            ArrivesAt = shipment.ArrivesAt, Leg = shipment.Leg, DepartureReceipt = shipment.DepartureReceipt,
            DeliveryParticipant = shipment.DeliveryParticipant,
            ReceivedAtNode = shipment.ReceivedAtNode, HoldReason = shipment.HoldReason,
            Flow = shipment.Flow,
            ReturnOperation = shipment.ReturnOperation,
            ReturnCaptured = shipment.ReturnCaptured,
        };
        shipment.Quantity -= quantity;
        if (shipment.Batch is { } batch)
        {
            part.Batch = SliceBatch(batch, quantity);
            shipment.Batch = ResizeBatch(batch, shipment.Quantity);
        }
        if (shipment.SupplyManifest is { } manifest)
        {
            var count = quantity / SWGlobalWarMarketUnits.PerUnit;
            var split = new SWGlobalWarSupplyManifest(manifest.Generation, manifest.Purchase,
                manifest.Origin, manifest.Owner, manifest.Items.Take(count).ToArray())
            {
                Custody = manifest.Custody, Recipient = manifest.Recipient,
                SpaceReserved = manifest.SpaceReserved, Stored = manifest.Stored,
                ArrivalRecorded = manifest.ArrivalRecorded,
                AwaitingPhysical = manifest.AwaitingPhysical,
            };
            manifest.Items = manifest.Items.Skip(count).ToArray();
            if (manifest.Capture is { } proof)
            {
                split.Capture = proof with { Batch = split.Id, Quantity = count };
                manifest.Capture = proof with { Quantity = manifest.Items.Count };
            }
            part.SupplyManifest = split;
        }
        return part;
    }

    private bool AllowsDispatch(SWGlobalWarMarketBatch batch, string commodity, string[] route)
    {
        if (TradePolicy == null)
            return false;
        var request = new SWGlobalWarTradeRequest(batch.Generation, commodity, batch.Origin,
            batch.Owner, route[0], route[^1], route[0], route[1], SWGlobalWarTradeStage.Dispatch,
            Batch: batch.Id, Quantity: batch.Quantity - batch.Reserved, Capture: batch.Capture);
        if (!_dispatchAnswersOpen)
            return TradePolicy(request).Allowed;
        if (!_dispatchAnswers.TryGetValue(request, out var allowed))
            _dispatchAnswers.Add(request, allowed = TradePolicy(request).Allowed);
        return allowed;
    }

    private bool AllowsTrade(SWGlobalWarShipment shipment, SWGlobalWarTradeStage stage, string next)
    {
        if (shipment.SupplyManifest != null)
            return AllowsSupplyTrade(shipment, stage, next);
        if (shipment.Batch is not { } batch || TradePolicy == null)
            return false;
        var key = new SWGlobalWarWaitingTrade(shipment.Route, shipment.Leg, stage, shipment.Commodity,
            batch.Generation, batch.Origin, batch.Owner);
        var shared = batch is { Capture: null, Quantity: > 0 };
        if (!shared || !_waitingTrades.TryGetValue(key, out var decision))
        {
            decision = TradePolicy(new SWGlobalWarTradeRequest(batch.Generation, shipment.Commodity,
                batch.Origin, batch.Owner, shipment.Source, shipment.Destination, shipment.Route[shipment.Leg], next,
                stage, SWGlobalWarTradePosition.AtNode, batch.Id, batch.Quantity, batch.Capture));
            if (shared)
                _waitingTrades[key] = decision;
        }
        shipment.HoldReason = decision.Reason;
        return decision.Allowed;
    }
}

/// <summary>Everything but the batch's capture evidence and positive quantity that the policy reads for waiting cargo.</summary>
internal readonly record struct SWGlobalWarWaitingTrade(string[] Route, int Leg, SWGlobalWarTradeStage Stage,
    string Commodity, Guid Generation, SWGlobalWarFaction Origin, SWGlobalWarFaction Owner);

public sealed record SWGlobalWarStockRequest(Guid Generation, Guid Operation, string Planet, string Commodity,
    int Quantity, SWGlobalWarFaction Owner, string Purpose);
public sealed record SWGlobalWarStockPart(Guid Batch, int Quantity);
public enum SWGlobalWarStockReservationStatus : byte { Pending, Committed, Cancelled }
public sealed record SWGlobalWarStockReservation(SWGlobalWarStockRequest Request,
    IReadOnlyList<SWGlobalWarStockPart> Parts, SWGlobalWarStockReservationStatus Status,
    IReadOnlyList<SWGlobalWarMarketBatch> Withdrawn);
public sealed record SWGlobalWarMarketOwnershipChange(string Planet, SWGlobalWarFaction Owner, Guid Operation);
