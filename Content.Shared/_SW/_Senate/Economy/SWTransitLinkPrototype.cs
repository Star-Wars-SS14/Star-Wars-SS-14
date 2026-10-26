using System;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>
/// Shared capacity of an undirected hyperspace link. Planned traffic sizes the initial scenario;
/// it is not an observation of live shipments. All hours are active game-hours.
/// </summary>
[Prototype("swTransitLink")]
public sealed partial class SWTransitLinkPrototype : IPrototype
{
    [IdDataField] public string ID { get; } = default!;
    [DataField(required: true)] public string From = string.Empty;
    [DataField(required: true)] public string To = string.Empty;
    [DataField(required: true)] public bool Accessible;
    [DataField(required: true)] public double TravelHours;
    /// <summary>Ship fuel consumed per occupied cargo slot for the whole crossing, including empty returns.</summary>
    [DataField(required: true)] public double FuelPerSlot;
    /// <summary>Total departure throughput shared by both directions and loaded/empty crossings.</summary>
    [DataField(required: true)] public double CapacitySlotsPerHour;
    [DataField(required: true)] public double PlannedLoadedSlotsPerHour;
    [DataField(required: true)] public double PlannedEmptySlotsPerHour;

    public double PlannedSlotsPerHour => PlannedLoadedSlotsPerHour + PlannedEmptySlotsPerHour;
    public double PlannedUtilization => CapacitySlotsPerHour > 0 ? PlannedSlotsPerHour / CapacitySlotsPerHour : 0;
    public double AvailableSlotsPerHour => Math.Max(0, CapacitySlotsPerHour - PlannedSlotsPerHour);

    /// <summary>World identifiers are stable ASCII keys; direction must not create a second capacity pool.</summary>
    public static string LinkId(string from, string to) => string.CompareOrdinal(from, to) <= 0
        ? $"{from}--{to}"
        : $"{to}--{from}";
}
