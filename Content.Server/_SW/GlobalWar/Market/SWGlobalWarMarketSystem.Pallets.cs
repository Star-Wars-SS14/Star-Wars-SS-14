using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Item;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private const float MarketPalletRange = 8f;

    private List<EntityUid> FindMarketPallets(EntityUid terminal, bool deposit)
    {
        var transform = Transform(terminal);
        if (transform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridData))
            return [];
        var position = _supplyTransforms.GetWorldPosition(terminal);
        var byTile = new Dictionary<Vector2i, List<EntityUid>>();
        var distances = new Dictionary<EntityUid, float>();
        var connected = new HashSet<Vector2i>();
        var pending = new Queue<Vector2i>();
        var query = EntityQueryEnumerator<SWGlobalWarMarketPalletComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pallet, out var location))
        {
            if (pallet.Deposit != deposit || !location.Anchored || location.GridUid != grid ||
                TerminatingOrDeleted(uid))
                continue;
            var tile = _hubMaps.TileIndicesFor(grid, gridData, location.Coordinates);
            if (!byTile.TryGetValue(tile, out var occupants))
                byTile[tile] = occupants = new();
            occupants.Add(uid);
            var distance = Vector2.DistanceSquared(position, _supplyTransforms.GetWorldPosition(uid));
            distances[uid] = distance;
            if (distance <= MarketPalletRange * MarketPalletRange && connected.Add(tile))
                pending.Enqueue(tile);
        }

        // The radius selects entry points. Continue through the entire edge-connected pallet group.
        Vector2i[] offsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        while (pending.TryDequeue(out var tile))
        {
            foreach (var offset in offsets)
            {
                var neighbor = tile + offset;
                if (byTile.ContainsKey(neighbor) && connected.Add(neighbor))
                    pending.Enqueue(neighbor);
            }
        }
        return connected.SelectMany(tile => byTile[tile]).OrderBy(uid => distances[uid])
            .ThenBy(uid => uid.Id).ToList();
    }

    private bool DepositPalletItems(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        SWGlobalWarMarketState market, ref string rejected)
    {
        var pallets = FindMarketPallets(ent, deposit: true);
        if (pallets.Count == 0)
        {
            rejected = "sw-global-war-terminal-no-input";
            return false;
        }
        var candidates = new HashSet<EntityUid>();
        foreach (var pallet in pallets)
            _factoryLookup.GetEntitiesIntersecting(pallet, candidates, LookupFlags.Dynamic | LookupFlags.Sundries);
        var items = new List<(EntityUid Item, string Commodity, int Units)>();
        var totals = new Dictionary<string, int>();
        var refunds = new List<SWGlobalWarLedgerCommand>();
        foreach (var item in candidates.OrderBy(e => e.Id))
        {
            if (TerminatingOrDeleted(item) || !HasComp<ItemComponent>(item) || Transform(item).Anchored ||
                Transform(item).GridUid != Transform(ent).GridUid ||
                _supplyContainers.TryGetContainingContainer(item, out _) ||
                _supplyItems.TryGetValue(item, out var supplied) && supplied.Order != null)
                continue;
            var commodity = MarketDepositCommodity(ent, actor, market, item);
            if (commodity == null)
                continue;
            var stock = market.Goods[commodity];
            var units = _supplyStacks.GetCount(item);
            var total = (long) totals.GetValueOrDefault(commodity) + units;
            var space = stock.Capacity - stock.Stock - stock.Incoming - stock.ReservedIncoming;
            if (units <= 0 || total > space / Simulation!.UnitsPerItem)
            {
                rejected = FactoryRejectedSpace;
                return false;
            }
            totals[commodity] = (int) total;
            items.Add((item, commodity, units));
            if (TryComp<SWGlobalWarMarketReceiptComponent>(item, out var receipt) &&
                receipt.Generation == actor.Generation && receipt.Commodity == commodity && receipt.Price > 0)
                refunds.Add(new(actor.Generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Credit,
                    receipt.Payer, SWGlobalWarAccount.War, receipt.Price, Purpose: "planetary-market-refund"));
        }
        if (items.Count == 0)
        {
            rejected = "sw-global-war-terminal-empty-input";
            return false;
        }
        if (refunds.Count > 0 && !_ledger.TryExecuteSystemBatch(Guid.NewGuid(), refunds))
            return false;
        foreach (var (commodity, units) in totals)
            if (!Simulation!.DepositMarketStock(market.Planet, commodity, units * Simulation.UnitsPerItem))
                throw new InvalidOperationException("Prepared pallet deposit exceeded market capacity.");
        foreach (var (item, _, _) in items)
        {
            _supplyItems.Remove(item);
            Del(item);
        }
        return true;
    }

    private string? MarketDepositCommodity(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        SWGlobalWarMarketState market, EntityUid item)
    {
        foreach (var commodity in market.Goods.Keys)
        {
            var military = MilitaryGoods.TryGetValue(commodity, out var supply);
            if (ent.Comp.Arsenal && !military)
                continue;
            if (military)
            {
                if (!IsSuitableSupplyItem(item, supply.Kind, actor.Faction, supply.Bacta))
                    continue;
                // Ration contents are checked by IsSuitableSupplyItem. Other goods must be empty.
                if (TryComp<ContainerManagerComponent>(item, out var containers) &&
                    _supplyContainers.GetAllContainers(item, containers).Any(c =>
                        c.ContainedEntities.Count > 0 && !(supply.Kind == SWGlobalWarSupplyKind.Ration &&
                                                         c.ID == "storagebase")))
                    continue;
                return commodity;
            }
            if (MatchesSupplyMaterial(item,
                    _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity.Id))
                return commodity;
        }
        return null;
    }
}
