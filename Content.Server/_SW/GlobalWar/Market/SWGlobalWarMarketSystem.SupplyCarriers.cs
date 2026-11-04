using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SharedEntityStorageSystem _supplyCrates = default!;

    /// <summary>
    /// A task is claimed at a market console, which stands far from the lines and arsenals. Loading and handing over
    /// happen at the point itself, so its own window offers them to the worker holding that task.
    /// </summary>
    private SWGlobalWarFactoryCargoWork? FactoryCargoWork(EntityUid store, SWGlobalWarAssignment actor)
    {
        foreach (var work in _supplyWork.Values)
        {
            if (work.Assignment != actor.Id || !ActiveSupplyWork(work) ||
                !_supplyPurchases.TryGetValue(work.Purchase, out var purchase) || purchase.ReturnPending)
                continue;
            var accept = work.Status == SWGlobalWarSupplyWorkStatus.Collected;
            if ((accept ? PhysicalFreightDestination(purchase, work.Goods) : purchase.Request.Source) != store)
                continue;
            var carriers = SupplyCarriers(actor.Body);
            EntityUid? carrier = accept ? (work.Carrier is { } held && carriers.Contains(held) ? held : null) :
                carriers.Length > 0 ? carriers[0] : null;
            var request = purchase.Request;
            var item = _prototypes.Index<EntityPrototype>(SupplyPrototype(request.Kind, request.Faction, request.Bacta)).Name;
            return new(work.Id, accept, item, work.Goods.Length, carrier is { } cargo ? GetNetEntity(cargo) : null,
                carrier is { } near && SupplyAtStore(near, store) && CanReachSupplyStore(actor.Body, store));
        }
        return null;
    }

    private bool ExecuteFactoryCargoWork(SWGlobalWarAssignment actor, Guid request, Guid id, NetEntity carrier)
    {
        if (!_supplyWork.TryGetValue(id, out var work) || !TryComp<ActorComponent>(actor.Body, out var player))
            return false;
        var action = work.Status == SWGlobalWarSupplyWorkStatus.Collected ?
            SWGlobalWarSupplyWorkAction.Accept : SWGlobalWarSupplyWorkAction.Collect;
        return TryExecuteSupplyWork(player.PlayerSession,
            new(actor.Generation, request, actor.Revision, action, id, 1, carrier), out _);
    }

    /// <summary>Grid storage (backpacks, wooden boxes) or a crate's flat contents; both are real physical cargo.</summary>
    private bool IsSupplyCarrierContainer(EntityUid carrier)
    {
        return HasComp<StorageComponent>(carrier) || HasComp<EntityStorageComponent>(carrier);
    }

    /// <summary>An open crate has spilled its contents, so goods on the floor next to it are not carried by it.</summary>
    private bool SupplyCarrierHolds(EntityUid carrier, EntityUid item)
    {
        if (TryComp<StorageComponent>(carrier, out var storage))
            return storage.Container.Contains(item) && storage.StoredItems.ContainsKey(item);
        return TryComp<EntityStorageComponent>(carrier, out var crate) && crate.Contents.Contains(item);
    }

    /// <summary>A crate must be shut to take goods: EntityStorage drops an insert next to an open one.</summary>
    private bool TryCloseSupplyCarrier(EntityUid carrier)
    {
        return !TryComp<EntityStorageComponent>(carrier, out var crate) || !crate.Open ||
               _supplyCrates.TryCloseStorage(carrier);
    }

    private bool TryLoadSupplyCarrier(EntityUid carrier, EntityUid item)
    {
        if (TryComp<StorageComponent>(carrier, out var storage))
            return _supplyContainers.Insert(item, storage.Container);
        return TryComp<EntityStorageComponent>(carrier, out var crate) && !crate.Open &&
               _supplyCrates.CanInsert(item, carrier, crate) && _supplyCrates.Insert(item, carrier, crate);
    }

    private ItemStorageLocation? SupplyCarrierPosition(EntityUid carrier, EntityUid item)
    {
        return TryComp<StorageComponent>(carrier, out var storage) ? storage.StoredItems[item] : null;
    }

    /// <summary>Puts an item back exactly where a failed acceptance took it from.</summary>
    private bool TryRestoreSupplyCarrier(EntityUid carrier, EntityUid item, ItemStorageLocation? position)
    {
        // Preserve the packing before exposing a failed payment. Greedy reinsertion can strand a later item
        // even when the original set fitted, and force does not suppress Storage's insertion callbacks.
        if (position is { } location && TryComp<StorageComponent>(carrier, out var storage))
            return _supplyStorage.InsertAt((carrier, storage), (item, null), location, out _,
                       playSound: false, stackAutomatically: false) && storage.Container.Contains(item) &&
                   storage.StoredItems[item] == location;
        return TryComp<EntityStorageComponent>(carrier, out var crate) && _supplyCrates.Insert(item, carrier, crate) &&
               crate.Contents.Contains(item);
    }
}
