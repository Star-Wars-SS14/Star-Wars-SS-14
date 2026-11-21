using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Item;
using Content.Shared.Chemistry.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Containers;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private int IndustrialPrice(string commodity) =>
        MilitaryGoods.TryGetValue(commodity, out var supply)
            ? SWGlobalWarSupplyProgram.UnitPrice(supply.Kind, supply.Bacta)
            : _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).PurchasePrice;

    private bool IndustrialRequest(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        Guid request, SWGlobalWarMarketState market, SWGlobalWarFactoryAction action, string commodity,
        int quantity, ref string rejected)
    {
        if (!FactoryOperator(actor))
            return false;
        if (ent.Comp.Warehouse && action == SWGlobalWarFactoryAction.DepositMarket)
            return DepositPalletItems(ent, actor, market, ref rejected);
        if (ent.Comp.IndustrialSupply || ent.Comp.AutomaticMarketAccess)
        {
            if (action != SWGlobalWarFactoryAction.ConfigureIndustrialRate || quantity < 0 ||
                !market.Goods.TryGetValue(commodity, out var good) ||
                quantity > good.Capacity / Simulation!.UnitsPerItem || IndustrialPrice(commodity) <= 0)
                return false;
            if (quantity == 0)
                ent.Comp.PurchaseRates.Remove(commodity);
            else
                ent.Comp.PurchaseRates[commodity] = quantity;
            RefreshAutomaticPurchaseDemand();
            return true;
        }
        if (!ent.Comp.Warehouse || action != SWGlobalWarFactoryAction.BuyMarket || quantity <= 0 ||
            market.FactoryStock.GetValueOrDefault(commodity) < quantity)
            return false;
        var pallets = FindMarketPallets(ent, false);
        if (pallets.Count == 0)
        {
            rejected = "sw-global-war-terminal-no-output";
            return false;
        }
        var output = PrepareIndustrialItems(commodity, actor.Faction, quantity);
        market.FactoryStock[commodity] -= quantity;
        for (var i = 0; i < output.Count; i++)
            _supplyTransforms.SetCoordinates(output[i], Transform(pallets[i % pallets.Count]).Coordinates);
        return true;
    }

    private List<EntityUid> PrepareIndustrialItems(string commodity, SWGlobalWarFaction owner, int quantity)
    {
        var military = MilitaryGoods.TryGetValue(commodity, out var supply);
        var prototype = military ? SupplyPrototype(supply.Kind, owner, supply.Bacta) :
            _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity;
        var escrow = CreateSupplyEscrow();
        var items = new List<EntityUid>();
        try
        {
            while (quantity > 0)
            {
                var item = Spawn(prototype, Transform(escrow).Coordinates);
                items.Add(item);
                var count = 1;
                if (!military && TryComp<StackComponent>(item, out var stack))
                {
                    count = Math.Min(quantity, _supplyStacks.GetMaxCount(stack));
                    _supplyStacks.SetCount(item, count, stack);
                }
                quantity -= count;
                if (military && _supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
                    foreach (var loaded in magazine.ContainedEntities.ToArray())
                        Del(loaded);
            }
            return items;
        }
        catch
        {
            foreach (var item in items)
                Del(item);
            throw;
        }
        finally
        {
            Del(escrow);
        }
    }

    /// <summary>Rebuild contributions instead of incrementing manual demand, so removal/movement cannot leak demand.</summary>
    private void RefreshAutomaticPurchaseDemand()
    {
        if (Simulation?.Generation is not { } generation)
            return;
        var totals = new Dictionary<(string Planet, string Commodity), long>();
        var query = EntityManager.AllEntityQueryEnumerator<SWGlobalWarFactoryComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (TerminatingOrDeleted(uid) || component.Generation != generation ||
                !(component.AutomaticMarketAccess || component.IndustrialSupply) ||
                !Transform(uid).Anchored || !TryGetTerminalMarket(uid, out var market))
                continue;
            foreach (var (commodity, rate) in component.PurchaseRates)
            {
                if (rate <= 0 || !market.Goods.ContainsKey(commodity))
                    continue;
                var key = (market.Planet, commodity);
                totals[key] = Math.Min(int.MaxValue, totals.GetValueOrDefault(key) + (long) rate * Simulation.UnitsPerItem);
            }
        }
        foreach (var (planet, market) in Simulation.Markets)
        foreach (var (commodity, good) in market.Goods)
            good.AutomaticPurchaseStock = (int) totals.GetValueOrDefault((planet, commodity));
    }

    private void UpdateIndustrialPallets(float frameTime)
    {
        if (Simulation?.Generation is not { } generation || !CanMutateCampaign(generation))
            return;
        RefreshAutomaticPurchaseDemand();
        var query = EntityQueryEnumerator<SWGlobalWarFactoryComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.IndustrialSupply && !component.IndustrialOutput &&
                !(component.AutomaticMarketAccess && component.PurchaseRates.Count > 0))
                continue;
            if (!BindFactory((uid, component), out var owner) || !this.IsPowered(uid, EntityManager) ||
                !TryGetTerminalMarket(uid, out var market))
            {
                component.IndustrialElapsed = 0;
                continue;
            }
            component.IndustrialElapsed += frameTime;
            if (component.IndustrialElapsed < 1)
                continue;
            component.IndustrialElapsed %= 1;
            using var notifications = _ledger.DeferNotifications();
            _settling = true;
            try
            {
                if (component.IndustrialSupply || component.AutomaticMarketAccess)
                {
                    var outputs = component.IndustrialSupply ? new List<EntityUid> { uid } : FindMarketPallets(uid, false);
                    if (outputs.Count == 0)
                        continue;
                    foreach (var (commodity, rate) in component.PurchaseRates)
                        BuyIndustrialStock(outputs, generation, market, commodity, rate);
                }
                else
                    CollectIndustrialOutput((uid, component), generation, market);
            }
            finally
            {
                _settling = false;
            }
        }
    }

    private void BuyIndustrialStock(IReadOnlyList<EntityUid> outputs, Guid generation, SWGlobalWarMarketState market,
        string commodity, int rate)
    {
        var price = IndustrialPrice(commodity);
        if (!market.Goods.TryGetValue(commodity, out var stock) || price <= 0)
            return;
        var quantity = (int) Math.Min(rate, Math.Min(stock.Available / Simulation!.UnitsPerItem,
            _ledger.GetBalance(market.Owner, SWGlobalWarAccount.Industry).Available / price));
        if (quantity <= 0)
            return;
        var operation = Guid.NewGuid();
        if (!Simulation.TryReserveStock(new(generation, operation, market.Planet, commodity,
                quantity * Simulation.UnitsPerItem, market.Owner, "industrial-pallet"), out _))
            return;
        var items = new List<EntityUid>();
        var committed = false;
        try
        {
            items = PrepareIndustrialItems(commodity, market.Owner, quantity);
            if (!_ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Debit,
                    market.Owner, SWGlobalWarAccount.Industry, (long) quantity * price,
                    Purpose: "industrial-purchase"), out _))
                return;
            if (!Simulation.TryCommitStock(generation, operation, out _))
                throw new InvalidOperationException("Prepared industrial withdrawal failed.");
            committed = true;
            for (var i = 0; i < items.Count; i++)
                _supplyTransforms.SetCoordinates(items[i], Transform(outputs[i % outputs.Count]).Coordinates);
        }
        finally
        {
            if (!committed)
            {
                foreach (var item in items)
                    Del(item);
                Simulation.TryCancelStock(generation, operation);
            }
        }
    }

    private void CollectIndustrialOutput(Entity<SWGlobalWarFactoryComponent> ent, Guid generation,
        SWGlobalWarMarketState market)
    {
        var candidates = new HashSet<EntityUid>();
        _factoryLookup.GetEntitiesIntersecting(ent, candidates, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var item in candidates)
        {
            if (TerminatingOrDeleted(item) || !HasComp<ItemComponent>(item) || Transform(item).Anchored ||
                Transform(item).GridUid != Transform(ent).GridUid || _supplyContainers.TryGetContainingContainer(item, out _) ||
                _supplyItems.TryGetValue(item, out var supplied) && supplied.Order != null)
                continue;
            foreach (var commodity in market.Goods.Keys)
            {
                var military = MilitaryGoods.TryGetValue(commodity, out var supply);
                if (military ? !IsSuitableSupplyItem(item, supply.Kind, market.Owner, supply.Bacta) :
                    !MatchesSupplyMaterial(item, _prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity.Id))
                    continue;
                // Food stores its own reagents as solution entities, not as cargo inside the item.
                if (TryComp<ContainerManagerComponent>(item, out var containers) &&
                    _supplyContainers.GetAllContainers(item, containers).Any(c =>
                    {
                        var id = c.ID;
                        if (military && supply.Kind == SWGlobalWarSupplyKind.Ration && id == "storagebase")
                            return false;
                        var solution = id.StartsWith("solution@", StringComparison.Ordinal);
                        return c.ContainedEntities.Any(contents => !solution || !HasComp<SolutionComponent>(contents));
                    }))
                    break;
                var quantity = _supplyStacks.GetCount(item);
                var kind = military ? supply.Kind : SWGlobalWarSupplyKind.AdvancedElectronics;
                var percent = Simulation!.SupplyPrograms[market.Owner].Lines[kind].LocalPercent;
                var fraction = ent.Comp.LocalRemainders.GetValueOrDefault(commodity) + quantity * percent / 100m;
                var local = (int) decimal.Floor(fraction);
                var sell = quantity - local;
                var stock = market.Goods[commodity];
                var price = IndustrialPrice(commodity);
                if (quantity <= 0 || sell > 0 && (price <= 0 ||
                    sell > (stock.Capacity - stock.Stock - stock.Incoming - stock.ReservedIncoming) / Simulation.UnitsPerItem) ||
                    (long) market.FactoryStock.GetValueOrDefault(commodity) + local > int.MaxValue)
                    break;
                var proceeds = ent.Comp.SaleRemainder + (decimal) sell * price * 0.75m;
                var money = (long) decimal.Floor(proceeds);
                if (money > 0 && !_ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), 0,
                        SWGlobalWarLedgerAction.Credit, market.Owner, SWGlobalWarAccount.Industry, money,
                        Purpose: "industrial-sale"), out _))
                    break;
                if (sell > 0 && !Simulation.DepositMarketStock(market.Planet, commodity, sell * Simulation.UnitsPerItem))
                    throw new InvalidOperationException("Prepared industrial sale exceeded capacity.");
                market.FactoryStock[commodity] = market.FactoryStock.GetValueOrDefault(commodity) + local;
                ent.Comp.LocalRemainders[commodity] = fraction - local;
                ent.Comp.SaleRemainder = proceeds - money;
                _supplyItems.Remove(item);
                Del(item);
                break;
            }
        }
    }
}
