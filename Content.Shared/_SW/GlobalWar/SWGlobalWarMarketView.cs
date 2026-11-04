using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

public static class SWGlobalWarMarketConstants
{
    public const int StockBufferMinutes = 10;
}

/// <summary>Detached logical-unit quantities. Campaign targets and reservations come from the authoritative inventory.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarMarketGoodView(ProtoId<SWGlobalWarCommodityPrototype> Commodity,
    double Stock, double ConsumptionPerMinute, double Incoming, double? TargetOverride = null, double Reserved = 0,
    double ProductionPerMinute = 0, double RecipeConsumptionPerMinute = 0, double GrowthConsumptionPerMinute = 0,
    double Capacity = double.MaxValue, double ReservedIncoming = 0)
{
    public double TargetStock => TargetOverride ?? ConsumptionPerMinute * SWGlobalWarMarketConstants.StockBufferMinutes;
    public double Available => Math.Max(0, Stock - Reserved);
    public double Demand => Math.Max(0, TargetStock - Available);
    public double Supply => Math.Max(0, Available - TargetStock);
}

[Serializable, NetSerializable]
public enum SWGlobalWarMarketAvailability : byte
{
    Ready,
    Unavailable,
    Restricted,
    Unknown,
    Stale,
}
