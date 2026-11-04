using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;
using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Runtime reservation owned by a travelling ship, including while it is on the hyperspace map.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarTravelSystem))]
public sealed partial class SWGlobalWarTravelComponent : Component
{
    public EntityUid GlobalWarGamerule;
    public Guid Generation;
    /// <summary>Unique server departure identity; returning retains it and a later trip creates a new one.</summary>
    public Guid FlightId;
    /// <summary>Prevents duplicate completion callbacks from acknowledging a manifest twice.</summary>
    public bool ArrivalPublished;
    public EntityUid SourcePlanet;
    public EntityUid SourceMap;
    public EntityCoordinates SourceCoordinates;
    public Angle SourceAngle;
    public bool UseStationArrival;
    public EntityUid? DockTarget;
    public Angle ArrivalAngle;
    public bool Returning;
    public TimeSpan? BlockedSince;
    public ProtoId<SWGlobalWarPlanetPrototype> Source;
    public ProtoId<SWGlobalWarPlanetPrototype> Destination;
    public EntityUid DestinationPlanet;
    public EntityUid DestinationMap;
    public Vector2 ArrivalCenter;
    public float ArrivalRadius;
    public Content.Server._SW.GlobalWar.Politics.SWGlobalWarShipWithdrawalOrder? Withdrawal;
}
