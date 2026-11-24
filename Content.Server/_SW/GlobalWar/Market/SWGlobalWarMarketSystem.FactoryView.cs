using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    /// <summary>What this line can make and what it has for that, for the window. Reads only.</summary>
    private List<SWGlobalWarFactoryProduct> FactoryProducts(Entity<SWGlobalWarFactoryComponent> ent,
        SWGlobalWarAssignment actor, BaseContainer inventory)
    {
        var products = new List<SWGlobalWarFactoryProduct>();
        var hasStore = _supplyStores.TryGetValue(ent, out var store);
        foreach (var kind in FactoryKinds(ent))
        {
            var recipe = SupplyRecipe(kind, false);
            var costs = recipe.Raw.Select(pair =>
            {
                var commodity = _prototypes.Index<SWGlobalWarCommodityPrototype>(pair.Key);
                var have = hasStore && Simulation != null && Simulation.Markets.TryGetValue(store!.Planet, out var market) &&
                    market.Goods.TryGetValue(pair.Key, out var good) ? good.Available / SWGlobalWarMarketUnits.PerUnit : 0;
                return new SWGlobalWarFactoryCost(Loc.GetString(commodity.Name), commodity.Entity, pair.Value, have);
            }).Concat(recipe.Physical.Select(pair => FactoryCost(pair.Key, pair.Value, inventory))).ToList();
            var seconds = kind switch
            {
                SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.Magazine or
                    SWGlobalWarSupplyKind.Ration or SWGlobalWarSupplyKind.AdvancedElectronics => 30,
                _ => 60,
            };
            products.Add(new(kind, null, Loc.GetString("sw-global-war-factory-kind-" + (int) kind),
                SupplyPrototype(kind, actor.Faction), recipe.Output, seconds, costs, costs.All(c => c.Have >= c.Need)));
        }

        foreach (var recipe in FactoryRecipes(ent))
        {
            var costs = recipe.Materials.Select(pair => FactoryCost(pair.Key, pair.Value, inventory)).ToList();
            products.Add(new(null, recipe.Id,
                string.Join(", ", recipe.Results.Select(p => _prototypes.Index<EntityPrototype>(p.Key).Name)),
                recipe.Results.Keys.FirstOrDefault(), recipe.Results.Values.Sum(), (int) recipe.Duration.TotalSeconds,
                costs, costs.All(c => c.Have >= c.Need)));
        }

        return products;
    }

    private SWGlobalWarFactoryCost FactoryCost(string prototype, int need, BaseContainer inventory)
    {
        var have = inventory.ContainedEntities.Where(item => MatchesSupplyMaterial(item, prototype))
            .Sum(item => _supplyStacks.GetCount(item));
        return new(_prototypes.Index<EntityPrototype>(prototype).Name, prototype, need, have);
    }
}
