using Content.Shared._SW.GlobalWar;
using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

public sealed partial class SWGlobalWarSpawnSystem
{
    /// <summary>Read-only civilian selection. The ordinary GameTicker admission still validates and reserves the spawn.</summary>
    public bool TryGetCivilianAdmission(ICommonSession session, SWGlobalWarFaction faction,
        out ProtoId<JobPrototype> job, out EntityUid station)
    {
        job = default;
        station = default;
        return _rules.TryGetGlobalWarGamerule(out var rule) && _generation == rule.Comp.Generation &&
               _rule == rule.Owner && TryComp<SWGlobalWarSpawnConfigurationComponent>(rule.Owner, out var configuration) &&
               _identity.CanJoin(session.UserId, faction) && TryGetCivilian(configuration, faction, out job, out station);
    }
}
