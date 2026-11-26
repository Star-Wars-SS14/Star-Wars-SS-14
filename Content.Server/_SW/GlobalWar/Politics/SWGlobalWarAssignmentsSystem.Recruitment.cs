using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Roles;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    [Dependency] private readonly SharedRoleSystem _roles = default!;

    /// <summary>
    /// Replaces only the current civilian appointment with an eligible ordinary soldier appointment.
    /// The caller validates the physical recruitment console. Identity, restrictions and slots stay authoritative here.
    /// No body, equipment, ID card or starting supply is created.
    /// </summary>
    internal bool TryRecruitCivilian(ICommonSession session, Guid generation, long revision)
    {
        if (_personnelTransaction || !TryGetRegistry(out var registry) || registry.Comp.Generation != generation ||
            !TryGetActor(session, generation, revision, out var actor) || actor.Role != SWGlobalWarRole.Civilian ||
            !TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog) ||
            !CanTake(actor.Faction, SWGlobalWarRole.Soldier))
            return false;
        foreach (var (job, entry) in catalog.Roles)
        {
            if (entry.Faction != actor.Faction || entry.Role != SWGlobalWarRole.Soldier ||
                !CanAssumeCampaignJob(session, actor.Body, job))
                continue;
            var eligibility = new SWGlobalWarEvaluateAppointmentEvent(actor.Body, actor.Mind, actor.Faction,
                SWGlobalWarRole.Soldier, generation);
            RaiseLocalEvent(ref eligibility);
            if (!eligibility.Allowed || !TryGetActor(session, generation, revision, out var current) || current.Id != actor.Id)
                return false;
            _personnelTransaction = true;
            _pendingNotification = null;
            try
            {
                Replace(registry, session, actor.Mind, actor.Faction, SWGlobalWarRole.Soldier, job);
                // The character panel names the mind's job: without this the recruit still read as a citizen.
                _roles.MindAddJobRole(actor.Mind, silent: true, jobPrototype: job.Id);
            }
            finally
            {
                _personnelTransaction = false;
            }
            var pending = _pendingNotification;
            _pendingNotification = null;
            if (pending is { } changed)
                NotifyAssignments(changed);
            return true;
        }
        return false;
    }
}
