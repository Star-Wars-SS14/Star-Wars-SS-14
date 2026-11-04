using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Allows campaign role stations to use job capacity without physical-station population quotas.</summary>
[ByRefEvent]
public record struct SWGlobalWarJobAllocationEvent(IReadOnlyList<EntityUid> Stations)
{
    public bool BalanceStations = true;
    public IReadOnlyDictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)>? PriorAllocations;
    public HashSet<NetUserId> AlreadyCounted = new();
}
