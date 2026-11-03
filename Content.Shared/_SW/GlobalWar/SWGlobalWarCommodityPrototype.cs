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

    /// <summary>Entity prototype representing one market unit; use a single-item prototype for stackable goods.</summary>
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>Transport capacity occupied by one unit of this commodity.</summary>
    [DataField]
    public double CargoVolume = 1;

    /// <summary>Credits per unit of actual mandatory civilian consumption; production and transport pay no tax.</summary>
    [DataField(serverOnly: true)]
    public int ConsumptionTax;
}

[DataDefinition]
public sealed partial class SWGlobalWarMarketGoodConfiguration
{
    [DataField]
    public int InitialStock;

    [DataField]
    public int ConsumptionPerMinute;
}
