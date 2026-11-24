using System.Linq;
using Content.Shared.CCVar;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_executing || !TryGetPolitics(out var politics) || Now() < politics.Comp.NextUpdate)
            return;
        politics.Comp.NextUpdate = Now() + TimeSpan.FromSeconds(1);
        var before = politics.Comp.Revision;
        _executing = true;
        try
        {
            Advance(politics);
        }
        finally
        {
            _executing = false;
        }
        if (before != politics.Comp.Revision)
            Notify(politics);
    }

    private void Advance(Entity<SWGlobalWarPoliticsComponent> politics)
    {
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            return;
        var now = Now();
        var state = politics.Comp;
        if (campaign.Comp.Phase == SWGlobalWarCampaignPhase.Capitulating)
        {
            if (state.Surrenders.Count == 1 && state.SurrenderTick < _timing.CurTick)
                foreach (var (loser, deadline) in state.Surrenders)
                    if (now >= deadline)
                        TransferSurrender(politics, loser);
            return;
        }
        AdvanceNeutrals(politics, campaign.Comp, now);
        AdvanceWithdrawals(politics, now);
        var represented = RepresentedFactions();
        if (!state.DoctrineChecked && now >= campaign.Comp.PeaceDuration)
        {
            state.DoctrineChecked = true;
            if (represented.Count == 2 && CurrentHead(SWGlobalWarFaction.Republic) == null &&
                CurrentHead(SWGlobalWarFaction.Empire) == null && state.Relations == SWGlobalWarRelations.Peace)
                SetRelations(politics, SWGlobalWarRelations.War);
            state.Revision++;
        }
    }

    internal void ArbitrateCampaignStep(Guid generation)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            return;
        var state = politics.Comp;
        var now = Now();
        if (campaign.Comp.Phase == SWGlobalWarCampaignPhase.Capitulating)
        {
            if (state.SurrenderTick >= _timing.CurTick)
                return;
            if (state.Surrenders.Count == 2)
                Finish(politics, SWGlobalWarOutcome.MutualSurrender);
            else
                foreach (var (loser, deadline) in state.Surrenders)
                    if (now >= deadline && !HasTerritory(politics, loser))
                        Finish(politics, Victory(Opponent(loser)));
            return;
        }
        if (!HasTerritory(politics, SWGlobalWarFaction.Republic))
        {
            if (HasTerritory(politics, SWGlobalWarFaction.Empire))
                Finish(politics, SWGlobalWarOutcome.EmpireVictory);
            return;
        }
        if (!HasTerritory(politics, SWGlobalWarFaction.Empire))
        {
            Finish(politics, SWGlobalWarOutcome.RepublicVictory);
            return;
        }
        var represented = RepresentedFactions();
        foreach (var faction in new[] { SWGlobalWarFaction.Republic, SWGlobalWarFaction.Empire })
        {
            if (represented.Contains(faction))
            {
                state.AbsentSince.Remove(faction);
                continue;
            }
            if (!state.AbsentSince.TryGetValue(faction, out var since))
                state.AbsentSince[faction] = since = now;
            if (now - since >= campaign.Comp.ParticipantAbsenceDuration)
            {
                Finish(politics, SWGlobalWarOutcome.InsufficientParticipants);
                return;
            }
        }
        if (campaign.Comp.TimeLimitVictory && !state.LimitAnnounced &&
            now >= campaign.Comp.MaximumDuration - campaign.Comp.TimeLimitWarning)
        {
            state.LimitAnnounced = true;
            var (republic, empire) = CountLocations(politics);
            _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-limit-warning",
                ("minutes", (int) campaign.Comp.TimeLimitWarning.TotalMinutes), ("republic", republic), ("empire", empire)));
        }
        if (now >= campaign.Comp.MaximumDuration)
            FinishAtLimit(politics, campaign.Comp);
    }

    private void FinishAtLimit(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarCampaignComponent campaign)
    {
        var (republic, empire) = CountLocations(politics);
        if (!campaign.TimeLimitVictory || republic == empire)
        {
            Finish(politics, SWGlobalWarOutcome.TimeLimit);
            return;
        }
        politics.Comp.LimitScore = (republic, empire);
        Finish(politics, Victory(republic > empire ? SWGlobalWarFaction.Republic : SWGlobalWarFaction.Empire));
    }

    private (int Republic, int Empire) CountLocations(Entity<SWGlobalWarPoliticsComponent> politics)
    {
        var republic = 0;
        var empire = 0;
        foreach (var uid in Comp<SWGlobalWarGameruleComponent>(politics.Owner).PlanetEntities.Values)
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                continue;
            foreach (var owner in new[] { planet.OrbitOwner, planet.SurfaceOwner })
            {
                if (owner == SWGlobalWarFaction.Republic)
                    republic++;
                else if (owner == SWGlobalWarFaction.Empire)
                    empire++;
            }
        }
        return (republic, empire);
    }

    private HashSet<SWGlobalWarFaction> RepresentedFactions()
    {
        return _players.Sessions.Where(s => s.Status == SessionStatus.InGame)
            .Select(s => _identity.TryGetMembership(s.UserId, out var faction) ? faction : SWGlobalWarFaction.Neutral)
            .Where(f => f != SWGlobalWarFaction.Neutral).ToHashSet();
    }

    private bool HasTerritory(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarFaction faction)
    {
        return Comp<SWGlobalWarGameruleComponent>(politics.Owner).PlanetEntities.Values.Any(p =>
            TryComp<SWGlobalWarPlanetComponent>(p, out var planet) &&
            (planet.OrbitOwner == faction || planet.SurfaceOwner == faction));
    }

    private void TransferSurrender(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarFaction loser)
    {
        foreach (var uid in Comp<SWGlobalWarGameruleComponent>(politics.Owner).PlanetEntities.Values)
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(uid, out var planet))
                continue;
            var revision = planet.OwnershipRevision;
            foreach (var location in new[] { SWGlobalWarPlanetLocation.Orbit, SWGlobalWarPlanetLocation.Surface })
            {
                var owner = location == SWGlobalWarPlanetLocation.Orbit ? planet.OrbitOwner : planet.SurfaceOwner;
                if (owner == loser)
                    if (_steps.TryQueueOwnership(uid, location, Opponent(loser), revision,
                            politics.Comp.Generation, Guid.NewGuid()))
                        revision++;
            }
        }
    }

    private static SWGlobalWarOutcome Victory(SWGlobalWarFaction faction)
    {
        return faction == SWGlobalWarFaction.Republic
            ? SWGlobalWarOutcome.RepublicVictory : SWGlobalWarOutcome.EmpireVictory;
    }

    private void Finish(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarOutcome outcome)
    {
        if (politics.Comp.Outcome != SWGlobalWarOutcome.None ||
            !_campaign.TryAdvancePhase(politics.Comp.Generation, SWGlobalWarCampaignPhase.Ending))
            return;
        politics.Comp.Outcome = outcome;
        politics.Comp.Revision++;
        politics.Comp.TruceConsents.Clear();
        politics.Comp.SurrenderConfirmations.Clear();
        foreach (var neutral in politics.Comp.Neutrals.Values)
            RetireCaptures(neutral);
        _outcomePending = true;
    }

    internal void PublishCampaignStep(Guid generation)
    {
        if (!TryGetPolitics(out var politics) || politics.Comp.Generation != generation)
            return;
        if (_publishedRevision != politics.Comp.Revision)
            Notify(politics);
        if (!_outcomePending)
            return;
        _outcomePending = false;
        var outcome = politics.Comp.Outcome;
        var completed = new SWGlobalWarCampaignOutcomeEvent(generation, outcome);
        RaiseLocalEvent(ref completed);
        // An admin may have ended the round already; a second end would repeat the summary with another outcome.
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;
        _ticker.EndRound(politics.Comp.LimitScore is { } score
            ? Loc.GetString(outcome == SWGlobalWarOutcome.RepublicVictory
                    ? "sw-global-war-outcome-limit-republic" : "sw-global-war-outcome-limit-empire",
                ("republic", score.Republic), ("empire", score.Empire))
            : Loc.GetString(outcome switch
            {
                SWGlobalWarOutcome.RepublicVictory => "sw-global-war-outcome-republic",
                SWGlobalWarOutcome.EmpireVictory => "sw-global-war-outcome-empire",
                SWGlobalWarOutcome.MutualSurrender => "sw-global-war-outcome-mutual",
                SWGlobalWarOutcome.InsufficientParticipants => "sw-global-war-outcome-participants",
                _ => "sw-global-war-outcome-time",
            }));
        // GameTicker.EndRound only enters PostRound; the restart is the rule's job, as in MaxTimeRestartRuleSystem.
        Timer.Spawn(TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.RoundRestartTime)), () =>
        {
            if (_ticker.RunLevel == GameRunLevel.PostRound)
                _ticker.RestartRound();
        });
    }
}
