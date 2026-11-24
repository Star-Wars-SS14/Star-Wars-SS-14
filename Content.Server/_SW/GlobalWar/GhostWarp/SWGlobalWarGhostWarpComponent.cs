using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.GhostWarp;

/// <summary>Ghost warp entry for one surface or orbit of a campaign planet, loaded or not.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarGhostWarpSystem))]
public sealed partial class SWGlobalWarGhostWarpComponent : Component
{
    [ViewVariables]
    public EntityUid Planet;

    [ViewVariables]
    public SWGlobalWarPlanetLocation Location;
}
