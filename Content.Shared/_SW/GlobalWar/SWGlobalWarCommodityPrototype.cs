using Robust.Shared.Prototypes;

namespace Content.Shared._SW.GlobalWar;

/// <summary>A commodity traded and transported in whole units by planetary markets.</summary>
[Prototype("swGlobalWarCommodity")]
public sealed partial class SWGlobalWarCommodityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    /// <summary>Transport capacity occupied by one unit of this commodity.</summary>
    [DataField]
    public double CargoVolume = 1;
}

[DataDefinition]
public sealed partial class SWGlobalWarMarketGoodConfiguration
{
    [DataField]
    public int InitialStock;

    [DataField]
    public int ConsumptionPerMinute;
}
