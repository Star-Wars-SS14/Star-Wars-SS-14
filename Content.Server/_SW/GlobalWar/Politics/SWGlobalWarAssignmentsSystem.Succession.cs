using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Robust.Shared.Network;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    /// <summary>Returns a recipient's current vacancy epoch and priority window without exposing other candidates.</summary>
    public bool TryGetSuccession(SWGlobalWarFaction faction, NetUserId user,
        out Guid vacancy, out int stage, out TimeSpan remaining, out bool eligible)
    {
        vacancy = Guid.Empty;
        stage = -1;
        remaining = TimeSpan.Zero;
        eligible = false;
        if (!TryGetRegistry(out var registry))
            return false;
        AdvanceSuccession(registry, faction);
        if (!registry.Comp.Successions.TryGetValue(faction, out var succession))
            return false;
        vacancy = succession.Id;
        stage = succession.Stage;
        remaining = TimeSpan.FromTicks(Math.Max(0, (succession.Deadline - CampaignNow()).Ticks));
        eligible = succession.Stage == 2
            ? _players.TryGetSessionById(user, out var session) && CanAssumeHead(registry, session, faction)
            : EligibleCandidate(registry, faction, succession, user);
        return true;
    }

    private void OnInitialAllocationComplete(RulePlayerJobsAssignedEvent args)
    {
        if (!TryGetRegistry(out var registry) || registry.Comp.InitialAllocationComplete)
            return;
        registry.Comp.InitialAllocationComplete = true;
        foreach (var faction in new[] { SWGlobalWarFaction.Republic, SWGlobalWarFaction.Empire })
        {
            if (Available(faction, SWGlobalWarRole.Head) == 1)
                BeginSuccession(registry, faction);
        }
        Changed(registry);
    }

    private void BeginSuccession(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarFaction faction)
    {
        if (!registry.Comp.InitialAllocationComplete || registry.Comp.Successions.ContainsKey(faction))
            return;
        registry.Comp.Successions.Add(faction, new SWGlobalWarSuccession(Guid.NewGuid(), 0,
            CampaignNow() + TimeSpan.FromSeconds(30), CaptureCandidates(registry, faction, 0)));
        AdvanceSuccession(registry, faction);
    }

    private bool HeadEntryOpen(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarFaction faction)
    {
        if (!registry.Comp.InitialAllocationComplete)
            return true;
        AdvanceSuccession(registry, faction);
        return registry.Comp.Successions.TryGetValue(faction, out var succession) && succession.Stage == 2;
    }

    private void AdvanceSuccession(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarFaction faction)
    {
        if (!registry.Comp.Successions.TryGetValue(faction, out var succession))
            return;
        var changed = false;
        var now = CampaignNow();
        while (succession.Stage < 2)
        {
            var expired = now >= succession.Deadline;
            if (!expired && succession.Candidates.Keys.Any(user => EligibleCandidate(registry, faction, succession, user)))
                break;
            var stage = succession.Stage + 1;
            // A delayed tick cannot extend either window. Empty/declined stages advance immediately.
            var start = expired ? succession.Deadline : now;
            succession = new SWGlobalWarSuccession(succession.Id, stage,
                stage < 2 ? start + TimeSpan.FromSeconds(30) : start,
                stage < 2 ? CaptureCandidates(registry, faction, stage) : new());
            registry.Comp.Successions[faction] = succession;
            changed = true;
        }
        if (changed)
            Changed(registry);
    }

    private Dictionary<NetUserId, Guid> CaptureCandidates(Entity<SWGlobalWarAssignmentsComponent> registry,
        SWGlobalWarFaction faction, int stage)
    {
        var result = new Dictionary<NetUserId, Guid>();
        foreach (var assignment in registry.Comp.Assignments.Values)
        {
            if (assignment.Faction == faction && SuccessionTier(assignment) == stage &&
                _players.TryGetSessionById(assignment.User, out var session) &&
                TryGetActor(session, assignment.Generation, assignment.Revision, out _) &&
                CanAssumeHead(registry, session, faction))
                result.Add(assignment.User, assignment.Id);
        }
        return result;
    }

    private bool EligibleCandidate(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarFaction faction,
        SWGlobalWarSuccession succession, NetUserId user)
    {
        return succession.Candidates.TryGetValue(user, out var id) &&
               registry.Comp.Assignments.TryGetValue(user, out var assignment) && assignment.Id == id &&
               assignment.Faction == faction && SuccessionTier(assignment) == succession.Stage &&
               _players.TryGetSessionById(user, out var session) &&
               TryGetActor(session, assignment.Generation, assignment.Revision, out _) &&
               CanAssumeHead(registry, session, faction);
    }

    private TimeSpan CampaignNow()
    {
        return _campaign.TryGetCampaign(out var campaign) ? _campaign.Elapsed(campaign) : TimeSpan.Zero;
    }
}
