using Robust.Shared.Maths;
using Robust.Shared.Map;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Local reception geometry; it grants no travel, admission or respawn rights.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarReceptionSystem))]
public sealed partial class SWGlobalWarReceptionComponent : Component
{
    /// <summary>Prepared placement area in the stationary parent grid's local coordinates.</summary>
    [DataField(required: true)]
    public Box2 PlacementArea;

    /// <summary>Bounded reception and public corridor area searched for a currently safe exit.</summary>
    [DataField(required: true)]
    public Box2 RouteArea;

    /// <summary>Two distinct grid cells beyond the exits, already in public corridors. These are not anchor links.</summary>
    [DataField(required: true)]
    public List<Vector2i> PublicExits = new();

    /// <summary>First prepared stationary source in this campaign; never inferred again from a detached marker.</summary>
    public SWGlobalWarReceptionBinding? Binding;
}

public sealed record SWGlobalWarReceptionBinding(Guid Generation, EntityUid Planet, EntityUid Map,
    EntityCoordinates Coordinates, EntityCoordinates GridCoordinates, Angle GridRotation, Angle MarkerWorldRotation);
