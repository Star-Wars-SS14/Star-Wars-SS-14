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
    public EntityUid Gamerule;

    [ViewVariables]
    public Dictionary<SWGlobalWarPlanetLocation, EntityUid> Maps = new();

    /// <summary>
    /// Stations created by this planet's game maps, tracked for failed-start cleanup.
    /// </summary>
    public HashSet<EntityUid> Stations = new();
}
