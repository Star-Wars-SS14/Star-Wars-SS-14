using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Runtime reservation owned by a travelling ship, including while it is on the hyperspace map.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarTravelSystem))]
public sealed partial class SWGlobalWarTravelComponent : Component
{
    public EntityUid GlobalWarGamerule;
    public ProtoId<SWGlobalWarPlanetPrototype> Source;
    public ProtoId<SWGlobalWarPlanetPrototype> Destination;
    public EntityUid DestinationPlanet;
    public EntityUid DestinationMap;
    public Vector2 ArrivalCenter;
    public float ArrivalRadius;
}
