using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.GlobalWar;

/// <summary>Campaign economic quantities are logical units; conversion to quanta occurs once on the server.</summary>
[Prototype("swGlobalWarEconomyProfile")]
public sealed partial class SWGlobalWarEconomyProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>Mandatory civilian consumption in units per minute at population one.</summary>
    [DataField] public Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, decimal> Mandatory = new();

    /// <summary>One-time stock. This overrides the ten-minute mandatory buffer for each specified commodity.</summary>
    [DataField] public Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, decimal> OpeningStock = new();

    /// <summary>Specific strategic buffers protected from export, while local consumption and recipes may use them.</summary>
    [DataField] public Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, decimal> ExportReserves = new();

    /// <summary>Independent production recipes, including primary extraction with an empty input vector.</summary>
    [DataField] public List<SWGlobalWarEconomyRecipe> Recipes = new();

    /// <summary>Independent receive and send capacity, in cargo units per minute.</summary>
    [DataField] public int HubCapacity = 25;

    /// <summary>Maximum hub capacity reached through paid local upgrades.</summary>
    [DataField] public int MaximumHubCapacity = 100;

    /// <summary>Neutral restoration halves output and corresponding recipe inputs before campaign time 30:00.</summary>
    [DataField] public bool HalfProductionBeforeThirtyMinutes;
}

[DataDefinition]
public sealed partial class SWGlobalWarEconomyRecipe
{
    /// <summary>The output commodity; physical weapon recipes are orders for E, never virtual sources here.</summary>
    [DataField(required: true)] public ProtoId<SWGlobalWarCommodityPrototype> Output;

    /// <summary>Maximum output units per minute at population one.</summary>
    [DataField(required: true)] public decimal PerMinute;

    /// <summary>Input units required per unit of output.</summary>
    [DataField] public Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, decimal> Inputs = new();
}

[DataDefinition]
public sealed partial class SWGlobalWarEconomicConnection
{
    /// <summary>Physical travel duration, independent of throughput and in-flight capacity.</summary>
    [DataField] public TimeSpan TravelTime = TimeSpan.FromSeconds(60);

    /// <summary>Shared in-flight cargo volume for this edge and its reverse direction.</summary>
    [DataField] public double Capacity = 64;
}
