using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>
/// Static placement and physical limits of one world's economy. The ID is a world ID in the
/// Republic graph. Population units are normalized demand, not player count or canonical population.
/// Facility eligibility does not create installed lines, goods or free resource stocks.
/// </summary>
[Prototype("swPlanetEconomy")]
public sealed partial class SWPlanetEconomyPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public bool ActiveEconomy;
    [DataField(required: true)] public double PopulationUnits;
    /// <summary>Local power capacity before domestic consumption, separate from fueled reactors.</summary>
    [DataField(required: true)] public double BaseloadPower;
    [DataField(required: true)] public double LaborCapacity;
    [DataField(required: true)] public double BufferHours;
    [DataField(required: true)] public List<ProtoId<SWProductionFacilityPrototype>> Facilities = new();
    [DataField(required: true)] public List<ProtoId<SWProductionConditionPrototype>> Conditions = new();
    [DataField(required: true)] public List<SWPlanetNaturalResource> NaturalResources = new();
}

/// <summary>
/// Geographic access to a resource or renewable biome. Baseline yields describe the solved
/// production plan, not inventory, a resource reserve, or a second source of production.
/// Actual extraction must run a recipe with its facility, inputs, energy and labor.
/// </summary>
[DataDefinition]
public sealed partial class SWPlanetNaturalResource
{
    [DataField(required: true)] public ProtoId<SWProductionConditionPrototype> Condition;
    [DataField(required: true)] public Dictionary<ProtoId<SWCommodityPrototype>, double> BaselineYieldPerHour = new();
}
