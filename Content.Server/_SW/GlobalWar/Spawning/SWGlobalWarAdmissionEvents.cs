using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Allows campaign admission to replace the legacy unrestricted overflow path.</summary>
[ByRefEvent]
public record struct SWGlobalWarOverflowJobsEvent(
    Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> AssignedJobs,
    IEnumerable<NetUserId> Players,
    IReadOnlyDictionary<NetUserId, HumanoidCharacterProfile> Profiles)
{
    public bool Handled;
}

/// <summary>Selects a permitted late-join role before normal job restrictions and character creation.</summary>
[ByRefEvent]
public record struct SWGlobalWarSelectAdmissionEvent(
    ICommonSession Player,
    HumanoidCharacterProfile Profile,
    EntityUid Station,
    string? Job)
{
    public bool Cancelled;
    public string Reason = string.Empty;
}
