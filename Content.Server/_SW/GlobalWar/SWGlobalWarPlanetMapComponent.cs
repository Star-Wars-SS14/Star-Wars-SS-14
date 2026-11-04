using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Associates a physical map with its planet and tracks location-specific activity.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarPlanetSystem))]
public sealed partial class SWGlobalWarPlanetMapComponent : Component
{
    [ViewVariables]
    public EntityUid Planet;

    [ViewVariables]
    public SWGlobalWarPlanetLocation Location;

    [ViewVariables]
    public TimeSpan? EmptySince;

    /// <summary>
    /// Future travel code can hold a destination awake using its travelling entity as an idempotent key.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> IncomingTravellers = new();

    /// <summary>Explicit activity reservations; deleting the owner releases its reservation.</summary>
    public Dictionary<Guid, (EntityUid Owner, string Reason)> Leases = new();
}
