using Content.Shared.Preferences;
using Robust.Shared.Player;
using Robust.Shared.Network;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Last synchronous preparation point before lobby state, mind, body or job accounting changes.</summary>
[ByRefEvent]
public record struct SWGlobalWarBeforeSpawnEvent(
    ICommonSession Player,
    HumanoidCharacterProfile Profile,
    string Job,
    bool LateJoin,
    EntityUid Station)
{
    public bool Cancelled;
    public string Reason = string.Empty;
}

/// <summary>Rechecks a prepared destination after preparation subscribers and before GameTicker mutates player state.</summary>
[ByRefEvent]
public record struct SWGlobalWarValidateSpawnEvent(ICommonSession Player, string Job, EntityUid Station)
{
    public bool Cancelled;
    public string Reason = string.Empty;
}

/// <summary>Always raised from GameTicker's finally, including later cancellation and character creation failure.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarSpawnFinishedEvent(NetUserId User, string Job, EntityUid Station);

[ByRefEvent]
public readonly record struct SWGlobalWarRoleStationsReadyEvent(EntityUid Rule, Guid Generation);

/// <summary>A managed appointment already reserves this place; preserve job history without another decrement.</summary>
[ByRefEvent]
public record struct SWGlobalWarAssignJobEvent(EntityUid Station, string Job, NetUserId User)
{
    public bool Handled;
    public bool Allowed;
}

[ByRefEvent]
public record struct SWGlobalWarRoundStartJobsEvent(EntityUid Station)
{
    public Dictionary<ProtoId<JobPrototype>, int?>? Jobs;
}
