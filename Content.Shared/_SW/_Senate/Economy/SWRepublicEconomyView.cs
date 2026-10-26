using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Public telemetry, copied from one completed economic step. Rates use active game hours.</summary>
[Serializable, NetSerializable]
public sealed class SWEconomyView
{
    public bool Ready;
    public bool Paused;
    public int Revision;
    public double Hour;
    public List<SWEconomyWorldView> Worlds = new();
    public List<SWGovernorView> Governors = new();
    public List<SWEconomyRouteFlow> Flows = new();
    public List<SWEconomyLinkView> Links = new();
}

[Serializable, NetSerializable]
public sealed class SWEconomyWorldView
{
    public string World = string.Empty;
    public List<SWEconomyGoodView> Goods = new();
    public List<SWEconomyServiceView> Services = new();
    public List<SWEconomyAssetView> Assets = new();
    public List<SWEconomyOrderView> Orders = new();
    public List<SWEconomyProductionView> Production = new();
    public SWEconomyCapacityView? Capacity;
    public SWEconomyDietView? Diet;
}

[Serializable, NetSerializable]
public sealed class SWEconomyProductionView
{
    public string Recipe = string.Empty;
    public double CompletedCyclesPerHour;
    public double StartedCyclesPerHour;
    public double WorkInProgress;
    // Definition rate, before assets, demand control and resource constraints.
    public double BaseCyclesPerHour;
    // Base plus commissioned expansions, before the world production multiplier; not actual output.
    public double EffectiveCyclesPerHour;
}

[Serializable, NetSerializable]
public sealed class SWEconomyAssetView
{
    public string Good = string.Empty;
    public double Installed;
    public double Operational;
    public double Target;
    public string Effect = string.Empty;
    public double EffectiveBonus;
}

[Serializable, NetSerializable]
public sealed class SWEconomyOrderView
{
    public string Good = string.Empty;
    public double Ordered;
    public double Delivered;
    public double? CompletedHour;
}

[Serializable, NetSerializable]
public sealed class SWEconomyCapacityView
{
    public double ProductionMultiplier;
    public double LaborCapacity;
    public double StorageMultiplier;
    public double DomesticPower;
    public double ReactorCapacityPower;
    public double FleetCapacity;
    public double FleetOccupied;
}

[Serializable, NetSerializable]
public sealed class SWEconomyDietView
{
    public double Consumed;
    public double Quality;
}

[Serializable, NetSerializable]
public sealed class SWEconomyGoodView
{
    public string Good = string.Empty;
    public double Stock;
    public double Capacity;
    public double ProducedPerHour;
    public double ConsumedPerHour;
    // Recurring consumer requests plus required production inputs and operating fuel.
    // Nutrition can substitute one food for another; group fulfillment describes food security.
    public double RequiredPerHour;
    public double ImportedPerHour;
    public double ExportedPerHour;
    public double Incoming;
}

[Serializable, NetSerializable]
public sealed class SWEconomyServiceView
{
    public string Group = string.Empty;
    public double RequiredPerHour;
    public double ServedPerHour;
}

[Serializable, NetSerializable]
public sealed class SWEconomyRouteFlow
{
    public string Edge = string.Empty;
    public string From = string.Empty;
    public string To = string.Empty;
    public string Good = string.Empty;
    public double UnitsPerHour;
    public double InTransit;
    public double TravelHours;
}

[Serializable, NetSerializable]
public sealed class SWEconomyLinkView
{
    public string Edge = string.Empty;
    public double LoadedSlotsPerHour;
    public double EmptySlotsPerHour;
    public double CapacitySlotsPerHour;
}

[Serializable, NetSerializable]
public sealed class SWRepublicEconomyUpdateEvent(SWEconomyView view) : EntityEventArgs
{
    public readonly SWEconomyView View = view;
}

[Serializable, NetSerializable]
public sealed class SWRepublicEconomyRequestEvent : EntityEventArgs;
