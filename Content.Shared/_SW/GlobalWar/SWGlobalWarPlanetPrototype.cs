using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.GlobalWar;

/// <summary>
/// Static definition of a Global War planet and its two independently simulated locations.
/// </summary>
[Prototype("swGlobalWarPlanet")]
public sealed partial class SWGlobalWarPlanetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>Position on the travel console's schematic map, independent of gameplay coordinates.</summary>
    [DataField]
    public Vector2 MapPosition;

    [DataField]
    public string Description = string.Empty;

    [DataField(required: true)]
    public ProtoId<GameMapPrototype> Surface;

    [DataField(required: true)]
    public ProtoId<GameMapPrototype> Orbit;

    /// <summary>
    /// Hyperspace travel time to this planet in seconds; null uses shuttle.travel_time.
    /// </summary>
    [DataField]
    public float? HyperspaceTime;

    /// <summary>
    /// Direct, directional connections to other planets. Return connections must be configured separately.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<SWGlobalWarPlanetPrototype>> Connections = new();
}

public enum SWGlobalWarPlanetLocation : byte
{
    Surface,
    Orbit,
}
