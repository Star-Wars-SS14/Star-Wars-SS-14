using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared.Humanoid;
using Content.Shared.Roles;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    [Dependency] private readonly IBanManager _bans = default!;
    [Dependency] private readonly UserDbDataManager _userData = default!;

    /// <summary>Evaluates the selected campaign job for the current owned body with loaded server-side restrictions.</summary>
    public bool CanAssumeCampaignJob(ICommonSession session, EntityUid body, ProtoId<JobPrototype> job)
    {
        if (!_userData.IsLoadComplete(session) || !TryGetRegistry(out var registry) ||
            !_identity.TryGetActor(session, body, out var mind, out var faction) || !_mobs.IsAlive(body) ||
            !TryGetRole(registry.Owner, job, out var jobFaction, out _) || jobFaction != faction ||
            !TryComp<SWGlobalWarCharacterProfileComponent>(body, out var snapshot) ||
            snapshot.Generation != registry.Comp.Generation || snapshot.User != session.UserId || snapshot.Mind != mind ||
            !TryComp<HumanoidAppearanceComponent>(body, out var appearance))
            return false;
        var bans = _bans.GetJobBans(session.UserId);
        if (bans == null || bans.Contains(job))
            return false;
        var profile = snapshot.Profile.WithSpecies(appearance.Species.Id).WithAge(appearance.Age);
        var allowed = new IsRoleAllowedEvent(session, [job], null) { ActiveCharacterProfile = profile };
        RaiseLocalEvent(ref allowed);
        return !allowed.Cancelled;
    }

    private bool CanAssumeHead(Entity<SWGlobalWarAssignmentsComponent> registry,
        ICommonSession session, SWGlobalWarFaction faction)
    {
        return TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog) &&
               session.AttachedEntity is { } body && catalog.Roles.Any(p => p.Value.Faction == faction &&
                   p.Value.Role == SWGlobalWarRole.Head && CanAssumeCampaignJob(session, body, p.Key));
    }
}
