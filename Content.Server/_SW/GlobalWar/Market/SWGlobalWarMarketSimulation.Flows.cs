using System.Linq;
using System.Runtime.InteropServices;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    private readonly Dictionary<string, SWGlobalWarMarketFlow> _flows = new();
    private readonly List<string> _productionPlanningOrder = new();
    // Incoming is a space obligation, not stock. Old destinations keep their own obligation after flow edits.
    private readonly Dictionary<(string Flow, string Destination, string Commodity), int> _flowIncoming = new();
    private readonly Dictionary<(string Planet, string Commodity), int> _stepReceipts = new();
    // Valid for one planning pass. Routes are immutable arrays shared by the shipments of a contract.
    private readonly Dictionary<SWGlobalWarPlannedReceipt, bool> _plannedReceipts = new();
    // The graph and travel times are fixed at construction, and route arrays are never changed in place.
    private readonly Dictionary<string[], (decimal Minutes, decimal Divisor)> _routeCosts = new();
    // Lookups for the planner and dispatcher, in _flows order: decimal sums round, so their order is part of the result.
    private readonly Dictionary<(string Planet, string Commodity), List<SWGlobalWarMarketFlow>> _flowsFrom = new();
    private readonly Dictionary<(string Planet, string Commodity), List<SWGlobalWarMarketFlow>> _flowsTo = new();
    private readonly Dictionary<SWGlobalWarFlowPriority, SWGlobalWarMarketFlow[]> _dispatchCandidates = new();
    private readonly List<(SWGlobalWarMarketFlow Flow, int Order, decimal Buffer, int Tie)> _dispatchOrder = new();
    private static readonly List<SWGlobalWarMarketFlow> NoFlows = new();
    private static readonly SWGlobalWarFlowPriority[] Priorities = Enum.GetValues<SWGlobalWarFlowPriority>();
    private bool _flowLookupsStale = true;
    // Exact repeated questions inside one dispatch loop, batch, quantity and capture evidence included.
    private readonly Dictionary<SWGlobalWarTradeRequest, bool> _dispatchAnswers = new();
    private bool _dispatchAnswersOpen;
    public IReadOnlyDictionary<string, SWGlobalWarMarketFlow> Flows => _flows;

    internal void ConfigureFlows(IEnumerable<SWGlobalWarEconomyFlowPrototype> definitions)
    {
        if (Generation == null || Time != TimeSpan.Zero || _flows.Count > 0)
            throw new InvalidOperationException("Default contracts are configured once at campaign creation.");
        foreach (var definition in definitions.OrderBy(d => d.ID, StringComparer.Ordinal))
        {
            var flow = new SWGlobalWarMarketFlow(definition);
            if (flow.Source == flow.Destination || flow.Share < 0 || !_commodities.ContainsKey(flow.Commodity) ||
                !_markets.TryGetValue(flow.Source, out var source) || !_markets.TryGetValue(flow.Destination, out var destination) ||
                !source.Goods.ContainsKey(flow.Commodity) || !destination.Goods.ContainsKey(flow.Commodity))
                throw new ArgumentException($"Invalid economic flow {flow.Id}.");
            flow.Route = FindRoute(flow.Source, flow.Destination, _commodities[flow.Commodity].CargoVolume, false);
            if (flow.Route == null)
                throw new ArgumentException($"Economic flow {flow.Id} has no structural route.");
            flow.ContractFaction = source.Owner == SWGlobalWarFaction.Neutral ? destination.Owner : source.Owner;
            flow.ExpectedRecipient = destination.Owner;
            _flows.Add(flow.Id, flow);
            _flowLookupsStale = true;
            if (source.Owner == SWGlobalWarFaction.Neutral)
                _neutralOfferDefinitions.Add((definition, flow.ContractFaction));
        }
        // Plan finished goods before their ingredients, independently of market/prototype enumeration.
        var recipes = _markets.Values.Where(m => m.Profile != null).SelectMany(m => m.Profile!.Recipes).ToArray();
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        foreach (var commodity in recipes.Select(r => r.Output.Id).Distinct().Order(StringComparer.Ordinal))
            Visit(commodity);

        void Visit(string commodity)
        {
            if (visited.Contains(commodity))
                return;
            if (!visiting.Add(commodity))
                throw new ArgumentException("Virtual production recipes must not form an ingredient cycle.");
            foreach (var consumer in recipes.Where(r => r.Inputs.ContainsKey(commodity))
                         .Select(r => r.Output.Id).Distinct().Order(StringComparer.Ordinal))
                Visit(consumer);
            visiting.Remove(commodity);
            visited.Add(commodity);
            _productionPlanningOrder.Add(commodity);
        }
    }

    private decimal MandatoryRate(SWGlobalWarMarketState market, string commodity)
    {
        return market.Goods[commodity].BaseConsumptionPerMinute * market.Population / UnitsPerItem;
    }

    private decimal MaximumProduction(SWGlobalWarMarketState market, string commodity)
    {
        if (market.Profile is not { } profile)
            return 0;
        foreach (var recipe in profile.Recipes)
        {
            if (recipe.Output.Id != commodity)
                continue;
            var rate = recipe.PerMinute * (decimal) Math.Sqrt((double) market.Population);
            return profile.HalfProductionBeforeThirtyMinutes && Time - StepDuration < TimeSpan.FromMinutes(30)
                ? rate / 2 : rate;
        }
        return 0;
    }

    private decimal ProductionInputRate(SWGlobalWarMarketState market, string commodity)
    {
        if (market.Profile is not { } profile)
            return 0;
        var rate = 0m;
        foreach (var recipe in profile.Recipes)
            rate += market.ProductionOrders.GetValueOrDefault(recipe.Output.Id) * recipe.Inputs.GetValueOrDefault(commodity);
        return rate;
    }

    private List<SWGlobalWarMarketFlow> FlowsFrom(string source, string commodity)
    {
        RefreshFlowLookups();
        return _flowsFrom.GetValueOrDefault((source, commodity)) ?? NoFlows;
    }

    private List<SWGlobalWarMarketFlow> FlowsTo(string destination, string commodity)
    {
        RefreshFlowLookups();
        return _flowsTo.GetValueOrDefault((destination, commodity)) ?? NoFlows;
    }

    private void RefreshFlowLookups()
    {
        if (!_flowLookupsStale)
            return;
        _flowLookupsStale = false;
        _flowsFrom.Clear();
        _flowsTo.Clear();
        _dispatchCandidates.Clear();
        foreach (var flow in _flows.Values)
        {
            Add(_flowsFrom, flow.Source);
            Add(_flowsTo, flow.Destination);

            void Add(Dictionary<(string, string), List<SWGlobalWarMarketFlow>> lookup, string planet)
            {
                if (!lookup.TryGetValue((planet, flow.Commodity), out var flows))
                    lookup.Add((planet, flow.Commodity), flows = new());
                flows.Add(flow);
            }
        }
        // Stable node order makes the tie rotation independent of prototype enumeration and flow names.
        foreach (var group in _flows.Values.GroupBy(f => f.Priority))
            _dispatchCandidates.Add(group.Key, group.OrderBy(f => f.Destination, StringComparer.Ordinal)
                .ThenBy(f => f.Source, StringComparer.Ordinal).ThenBy(f => f.Commodity, StringComparer.Ordinal)
                .ThenBy(f => f.Id, StringComparer.Ordinal).ToArray());
    }

    private (decimal Minutes, decimal Divisor) RouteCost(string[] route)
    {
        if (_routeCosts.TryGetValue(route, out var cost))
            return cost;
        var minutes = 0m;
        for (var leg = 1; leg < route.Length; leg++)
            minutes += (decimal) _connections[(route[leg - 1], route[leg])].TravelTime.Ticks / TimeSpan.TicksPerMinute;
        cost = (minutes, UnitsPerItem * Math.Max(minutes, (decimal) StepDuration.Ticks / TimeSpan.TicksPerMinute));
        _routeCosts.Add(route, cost);
        return cost;
    }

    private void UpdateFlowRates()
    {
        foreach (var flow in _flows.Values)
        {
            if (!flow.Enabled || !FlowDispatchAuthorized(flow))
            {
                flow.PerMinute = 0;
                flow.WaitReason = flow.Enabled ? "sw-global-war-economy-wait-ownership" : string.Empty;
                continue;
            }
            flow.PerMinute = DesiredFlowRate(flow);
        }
    }

    private void PlanProductionOrders()
    {
        UpdateFlowRates();
        var incomingRates = new Dictionary<(string Planet, string Commodity), decimal>();
        _plannedReceipts.Clear();
        foreach (var shipment in _shipments)
        {
            if (!CanPlanReceiptOnce(shipment))
                continue;
            // Actual cargo spread over its route duration estimates supported supply. A nominal contract alone
            // contributes nothing; a blocked final leg contributes nothing until it becomes receivable again.
            ref var incoming = ref CollectionsMarshal.GetValueRefOrAddDefault(incomingRates,
                (shipment.Destination, shipment.Commodity), out _);
            incoming += shipment.Quantity / RouteCost(shipment.Route).Divisor;
        }
        foreach (var commodity in _productionPlanningOrder)
        {
            var orders = new List<(SWGlobalWarMarketState Market, decimal Rate)>();
            foreach (var market in _markets.Values)
            {
                if (market.Profile?.Recipes.Any(r => r.Output.Id == commodity) != true)
                    continue;
                var good = market.Goods[commodity];
                var exports = PlannedExports(market.Planet, commodity, true);
                var local = MandatoryRate(market, commodity) + ProductionInputRate(market, commodity);
                if (commodity == "SWGlobalWarF")
                    local += MandatoryRate(market, commodity) * market.GrowthFoodFraction;
                // A contract is desired supply, not material. Only existing stock and currently receivable cargo
                // reduce the local order; a blocked or material-starved supplier cannot promise away production.
                // Signed replenishment also consumes excess finished stock before ordering more raw materials.
                var replenish = (good.TargetStock - good.Available) /
                    (decimal) UnitsPerItem / SWGlobalWarMarketConstants.StockBufferMinutes;
                var headroom = good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming;
                var rate = headroom <= 0 ? 0 : Math.Clamp(local + exports + replenish -
                    incomingRates.GetValueOrDefault((market.Planet, commodity)),
                    0, MaximumProduction(market, commodity));
                orders.Add((market, rate));
            }
            foreach (var (market, rate) in orders)
                market.ProductionOrders[commodity] = rate;
            // Ingredient targets and contracts use this plan, not the previous second's recipe orders.
            foreach (var market in _markets.Values)
            foreach (var (input, good) in market.Goods)
                good.ProductionDemandPerMinute = (int) decimal.Floor(ProductionInputRate(market, input) * UnitsPerItem);
            UpdateFlowRates();
        }
    }

    private decimal RecipientUse(SWGlobalWarMarketFlow flow)
    {
        var recipient = _markets[flow.Destination];
        var use = MandatoryRate(recipient, flow.Commodity) + ProductionInputRate(recipient, flow.Commodity) +
                  PlannedExports(flow.Destination, flow.Commodity, false);
        if (flow.Commodity == "SWGlobalWarF")
            use += MandatoryRate(recipient, flow.Commodity) * recipient.GrowthFoodFraction;
        return use;
    }

    /// <summary>
    /// The planning loop only reads, so one pass asks the policy once per distinct question. The policy reads a batch's
    /// identity and quantity only through its capture evidence and a positive-quantity check; anything else is in the key.
    /// </summary>
    private bool CanPlanReceiptOnce(SWGlobalWarShipment shipment)
    {
        if (shipment.Batch is not { Capture: null, Quantity: > 0 } batch)
            return CanPlanReceipt(shipment);
        var key = new SWGlobalWarPlannedReceipt(shipment.Route, shipment.Leg, shipment.ArrivesAt != null,
            shipment.Commodity, batch.Generation, batch.Origin, batch.Owner);
        if (!_plannedReceipts.TryGetValue(key, out var allowed))
            _plannedReceipts.Add(key, allowed = CanPlanReceipt(shipment));
        return allowed;
    }

    private bool CanPlanReceipt(SWGlobalWarShipment shipment)
    {
        if (shipment.Batch is not { } batch || TradePolicy == null)
            return false;
        var request = new SWGlobalWarTradeRequest(batch.Generation, shipment.Commodity, batch.Origin,
            batch.Owner, shipment.Source, shipment.Destination, shipment.Route[shipment.Leg],
            shipment.Destination, SWGlobalWarTradeStage.Receipt, Batch: batch.Id, Quantity: batch.Quantity,
            Capture: batch.Capture);
        for (var leg = shipment.Leg; leg < shipment.Route.Length - 1; leg++)
        {
            if (!TradePolicy(request with { Current = shipment.Route[leg], Next = shipment.Route[leg + 1],
                    Stage = SWGlobalWarTradeStage.NextLeg,
                    Position = leg == shipment.Leg && shipment.ArrivesAt != null
                        ? SWGlobalWarTradePosition.InTransit : SWGlobalWarTradePosition.AtNode }).Allowed)
                return false;
        }
        return TradePolicy(request with { Current = shipment.Destination }).Allowed;
    }

    private int FlowNeed(SWGlobalWarMarketFlow flow)
    {
        if (flow.PerMinute <= 0 || flow.Route == null)
            return 0;
        var recipient = _markets[flow.Destination];
        var good = recipient.Goods[flow.Commodity];
        var totalRate = TotalRateTo(flow);
        if (totalRate <= 0)
            return 0;
        var share = flow.PerMinute / totalRate;
        var minutes = RouteCost(flow.Route).Minutes;
        var use = RecipientUse(flow);
        var stock = good.Stock;
        // Existing local production is an alternative source, not a reason to erase an explicit import contract.
        // Import may replace actual use, but an output buffer with no consumer cannot manufacture demand.
        if (good.LocallyProduced)
        {
            use = Math.Min(use, totalRate);
            // Exports left at an earlier priority than this step's receipts, so those receipts sit above the export
            // floor. That phase offset is not surplus and must not shorten the import pipeline.
            stock -= _stepReceipts.GetValueOrDefault((flow.Destination, flow.Commodity));
        }
        var incoming = _flowIncoming.GetValueOrDefault((flow.Id, flow.Destination, flow.Commodity));
        var wanted = decimal.Ceiling((good.TargetStock + use * UnitsPerItem * minutes - stock -
                                     good.ReservedIncoming) * share) - incoming;
        return (int) Math.Min(Math.Max(0, wanted), good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming);
    }

    private decimal TotalRateTo(SWGlobalWarMarketFlow flow)
    {
        var rate = 0m;
        foreach (var other in FlowsTo(flow.Destination, flow.Commodity))
            rate += other.PerMinute;
        return rate;
    }

    private void DispatchFlows()
    {
        RefillNeutralOffers();
        _stepReceipts.Clear();
        foreach (var flow in _flows.Values)
        {
            var minute = flow.PerMinute * UnitsPerItem * TimeSpan.TicksPerMinute;
            // A slow flow of whole items saves up for one item instead of capping below it.
            if (_commodities[flow.Commodity].WholeItems && flow.PerMinute > 0)
                minute = Math.Max(minute, (decimal) UnitsPerItem * TimeSpan.TicksPerMinute);
            flow.DispatchBudget = Math.Min(minute, flow.DispatchBudget + flow.PerMinute * UnitsPerItem * StepDuration.Ticks);
        }
        var dispatched = false;
        foreach (var priority in Priorities)
        {
            AdvanceWaitingShipments(priority);
            if (priority == SWGlobalWarFlowPriority.BasicMilitary)
                AdvanceSupplyShipments();
            if (IsActive?.Invoke() == false)
                return;
            RefreshFlowLookups();
            if (!_dispatchCandidates.TryGetValue(priority, out var candidates))
                continue;
            var turn = (int) (_dispatchTurn % candidates.Length);
            _dispatchOrder.Clear();
            for (var index = 0; index < candidates.Length; index++)
                _dispatchOrder.Add((candidates[index], candidates[index].Order, FlowBufferMinutes(candidates[index]),
                    (index + turn) % candidates.Length));
            _dispatchOrder.Sort(static (a, b) =>
            {
                var order = a.Order.CompareTo(b.Order);
                if (order == 0)
                    order = a.Buffer.CompareTo(b.Buffer);
                return order != 0 ? order : a.Tie.CompareTo(b.Tie);
            });
            // Dispatching moves goods only; ownership and diplomacy hold still until the loop ends.
            _dispatchAnswers.Clear();
            _dispatchAnswersOpen = true;
            try
            {
                foreach (var entry in _dispatchOrder)
                {
                    var quantity = DispatchableQuantity(entry.Flow);
                    if (quantity > 0 && DispatchFlow(entry.Flow, quantity) > 0)
                        dispatched = true;
                }
            }
            finally
            {
                _dispatchAnswersOpen = false;
            }
        }
        if (dispatched)
            _dispatchTurn++;
    }

    private SWGlobalWarFlowPriority ShipmentPriority(SWGlobalWarShipment shipment)
    {
        return shipment.Flow is { } id && _flows.TryGetValue(id, out var flow) ? flow.Priority :
            SWGlobalWarFlowPriority.DomesticCivilian;
    }

    private decimal FlowBufferMinutes(SWGlobalWarMarketFlow flow)
    {
        var recipient = _markets[flow.Destination];
        var good = recipient.Goods[flow.Commodity];
        var rate = MandatoryRate(recipient, flow.Commodity) + ProductionInputRate(recipient, flow.Commodity);
        if (rate <= 0)
            rate = TotalRateTo(flow);
        // Cargo in transit is a space reservation, not food currently available to the population.
        return rate > 0 ? good.Available / (UnitsPerItem * rate) : decimal.MaxValue;
    }

    private int DispatchableQuantity(SWGlobalWarMarketFlow flow)
    {
        if (flow.PerMinute <= 0 || flow.Route is not { } route || FlowNeed(flow) == 0)
            return 0;
        flow.WaitReason = string.Empty;
        var source = _markets[flow.Source];
        var good = source.Goods[flow.Commodity];
        var volume = _commodities[flow.Commodity].CargoVolume;
        var lane = Lane(route[0], route[1]);
        var quantity = Math.Min(FlowNeed(flow), Math.Min(good.Supply,
            (int) decimal.Floor(flow.DispatchBudget / TimeSpan.TicksPerMinute)));
        quantity = TransferQuantity(quantity, Math.Min(source.SendBudget, _remainingCapacity[lane]), volume);
        if (quantity == 0)
        {
            flow.WaitReason = good.Supply == 0 ? "sw-global-war-market-wait-stock" :
                source.SendBudget < volume ? "sw-global-war-market-wait-send" :
                _remainingCapacity[lane] < volume ? "sw-global-war-market-wait-lane" :
                "sw-global-war-market-wait-rate";
            return 0;
        }
        var eligible = 0;
        foreach (var batch in good.MutableBatches!.Values)
        {
            if (batch.Quantity > batch.Reserved && AllowsDispatch(batch, flow.Commodity, route))
                eligible += batch.Quantity - batch.Reserved;
        }
        if (source.Owner == SWGlobalWarFaction.Neutral && flow.ContractFaction != SWGlobalWarFaction.Neutral)
        {
            var budget = _neutralOfferBudgets.GetValueOrDefault((flow.Source, flow.Commodity, flow.ContractFaction));
            quantity = Math.Min(quantity, (int) decimal.Min(int.MaxValue, decimal.Floor(budget / TimeSpan.TicksPerMinute)));
            if (quantity == 0)
                flow.WaitReason = "sw-global-war-economy-wait-offer";
        }
        if (eligible == 0)
            flow.WaitReason = "sw-global-war-market-wait-policy";
        return WholeQuantity(flow.Commodity, Math.Min(quantity, eligible));
    }

    /// <summary>Whole-item goods leave only in whole items; anything less waits for the next step.</summary>
    private int WholeQuantity(string commodity, int quantity)
    {
        return _commodities[commodity].WholeItems ? quantity - quantity % UnitsPerItem : quantity;
    }

    private int DispatchFlow(SWGlobalWarMarketFlow flow, int allocation)
    {
        var remaining = Math.Min(allocation, DispatchableQuantity(flow));
        var sent = 0;
        var source = _markets[flow.Source];
        var good = source.Goods[flow.Commodity];
        var recipient = _markets[flow.Destination].Goods[flow.Commodity];
        var route = flow.Route!;
        var first = _connections[(route[0], route[1])];
        var lane = Lane(first.From, first.To);
        var volume = _commodities[flow.Commodity].CargoVolume;
        foreach (var batch in good.MutableBatches!.Values.ToArray())
        {
            if (remaining == 0)
                break;
            if (batch.Quantity == batch.Reserved || !AllowsDispatch(batch, flow.Commodity, route))
                continue;
            var quantity = WholeQuantity(flow.Commodity, Math.Min(remaining, batch.Quantity - batch.Reserved));
            if (quantity == 0)
                continue;
            var cargo = TakeBatch(good, batch, quantity);
            recipient.Incoming += quantity;
            var obligation = (flow.Id, flow.Destination, flow.Commodity);
            _flowIncoming[obligation] = _flowIncoming.GetValueOrDefault(obligation) + quantity;
            source.SendBudget = Math.Max(0, source.SendBudget - quantity * volume);
            _remainingCapacity[lane] = Math.Max(0, _remainingCapacity[lane] - quantity * volume);
            flow.DispatchBudget -= (decimal) quantity * TimeSpan.TicksPerMinute;
            if (source.Owner == SWGlobalWarFaction.Neutral && flow.ContractFaction != SWGlobalWarFaction.Neutral)
                _neutralOfferBudgets[(flow.Source, flow.Commodity, flow.ContractFaction)] -=
                    (decimal) quantity * TimeSpan.TicksPerMinute;
            flow.Dispatched += quantity;
            _shipments.Add(new SWGlobalWarShipment(flow.Commodity, quantity, route, Time, Time + first.TravelTime)
            {
                Batch = cargo, DepartureReceipt = Guid.NewGuid(), Flow = flow.Id,
                DeliveryParticipant = _markets[flow.Destination].Owner,
            });
            sent += quantity;
            remaining -= quantity;
        }
        return sent;
    }
}

internal readonly record struct SWGlobalWarPlannedReceipt(string[] Route, int Leg, bool InTransit, string Commodity,
    Guid Generation, SWGlobalWarFaction Origin, SWGlobalWarFaction Owner);
