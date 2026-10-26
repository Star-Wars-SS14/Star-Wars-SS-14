using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Storage requirements for a commodity lot, not an entity container type.</summary>
public enum SWCommodityStorage : byte
{
    Bulk = 0,
    BulkLiquid = 1,
    Chemical = 2,
    Cold = 3,
    Commissioning = 4,
    Dry = 5,
    Hazardous = 6,
    Pressure = 7,
    Secure = 8,
}

public enum SWCommodityUse : byte
{
    Asset = 0,
    Consumable = 1,
    Durable = 2,
    Intermediate = 3,
}

public enum SWCommodityRegulation : byte
{
    Licensed = 0,
    Ordinary = 1,
    Prohibited = 2,
}

/// <summary>How a demand profile relates to its listed commodities.</summary>
public enum SWCommodityDemandMode : byte
{
    /// <summary>All listed inputs are required together in their stated proportions.</summary>
    Bundle = 0,
    /// <summary>Each listed good commissions its own asset type; these are not a bundle.</summary>
    Commission = 1,
    /// <summary>Independent consumption of listed goods against the profile's demand.</summary>
    Consume = 2,
    /// <summary>Score already consumed goods; do not consume them again.</summary>
    Derived = 3,
    /// <summary>Alternative goods meet one shared demand using their substitution coefficients.</summary>
    Substitute = 4,
}

[Prototype("swCommodityCategory")]
public sealed partial class SWCommodityCategoryPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public int Order;
}

[Prototype("swProductionFacility")]
public sealed partial class SWProductionFacilityPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
}

[Prototype("swProductionCondition")]
public sealed partial class SWProductionConditionPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
}

/// <summary>Static Republic commodity definition. Quantities are commodity-specific industrial lots.</summary>
[Prototype("swCommodity")]
public sealed partial class SWCommodityPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public string Description = string.Empty;
    [DataField(required: true)] public string Shortage = string.Empty;
    [DataField(required: true)] public ProtoId<SWCommodityCategoryPrototype> Category;
    /// <summary>Transport capacity occupied by one lot. Commissioned fleets are not freight.</summary>
    [DataField(required: true)] public int CargoSlots;
    [DataField(required: true)] public SWCommodityStorage Storage;
    [DataField(required: true)] public SWCommodityUse Use;
    [DataField(required: true)] public SWCommodityRegulation Regulation;
    [DataField(required: true)] public int Order;
}

[Prototype("swProductionRecipe")]
public sealed partial class SWProductionRecipePrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public ProtoId<SWProductionFacilityPrototype> Facility;
    [DataField(required: true)] public Dictionary<ProtoId<SWCommodityPrototype>, float> Inputs = new();
    [DataField(required: true)] public Dictionary<ProtoId<SWCommodityPrototype>, float> Outputs = new();
    /// <summary>Active game-hours per cycle of one production line. Night does not advance production.</summary>
    [DataField(required: true)] public float Hours;
    /// <summary>Local power capacity reserved while the line runs, not a consumed stock item.</summary>
    [DataField(required: true)] public float Power;
    /// <summary>Workforce capacity reserved while the line runs, not consumed workers.</summary>
    [DataField(required: true)] public float Labor;
    /// <summary>Eligibility prerequisites; their presence does not imply infinite resource reserves.</summary>
    [DataField(required: true)] public List<ProtoId<SWProductionConditionPrototype>> Requirements = new();
    /// <summary>Finite local resource amounts debited per cycle, distinct from commodity inputs.</summary>
    [DataField(required: true)] public Dictionary<ProtoId<SWProductionConditionPrototype>, float> ResourceCosts = new();
    [DataField(required: true)] public int Order;
}

/// <summary>Demand coefficients and semantics; absolute demand is supplied by the economic model.</summary>
[Prototype("swCommodityDemand")]
public sealed partial class SWCommodityDemandPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public string Basis = string.Empty;
    [DataField(required: true)] public SWCommodityDemandMode Mode;
    [DataField(required: true)] public Dictionary<ProtoId<SWCommodityPrototype>, float> Goods = new();
}
