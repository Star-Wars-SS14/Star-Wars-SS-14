using Robust.Shared.Map;

namespace Content.Server._SW.GlobalWar.Access;

/// <summary>Raised before ordinary FTL mutates thrusters, docking or flight state.</summary>
[ByRefEvent]
public record struct SWGlobalWarFtlAttemptEvent(EntityUid Shuttle, EntityCoordinates Destination, EntityUid? DockTarget = null)
{
    public bool Cancelled;
}
