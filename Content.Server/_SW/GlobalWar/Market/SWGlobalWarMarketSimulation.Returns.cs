using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly Dictionary<Guid, SWGlobalWarMarketReturnReceipt> _returnReceipts = new();

    internal SWGlobalWarMarketReturnReceipt? GetReturnReceipt(Guid operation)
    {
        return _returnReceipts.GetValueOrDefault(operation);
    }

    internal SWGlobalWarReturnFailure CheckReturn(SWGlobalWarMarketReturnRequest request)
    {
        return CheckReturn(_shipments.FirstOrDefault(s => s.Id == request.Shipment), request);
    }

    /// <summary>For a caller already holding the shipment, which must be the one the request names.</summary>
    internal SWGlobalWarReturnFailure CheckReturn(SWGlobalWarShipment? shipment, SWGlobalWarMarketReturnRequest request)
    {
        if (shipment == null || shipment.Quantity != request.Quantity)
            return SWGlobalWarReturnFailure.Changed;
        if ((shipment.SupplyManifest?.Owner ?? shipment.Batch?.Owner) != request.Owner)
            return SWGlobalWarReturnFailure.NotOwner;
        if (shipment.ArrivesAt != null)
            return SWGlobalWarReturnFailure.InTransit;
        if (shipment.ReturnOperation != null && !shipment.ReturnCaptured && shipment.SupplyManifest?.Stored != true)
            return SWGlobalWarReturnFailure.AlreadyReturning;
        if (shipment.SupplyManifest is { } manifest)
        {
            var physical = CheckSupplyReturn?.Invoke(shipment, request) ?? SWGlobalWarReturnFailure.InvalidDestination;
            if (physical != SWGlobalWarReturnFailure.None)
                return physical;
            return FindSupplyRoute(shipment.Route[shipment.Leg], request.Destination, shipment.Commodity,
                manifest.Origin, manifest.Owner, manifest.Id, manifest.Items.Count, manifest.Capture) == null ?
                SWGlobalWarReturnFailure.NoRoute : SWGlobalWarReturnFailure.None;
        }
        if (!_markets.TryGetValue(request.Destination, out var destination) || destination.Owner != request.Owner ||
            !destination.Goods.TryGetValue(shipment.Commodity, out var target) ||
            !shipment.ReturnCaptured && (request.Destination == shipment.Destination ||
                                        request.Destination == shipment.Route[shipment.Leg]))
            return SWGlobalWarReturnFailure.InvalidDestination;
        if (target.Capacity - target.Stock - target.Incoming - target.ReservedIncoming +
            (request.Destination == shipment.Destination ? shipment.Quantity : 0) < shipment.Quantity)
            return SWGlobalWarReturnFailure.NoSpace;
        return FindRoute(shipment.Route[shipment.Leg], request.Destination,
            _commodities[shipment.Commodity].CargoVolume, false) == null
            ? SWGlobalWarReturnFailure.NoRoute : SWGlobalWarReturnFailure.None;
    }

    /// <summary>
    /// Moves one destination-space obligation for cargo physically at a node. The server system authorizes the actor;
    /// this transaction preserves the batch and leaves departure, transit, unloading and policy checks to normal steps.
    /// A blocked return waits at its current node and never teleports or creates a second inventory balance.
    /// </summary>
    internal bool TryReturnShipment(SWGlobalWarMarketReturnRequest request, out SWGlobalWarMarketReturnReceipt receipt)
    {
        receipt = default!;
        if (_advancing || Generation != request.Generation || request.Operation == Guid.Empty ||
            request.Quantity <= 0 || IsActive?.Invoke() == false)
            return false;
        if (_returnReceipts.TryGetValue(request.Operation, out var previous))
        {
            receipt = previous;
            return previous.Request == request;
        }
        if (CheckReturn(request) != SWGlobalWarReturnFailure.None)
            return false;
        var shipment = _shipments.First(s => s.Id == request.Shipment);
        var manifest = shipment.SupplyManifest;
        var route = manifest != null ? FindSupplyRoute(shipment.Route[shipment.Leg], request.Destination,
            shipment.Commodity, manifest.Origin, manifest.Owner, manifest.Id, manifest.Items.Count, manifest.Capture) :
            FindRoute(shipment.Route[shipment.Leg], request.Destination, _commodities[shipment.Commodity].CargoVolume, false);
        if (route == null)
            return false;
        if (manifest != null && CommitSupplyReturn?.Invoke(shipment, request) != true)
            return false;
        SWGlobalWarMarketReturnReceipt? prior = shipment.ReturnOperation is { } oldOperation
            ? _returnReceipts[oldOperation] : null;
        receipt = new SWGlobalWarMarketReturnReceipt(request, shipment.DepartureReceipt,
            prior?.OriginalSource ?? shipment.Source, prior?.OriginalDestination ?? shipment.Destination,
            shipment.Commodity, prior?.Flow ?? shipment.Flow, Time, 0, 0, 0, shipment.ReturnOperation);
        if (manifest == null)
        {
            _markets[shipment.Destination].Goods[shipment.Commodity].Incoming -= shipment.Quantity;
            if (shipment.Flow is { } flow)
                _flowIncoming[(flow, shipment.Destination, shipment.Commodity)] -= shipment.Quantity;
            _markets[request.Destination].Goods[shipment.Commodity].Incoming += shipment.Quantity;
        }
        else
        {
            manifest.ArrivalRecorded = false;
        }
        if (prior != null)
        {
            _returnReceipts[prior.Request.Operation] = prior with
            {
                RedirectedQuantity = prior.RedirectedQuantity + shipment.Quantity,
            };
            if (prior.Flow is { } oldFlow && _flows.TryGetValue(oldFlow, out var contract))
                contract.GetStatistics(shipment.Commodity).ReturnRedirected += shipment.Quantity;
        }
        shipment.Route = route;
        shipment.Leg = 0;
        shipment.Flow = null;
        shipment.ReturnOperation = request.Operation;
        shipment.DeliveryParticipant = request.Owner;
        shipment.ReturnCaptured = false;
        shipment.HoldReason = "sw-global-war-market-return-pending";
        _returnReceipts.Add(request.Operation, receipt);
        return true;
    }

    private void CompleteReturnPart(SWGlobalWarShipment shipment)
    {
        if (shipment.ReturnCaptured || shipment.ReturnOperation is not { } operation)
            return;
        var receipt = _returnReceipts[operation];
        _returnReceipts[operation] = receipt with { ReceivedQuantity = receipt.ReceivedQuantity + shipment.Quantity };
        if (receipt.Flow is { } flow && _flows.TryGetValue(flow, out var contract))
        {
            if (receipt.PreviousOperation == null)
                contract.GetStatistics(shipment.Commodity).Returned += shipment.Quantity;
            else
                contract.GetStatistics(shipment.Commodity).RedirectedReturnReceived += shipment.Quantity;
        }
    }

    private void CaptureReturnPart(SWGlobalWarShipment shipment)
    {
        if (shipment.ReturnOperation is not { } operation || shipment.ReturnCaptured)
            return;
        shipment.ReturnCaptured = true;
        var receipt = _returnReceipts[operation];
        _returnReceipts[operation] = receipt with { CapturedQuantity = receipt.CapturedQuantity + shipment.Quantity };
        if (receipt.Flow is { } flow && _flows.TryGetValue(flow, out var contract))
            contract.GetStatistics(shipment.Commodity).ReturnCaptured += shipment.Quantity;
    }
}

public sealed record SWGlobalWarMarketReturnRequest(Guid Generation, Guid Operation, Guid Shipment,
    SWGlobalWarFaction Owner, string Destination, int Quantity, EntityUid? DestinationStore = null);

public sealed record SWGlobalWarMarketReturnReceipt(SWGlobalWarMarketReturnRequest Request, Guid DepartureReceipt,
    string OriginalSource, string OriginalDestination, string Commodity, string? Flow, TimeSpan RequestedAt,
    int ReceivedQuantity, int CapturedQuantity, int RedirectedQuantity, Guid? PreviousOperation);
