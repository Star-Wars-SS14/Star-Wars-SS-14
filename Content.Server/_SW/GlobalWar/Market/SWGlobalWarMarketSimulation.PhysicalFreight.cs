using System.Linq;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    /// <summary>Registers real carrier references in the existing shared lane/send/receive scheduler atomically.</summary>
    internal bool TryQueuePhysicalSupplyManifests(string commodity, string source, string destination,
        IReadOnlyList<SWGlobalWarSupplyManifest> manifests)
    {
        if (manifests.Count == 0 || manifests.Any(m => !m.AwaitingPhysical))
            return false;
        var queued = new List<SWGlobalWarShipment>();
        foreach (var manifest in manifests)
        {
            // The owning system has revalidated its epoch after container callbacks and holds its reentrancy guard.
            if (TryQueueSupplyManifest(commodity, source, destination, manifest, out var shipment, ownerSettlement: true))
            {
                queued.Add(shipment);
                continue;
            }
            foreach (var rollback in queued)
                _shipments.Remove(rollback);
            return false;
        }
        return true;
    }

    /// <summary>Closes only the paid physical tranche after D's ordinary acceptance transaction committed.</summary>
    internal void CompletePhysicalSupplyItems(Guid generation, Guid purchase, IReadOnlyList<EntityUid> goods)
    {
        if (Generation != generation)
            return;
        var accepted = goods.ToHashSet();
        foreach (var shipment in _shipments.Where(s => s.SupplyManifest is { AwaitingPhysical: true } manifest &&
                     manifest.Purchase == purchase && manifest.Items.Any(i => accepted.Contains(i.Entity))).ToArray())
        {
            var manifest = shipment.SupplyManifest!;
            var completed = manifest.Items.Where(i => accepted.Contains(i.Entity)).ToArray();
            _closedSupplyManifests.Add(new(shipment.Id, manifest.Id, purchase,
                completed.Select(i => i.Unit).ToArray(), shipment.Destination, Time));
            manifest.Items = manifest.Items.Where(i => !accepted.Contains(i.Entity)).ToArray();
            shipment.Quantity = manifest.Items.Count * UnitsPerItem;
            if (manifest.Items.Count == 0)
            {
                manifest.SpaceReserved = false;
                _shipments.Remove(shipment);
            }
        }
    }
}
