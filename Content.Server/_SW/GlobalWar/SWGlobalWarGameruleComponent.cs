using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Configuration and planet registry for a Global War campaign.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarGameruleSystem), typeof(SWGlobalWarPlanetSystem))]
public sealed partial class SWGlobalWarGameruleComponent : Component
{
    [DataField]
    public List<ProtoId<SWGlobalWarPlanetPrototype>> Planets = new();

    [DataField]
    public TimeSpan EmptyMapPauseDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runtime registry. Entity UIDs are never used as persistent planet identities.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, EntityUid> PlanetEntities = new();
}
