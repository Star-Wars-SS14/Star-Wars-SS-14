using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar;

/// <summary>
/// Configuration and planet registry for the singleton GlobalWarGamerule.
/// </summary>
[RegisterComponent, Access(typeof(SWGlobalWarGameruleSystem), typeof(SWGlobalWarPlanetSystem))]
public sealed partial class SWGlobalWarGameruleComponent : Component
{
    /// <summary>Unique identity for operations belonging to this activation of the rule.</summary>
    [ViewVariables]
    public Guid Generation;

    [DataField]
    public List<ProtoId<SWGlobalWarPlanetPrototype>> Planets = new();

    [DataField]
    public TimeSpan EmptyMapPauseDelay = TimeSpan.FromMinutes(5);

    /// <summary>Maximum cargo volume simultaneously in flight, shared by both directions of each connected pair.</summary>
    [DataField]
    public double AutoTradeCapacity = 60;

    /// <summary>Virtual freight travel time per connection, independent of player shuttle FTL settings.</summary>
    [DataField]
    public TimeSpan AutoTradeTravelTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runtime registry. Entity UIDs are never used as persistent planet identities.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, EntityUid> PlanetEntities = new();
}
