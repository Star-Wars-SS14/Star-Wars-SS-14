using Content.Shared._SW.GlobalWar;
using Robust.Shared.Audio;

namespace Content.Server._SW.GlobalWar;

/// <summary>Automatically pairs a surface/orbit endpoint using the planet of its current map.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarPlanetTeleporterSystem))]
public sealed partial class SWGlobalWarPlanetTeleporterComponent : Component
{
    [ViewVariables]
    public EntityUid? Planet;

    [ViewVariables]
    public SWGlobalWarPlanetLocation Location;

    [ViewVariables]
    public EntityUid? LinkedTeleporter;

    [DataField]
    public SoundSpecifier DepartureSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    [DataField]
    public SoundSpecifier ArrivalSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");
}
