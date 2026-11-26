using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly List<SWGlobalWarSupplyManifestClosure> _closedSupplyManifests = new();
    internal IReadOnlyList<SWGlobalWarSupplyManifestClosure> ClosedSupplyManifests => _closedSupplyManifests;

    internal void CloseCancelledStoredSupply(Guid generation, Guid purchase, SWGlobalWarFaction owner)
    {
        if (Generation != generation)
            return;
        foreach (var shipment in _shipments.Where(s => s.ArrivesAt == null &&
                     s.SupplyManifest is { Stored: true } m && m.Purchase == purchase && m.Owner == owner).ToArray())
        {
            var manifest = shipment.SupplyManifest!;
            _closedSupplyManifests.Add(new(shipment.Id, manifest.Id, purchase,
                manifest.Items.Select(i => i.Unit).ToArray(), shipment.Route[shipment.Leg], Time));
            _shipments.Remove(shipment);
        }
    }

    /// <summary>The physical consumer receives the same manifest items; no raw stock is created on receipt.</summary>
    internal Func<SWGlobalWarShipment, bool>? ReceiveSupplyManifest;
    internal Action<SWGlobalWarShipment, SWGlobalWarMarketOwnershipChange>? CaptureSupplyManifest;
    internal Func<SWGlobalWarShipment, SWGlobalWarMarketReturnRequest, SWGlobalWarReturnFailure>? CheckSupplyReturn;
    internal Func<SWGlobalWarShipment, SWGlobalWarMarketReturnRequest, bool>? CommitSupplyReturn;

    /// <summary>Finds an allowed alternative route independently of momentary lane or hub congestion.</summary>
    internal string[]? FindSupplyRoute(string source, string destination, string commodity,
        SWGlobalWarFaction origin, SWGlobalWarFaction owner, Guid manifest = default,
        int count = 1, SWGlobalWarTradeCaptureEvidence? capture = null)
    {
        if (Generation is not { } generation || TradePolicy == null || count <= 0 ||
            !_commodities.ContainsKey(commodity) || !_markets.ContainsKey(source) || !_markets.ContainsKey(destination))
            return null;
        var request = new SWGlobalWarTradeRequest(generation, commodity, origin, owner, source, destination,
            source, destination, SWGlobalWarTradeStage.Receipt, Batch: manifest, Quantity: count, Capture: capture);
        var previous = new Dictionary<string, string>();
        var distance = new Dictionary<string, TimeSpan> { [source] = TimeSpan.Zero };
        var queue = new PriorityQueue<string, TimeSpan>();
        queue.Enqueue(source, TimeSpan.Zero);
        while (queue.TryDequeue(out var node, out var cost))
        {
            if (distance[node] != cost)
                continue;
            if (node == destination)
            {
                if (!TradePolicy(request with { Current = destination }).Allowed)
                    return null;
                var route = new List<string> { destination };
                while (route[^1] != source)
                    route.Add(previous[route[^1]]);
                route.Reverse();
                return route.ToArray();
            }
            foreach (var edge in _outgoing[node])
            {
                if (_laneCapacity[Lane(node, edge.To)] + Epsilon < 1 ||
                    !TradePolicy(request with { Current = node, Next = edge.To,
                        Stage = node == source ? SWGlobalWarTradeStage.Dispatch : SWGlobalWarTradeStage.NextLeg }).Allowed)
                    continue;
                var nextCost = cost + edge.TravelTime;
                if (distance.TryGetValue(edge.To, out var old) && old <= nextCost)
                    continue;
                distance[edge.To] = nextCost;
                previous[edge.To] = node;
                queue.Enqueue(edge.To, nextCost);
            }
        }
        return null;
    }

    /// <summary>Queues references to real items. Physical custody is established by the caller before this commit.</summary>
    internal bool TryQueueSupplyManifest(string commodity, string source, string destination,
        SWGlobalWarSupplyManifest manifest, out SWGlobalWarShipment shipment, bool ownerSettlement = false)
    {
        shipment = default!;
        if (Generation != manifest.Generation || ownerSettlement && !manifest.AwaitingPhysical ||
            !ownerSettlement && IsActive?.Invoke() == false || manifest.Items.Count == 0 ||
            manifest.Items.Count > 600 || manifest.Items.Select(i => i.Entity).Distinct().Count() != manifest.Items.Count ||
            manifest.Items.Select(i => i.Unit).Distinct().Count() != manifest.Items.Count ||
            _shipments.Any(s => s.SupplyManifest is { } existing && existing.Items.Any(i =>
                manifest.Items.Any(item => item.Entity == i.Entity || item.Unit == i.Unit))))
            return false;
        var route = FindSupplyRoute(source, destination, commodity, manifest.Origin, manifest.Owner,
            manifest.Id, manifest.Items.Count, manifest.Capture);
        if (route == null || route.Length < 2)
            return false;
        shipment = new SWGlobalWarShipment(commodity, manifest.Items.Count * UnitsPerItem, route, Time, TimeSpan.Zero)
        {
            ArrivesAt = null, ReceivedAtNode = true, SupplyManifest = manifest,
            DepartureReceipt = Guid.NewGuid(), DeliveryParticipant = manifest.Owner,
        };
        _shipments.Add(shipment);
        return true;
    }

    private bool AllowsSupplyTrade(SWGlobalWarShipment shipment, SWGlobalWarTradeStage stage, string next)
    {
        if (shipment.SupplyManifest is not { } manifest || TradePolicy == null)
            return false;
        var decision = TradePolicy(new(manifest.Generation, shipment.Commodity, manifest.Origin, manifest.Owner,
            shipment.Source, shipment.Destination, shipment.Route[shipment.Leg], next, stage,
            shipment.ArrivesAt != null ? SWGlobalWarTradePosition.InTransit : SWGlobalWarTradePosition.AtNode,
            manifest.Id, manifest.Items.Count, manifest.Capture));
        shipment.HoldReason = decision.Reason;
        return decision.Allowed;
    }

    internal bool CanReceiveSupplyManifest(SWGlobalWarShipment shipment)
    {
        return IsActive?.Invoke() != false && _shipments.Contains(shipment) && shipment.ArrivesAt == null &&
            shipment.Leg == shipment.Route.Length - 1 && shipment.ReceivedAtNode &&
            AllowsSupplyTrade(shipment, SWGlobalWarTradeStage.Receipt, shipment.Destination);
    }

    /// <summary>Uses the common budgets at BasicMilitary priority, after food and civilian inputs and before growth.</summary>
    private void AdvanceSupplyShipments()
    {
        for (var i = 0; i < _shipments.Count; i++)
        {
            var shipment = _shipments[i];
            if (shipment.SupplyManifest == null || shipment.ArrivesAt != null)
                continue;
            if (IsActive?.Invoke() == false)
                return;
            if (shipment.ReturnCaptured)
            {
                shipment.HoldReason = "sw-global-war-market-return-captured";
                continue;
            }
            var node = _markets[shipment.Route[shipment.Leg]];
            if (!shipment.ReceivedAtNode)
            {
                var count = WholeSupplyTransfer(shipment, node.Profile == null ? double.MaxValue : node.ReceiveBudget);
                if (count == 0)
                {
                    shipment.HoldReason = "sw-global-war-market-wait-receive";
                    continue;
                }
                if (count < shipment.SupplyManifest.Items.Count)
                {
                    var part = SplitShipment(shipment, count * UnitsPerItem);
                    _shipments.Insert(++i, part);
                    shipment = part;
                }
                if (node.Profile != null)
                    node.ReceiveBudget = Math.Max(0, node.ReceiveBudget - count);
                shipment.ReceivedAtNode = true;
            }
            if (shipment.Leg == shipment.Route.Length - 1)
            {
                if (!AllowsSupplyTrade(shipment, SWGlobalWarTradeStage.Receipt, shipment.Destination))
                    continue;
                if (ReceiveSupplyManifest?.Invoke(shipment) != true)
                {
                    shipment.HoldReason = "sw-global-war-supply-transport-wait-accept";
                    continue;
                }
                if (IsActive?.Invoke() == false)
                    return;
                if (!shipment.SupplyManifest!.ArrivalRecorded)
                {
                    CompleteReturnPart(shipment);
                    shipment.SupplyManifest.ArrivalRecorded = true;
                }
                if (shipment.SupplyManifest.Stored)
                {
                    shipment.HoldReason = "sw-global-war-supply-transport-stored";
                    continue;
                }
                _shipments.RemoveAt(i--);
                continue;
            }
            var next = shipment.Route[shipment.Leg + 1];
            var stage = shipment.Leg == 0 && shipment.DepartureReceipt != Guid.Empty ?
                SWGlobalWarTradeStage.Dispatch : SWGlobalWarTradeStage.NextLeg;
            if (!AllowsSupplyTrade(shipment, stage, next))
                continue;
            var lane = Lane(node.Planet, next);
            var capacity = Math.Min(_remainingCapacity[lane], node.Profile == null ? double.MaxValue : node.SendBudget);
            var departing = WholeSupplyTransfer(shipment, capacity);
            if (departing == 0)
            {
                shipment.HoldReason = node.Profile != null && node.SendBudget + Epsilon < 1 ?
                    "sw-global-war-market-wait-send" : "sw-global-war-market-wait-lane";
                continue;
            }
            if (departing < shipment.SupplyManifest!.Items.Count)
            {
                var part = SplitShipment(shipment, departing * UnitsPerItem);
                _shipments.Insert(++i, part);
                shipment = part;
            }
            _remainingCapacity[lane] = Math.Max(0, _remainingCapacity[lane] - departing);
            if (node.Profile != null)
                node.SendBudget = Math.Max(0, node.SendBudget - departing);
            shipment.ArrivesAt = Time + _connections[(node.Planet, next)].TravelTime;
            shipment.ReceivedAtNode = false;
            shipment.HoldReason = string.Empty;
        }
    }

    private static int WholeSupplyTransfer(SWGlobalWarShipment shipment, double capacity)
    {
        return (int) Math.Min(shipment.SupplyManifest!.Items.Count, Math.Max(0, Math.Floor(capacity + Epsilon)));
    }
}

internal sealed record SWGlobalWarSupplyManifestClosure(Guid Shipment, Guid Manifest, Guid Purchase,
    IReadOnlyList<Guid> Units, string Node, TimeSpan At);
