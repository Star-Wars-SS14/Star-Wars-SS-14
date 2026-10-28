using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

public static class SWGlobalWarMarketConstants
{
    public const int StockBufferMinutes = 10;
}

/// <summary>Detached market data; demand and supply always follow the ten-minute stock target.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarMarketGoodView(ProtoId<SWGlobalWarCommodityPrototype> Commodity,
    int Stock, int ConsumptionPerMinute, int Incoming)
{
    public int TargetStock => ConsumptionPerMinute * SWGlobalWarMarketConstants.StockBufferMinutes;
    public int Demand => Math.Max(0, TargetStock - Stock);
    public int Supply => Math.Max(0, Stock - TargetStock);
}
