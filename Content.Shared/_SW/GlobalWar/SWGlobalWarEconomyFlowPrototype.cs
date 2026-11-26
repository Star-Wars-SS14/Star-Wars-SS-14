using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

/// <summary>Default economic contracts are independent of the FTL graph and never create goods.</summary>
[Prototype("swGlobalWarEconomyFlow")]
public sealed partial class SWGlobalWarEconomyFlowPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>Market supplying an existing batch.</summary>
    [DataField(required: true)] public ProtoId<SWGlobalWarPlanetPrototype> Source;

    /// <summary>Final destination; intermediate hubs never acquire the cargo as their stock.</summary>
    [DataField(required: true)] public ProtoId<SWGlobalWarPlanetPrototype> Destination;

    /// <summary>Commodity whose stock and incoming reservation this flow uses.</summary>
    [DataField(required: true)] public ProtoId<SWGlobalWarCommodityPrototype> Commodity;

    /// <summary>Priority category; player edits may reorder only within an authorized category.</summary>
    [DataField] public SWGlobalWarFlowPriority Priority;

    /// <summary>Default rate derivation; edited explicit rates do not change any production recipe.</summary>
    [DataField] public SWGlobalWarFlowRateMode RateMode;

    /// <summary>Share of a common recipient requirement or source surplus, or units/minute for Fixed.</summary>
    [DataField] public decimal Share = 1;
}

[Serializable, NetSerializable]
public enum SWGlobalWarFlowPriority : byte
{
    MandatoryFood,
    DomesticCivilian,
    BasicMilitary,
    NeutralIndustry,
    GrowthFood,
    LuxuryAndExpansion,
}

[Serializable, NetSerializable]
public enum SWGlobalWarFlowRateMode : byte
{
    Fixed,
    RecipientMandatory,
    RecipientGrowth,
    ProductionInput,
    ProducerSurplus,
    NeutralLuxury,
}
