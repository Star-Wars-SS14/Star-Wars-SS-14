using Robust.Shared.Map;

namespace Content.Shared._SW.GlobalWar.Access;

/// <summary>Server policy may reject a portal transfer before pulling, timeout or movement side effects.</summary>
[ByRefEvent]
public record struct SWGlobalWarPortalAttemptEvent(EntityUid Subject, EntityCoordinates Target)
{
    public bool Cancelled;
}
