using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.Containers;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    internal SWGlobalWarSupplyTransportView? SupplyTransportView(SWGlobalWarShipment shipment)
    {
        if (shipment.SupplyManifest is not { } manifest || !_supplyPurchases.TryGetValue(manifest.Purchase, out var purchase))
            return null;
        return new(manifest.Purchase, purchase.Number, purchase.Obligation.Accepted, purchase.Obligation.Outstanding,
            manifest.Recipient is { } recipient ? SupplyWorkPointName(recipient) : string.Empty,
            purchase.AllowedArsenals.Select(p => p.Name).ToList(),
            manifest.Owner == purchase.Request.Faction && manifest.Recipient is { } point &&
            _supplyStores.TryGetValue(point, out var binding) && AllowedSupplyArsenal(purchase, binding),
            manifest.AwaitingPhysical ? manifest.Items.Select(i => _physicalCargo.GetValueOrDefault(i.Entity)?.Current ??
                _supplyStores[purchase.Request.Source].Planet).Distinct()
                .Select(p => new Robust.Shared.Prototypes.ProtoId<SWGlobalWarPlanetPrototype>(p)).ToList() : null);
    }

    private bool HasSupplyPurchaseRoute(SWGlobalWarSupplyPurchaseRequest request)
    {
        return _supplyStores.TryGetValue(request.Source, out var source) &&
            _supplyStores.TryGetValue(request.Recipient, out var recipient) &&
            (source.Planet == recipient.Planet || Simulation?.FindSupplyRoute(source.Planet, recipient.Planet,
                SupplyCommodity(request.Kind, request.Bacta), request.Faction, request.Faction,
                count: request.Quantity) != null);
    }

    private bool IsInterplanetSupplyPurchase(SWGlobalWarSupplyPurchase purchase)
    {
        return _supplyStores[purchase.Request.Source].Planet != _supplyStores[purchase.Request.Recipient].Planet;
    }

    private bool IsHomeSupplyArsenal(SWGlobalWarSupplyStore store, SWGlobalWarFaction faction)
    {
        var market = Simulation!.Markets[store.Planet];
        return store.Arsenal && market.OriginalOwner == faction && market.Owner == faction && !market.Enclave &&
            market.Kind is SWGlobalWarSectorKind.Capital or SWGlobalWarSectorKind.Industry;
    }

    private IEnumerable<KeyValuePair<EntityUid, SWGlobalWarSupplyStore>> SupplyArsenalPoints(SWGlobalWarSupplyPurchaseRequest request)
    {
        return _supplyStores.Where(p => (p.Key == request.Recipient || IsHomeSupplyArsenal(p.Value, request.Faction)) &&
                TryGetSupplyStore(request.Generation, p.Key, request.Faction, out _, out _))
            .OrderBy(p => p.Key != request.Recipient).ThenBy(p => p.Key.Id);
    }

    private static string SupplyCommodity(SWGlobalWarSupplyKind kind, bool bacta)
    {
        return kind switch
        {
            SWGlobalWarSupplyKind.T1 => "SWGlobalWarSupplyT1",
            SWGlobalWarSupplyKind.T2 => "SWGlobalWarSupplyT2",
            SWGlobalWarSupplyKind.Armor => "SWGlobalWarSupplyArmor",
            SWGlobalWarSupplyKind.Magazine => "SWGlobalWarSupplyMagazine",
            SWGlobalWarSupplyKind.Ration => "SWGlobalWarSupplyRation",
            SWGlobalWarSupplyKind.Medicine => bacta ? "SWGlobalWarSupplyBacta" : "SWGlobalWarSupplyKolto",
            SWGlobalWarSupplyKind.AdvancedElectronics => "SWGlobalWarAE",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private void RunSupplyProgramsAndTransport()
    {
        RunAutomaticSupplyPrograms();
        if (Simulation is not { Generation: { } generation } model || !CanMutateCampaign(generation, _ownershipCatchUp))
            return;
        foreach (var purchase in _supplyPurchases.Values.ToArray())
        {
            if (!CanMutateCampaign(generation, _ownershipCatchUp))
                return;
            QueuePhysicalSupplyPurchase(purchase);
        }
    }

    private void QueuePhysicalSupplyPurchase(SWGlobalWarSupplyPurchase purchase)
    {
        var request = purchase.Request;
        purchase.TransportHold = string.Empty;
        if (purchase.AwaitingPhysical)
        {
            if (purchase.Obligation.Outstanding > 0)
                purchase.TransportHold = "sw-global-war-supply-order-awaiting-physical";
            return;
        }
        if (purchase.ReturnPending || purchase.Obligation.Outstanding == 0 ||
            !TryGetSupplyStore(request.Generation, request.Source, request.Faction, out var source, out _) ||
            !TryGetSupplyStore(request.Generation, request.Recipient, request.Faction, out var recipient, out _) ||
            source.Planet == recipient.Planet ||
            !_supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out var held))
            return;
        var goods = held.ContainedEntities.Where(e => purchase.Goods.Contains(e) &&
            _supplyItems.TryGetValue(e, out var item) && item.Order == request.Operation &&
            item.Owner == request.Faction && !item.Accepted && item.Payment == null &&
            item.Origin == SWGlobalWarSupplyOrigin.Industrial &&
            IsSuitableSupplyItem(e, request.Kind, request.Faction, request.Bacta)).ToArray();
        foreach (var group in goods.GroupBy(e => _supplyItems[e].OriginOwner))
        {
            var items = group.Select(e => new SWGlobalWarSupplyManifestItem(e, _supplyItems[e].Unit)).ToArray();
            var commodity = SupplyCommodity(request.Kind, request.Bacta);
            if (Simulation!.FindSupplyRoute(source.Planet, recipient.Planet, commodity, group.Key, request.Faction,
                    count: items.Length) == null)
            {
                purchase.TransportHold = "sw-global-war-supply-order-route-blocked";
                continue;
            }
            var custody = CreateSupplyEscrow();
            var container = _supplyContainers.EnsureContainer<Container>(custody, SupplyContainer);
            var manifest = new SWGlobalWarSupplyManifest(request.Generation, request.Operation,
                group.Key, request.Faction, items)
            {
                Custody = custody, Recipient = request.Recipient, SpaceReserved = true,
            };
            var queued = false;
            try
            {
                if (items.Any(item => !_supplyContainers.Insert(item.Entity, container)))
                    continue;
                queued = Simulation.TryQueueSupplyManifest(commodity, source.Planet, recipient.Planet, manifest, out _);
                if (queued)
                    purchase.Obligation.Stage = SWGlobalWarSupplyStage.InTransit;
            }
            finally
            {
                if (!queued)
                {
                    foreach (var item in container.ContainedEntities.ToArray())
                        _supplyContainers.Insert(item, held, force: true);
                    Del(custody);
                }
            }
        }
    }

    private bool ReceivePhysicalSupplyManifest(SWGlobalWarShipment shipment)
    {
        if (shipment.SupplyManifest is { AwaitingPhysical: true })
            return false;
        if (shipment.SupplyManifest is not { } manifest ||
            !_supplyPurchases.TryGetValue(manifest.Purchase, out var purchase) ||
            manifest.Recipient is not { } recipient ||
            !TryGetSupplyStore(manifest.Generation, recipient, manifest.Owner, out var binding, out var receiving) ||
            binding.Planet != shipment.Destination || !_ledger.TryGetLedger(out var ledger) ||
            Simulation?.CanReceiveSupplyManifest(shipment) != true ||
            manifest.Items.Any(i => !_supplyItems.TryGetValue(i.Entity, out var item) || item.Unit != i.Unit ||
                !IsPhysicalSupplyInCustody(shipment, i.Entity)))
            return false;
        if (purchase.Request.Faction == manifest.Owner && purchase.Obligation.Outstanding > 0 &&
            AllowedSupplyArsenal(purchase, binding) && manifest.Items.All(i =>
                _supplyItems[i.Entity].Origin == SWGlobalWarSupplyOrigin.Industrial))
            return AcceptSupplyPurchase(manifest.Generation, manifest.Id, manifest.Purchase, ledger.Comp.Revision,
                manifest.Items.Select(i => i.Entity).ToArray(), shipment);
        if (manifest.Stored)
            return true;
        if (!manifest.SpaceReserved && !HasSupplyPlace(recipient, purchase.Request.Kind, manifest.Items.Count, receiving))
            return false;
        var original = manifest.Custody!.Value;
        if (!_supplyContainers.TryGetContainer(original, SupplyContainer, out var held))
            return false;
        _settling = true;
        var moved = new List<EntityUid>();
        try
        {
            foreach (var item in manifest.Items)
            {
                if (!_supplyContainers.Insert(item.Entity, receiving))
                {
                    foreach (var entity in moved)
                        _supplyContainers.Insert(entity, held, force: true);
                    return false;
                }
                moved.Add(item.Entity);
            }
            ReleaseManifestPlace(manifest, purchase.Request.Kind);
            manifest.Custody = recipient;
            manifest.Stored = true;
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    private bool IsPhysicalSupplyInCustody(SWGlobalWarShipment shipment, EntityUid entity)
    {
        return shipment.SupplyManifest is { Custody: { } custody } manifest &&
            manifest.Generation == _campaignGeneration && manifest.Owner == _supplyItems[entity].Owner &&
            manifest.Items.Any(i => i.Entity == entity && i.Unit == _supplyItems[entity].Unit) &&
            _supplyContainers.TryGetContainer(custody, SupplyContainer, out var container) && container.Contains(entity) &&
            Simulation?.CanReceiveSupplyManifest(shipment) == true;
    }

    private void CapturePhysicalSupplyManifest(SWGlobalWarShipment shipment, SWGlobalWarMarketOwnershipChange change)
    {
        foreach (var entry in shipment.SupplyManifest!.Items)
            if (_supplyItems.TryGetValue(entry.Entity, out var item) && item.Unit == entry.Unit)
                _supplyItems[entry.Entity] = item with { Owner = change.Owner, Origin = SWGlobalWarSupplyOrigin.Trophy };
    }

    private static bool AllowedSupplyArsenal(SWGlobalWarSupplyPurchase purchase, SWGlobalWarSupplyStore point)
    {
        return point.Arsenal && purchase.AllowedArsenals.Any(p => p.Planet == point.Planet &&
            p.Coordinates == point.Coordinates && p.Arsenal == point.Arsenal);
    }

    private SWGlobalWarReturnFailure CheckPhysicalSupplyReturn(SWGlobalWarShipment shipment,
        SWGlobalWarMarketReturnRequest request)
    {
        var manifest = shipment.SupplyManifest!;
        if (request.DestinationStore is not { } destination ||
            !_supplyPurchases.TryGetValue(manifest.Purchase, out var purchase) ||
            !TryGetSupplyStore(request.Generation, destination, request.Owner, out var point, out var receiving) ||
            point.Planet != request.Destination || !shipment.ReturnCaptured && destination == manifest.Recipient)
            return SWGlobalWarReturnFailure.InvalidDestination;
        var count = manifest.Items.Count;
        if (manifest.SpaceReserved && manifest.Recipient == destination ||
            manifest.Stored && manifest.Custody == destination && manifest.Items.All(i => receiving.Contains(i.Entity)))
            count = 0;
        if (!HasSupplyPlace(destination, purchase.Request.Kind, count, receiving))
            return SWGlobalWarReturnFailure.NoSpace;
        if (manifest.AwaitingPhysical && !manifest.Stored)
        {
            return manifest.Items.All(i => _supplyItems.TryGetValue(i.Entity, out var item) &&
                item.Generation == request.Generation && item.Unit == i.Unit && item.Owner == request.Owner &&
                _physicalCargo.TryGetValue(i.Entity, out var proof) && proof.Unit == i.Unit &&
                proof.Current == shipment.Route[shipment.Leg] &&
                purchase.Carriers.TryGetValue(i.Entity, out var carrier) &&
                SupplyCarrierHolds(carrier, i.Entity) &&
                _hubPlanets.TryGetPhysicalLocation(carrier, out var planet, out _) &&
                Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id == proof.Current)
                ? SWGlobalWarReturnFailure.None : SWGlobalWarReturnFailure.Changed;
        }
        if (manifest.Custody is not { } custody ||
            !_supplyContainers.TryGetContainer(custody, SupplyContainer, out var held) ||
            manifest.Items.Any(i => !held.Contains(i.Entity) || !_supplyItems.TryGetValue(i.Entity, out var item) ||
                item.Generation != request.Generation || item.Unit != i.Unit || item.Owner != request.Owner))
            return SWGlobalWarReturnFailure.Changed;
        return SWGlobalWarReturnFailure.None;
    }

    private bool CommitPhysicalSupplyReturn(SWGlobalWarShipment shipment, SWGlobalWarMarketReturnRequest request)
    {
        if (CheckPhysicalSupplyReturn(shipment, request) != SWGlobalWarReturnFailure.None)
            return false;
        var manifest = shipment.SupplyManifest!;
        var kind = _supplyPurchases[manifest.Purchase].Request.Kind;
        if (manifest.Stored && manifest.Custody == request.DestinationStore)
        {
            // The confirmed items already occupy this exact store. Reauthorizing captured custody moves nothing.
            manifest.Recipient = request.DestinationStore;
            manifest.DestinationRevision = Guid.NewGuid();
            return true;
        }
        if (manifest.Stored)
        {
            var custody = CreateSupplyEscrow();
            var target = _supplyContainers.EnsureContainer<Container>(custody, SupplyContainer);
            _supplyContainers.TryGetContainer(manifest.Custody!.Value, SupplyContainer, out var source);
            foreach (var item in manifest.Items)
            {
                if (_supplyContainers.Insert(item.Entity, target))
                    continue;
                foreach (var moved in target.ContainedEntities.ToArray())
                    _supplyContainers.Insert(moved, source!, force: true);
                Del(custody);
                return false;
            }
            manifest.Custody = custody;
            manifest.Stored = false;
        }
        ReleaseManifestPlace(manifest, kind);
        manifest.Recipient = request.DestinationStore;
        manifest.DestinationRevision = Guid.NewGuid();
        var key = (request.DestinationStore!.Value, kind);
        _supplyPlaces[key] = _supplyPlaces.GetValueOrDefault(key) + manifest.Items.Count;
        manifest.SpaceReserved = true;
        return true;
    }

    private void ReleaseManifestPlace(SWGlobalWarSupplyManifest manifest, SWGlobalWarSupplyKind kind)
    {
        if (!manifest.SpaceReserved || manifest.Recipient is not { } recipient)
            return;
        _supplyPlaces[(recipient, kind)] -= manifest.Items.Count;
        manifest.SpaceReserved = false;
    }

    private void ReleasePurchasePlaces(SWGlobalWarSupplyPurchase purchase)
    {
        var remainder = purchase.Obligation.Outstanding;
        foreach (var shipment in Simulation!.Shipments.Where(s => s.SupplyManifest?.Purchase == purchase.Request.Operation))
        {
            var manifest = shipment.SupplyManifest!;
            remainder -= manifest.Items.Count;
            ReleaseManifestPlace(manifest, purchase.Request.Kind);
        }
        _supplyPlaces[(purchase.Request.Recipient, purchase.Request.Kind)] -= remainder;
    }

    private List<SWGlobalWarReturnDestination> SupplyReturnDestinations(SWGlobalWarAssignment actor,
        SWGlobalWarShipment shipment)
    {
        var purchase = _supplyPurchases[shipment.SupplyManifest!.Purchase];
        return _supplyStores.Where(p => TryGetSupplyStore(actor.Generation, p.Key, actor.Faction, out _, out _))
            .Select(p => new SWGlobalWarReturnDestination(p.Value.Planet,
                Simulation!.CheckReturn(new(actor.Generation, Guid.Empty, shipment.Id, actor.Faction,
                    p.Value.Planet, shipment.Quantity, p.Key)), GetNetEntity(p.Key), p.Value.Name,
                purchase.Request.Faction == actor.Faction && AllowedSupplyArsenal(purchase, p.Value))).ToList();
    }

    /// <summary>Changes only the finite receiving contract; the authorized caller supplies the observed remainder.</summary>
    internal bool TryApproveSupplyArsenal(Guid generation, Guid id, SWGlobalWarFaction faction, EntityUid arsenal,
        int outstanding, long revision, out SWGlobalWarSupplyFailure failure)
    {
        failure = SWGlobalWarSupplyFailure.Unavailable;
        if (!CanMutateCampaign(generation) || !_supplyPurchases.TryGetValue(id, out var purchase) ||
            purchase.Request.Generation != generation || purchase.Request.Faction != faction || purchase.ReturnPending ||
            !TryGetSupplyStore(generation, arsenal, faction, out var point, out var receiving) || !point.Arsenal)
            return false;
        failure = SWGlobalWarSupplyFailure.Revision;
        if (outstanding <= 0 || purchase.Obligation.Outstanding != outstanding || purchase.ArsenalRevision != revision)
            return false;
        if (!AllowedSupplyArsenal(purchase, point))
        {
            var stored = receiving.ContainedEntities.Count(e => _supplyItems.TryGetValue(e, out var item) &&
                item.Order == id && item.Owner == faction && !item.Accepted);
            var reserved = Simulation!.Shipments.Where(s => s.SupplyManifest is { SpaceReserved: true } m &&
                m.Purchase == id && m.Recipient == arsenal).Sum(s => s.SupplyManifest!.Items.Count);
            failure = SWGlobalWarSupplyFailure.ReceiveSpace;
            if (!HasSupplyPlace(arsenal, purchase.Request.Kind, Math.Max(0, outstanding - stored - reserved), receiving))
                return false;
            purchase.AllowedArsenals.Add(point);
            purchase.ArsenalRevision++;
        }
        failure = SWGlobalWarSupplyFailure.None;
        return true;
    }
}
