using System.Linq;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Single-owner, map-independent stock simulation with virtual, capacity-limited freight.</summary>
public sealed class SWGlobalWarMarketSimulation
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
    private TimeSpan _pendingTime;
    private long _dispatchTurn;

    public TimeSpan Time { get; private set; }
    public IReadOnlyDictionary<string, SWGlobalWarMarketState> Markets => _markets;
    public IReadOnlyList<SWGlobalWarShipment> Shipments => _shipments;
    public IReadOnlyDictionary<(string From, string To), SWGlobalWarTradeConnection> Connections => _connections;

    public SWGlobalWarMarketSimulation(IEnumerable<SWGlobalWarCommodityDefinition> commodities,
        IEnumerable<SWGlobalWarMarketDefinition> markets, IEnumerable<SWGlobalWarTradeConnection> connections)
    {
        foreach (var commodity in commodities.OrderBy(commodity => commodity.Id, StringComparer.Ordinal))
        {
            if (!double.IsFinite(commodity.CargoVolume) || commodity.CargoVolume <= 0)
                throw new ArgumentException($"Commodity {commodity.Id} needs a positive finite cargo volume.");
            _commodities.Add(commodity.Id, commodity);
        }
        foreach (var definition in markets.OrderBy(market => market.Planet, StringComparer.Ordinal))
        {
            var market = new SWGlobalWarMarketState(definition.Planet);
            foreach (var good in definition.Goods.OrderBy(good => good.Commodity, StringComparer.Ordinal))
            {
                if (!_commodities.ContainsKey(good.Commodity) || good.Stock < 0 || !ValidConsumption(good.ConsumptionPerMinute))
                    throw new ArgumentException($"Invalid market good {definition.Planet}/{good.Commodity}.");
                market.Goods.Add(good.Commodity, new SWGlobalWarMarketGoodState
                {
                    Stock = good.Stock,
                    ConsumptionPerMinute = good.ConsumptionPerMinute,
                });
            }
            _markets.Add(definition.Planet, market);
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
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        _pendingTime += elapsed;
        while (_pendingTime >= StepDuration)
        {
            _pendingTime -= StepDuration;
            Time += StepDuration;
            Step();
        }
    }

    private void Step()
    {
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

    /// <summary>Releases all completed legs before forwarding waiting cargo, so arrival order cannot delay departures.</summary>
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
            if (shipment.Leg == shipment.Route.Length - 1)
            {
                var good = _markets[shipment.Destination].Goods[shipment.Commodity];
                good.Stock += shipment.Quantity;
                good.Incoming -= shipment.Quantity;
                _shipments.RemoveAt(i--);
                continue;
            }
            shipment.ArrivesAt = null;
        }

        for (var i = 0; i < _shipments.Count; i++)
        {
            var shipment = _shipments[i];
            if (shipment.ArrivesAt != null)
                continue;

            // Transit cargo waits for the next link without entering the intermediate market's inventory.
            var from = shipment.Route[shipment.Leg];
            var to = shipment.Route[shipment.Leg + 1];
            var lane = Lane(from, to);
            var capacity = _remainingCapacity[lane];
            var cargoVolume = _commodities[shipment.Commodity].CargoVolume;
            var quantity = TransferQuantity(shipment.Quantity, capacity, cargoVolume);
            if (quantity == 0)
                continue;
            _remainingCapacity[lane] = Math.Max(0, capacity - quantity * cargoVolume);
            var arrivesAt = Time + _connections[(from, to)].TravelTime;
            if (quantity < shipment.Quantity)
            {
                // Keep the remainder waiting; splitting existing cargo does not change destination Incoming.
                shipment.Quantity -= quantity;
                var departing = new SWGlobalWarShipment(shipment.Commodity, quantity, shipment.Route, shipment.DepartedAt, arrivesAt)
                {
                    Leg = shipment.Leg,
                };
                // Skip the departing part this step and preserve the waiting remainder's queue position.
                _shipments.Insert(++i, departing);
            }
            else
                shipment.ArrivesAt = arrivesAt;
        }
    }

    private void Dispatch()
    {
        var needs = _markets.Values.SelectMany(market => market.Goods
            .Where(pair => pair.Value.ImportNeed > 0)
            .Select(pair => (Market: market, Commodity: pair.Key, Good: pair.Value)))
            .OrderBy(need => need.Market.Planet, StringComparer.Ordinal).ThenBy(need => need.Commodity, StringComparer.Ordinal).ToArray();
        if (needs.Length == 0)
            return;

        // Supply the shortest remaining consumption buffers first; equal-priority ties rotate after dispatch.
        var turn = (int) (_dispatchTurn % needs.Length);
        var dispatched = false;
        foreach (var need in needs.Select((need, index) => (Need: need, Tie: (index + turn) % needs.Length))
                     .OrderBy(entry => (double) entry.Need.Good.Stock / entry.Need.Good.ConsumptionPerMinute).ThenBy(entry => entry.Tie))
        {
            while (need.Need.Good.ImportNeed > 0)
            {
                var supplier = FindSupplier(need.Need.Market.Planet, need.Need.Commodity, need.Need.Good.ImportNeed);
                if (supplier == null)
                    break;

                var (source, route, quantity) = supplier.Value;
                var commodity = need.Need.Commodity;
                source.Goods[commodity].Stock -= quantity;
                need.Need.Good.Incoming += quantity;
                var first = _connections[(route[0], route[1])];
                var lane = Lane(first.From, first.To);
                _remainingCapacity[lane] = Math.Max(0, _remainingCapacity[lane] - quantity * _commodities[commodity].CargoVolume);
                _shipments.Add(new SWGlobalWarShipment(commodity, quantity, route, Time, Time + first.TravelTime));
                dispatched = true;
            }
        }
        // Rotate equal-priority recipients only when something actually departs.
        if (dispatched)
            _dispatchTurn++;
    }

    private (SWGlobalWarMarketState Source, string[] Route, int Quantity)? FindSupplier(string destination, string commodity, int need)
    {
        (SWGlobalWarMarketState Source, string[] Route, int Quantity)? result = null;
        var bestTime = TimeSpan.MaxValue;
        var cargoVolume = _commodities[commodity].CargoVolume;
        foreach (var source in _markets.Values)
        {
            if (source.Planet == destination || !source.Goods.TryGetValue(commodity, out var good) || good.Supply == 0)
                continue;
            var route = FindRoute(source.Planet, destination, cargoVolume);
            if (route == null)
                continue;

            var time = TimeSpan.Zero;
            var lane = Lane(route[0], route[1]);
            var capacity = _remainingCapacity[lane];
            for (var i = 1; i < route.Length; i++)
            {
                time += _connections[(route[i - 1], route[i])].TravelTime;
            }
            // Departure uses the first link's capacity; narrower transit links split cargo when it reaches them.
            var quantity = TransferQuantity(Math.Min(good.Supply, need), capacity, cargoVolume);
            if (quantity == 0 || time >= bestTime)
                continue;
            bestTime = time;
            result = (source, route, quantity);
        }
        return result;
    }

    private static int TransferQuantity(int limit, double capacity, double cargoVolume) =>
        (int) Math.Min(limit, Math.Floor(capacity / cargoVolume + Epsilon));

    /// <summary>Finds the fastest directed route that fits a whole item and has room for departure on its first leg.</summary>
    /// <param name="source">Market exporting the commodity.</param>
    /// <param name="destination">Final recipient; intermediate markets do not acquire the cargo.</param>
    /// <param name="cargoVolume">Volume of one item; links smaller than this can never carry the commodity.</param>
    private string[]? FindRoute(string source, string destination, double cargoVolume)
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
                    || current == source && TransferQuantity(1, _remainingCapacity[lane], cargoVolume) == 0)
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
        if (quantity < 0 || !TryGetGood(planet, commodity, out var good) || (long) good.Stock + good.Incoming + quantity > int.MaxValue)
            return false;
        good.Stock += quantity;
        return true;
    }

    public bool TryTakeStock(string planet, string commodity, int quantity)
    {
        if (quantity < 0 || !TryGetGood(planet, commodity, out var good) || quantity > good.Stock)
            return false;
        good.Stock -= quantity;
        return true;
    }

    /// <summary>Changes future consumption and its ten-minute stock target; already dispatched cargo continues travelling.</summary>
    public bool TrySetConsumption(string planet, string commodity, int perMinute)
    {
        if (!ValidConsumption(perMinute) || !TryGetGood(planet, commodity, out var good))
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
