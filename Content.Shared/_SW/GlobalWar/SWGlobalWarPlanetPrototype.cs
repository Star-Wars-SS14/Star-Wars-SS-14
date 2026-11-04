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

    /// <summary>Server map to load on first visit; absent means content is not available yet.</summary>
    [DataField(serverOnly: true, customTypeSerializer: typeof(SWGlobalWarMapIdValidator))]
    public ProtoId<GameMapPrototype>? Surface;

    /// <summary>Server orbit map to load on first visit; never substitute an arbitrary map.</summary>
    [DataField(serverOnly: true, customTypeSerializer: typeof(SWGlobalWarMapIdValidator))]
    public ProtoId<GameMapPrototype>? Orbit;

    /// <summary>Initial ownership copied once to the runtime controller.</summary>
    [DataField]
    public SWGlobalWarFaction InitialOwner;

    /// <summary>Economic and spawn-policy role of this sector.</summary>
    [DataField]
    public SWGlobalWarSectorKind Kind;

    /// <summary>Whether this mining sector is outside its faction's home rings.</summary>
    [DataField]
    public bool Enclave;

    /// <summary>One finite emergency lanthanum deposit; ownership changes never replenish it.</summary>
    [DataField(serverOnly: true)]
    public bool LanthanumReserve;

    /// <summary>Optional directed edge travel durations, including long enclave corridors.</summary>
    [DataField]
    public Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, TimeSpan> TravelTimes = new();

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

    /// <summary>Initial inventory and consumption; runtime stock belongs to the planet controller.</summary>
    [DataField]
    public Dictionary<ProtoId<SWGlobalWarCommodityPrototype>, SWGlobalWarMarketGoodConfiguration> Market = new();

    /// <summary>Optional campaign profile; legacy GlobalWar continues to use Market.</summary>
    [DataField(serverOnly: true)]
    public ProtoId<SWGlobalWarEconomyProfilePrototype>? EconomyProfile;

    /// <summary>Economic graph, independent of FTL Connections. Each direction must be explicitly configured.</summary>
    [DataField(serverOnly: true)]
    public Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarEconomicConnection> TradeConnections = new();
}

public enum SWGlobalWarPlanetLocation : byte
{
    Surface,
    Orbit,
}
