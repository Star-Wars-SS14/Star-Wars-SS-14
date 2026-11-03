using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// A planet controller in nullspace, independent of the lifetime and pause state of its maps.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarPlanetSystem))]
public sealed partial class SWGlobalWarPlanetComponent : Component
{
    [ViewVariables]
    public ProtoId<SWGlobalWarPlanetPrototype> Prototype;

    [ViewVariables]
    public EntityUid GlobalWarGamerule;

    /// <summary>Current station owner. Only the ownership API may change it.</summary>
    [ViewVariables]
    public SWGlobalWarFaction OrbitOwner;

    /// <summary>Current surface owner; station capture does not overwrite it.</summary>
    [ViewVariables]
    public SWGlobalWarFaction SurfaceOwner;

    /// <summary>Monotonic version used to reject stale ownership mutations.</summary>
    [ViewVariables]
    public long OwnershipRevision;

    /// <summary>Accepted mutations keyed by caller-generated operation ID for this controller.</summary>
    public Dictionary<Guid, (SWGlobalWarPlanetLocation Location, SWGlobalWarFaction Owner, long Revision)> OwnershipOperations = new();

    /// <summary>Maps remain registered after destruction so loading cannot replenish their contents.</summary>
    public Dictionary<SWGlobalWarPlanetLocation, SWGlobalWarLocationState> Locations = new();

    [ViewVariables]
    public Dictionary<SWGlobalWarPlanetLocation, EntityUid> Maps = new();

    /// <summary>
    /// Stations created by this planet's game maps, tracked for failed-start cleanup.
    /// </summary>
    public HashSet<EntityUid> Stations = new();

    /// <summary>
    /// Original station grids, excluding ships assigned to these stations later in the round.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> StationGrids = new();
}
