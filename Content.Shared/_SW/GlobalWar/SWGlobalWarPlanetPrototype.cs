using System.Collections.Generic;
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

    [DataField(required: true)]
    public ProtoId<GameMapPrototype> Surface;

    [DataField(required: true)]
    public ProtoId<GameMapPrototype> Orbit;

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
