using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _freightOwnership = default!;
    private readonly Dictionary<EntityUid, SWGlobalWarPhysicalCargoProof> _physicalCargo = new();
    private readonly Dictionary<Guid, SWGlobalWarPhysicalFlightProof> _physicalFlights = new();

    private static bool HasPhysicalSupplyCarrier(SWGlobalWarSupplyPurchase purchase)
    {
        return purchase.AwaitingPhysical;
    }

    private bool RecordPhysicalCollection(SWGlobalWarSupplyPurchase purchase, EntityUid carrier,
        IReadOnlyList<EntityUid> goods)
    {
        if (!purchase.AwaitingPhysical)
            return true;
        if (_campaignGeneration != purchase.Request.Generation || Simulation?.Generation != purchase.Request.Generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Owner != _rule ||
            campaign.Comp.Generation != purchase.Request.Generation || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending ||
            _campaignStep.IsClosing)
            return false;
        var existing = Simulation!.Shipments.Where(s => s.SupplyManifest is { AwaitingPhysical: true } manifest &&
            manifest.Purchase == purchase.Request.Operation).SelectMany(s => s.SupplyManifest!.Items)
            .Select(i => i.Entity).ToHashSet();
        if (goods.All(existing.Contains))
            return true;
        if (goods.Any(existing.Contains))
            return false;
        var manifests = goods.GroupBy(e => _supplyItems[e].OriginOwner).Select(group =>
            new SWGlobalWarSupplyManifest(purchase.Request.Generation, purchase.Request.Operation,
                group.Key, purchase.Request.Faction, group.Select(e => new SWGlobalWarSupplyManifestItem(e,
                    _supplyItems[e].Unit)).ToArray())
            {
                AwaitingPhysical = true, Custody = carrier, Recipient = purchase.Request.Recipient, SpaceReserved = true,
            }).ToArray();
        if (!Simulation.TryQueuePhysicalSupplyManifests(SupplyCommodity(purchase.Request.Kind, purchase.Request.Bacta),
                _supplyStores[purchase.Request.Source].Planet, _supplyStores[purchase.Request.Recipient].Planet, manifests))
            return false;
        foreach (var entity in goods)
        {
            var item = _supplyItems[entity];
            _physicalCargo[entity] = new(item.Generation, purchase.Request.Operation, item.Unit,
                _supplyStores[purchase.Request.Source].Planet);
        }
        return true;
    }

    private void TrackPhysicalFlights()
    {
        if (Simulation?.Generation is not { } generation || !CanMutateCampaign(generation))
            return;
        var query = EntityQueryEnumerator<SWGlobalWarTravelComponent>();
        while (query.MoveNext(out var ship, out var travel))
        {
            // Snapshot only after departure. Objects loaded later cannot inherit the flight receipt.
            if (travel.Generation != generation || travel.FlightId == Guid.Empty || travel.Returning ||
                travel.Withdrawal != null || Transform(ship).MapUid == travel.SourceMap ||
                _physicalFlights.ContainsKey(travel.FlightId) ||
                !_freightOwnership.TryGetOwner(ship, generation, out var owner, out var revision))
                continue;
            var source = Comp<SWGlobalWarPlanetComponent>(travel.SourcePlanet).Prototype.Id;
            var destination = Comp<SWGlobalWarPlanetComponent>(travel.DestinationPlanet).Prototype.Id;
            var goods = _physicalCargo.Where(p => p.Value.Generation == generation && p.Value.Current == source &&
                    !TerminatingOrDeleted(p.Key) && Transform(p.Key).GridUid == ship &&
                    _supplyItems.TryGetValue(p.Key, out var item) && item.Unit == p.Value.Unit &&
                    item.Owner == owner && item.Order == p.Value.Purchase && !item.Accepted && item.Payment == null &&
                    PhysicalFreightDepartureAllowed(p.Key, source, destination))
                .Select(p => new SWGlobalWarSupplyManifestItem(p.Key, p.Value.Unit)).ToArray();
            _physicalFlights.Add(travel.FlightId, new(generation, ship, travel.SourcePlanet,
                travel.DestinationPlanet, owner, revision, goods));
        }
    }

    private bool PhysicalFreightDepartureAllowed(EntityUid entity, string source, string destination)
    {
        if (!_supplyItems.TryGetValue(entity, out var item) || item.Order is not { } order ||
            !_supplyPurchases.TryGetValue(order, out var purchase) || Simulation?.TradePolicy == null)
            return false;
        var shipment = Simulation.Shipments.FirstOrDefault(s => s.SupplyManifest is { AwaitingPhysical: true } manifest &&
            manifest.Purchase == order && manifest.Items.Any(i => i.Entity == entity && i.Unit == item.Unit));
        if (shipment == null)
            return false;
        return Simulation.TradePolicy(new(item.Generation, SupplyCommodity(item.Kind, item.Bacta), item.OriginOwner,
            item.Owner, shipment.Source, shipment.Destination, source, destination,
            source == shipment.Source ? SWGlobalWarTradeStage.Dispatch : SWGlobalWarTradeStage.NextLeg,
            Batch: item.Unit)).Allowed;
    }

    private void OnPhysicalFreightArrival(ref SWGlobalWarShipArrivalCommittedEvent args)
    {
        if (!CanMutateCampaign(args.Generation) || !_physicalFlights.TryGetValue(args.FlightId, out var flight) ||
            flight.Arrived || flight.Generation != args.Generation || flight.Ship != args.Ship ||
            flight.Source != args.SourcePlanet || flight.Destination != args.DestinationPlanet ||
            flight.Owner != args.Owner || flight.OwnershipRevision != args.OwnershipRevision ||
            !_freightOwnership.TryGetOwner(args.Ship, args.Generation, out var currentOwner, out var currentRevision) ||
            currentOwner != args.Owner || currentRevision != args.OwnershipRevision ||
            !_hubPlanets.TryGetPhysicalLocation(args.Ship, out var destination, out _) ||
            destination != args.DestinationPlanet)
            return;
        flight.Arrived = true;
        var source = Comp<SWGlobalWarPlanetComponent>(args.SourcePlanet).Prototype.Id;
        var target = Comp<SWGlobalWarPlanetComponent>(args.DestinationPlanet).Prototype.Id;
        foreach (var cargo in flight.Goods)
        {
            if (TerminatingOrDeleted(cargo.Entity) || Transform(cargo.Entity).GridUid != args.Ship ||
                !_physicalCargo.TryGetValue(cargo.Entity, out var proof) || proof.Generation != args.Generation ||
                proof.Current != source || proof.Unit != cargo.Unit ||
                !_supplyItems.TryGetValue(cargo.Entity, out var item) || item.Unit != cargo.Unit ||
                item.Owner != args.Owner || item.Order != proof.Purchase || item.Accepted || item.Payment != null)
                continue;
            proof.Legs.Add(new(args.FlightId, source, target));
            proof.Current = target;
        }
        // No inventory movement, purchase acceptance, quota change or payment occurs at this boundary.
    }

    private EntityUid? PhysicalFreightDestination(SWGlobalWarSupplyPurchase purchase, IReadOnlyList<EntityUid> goods)
    {
        if (!purchase.AwaitingPhysical)
            return purchase.Request.Recipient;
        var manifests = Simulation!.Shipments.Where(s => s.SupplyManifest is { AwaitingPhysical: true } manifest &&
            manifest.Purchase == purchase.Request.Operation && manifest.Items.Any(i => goods.Contains(i.Entity)))
            .Select(s => s.SupplyManifest!).ToArray();
        if (manifests.Length == 0)
            return purchase.Request.Recipient;
        var destinations = manifests.Select(m => m.Recipient).Distinct().ToArray();
        return destinations.Length == 1 && goods.All(e => manifests.Any(m => m.Items.Any(i => i.Entity == e)))
            ? destinations[0] : null;
    }

    private bool CanAcceptPhysicalFreight(SWGlobalWarSupplyPurchase purchase, IReadOnlyList<EntityUid> goods,
        EntityUid recipient)
    {
        if (!purchase.AwaitingPhysical || Simulation?.TradePolicy == null)
            return false;
        var source = _supplyStores[purchase.Request.Source].Planet;
        var destination = _supplyStores[recipient].Planet;
        var manifests = Simulation.Shipments.Where(s => s.SupplyManifest is { AwaitingPhysical: true } manifest &&
            manifest.Purchase == purchase.Request.Operation && manifest.Items.Any(i => goods.Contains(i.Entity))).ToArray();
        if (manifests.Length == 0 || manifests.Any(s => !Simulation.CanReceiveSupplyManifest(s) ||
                s.SupplyManifest!.Recipient != recipient) ||
            goods.Any(e => !manifests.Any(s => s.SupplyManifest!.Items.Any(i => i.Entity == e &&
                _supplyItems.TryGetValue(e, out var item) && item.Unit == i.Unit))))
            return false;
        foreach (var entity in goods)
        {
            if (!_physicalCargo.TryGetValue(entity, out var proof) || proof.Generation != purchase.Request.Generation ||
                proof.Purchase != purchase.Request.Operation || proof.Current != destination || proof.Legs.Count == 0 ||
                !_supplyItems.TryGetValue(entity, out var item) || item.Unit != proof.Unit)
                return false;
            var current = source;
            var path = new List<string> { source };
            foreach (var leg in proof.Legs)
            {
                if (leg.Source != current)
                    return false;
                current = leg.Destination;
                path.Add(current);
            }
            // Hyperspace lanes differ from the economic graph: R7 reaches its capital through R2, while the manifest's
            // scheduling route is the direct R7-R0 lane. Real flights must pass the route's start and end at its end.
            var route = manifests.Single(s => s.SupplyManifest!.Items.Any(i => i.Entity == entity)).Route;
            if (!path.Contains(route[0]) || path[^1] != route[^1])
                return false;
            if (current != destination || !Simulation.TradePolicy(new(purchase.Request.Generation,
                    SupplyCommodity(purchase.Request.Kind, purchase.Request.Bacta), item.OriginOwner, item.Owner,
                    source, destination, destination, destination, SWGlobalWarTradeStage.Receipt,
                    Batch: item.Unit)).Allowed)
                return false;
        }
        return true;
    }
}

internal sealed class SWGlobalWarPhysicalCargoProof(Guid generation, Guid purchase, Guid unit, string source)
{
    public readonly Guid Generation = generation;
    public readonly Guid Purchase = purchase;
    public readonly Guid Unit = unit;
    public string Current = source;
    public readonly List<SWGlobalWarPhysicalCargoLeg> Legs = new();
}

internal sealed record SWGlobalWarPhysicalCargoLeg(Guid Flight, string Source, string Destination);

internal sealed class SWGlobalWarPhysicalFlightProof(Guid generation, EntityUid ship, EntityUid source,
    EntityUid destination, SWGlobalWarFaction owner, long ownershipRevision,
    IReadOnlyList<SWGlobalWarSupplyManifestItem> goods)
{
    public readonly Guid Generation = generation;
    public readonly EntityUid Ship = ship;
    public readonly EntityUid Source = source;
    public readonly EntityUid Destination = destination;
    public readonly SWGlobalWarFaction Owner = owner;
    public readonly long OwnershipRevision = ownershipRevision;
    public readonly IReadOnlyList<SWGlobalWarSupplyManifestItem> Goods = goods;
    public bool Arrived;
}
