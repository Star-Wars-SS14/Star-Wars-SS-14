using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

/// <summary>Public, read-only crisis information; deliberately excludes political logs and actor identities.</summary>
[Serializable, NetSerializable]
public sealed class SWMapCrisisEntry
{
    public string Id = string.Empty;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public string Status = string.Empty;
    public string LocationLabel = string.Empty;
    public string Consequences = string.Empty;
    public List<string> SectorIds = new();
    public string WorldId = string.Empty;
    public string EdgeId = string.Empty;
    public float Severity;
    public string EffectsLabel = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
    public List<SWMapCrisisRouteEntry> Routes = new();
}

[Serializable, NetSerializable]
public sealed class SWMapCrisisRouteEntry
{
    public string EdgeId = string.Empty;
    /// <summary>Combined current defense restriction on new departures, independently of freight usage.</summary>
    public double CapacityMultiplier = 1;
    public double BaseCapacity;
    public double UsedSlotsPerHour;
    public bool HasUsage;
}
