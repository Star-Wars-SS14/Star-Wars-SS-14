using System;
using System.Collections.Generic;

namespace Content.Server._SW._Senate.Economy.Simulation;

// Definitions are value records. Array ownership is transferred only for construction:
// RepublicEconomyModel copies every array, including recipe inputs and outputs.
public sealed record EconomyGood(string Id, bool Cold, double CargoSlots = 1);
public sealed record EconomyWorld(string Id, double BaseloadPower, double DomesticPower, double LaborCapacity, double ReactorCapacityPower, double? FleetCapacity = null, bool Active = true);
public sealed record EconomyAmount(string Good, double Quantity);
public sealed record EconomyInventory(string World, string Good, double Initial, double Target, double Capacity);
public sealed record EconomyProduction(string World, string Recipe, double Rate, double Hours, double Power, double Labor, EconomyAmount[] Inputs, EconomyAmount[] Outputs);
public sealed record EconomyEdge(string Id, string A, string B, double CapacitySlotsPerHour, double TravelHours = .25, double FuelPerSlot = 0);
// Good=null denotes scheduled empty returns. Their Rate is slots/hour, CargoSlots is 1.
public sealed record EconomyShipping(string Edge, string Source, string Target, string? Good, double Rate, double TravelHours, double FuelPerLot, double CargoSlots);
public sealed record EconomyDemand(string World, string Good, string Group, double Rate, double OrderTotal = 0);
public sealed record EconomyDemandModifier(string World, string Group, double Multiplier);
public sealed record EconomyDefinition(
    EconomyGood[] Goods,
    EconomyWorld[] Worlds,
    EconomyInventory[] Inventory,
    EconomyProduction[] Production,
    EconomyEdge[] Edges,
    EconomyShipping[] Shipping,
    EconomyDemand[] Demands,
    double Headroom = 1.25,
    double ColdDecayPerHour = .01,
    double ReactorPowerPerFuel = 12,
    double RecoveryHours = 4,
    double OrderDeadlineHours = 42,
    bool EnableAssets = false)
{
    public EconomyNavyDepot[] NavyDepots { get; init; } = Array.Empty<EconomyNavyDepot>();
}
public sealed record EconomyNavyDepot(string World, EconomyAmount[] Materials);

public sealed record EconomyAssetState(string World, string Good, double Installed, double Operational, double Target, string Effect, double EffectiveBonus);
public sealed record EconomyCapacityState(string World, double ProductionMultiplier, double LaborCapacity, double StorageMultiplier,
    double DomesticPower, double ReactorCapacityPower, double FleetCapacity, double FleetOccupied);
public sealed record EconomyDietState(string World, double Consumed, double Quality);
public sealed record EconomyProductionExpansion(string Recipe, double AddedCyclesPerHour);
public enum EconomyNavyProjectKind : byte { Supply, Repair, Construction, PlanetaryConstruction, PlanetarySupply }
public sealed record EconomyProjectRequest(string Id, string World, EconomyAmount[] Materials, double WorkHours,
    EconomyProductionExpansion? Expansion = null, bool NavySupply = false,
    EconomyNavyProjectKind? NavyKind = null, int? RecipientFleetId = null);
public sealed record EconomyNavySupplyFlow(string Id, string ProjectId, EconomyNavyProjectKind Kind,
    int? RecipientFleetId, string TargetWorldId, string EdgeId, string From, string To, string Good,
    double Quantity, double DepartAtHours, double ArriveAtHours);
public enum EconomyProjectStatus : byte { WaitingMaterials, Working, Completed }
public sealed record EconomyProjectMaterialState(string Good, double Required, double Delivered, double Incoming);
public sealed record EconomyProjectState(string Id, string World, IReadOnlyList<EconomyProjectMaterialState> Materials,
    double WorkHours, double WorkDone, EconomyProjectStatus Status, string Reason, double? CompletedHour);

public sealed record EconomyInventoryState(string World, string Good, double Stock, double Target, double Capacity, double Reserved);
public sealed record EconomyServiceState(string World, string Group, double Required, double Served, double LastRequired, double LastServed, double? FirstShortageHour);
public sealed record EconomyOrderState(string World, string Good, double Ordered, double Delivered, double? CompletedHour);
public sealed record EconomyEdgeState(string Id, double UsedSlotsLastStep, double CapacitySlotsPerHour, double EmptySlotsLastStep = 0);
public sealed record EconomyGoodActivity(string World, string Good, double Produced, double Consumed, double Required, double Imported, double Exported, double Incoming);
public sealed record EconomyProductionState(string World, string Recipe, double CompletedCyclesLastStep,
    double StartedCyclesLastStep, double WorkInProgress, double BaseCyclesPerHour)
{
    public double EffectiveCyclesPerHour { get; init; }
    public double CommittedExpansionCyclesPerHour { get; init; }
}
public sealed record EconomyRouteActivity(string Edge, string From, string To, string Good, double Dispatched, double InTransit, double TravelHours);
public sealed record EconomyLedgerState(string Good, double Initial, double Manufactured, double Transformed, double Consumed, double Losses, double Stock, double WipInputs, double Transit)
{
    public double Error => Initial + Manufactured - Transformed - Consumed - Losses - Stock - WipInputs - Transit;
}

/// <summary>Detached read-only publication. No collection exposes mutable worker state.</summary>
public sealed record EconomySnapshot(
    double Hour,
    IReadOnlyList<EconomyInventoryState> Inventory,
    IReadOnlyList<EconomyServiceState> Services,
    IReadOnlyList<EconomyOrderState> Orders,
    IReadOnlyList<EconomyEdgeState> Edges,
    IReadOnlyList<EconomyLedgerState> Ledger,
    int WipBatches,
    int TransitCargo,
    IReadOnlyList<EconomyGoodActivity> Goods,
    IReadOnlyList<EconomyRouteActivity> Flows)
{
    public IReadOnlyList<EconomyAssetState> Assets { get; init; } = Array.Empty<EconomyAssetState>();
    public IReadOnlyList<EconomyCapacityState> Capacities { get; init; } = Array.Empty<EconomyCapacityState>();
    public IReadOnlyList<EconomyDietState> Diets { get; init; } = Array.Empty<EconomyDietState>();
    public IReadOnlyList<EconomyProjectState> Projects { get; init; } = Array.Empty<EconomyProjectState>();
    public IReadOnlyList<EconomyProductionState> Production { get; init; } = Array.Empty<EconomyProductionState>();
    public IReadOnlyList<EconomyNavySupplyFlow> NavySupplyFlows { get; init; } = Array.Empty<EconomyNavySupplyFlow>();
}
