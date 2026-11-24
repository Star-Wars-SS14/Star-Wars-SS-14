using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

/// <summary>A player opens or closes the galaxy map. While open, the server sends the owners whenever they change.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarGalaxyRequestEvent(bool open) : EntityEventArgs
{
    public readonly bool Open = open;
}

/// <summary>Who holds each world's station and surface. Public in the campaign: every navigation console shows it.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarGalaxyWorld(ProtoId<SWGlobalWarPlanetPrototype> Planet, SWGlobalWarFaction Orbit, SWGlobalWarFaction Surface);

[Serializable, NetSerializable]
public sealed class SWGlobalWarGalaxyStateEvent(List<SWGlobalWarGalaxyWorld> worlds) : EntityEventArgs
{
    public readonly List<SWGlobalWarGalaxyWorld> Worlds = worlds;
}
