using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed record SWGlobalWarCommodityDefinition(string Id, double CargoVolume, bool WholeItems = false);
public sealed record SWGlobalWarMarketGoodDefinition(string Commodity, int Stock, int ConsumptionPerMinute);
public sealed record SWGlobalWarMarketDefinition(string Planet, IReadOnlyList<SWGlobalWarMarketGoodDefinition> Goods,
    SWGlobalWarFaction Owner = SWGlobalWarFaction.Neutral, SWGlobalWarEconomyProfilePrototype? Profile = null,
    SWGlobalWarSectorKind Kind = default, bool Enclave = false, bool LanthanumReserve = false);
/// <summary>A directed connection whose capacity limits in-flight cargo volume shared with the reverse connection.</summary>
public sealed record SWGlobalWarTradeConnection(string From, string To, TimeSpan TravelTime, double Capacity);

public sealed class SWGlobalWarMarketState(string planet)
{
    public string Planet { get; } = planet;
    public Dictionary<string, SWGlobalWarMarketGoodState> Goods { get; } = new();
    public SWGlobalWarFaction Owner { get; internal set; }
    public SWGlobalWarFaction OriginalOwner { get; internal set; }
    public SWGlobalWarSectorKind Kind { get; internal set; }
    public bool Enclave { get; internal set; }
    public TimeSpan? CapturedAt { get; internal set; }
    public SWGlobalWarEconomyProfilePrototype? Profile { get; internal set; }
    public decimal Population { get; internal set; } = 1;
    public decimal GrowthFoodFraction { get; internal set; } = 0.1m;
    public long GrowthRevision { get; internal set; }
    public int HubCapacity { get; internal set; }
    public double SendBudget { get; internal set; }
    public double ReceiveBudget { get; internal set; }
    internal Dictionary<string, decimal> ProductionProgress = new();
    internal Dictionary<string, decimal> ProductionOrders = new();
    internal long MinuteFoodRequired;
    internal long MinuteFoodConsumed;
    internal long MinuteGrowthConsumed;
}

public sealed class SWGlobalWarMarketGoodState
{
    private int _legacyStock;
    internal Dictionary<Guid, SWGlobalWarMarketBatch>? MutableBatches;
    public IReadOnlyCollection<SWGlobalWarMarketBatch> Batches => MutableBatches?.Values.ToArray() ?? [];
    public int Stock
    {
        get
        {
            if (MutableBatches == null)
                return _legacyStock;
            var stock = 0;
            foreach (var batch in MutableBatches.Values)
                stock = checked(stock + batch.Quantity);
            return stock;
        }
        internal set
        {
            if (MutableBatches != null)
                throw new InvalidOperationException("Campaign inventory must be changed through its batches.");
            _legacyStock = value;
        }
    }
    public int Reserved
    {
        get
        {
            if (MutableBatches == null)
                return 0;
            var reserved = 0;
            foreach (var batch in MutableBatches.Values)
                reserved = checked(reserved + batch.Reserved);
            return reserved;
        }
    }

    public int Available => Stock - Reserved;
    public int ReservedIncoming { get; internal set; }
    public int Capacity { get; internal set; } = int.MaxValue;
    public int ConsumptionPerMinute { get; internal set; }
    public int BaseConsumptionPerMinute { get; internal set; }
    public int ProductionDemandPerMinute { get; internal set; }
    public int GrowthDemandPerMinute { get; internal set; }
    public int ImportPerMinute => ConsumptionPerMinute + ProductionDemandPerMinute + GrowthDemandPerMinute;
    public int MinimumBuffer { get; internal set; }
    public bool LocallyProduced { get; internal set; }
    public int Incoming { get; internal set; }
    public long Consumed { get; internal set; }
    public long Produced { get; internal set; }
    public long RecipeConsumed { get; internal set; }
    public long GrowthConsumed { get; internal set; }
    public long UnmetConsumption { get; internal set; }
    public int TargetStock => Math.Max(MinimumBuffer, ImportPerMinute * SWGlobalWarMarketConstants.StockBufferMinutes);
    public int Demand => Math.Max(0, TargetStock - Stock);
    public int Supply => Math.Max(0, Available - TargetStock);
    // Immediate uncovered demand; dispatch additionally accounts for consumption along each candidate route.
    public int ImportNeed => Math.Max(0, Demand - Incoming);

    // Fractional consumption in item-ticks; one whole item requires TimeSpan.TicksPerMinute.
    internal long ConsumptionProgress;
    internal decimal CampaignConsumptionProgress;
    internal decimal GrowthProgress;
}

/// <summary>A virtual shipment follows directed connections; intermediate planets never own its contents.</summary>
public sealed class SWGlobalWarShipment(string commodity, int quantity, string[] route, TimeSpan departedAt, TimeSpan arrivesAt)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Commodity { get; } = commodity;
    // Reduced when part of the cargo leaves a transit planet as a separate shipment.
    public int Quantity { get; internal set; } = quantity;

    // Ordered planet IDs, including the source, transit planets, and final destination.
    public string[] Route { get; internal set; } = route;
    public string Source => Route[0];
    public string Destination => Route[^1];
    public TimeSpan DepartedAt { get; internal set; } = departedAt;

    // The current leg runs from Route[Leg] to Route[Leg + 1]. Starts at zero and advances on arrival.
    // Reaching the last route index completes the shipment and adds its cargo to the destination stock.
    public int Leg { get; internal set; }

    // Arrival time since simulation start; null while waiting for transit link capacity.
    public TimeSpan? ArrivesAt { get; internal set; } = arrivesAt;
    public SWGlobalWarMarketBatch? Batch { get; internal set; }
    /// <summary>Physical cargo uses these existing entities instead of raw inventory batches.</summary>
    public SWGlobalWarSupplyManifest? SupplyManifest { get; internal set; }
    public Guid DepartureReceipt { get; internal set; }
    /// <summary>Delivery participant authorized at dispatch or an explicit return; territorial ownership grants no access.</summary>
    public SWGlobalWarFaction? DeliveryParticipant { get; internal set; }
    public string HoldReason { get; internal set; } = string.Empty;
    public bool ReceivedAtNode { get; internal set; }
    public string? Flow { get; internal set; }
    public Guid? ReturnOperation { get; internal set; }
    public bool ReturnCaptured { get; internal set; }
}

/// <summary>Campaign quantities are integer quanta; this record is the inventory, never a second stock balance.</summary>
public sealed record SWGlobalWarMarketBatch(Guid Id, Guid Generation, SWGlobalWarFaction Origin,
    SWGlobalWarFaction Owner, int Quantity, int Reserved = 0, SWGlobalWarTradeCaptureEvidence? Capture = null);

public static class SWGlobalWarMarketUnits
{
    public const int PerUnit = SWGlobalWarFlowSettings.QuantaPerUnit;
    public const int StockCapacity = 600 * PerUnit;

    /// <summary>Converts configuration without silently rounding a fractional rate or allowing overflow.</summary>
    public static int FromUnits(decimal units)
    {
        var quanta = units * PerUnit;
        if (quanta < 0 || quanta > int.MaxValue || decimal.Truncate(quanta) != quanta)
            throw new ArgumentOutOfRangeException(nameof(units));
        return (int) quanta;
    }
}
