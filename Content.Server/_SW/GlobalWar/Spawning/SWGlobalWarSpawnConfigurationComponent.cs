using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Trusted role catalog and independent job accounting for a campaign.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarSpawnSystem), typeof(SWGlobalWarAssignmentsSystem))]
public sealed partial class SWGlobalWarSpawnConfigurationComponent : Component
{
    /// <summary>Roles available in this campaign; body components never define faction or Force eligibility.</summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, SWGlobalWarSpawnRole> Roles = new();

    /// <summary>One job station per faction, independent of the physical maps loaded during the campaign.</summary>
    [DataField]
    public Dictionary<SWGlobalWarFaction, StationConfig> Stations = new();

    /// <summary>Explicit civilian fallback per faction; consent and ordinary job restrictions still apply.</summary>
    [DataField]
    public Dictionary<SWGlobalWarFaction, ProtoId<JobPrototype>> CivilianRoles = new();

    /// <summary>How long a death must last, without revival, before the player may continue in a new life.</summary>
    [DataField]
    public TimeSpan RespawnDelay = TimeSpan.FromSeconds(300);
}

[DataDefinition]
public sealed partial class SWGlobalWarSpawnRole
{
    /// <summary>Campaign office accounted for by the single appointment registry; absent in legacy presets.</summary>
    [DataField]
    public SWGlobalWarRole? Role;

    /// <summary>The faction permanently selected by successfully spawning in this role.</summary>
    [DataField(required: true)]
    public SWGlobalWarFaction Faction;

    /// <summary>Force roles use only their original home Force world and the configured job slot.</summary>
    [DataField]
    public bool Force;
}
