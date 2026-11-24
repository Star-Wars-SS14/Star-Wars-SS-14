using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Single-owner, map-independent stock simulation with virtual, capacity-limited freight.</summary>
public sealed partial class SWGlobalWarMarketSimulation
{
    public static readonly TimeSpan StepDuration = TimeSpan.FromSeconds(1);
    private const double Epsilon = 1e-8;
    private readonly Dictionary<string, SWGlobalWarCommodityDefinition> _commodities = new();
    private readonly Dictionary<string, SWGlobalWarMarketState> _markets = new();
    private readonly Dictionary<(string From, string To), SWGlobalWarTradeConnection> _connections = new();
    private readonly Dictionary<string, List<SWGlobalWarTradeConnection>> _outgoing = new();
    private readonly Dictionary<(string A, string B), double> _laneCapacity = new();
    // Free volume changes only when cargo departs or finishes its current leg.
    private readonly Dictionary<(string A, string B), double> _remainingCapacity = new();
    private readonly List<SWGlobalWarShipment> _shipments = new();
    private readonly Dictionary<SWGlobalWarWaitingTrade, SWGlobalWarTradeDecision> _waitingTrades = new();
    private readonly Dictionary<(string Source, string Destination, double Volume), string[]?> _structuralRoutes = new();
    private TimeSpan _pendingTime;
    private long _dispatchTurn;
    private bool _advancing;
    internal Func<SWGlobalWarTradeRequest, SWGlobalWarTradeDecision>? TradePolicy;
    internal Func<bool>? IsActive;

    public TimeSpan Time { get; private set; }
    public Guid? Generation { get; }
    public int UnitsPerItem => Generation == null ? 1 : SWGlobalWarMarketUnits.PerUnit;
    public bool DemographyEnabled { get; set; } = true;
    public IReadOnlyDictionary<string, SWGlobalWarMarketState> Markets => _markets;
    public IReadOnlyList<SWGlobalWarShipment> Shipments => _shipments;
    public IReadOnlyDictionary<(string From, string To), SWGlobalWarTradeConnection> Connections => _connections;

    public SWGlobalWarMarketSimulation(IEnumerable<SWGlobalWarCommodityDefinition> commodities,
        IEnumerable<SWGlobalWarMarketDefinition> markets, IEnumerable<SWGlobalWarTradeConnection> connections,
        Guid? generation = null)
    {
        if (generation == Guid.Empty)
            throw new ArgumentException("A campaign market needs a non-empty generation.", nameof(generation));
        Generation = generation;
        foreach (var commodity in commodities.OrderBy(commodity => commodity.Id, StringComparer.Ordinal))
        {
            if (!double.IsFinite(commodity.CargoVolume) || commodity.CargoVolume <= 0)
                throw new ArgumentException($"Commodity {commodity.Id} needs a positive finite cargo volume.");
            _commodities.Add(commodity.Id, commodity with { CargoVolume = commodity.CargoVolume / UnitsPerItem });
        }
        foreach (var definition in markets.OrderBy(market => market.Planet, StringComparer.Ordinal))
        {
            var market = new SWGlobalWarMarketState(definition.Planet)
            {
                Owner = definition.Owner, Profile = definition.Profile,
                OriginalOwner = definition.Owner, Kind = definition.Kind, Enclave = definition.Enclave,
                HubCapacity = definition.Profile?.HubCapacity ?? 0,
            };
            foreach (var good in definition.Goods.OrderBy(good => good.Commodity, StringComparer.Ordinal))
            {
                if (!_commodities.ContainsKey(good.Commodity) || good.Stock < 0 || !ValidConsumption(good.ConsumptionPerMinute))
                    throw new ArgumentException($"Invalid market good {definition.Planet}/{good.Commodity}.");
                var state = new SWGlobalWarMarketGoodState
                {
                    Stock = good.Stock,
                    ConsumptionPerMinute = good.ConsumptionPerMinute,
                    BaseConsumptionPerMinute = good.ConsumptionPerMinute,
                };
                if (generation is { } campaign)
                {
                    if (good.Stock > SWGlobalWarMarketUnits.StockCapacity)
                        throw new ArgumentException("Campaign opening stock exceeds warehouse capacity.");
                    state.Capacity = SWGlobalWarMarketUnits.StockCapacity;
                    state.MutableBatches = new();
                    if (good.Stock > 0)
                    {
                        var batch = new SWGlobalWarMarketBatch(Guid.NewGuid(), campaign,
                            definition.Owner, definition.Owner, good.Stock);
                        state.MutableBatches.Add(batch.Id, batch);
                    }
                }
                market.Goods.Add(good.Commodity, state);
            }
            if (definition.Profile is { } profile)
            {
                foreach (var recipe in profile.Recipes)
                foreach (var (input, perOutput) in recipe.Inputs)
                    market.Goods[input.Id].ProductionDemandPerMinute +=
                        SWGlobalWarMarketUnits.FromUnits(perOutput * recipe.PerMinute);
                foreach (var recipe in profile.Recipes)
                {
                    var output = market.Goods[recipe.Output.Id];
                    output.LocallyProduced = true;
                    if (recipe.Inputs.Count > 0)
                        output.MinimumBuffer = output.Stock;
                }
                foreach (var (commodity, reserve) in profile.ExportReserves)
                    market.Goods[commodity.Id].MinimumBuffer = SWGlobalWarMarketUnits.FromUnits(reserve);
            }
            _markets.Add(definition.Planet, market);
            if (definition.LanthanumReserve)
                _lanthanumDeposits.Add(definition.Planet, new(definition.Planet));
            _outgoing.Add(definition.Planet, new List<SWGlobalWarTradeConnection>());
        }
        foreach (var connection in connections.OrderBy(connection => connection.From, StringComparer.Ordinal)
                     .ThenBy(connection => connection.To, StringComparer.Ordinal))
        {
            if (connection.From == connection.To || !_markets.ContainsKey(connection.From) || !_markets.ContainsKey(connection.To)
                || connection.TravelTime <= TimeSpan.Zero || !NonNegative(connection.Capacity))
                throw new ArgumentException($"Invalid trade connection {connection.From} -> {connection.To}.");

            _connections.Add((connection.From, connection.To), connection);
            _outgoing[connection.From].Add(connection);
            var lane = Lane(connection.From, connection.To);
            var capacity = connection.Capacity;
            if (_laneCapacity.TryGetValue(lane, out var existing) && existing != capacity)
                throw new ArgumentException($"Both directions of {lane} must share the same cargo capacity.");
            _laneCapacity[lane] = capacity;
            _remainingCapacity.TryAdd(lane, capacity);
        }
    }

    private static bool NonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool ValidConsumption(int value) =>
        value >= 0 && value <= int.MaxValue / SWGlobalWarMarketConstants.StockBufferMinutes;
    private static (string A, string B) Lane(string from, string to) =>
        string.CompareOrdinal(from, to) <= 0 ? (from, to) : (to, from);

    /// <summary>Advances fixed economic steps; unused elapsed time is retained so frame rate cannot change simulation results.</summary>
    /// <param name="elapsed">Elapsed active round time, independent of whether any planet map is paused.</param>
    public void Advance(TimeSpan elapsed)
    {
        if (_advancing)
            throw new InvalidOperationException("A market step cannot be entered recursively.");
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        _advancing = true;
        try
        {
            _pendingTime += elapsed;
            while (_pendingTime >= StepDuration)
            {
                if (IsActive?.Invoke() == false)
                    break;
                _pendingTime -= StepDuration;
                Time += StepDuration;
                Step();
            }
        }
        finally
        {
            _advancing = false;
        }
    }

    private void Step()
    {
        if (Generation != null)
        {
            StepSupplyPrograms();
            StepLanthanumReserves();
            if (IsActive?.Invoke() == false)
                return;
            StepCampaignProduction();
            StepCampaignConsumption();
            if (IsActive?.Invoke() == false)
                return;
            AdvanceShipments();
            if (IsActive?.Invoke() == false)
                return;
            RunSupplyPrograms?.Invoke();
            if (IsActive?.Invoke() == false)
                return;
            Dispatch();
            return;
        }
        foreach (var market in _markets.Values)
        {
            foreach (var good in market.Goods.Values)
            {
                // Carry fractional progress exactly; inventory changes only when a whole item is due.
                good.ConsumptionProgress += good.ConsumptionPerMinute * StepDuration.Ticks;
                var required = (int) (good.ConsumptionProgress / TimeSpan.TicksPerMinute);
                good.ConsumptionProgress %= TimeSpan.TicksPerMinute;
                var consumed = Math.Min(good.Stock, required);
                good.Stock -= consumed;
                good.Consumed += consumed;
                good.UnmetConsumption += required - consumed;
            }
        }

        // Arrivals become available at the step boundary, after consumption for the preceding second.
        AdvanceShipments();
        Dispatch();
    }

    /// <summary>Releases completed legs before the shared priority scheduler forwards campaign cargo.</summary>
    private void AdvanceShipments()
    {
        for (var i = 0; i < _shipments.Count; i++)
        {
            var shipment = _shipments[i];
            if (shipment.ArrivesAt is not { } arrival || arrival > Time)
                continue;

            // Only the current leg holds capacity; waiting at a transit planet holds none.
            var lane = Lane(shipment.Route[shipment.Leg], shipment.Route[shipment.Leg + 1]);
            var volume = shipment.Quantity * _commodities[shipment.Commodity].CargoVolume;
            _remainingCapacity[lane] = Math.Min(_laneCapacity[lane], _remainingCapacity[lane] + volume);
            shipment.Leg++;
            shipment.ArrivesAt = null;
            shipment.ReceivedAtNode = false;
            if (shipment.Leg == shipment.Route.Length - 1)
            {
                if (Generation != null)
                    continue;
                var good = _markets[shipment.Destination].Goods[shipment.Commodity];
                good.Stock += shipment.Quantity;
                good.Incoming -= shipment.Quantity;
                _shipments.RemoveAt(i--);
                continue;
            }
            shipment.ArrivesAt = null;
        }

        if (Generation == null)
            AdvanceWaitingShipments();
    }

    private void AdvanceWaitingShipments(SWGlobalWarFlowPriority? priority = null)
    {
        // One pass moves goods and never touches ownership or diplomacy, so cargo that sits together asks once.
        _waitingTrades.Clear();
        for (var i = 0; i < _shipments.Count; i++)
        {
            var shipment = _shipments[i];
            if (shipment.ArrivesAt != null || shipment.SupplyManifest != null ||
                priority != null && ShipmentPriority(shipment) != priority)
                continue;

            if (shipment.ReturnCaptured)
            {
                shipment.HoldReason = "sw-global-war-market-return-captured";
                continue;
            }

            if (Generation != null && !shipment.ReceivedAtNode)
            {
                var node = _markets[shipment.Route[shipment.Leg]];
                if (node.Profile != null)
                {
                    var receiveVolume = _commodities[shipment.Commodity].CargoVolume;
                    var received = TransferQuantity(shipment.Quantity, node.ReceiveBudget, receiveVolume);
                    if (received == 0)
                    {
                        shipment.HoldReason = "sw-global-war-market-wait-receive";
                        continue;
                    }
                    if (received < shipment.Quantity)
                    {
                        var part = SplitShipment(shipment, received);
                        _shipments.Insert(++i, part);
                        shipment = part;
                    }
                    node.ReceiveBudget = Math.Max(0, node.ReceiveBudget - received * receiveVolume);
                }
                shipment.ReceivedAtNode = true;
            }

            if (Generation != null && shipment.Leg == shipment.Route.Length - 1)
            {
                if (!AllowsTrade(shipment, SWGlobalWarTradeStage.Receipt, shipment.Destination))
                    continue;
                var good = _markets[shipment.Destination].Goods[shipment.Commodity];
                AddBatch(good, shipment.Batch! with { Owner = _markets[shipment.Destination].Owner });
                good.Incoming -= shipment.Quantity;
                if (shipment.Flow is { } flowId && _flows.TryGetValue(flowId, out var flow))
                {
                    flow.GetStatistics(shipment.Commodity).Received += shipment.Quantity;
                    var obligation = (flowId, shipment.Destination, shipment.Commodity);
                    _flowIncoming[obligation] -= shipment.Quantity;
                    var receipt = (shipment.Destination, shipment.Commodity);
                    _stepReceipts[receipt] = _stepReceipts.GetValueOrDefault(receipt) + shipment.Quantity;
                }
                CompleteReturnPart(shipment);
                _shipments.RemoveAt(i--);
                continue;
            }

            // Transit cargo waits for the next link without entering the intermediate market's inventory.
            var from = shipment.Route[shipment.Leg];
            var to = shipment.Route[shipment.Leg + 1];
            if (Generation != null && !AllowsTrade(shipment, SWGlobalWarTradeStage.NextLeg, to))
                continue;
            var lane = Lane(from, to);
            var capacity = _remainingCapacity[lane];
            var sender = _markets[from];
            if (Generation != null && sender.Profile != null)
                capacity = Math.Min(capacity, sender.SendBudget);
            var cargoVolume = _commodities[shipment.Commodity].CargoVolume;
            var quantity = TransferQuantity(shipment.Quantity, capacity, cargoVolume);
            if (quantity == 0)
            {
                shipment.HoldReason = Generation != null && sender.Profile != null && sender.SendBudget < cargoVolume
                    ? "sw-global-war-market-wait-send" : "sw-global-war-market-wait-lane";
                continue;
            }
            _remainingCapacity[lane] = Math.Max(0, _remainingCapacity[lane] - quantity * cargoVolume);
            if (Generation != null && sender.Profile != null)
                sender.SendBudget = Math.Max(0, sender.SendBudget - quantity * cargoVolume);
            var arrivesAt = Time + _connections[(from, to)].TravelTime;
            if (shipment.ReturnOperation != null && shipment.Leg == 0)
                shipment.DepartedAt = Time;
            if (quantity < shipment.Quantity)
            {
                // Keep the remainder waiting; splitting existing cargo does not change destination Incoming.
                shipment.Quantity -= quantity;
                var departing = new SWGlobalWarShipment(shipment.Commodity, quantity, shipment.Route, shipment.DepartedAt, arrivesAt)
                {
                    Leg = shipment.Leg,
                    DepartureReceipt = shipment.DepartureReceipt,
                    DeliveryParticipant = shipment.DeliveryParticipant,
                    Flow = shipment.Flow,
                    ReturnOperation = shipment.ReturnOperation,
                    ReturnCaptured = shipment.ReturnCaptured,
                };
                if (shipment.Batch is { } batch)
                {
                    departing.Batch = SliceBatch(batch, quantity);
                    shipment.Batch = ResizeBatch(batch, shipment.Quantity);
                }
                // Skip the departing part this step and preserve the waiting remainder's queue position.
                _shipments.Insert(++i, departing);
            }
            else
            {
                shipment.ArrivesAt = arrivesAt;
                shipment.ReceivedAtNode = false;
            }
        }
    }

    private void Dispatch()
    {
        if (Generation != null || _flows.Count > 0)
        {
            DispatchFlows();
            if (_flows.Count > 0 || IsActive?.Invoke() == false)
                return;
        }
        var needs = _markets.Values.SelectMany(market => market.Goods
            .Where(pair => pair.Value.ImportPerMinute > 0 && !pair.Value.LocallyProduced)
            .Select(pair => (Market: market, Commodity: pair.Key, Good: pair.Value)))
            .OrderBy(need => need.Market.Planet, StringComparer.Ordinal).ThenBy(need => need.Commodity, StringComparer.Ordinal).ToArray();
        if (needs.Length == 0)
            return;

        // Supply the shortest remaining consumption buffers first; equal-priority ties rotate after dispatch.
        var turn = (int) (_dispatchTurn % needs.Length);
        var dispatched = false;
        foreach (var need in needs.Select((need, index) => (Need: need, Tie: (index + turn) % needs.Length))
                     .OrderBy(entry => (double) entry.Need.Good.Stock / entry.Need.Good.ImportPerMinute).ThenBy(entry => entry.Tie))
        {
            // A full stock still needs replacement cargo before consumption during delivery creates a deficit.
            while (true)
            {
                var supplier = FindSupplier(need.Need.Market.Planet, need.Need.Commodity);
                if (supplier == null)
                    break;

                var (source, route, quantity) = supplier.Value;
                var commodity = need.Need.Commodity;
                if (Generation != null && source.Profile != null)
                {
                    quantity = TransferQuantity(quantity, source.SendBudget, _commodities[commodity].CargoVolume);
                    if (quantity == 0)
                        break;
                }
                SWGlobalWarMarketBatch? cargo = null;
                if (Generation != null)
                {
                    var batch = source.Goods[commodity].MutableBatches!.Values
                        .FirstOrDefault(b => b.Quantity > b.Reserved && AllowsDispatch(b, commodity, route));
                    if (batch == null)
                        break;
                    quantity = Math.Min(quantity, batch.Quantity - batch.Reserved);
                    cargo = TakeBatch(source.Goods[commodity], batch, quantity);
                }
                else
                    source.Goods[commodity].Stock -= quantity;
                need.Need.Good.Incoming += quantity;
                var first = _connections[(route[0], route[1])];
                var lane = Lane(first.From, first.To);
                _remainingCapacity[lane] = Math.Max(0, _remainingCapacity[lane] - quantity * _commodities[commodity].CargoVolume);
                if (Generation != null && source.Profile != null)
                    source.SendBudget = Math.Max(0, source.SendBudget - quantity * _commodities[commodity].CargoVolume);
                _shipments.Add(new SWGlobalWarShipment(commodity, quantity, route, Time, Time + first.TravelTime)
                {
                    Batch = cargo,
                    DepartureReceipt = Generation != null ? Guid.NewGuid() : Guid.Empty,
                    DeliveryParticipant = Generation != null ? _markets[route[^1]].Owner : null,
                });
                dispatched = true;
            }
        }
        // Rotate equal-priority recipients only when something actually departs.
        if (dispatched)
            _dispatchTurn++;
    }

    private (SWGlobalWarMarketState Source, string[] Route, int Quantity)? FindSupplier(string destination, string commodity)
    {
        (SWGlobalWarMarketState Source, string[] Route, int Quantity)? result = null;
        var bestTime = TimeSpan.MaxValue;
        var cargoVolume = _commodities[commodity].CargoVolume;
        var recipient = _markets[destination].Goods[commodity];
        foreach (var source in _markets.Values)
        {
            if (source.Planet == destination || !source.Goods.TryGetValue(commodity, out var good) || good.Supply == 0)
                continue;
            var route = FindRoute(source.Planet, destination, cargoVolume);
            if (route == null)
                continue;
            if (Generation != null && !good.MutableBatches!.Values.Any(batch =>
                    batch.Quantity > batch.Reserved && AllowsDispatch(batch, commodity, route)))
                continue;

            var time = TimeSpan.Zero;
            var lane = Lane(route[0], route[1]);
            var capacity = _remainingCapacity[lane];
            if (Generation != null && source.Profile != null)
                capacity = Math.Min(capacity, source.SendBudget);
            for (var i = 1; i < route.Length; i++)
            {
                var duration = _connections[(route[i - 1], route[i])].TravelTime;
                time += duration;
                // Each leg arrives on its own step boundary, including legs shorter than a full step.
                var remainder = duration.Ticks % StepDuration.Ticks;
                if (remainder > 0)
                    time += TimeSpan.FromTicks(StepDuration.Ticks - remainder);
            }
            // Departure uses the first link's capacity; narrower transit links split cargo when it reaches them.
            var quantity = TransferQuantity(Math.Min(good.Supply, GetImportNeed(recipient, time)), capacity, cargoVolume);
            if (quantity == 0 || time >= bestTime)
                continue;
            bestTime = time;
            result = (source, route, quantity);
        }
        return result;
    }

    /// <summary>Targets a ten-minute physical stock after delivery, accounting for consumption and all dispatched cargo.</summary>
    /// <param name="recipient">Destination inventory and consumption rate.</param>
    /// <param name="deliveryTime">Sum of route leg durations rounded to simulation steps; excludes uncertain capacity queues.</param>
    private static int GetImportNeed(SWGlobalWarMarketGoodState recipient, TimeSpan deliveryTime)
    {
        var consumption = decimal.Ceiling((decimal) recipient.ImportPerMinute * deliveryTime.Ticks / TimeSpan.TicksPerMinute);
        var need = Math.Max(0m, recipient.TargetStock + consumption - recipient.Stock - recipient.Incoming);
        // Reserve integer storage space for incoming cargo as well as the current stock.
        var headroom = recipient.Capacity - recipient.Stock - recipient.Incoming - recipient.ReservedIncoming;
        return (int) Math.Min(need, headroom);
    }

    private static int TransferQuantity(int limit, double capacity, double cargoVolume) =>
        (int) Math.Min(limit, Math.Floor(capacity / cargoVolume + Epsilon));

    /// <summary>Finds the fastest directed route that fits a whole item and has room for departure on its first leg.</summary>
    /// <param name="source">Market exporting the commodity.</param>
    /// <param name="destination">Final recipient; intermediate markets do not acquire the cargo.</param>
    /// <param name="cargoVolume">Volume of one item; links smaller than this can never carry the commodity.</param>
    private string[]? FindRoute(string source, string destination, double cargoVolume, bool requireDepartureSpace = true)
    {
        if (requireDepartureSpace)
            return SearchRoute(source, destination, cargoVolume, true);
        // Without the departure check the answer depends only on the graph and lane capacities, fixed at construction.
        var key = (source, destination, cargoVolume);
        if (!_structuralRoutes.TryGetValue(key, out var route))
            _structuralRoutes.Add(key, route = SearchRoute(source, destination, cargoVolume, false));
        return route;
    }

    private string[]? SearchRoute(string source, string destination, double cargoVolume, bool requireDepartureSpace)
    {
        var distance = new Dictionary<string, TimeSpan> { [source] = TimeSpan.Zero };
        var previous = new Dictionary<string, string>();
        var visited = new HashSet<string>();
        while (true)
        {
            string? current = null;
            var best = TimeSpan.MaxValue;
            foreach (var (planet, value) in distance)
            {
                if (!visited.Contains(planet) && value < best)
                {
                    current = planet;
                    best = value;
                }
            }
            if (current == null)
                return null;
            if (current == destination)
                break;
            visited.Add(current);
            foreach (var connection in _outgoing[current])
            {
                var lane = Lane(connection.From, connection.To);
                if (TransferQuantity(1, _laneCapacity[lane], cargoVolume) == 0
                    || requireDepartureSpace && current == source && TransferQuantity(1, _remainingCapacity[lane], cargoVolume) == 0)
                    continue;
                var next = best + connection.TravelTime;
                if (distance.TryGetValue(connection.To, out var old) && old <= next)
                    continue;
                distance[connection.To] = next;
                previous[connection.To] = current;
            }
        }

        var route = new List<string> { destination };
        while (route[^1] != source)
            route.Add(previous[route[^1]]);
        route.Reverse();
        return route.ToArray();
    }

    /// <summary>Adds externally supplied or produced goods without creating physical items or shipments.</summary>
    public bool TryAddStock(string planet, string commodity, int quantity)
    {
        if (Generation != null || quantity < 0 || !TryGetGood(planet, commodity, out var good) ||
            (long) good.Stock + good.Incoming + quantity > int.MaxValue)
            return false;
        good.Stock += quantity;
        return true;
    }

    public bool TryTakeStock(string planet, string commodity, int quantity)
    {
        if (Generation != null || quantity < 0 || !TryGetGood(planet, commodity, out var good) || quantity > good.Stock)
            return false;
        good.Stock -= quantity;
        return true;
    }

    /// <summary>Changes future consumption and its ten-minute stock target; already dispatched cargo continues travelling.</summary>
    public bool TrySetConsumption(string planet, string commodity, int perMinute)
    {
        if (Generation != null || !ValidConsumption(perMinute) || !TryGetGood(planet, commodity, out var good))
            return false;
        good.ConsumptionPerMinute = perMinute;
        return true;
    }

    private bool TryGetGood(string planet, string commodity, out SWGlobalWarMarketGoodState good)
    {
        good = default!;
        if (!_commodities.ContainsKey(commodity) || !_markets.TryGetValue(planet, out var market))
            return false;
        if (!market.Goods.TryGetValue(commodity, out var existing))
        {
            existing = new SWGlobalWarMarketGoodState();
            market.Goods.Add(commodity, existing);
        }
        good = existing;
        return true;
    }
}
