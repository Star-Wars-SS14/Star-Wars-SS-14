using System.Linq;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private void StepCampaignProduction()
    {
        if (Generation is not { } generation)
            return;
        if (_flows.Count > 0)
            PlanProductionOrders();
        foreach (var market in _markets.Values)
        {
            if (market.Profile is not { } profile)
                continue;
            market.SendBudget = Math.Min(market.HubCapacity, market.SendBudget + market.HubCapacity / 60.0);
            market.ReceiveBudget = Math.Min(market.HubCapacity, market.ReceiveBudget + market.HubCapacity / 60.0);
            // Each step accounts for its preceding second: the first fully restored interval starts at 30:00.
            var factor = (decimal) Math.Sqrt((double) market.Population);
            if (profile.HalfProductionBeforeThirtyMinutes && Time - StepDuration < TimeSpan.FromMinutes(30))
                factor /= 2;
            foreach (var good in market.Goods.Values)
                good.ProductionDemandPerMinute = 0;
            foreach (var recipe in profile.Recipes)
            foreach (var (input, perOutput) in recipe.Inputs)
                market.Goods[input.Id].ProductionDemandPerMinute +=
                    (int) decimal.Floor(perOutput * (_flows.Count > 0
                        ? market.ProductionOrders.GetValueOrDefault(recipe.Output.Id) : recipe.PerMinute * factor) * UnitsPerItem);
            foreach (var recipe in profile.Recipes)
            {
                var commodity = recipe.Output.Id;
                var good = market.Goods[commodity];
                var ordered = _flows.Count > 0 ? market.ProductionOrders.GetValueOrDefault(commodity)
                    : recipe.PerMinute * factor;
                var progress = market.ProductionProgress.GetValueOrDefault(commodity) +
                               ordered * UnitsPerItem * StepDuration.Ticks;
                var possible = (int) decimal.Floor(progress / TimeSpan.TicksPerMinute);
                // Unused production time is not a stored ability to burst after an outage.
                market.ProductionProgress[commodity] = progress - (decimal) possible * TimeSpan.TicksPerMinute;
                var headroom = good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming;
                long demand = good.TargetStock;
                if (_flows.Count > 0)
                {
                    // Covered pipelines still dispatch their next second of replacement. Keep that small
                    // source buffer even when instantaneous destination backlog is zero before arrivals.
                    foreach (var f in FlowsFrom(market.Planet, commodity))
                        demand += Math.Max((long) FlowNeed(f), RecipientUse(f) > 0
                            ? (long) decimal.Ceiling(f.PerMinute * UnitsPerItem * StepDuration.Ticks / TimeSpan.TicksPerMinute)
                            : 0);
                    var local = MandatoryRate(market, commodity) + ProductionInputRate(market, commodity);
                    if (commodity == "SWGlobalWarF")
                        local += MandatoryRate(market, commodity) * market.GrowthFoodFraction;
                    demand += (long) decimal.Ceiling(local * UnitsPerItem * StepDuration.Ticks / TimeSpan.TicksPerMinute);
                }
                else
                {
                    foreach (var other in _markets.Values)
                    {
                        if (other == market || !other.Goods.TryGetValue(commodity, out var recipient))
                            continue;
                        demand += recipient.ImportNeed;
                    }
                }
                var quantity = (int) Math.Min(Math.Min(possible, headroom), Math.Max(0L, demand - good.Stock));
                foreach (var (input, perOutput) in recipe.Inputs)
                    quantity = Math.Min(quantity, (int) decimal.Floor(market.Goods[input.Id].Available / perOutput));
                if (quantity <= 0)
                    continue;
                // All configured virtual recipes have integral input ratios. Physical recipes complete in E.
                var inputs = recipe.Inputs.Select(input => (Good: market.Goods[input.Key.Id],
                    Quantity: checked((int) (quantity * input.Value)))).ToArray();
                foreach (var input in inputs)
                {
                    var remaining = input.Quantity;
                    foreach (var batch in input.Good.MutableBatches!.Values.ToArray())
                    {
                        var take = Math.Min(remaining, batch.Quantity - batch.Reserved);
                        if (take == 0)
                            continue;
                        TakeBatch(input.Good, batch, take);
                        remaining -= take;
                        if (remaining == 0)
                            break;
                    }
                    input.Good.RecipeConsumed += input.Quantity;
                }
                AddBatch(good, new SWGlobalWarMarketBatch(Guid.NewGuid(), generation, market.Owner, market.Owner, quantity));
                good.Produced += quantity;
            }
        }
    }
}
