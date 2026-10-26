using System.Collections.Generic;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Initial physical economy. Recipes, world resources and route capacities have their own prototypes.</summary>
[Prototype("swRepublicEconomyScenario")]
public sealed partial class SWRepublicEconomyScenarioPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public ProtoId<SWGalacticMapPrototype> Map;
    [DataField] public double Headroom = 1.25;
    [DataField] public double ColdDecayPerHour = .01;
    [DataField] public double ReactorPowerPerFuel = 12;
    [DataField] public double DomesticPowerPerPopulation = 1.5;
    [DataField] public double RecoveryHours = 4;
    [DataField] public double OrderDeadlineHours = 42;
    [DataField(required: true)] public List<SWEconomyInitialProduction> Production = new();
    [DataField(required: true)] public List<SWEconomyInitialStock> Inventory = new();
    [DataField(required: true)] public List<SWEconomyInitialShipping> Shipping = new();
    [DataField(required: true)] public List<SWEconomyInitialEmptyReturn> EmptyReturns = new();
    [DataField(required: true)] public List<SWEconomyInitialDemand> Demands = new();
    [DataField(required: true)] public List<SWEconomyInitialReactor> Reactors = new();
}

[DataDefinition]
public sealed partial class SWEconomyInitialProduction
{
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> World;
    [DataField(required: true)] public ProtoId<SWProductionRecipePrototype> Recipe;
    /// <summary>Normal cycles per active game-hour, before installed headroom.</summary>
    [DataField(required: true)] public double Rate;
}

[DataDefinition]
public sealed partial class SWEconomyInitialStock
{
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> World;
    [DataField(required: true)] public ProtoId<SWCommodityPrototype> Good;
    [DataField(required: true)] public double Initial;
    [DataField(required: true)] public double Target;
    [DataField(required: true)] public double Capacity;
}

[DataDefinition]
public sealed partial class SWEconomyInitialShipping
{
    [DataField(required: true)] public ProtoId<SWTransitLinkPrototype> Link;
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> Source;
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> Target;
    [DataField(required: true)] public ProtoId<SWCommodityPrototype> Good;
    /// <summary>Lots per active game-hour; cargo size comes from the commodity.</summary>
    [DataField(required: true)] public double Rate;
}

[DataDefinition]
public sealed partial class SWEconomyInitialEmptyReturn
{
    [DataField(required: true)] public ProtoId<SWTransitLinkPrototype> Link;
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> Source;
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> Target;
    [DataField(required: true)] public double SlotsPerHour;
}

[DataDefinition]
public sealed partial class SWEconomyInitialDemand
{
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> World;
    [DataField(required: true)] public ProtoId<SWCommodityPrototype> Good;
    [DataField(required: true)] public string Group = string.Empty;
    [DataField(required: true)] public double Rate;
    /// <summary>Finite commissioning quota, never an hourly recurring purchase.</summary>
    [DataField] public double OrderTotal;
}

[DataDefinition]
public sealed partial class SWEconomyInitialReactor
{
    [DataField(required: true)] public ProtoId<SWPlanetEconomyPrototype> World;
    [DataField(required: true)] public double CapacityPower;
}
