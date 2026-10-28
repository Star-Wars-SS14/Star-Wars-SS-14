using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed record SWGlobalWarCommodityDefinition(string Id, double CargoVolume);
public sealed record SWGlobalWarMarketGoodDefinition(string Commodity, int Stock, int ConsumptionPerMinute);
public sealed record SWGlobalWarMarketDefinition(string Planet, IReadOnlyList<SWGlobalWarMarketGoodDefinition> Goods);
/// <summary>A directed connection whose capacity limits in-flight cargo volume shared with the reverse connection.</summary>
public sealed record SWGlobalWarTradeConnection(string From, string To, TimeSpan TravelTime, double Capacity);

public sealed class SWGlobalWarMarketState(string planet)
{
    public string Planet { get; } = planet;
    public Dictionary<string, SWGlobalWarMarketGoodState> Goods { get; } = new();
}

public sealed class SWGlobalWarMarketGoodState
{
    public int Stock { get; internal set; }
    public int ConsumptionPerMinute { get; internal set; }
    public int Incoming { get; internal set; }
    public long Consumed { get; internal set; }
    public long UnmetConsumption { get; internal set; }
    public int TargetStock => ConsumptionPerMinute * SWGlobalWarMarketConstants.StockBufferMinutes;
    public int Demand => Math.Max(0, TargetStock - Stock);
    public int Supply => Math.Max(0, Stock - TargetStock);
    // Immediate uncovered demand; dispatch additionally accounts for consumption along each candidate route.
    public int ImportNeed => Math.Max(0, Demand - Incoming);

    // Fractional consumption in item-ticks; one whole item requires TimeSpan.TicksPerMinute.
    internal long ConsumptionProgress;
}

/// <summary>A virtual shipment follows directed connections; intermediate planets never own its contents.</summary>
public sealed class SWGlobalWarShipment(string commodity, int quantity, string[] route, TimeSpan departedAt, TimeSpan arrivesAt)
{
    public string Commodity { get; } = commodity;
    // Reduced when part of the cargo leaves a transit planet as a separate shipment.
    public int Quantity { get; internal set; } = quantity;

    // Ordered planet IDs, including the source, transit planets, and final destination.
    public string[] Route { get; } = route;
    public string Source => Route[0];
    public string Destination => Route[^1];
    public TimeSpan DepartedAt { get; } = departedAt;

    // The current leg runs from Route[Leg] to Route[Leg + 1]. Starts at zero and advances on arrival.
    // Reaching the last route index completes the shipment and adds its cargo to the destination stock.
    public int Leg { get; internal set; }

    // Arrival time since simulation start; null while waiting for transit link capacity.
    public TimeSpan? ArrivesAt { get; internal set; } = arrivesAt;
}
