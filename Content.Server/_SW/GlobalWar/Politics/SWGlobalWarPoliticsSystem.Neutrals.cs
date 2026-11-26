using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    /// <summary>Server-only probability; decimal intermediates cannot overflow when adding two nonnegative longs.</summary>
    public static double RepublicProbability(long republic, long empire)
    {
        if (republic < 0 || empire < 0)
            throw new ArgumentOutOfRangeException(nameof(republic));
        return (double) (((decimal) republic + 1m) / ((decimal) republic + empire + 2m));
    }

    /// <summary>
    /// Registers an actual capture operation owned by F. Repeated activation/cancellation is harmless.
    /// F retains the token during contesting, console failure and the 30-second absence grace period.
    /// It cancels only when the capture operation itself expires, fails, completes or loses diplomatic permission.
    /// </summary>
    public bool SetNeutralCapture(EntityUid actor, ProtoId<SWGlobalWarPlanetPrototype> sector, Guid generation,
        Guid operation, bool active)
    {
        if (_executing || _steps.IsClosing || operation == Guid.Empty ||
            !TryGetPolitics(out var politics) || politics.Comp.Generation != generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            !politics.Comp.Neutrals.TryGetValue(sector, out var neutral))
            return false;
        Advance(politics);
        if (neutral.Phase != SWGlobalWarNeutralPhase.Annexing ||
            active && (neutral.RetiredCaptures.Contains(operation) || neutral.Remaining <= TimeSpan.Zero ||
                       !CanCapture(actor, neutral.Planet, SWGlobalWarPlanetLocation.Orbit, generation)))
            return false;
        if (!active)
            neutral.RetiredCaptures.Add(operation);
        var changed = active ? neutral.ActiveCaptures.Add(operation) : neutral.ActiveCaptures.Remove(operation);
        if (changed)
        {
            neutral.Revision++;
            politics.Comp.Revision++;
            Notify(politics);
        }
        return true;
    }

    private void AdvanceNeutrals(Entity<SWGlobalWarPoliticsComponent> politics,
        SWGlobalWarCampaignComponent campaign, TimeSpan now)
    {
        foreach (var (id, neutral) in politics.Comp.Neutrals)
        {
            if (neutral.PendingOwnership)
                continue;
            if (neutral.Phase == SWGlobalWarNeutralPhase.CivilWar && now >= campaign.NeutralFundingDuration)
            {
                // Save the draw before ownership callbacks; retries never reroll this sector.
                neutral.DrawnWinner ??= _random.NextDouble() <
                    RepublicProbability(neutral.RepublicContribution, neutral.EmpireContribution)
                    ? SWGlobalWarFaction.Republic : SWGlobalWarFaction.Empire;
                if (neutral.DrawOperation == Guid.Empty)
                    neutral.DrawOperation = Guid.NewGuid();
                QueueNeutralOwner(politics, neutral, SWGlobalWarPlanetLocation.Orbit,
                    neutral.DrawnWinner.Value, neutral.DrawOperation, () =>
                {
                    neutral.Claimant = neutral.DrawnWinner.Value;
                    neutral.Phase = SWGlobalWarNeutralPhase.Annexing;
                    neutral.Remaining = campaign.AnnexationDuration;
                    neutral.UpdatedAt = campaign.NeutralFundingDuration;
                    _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-neutral-claim",
                        ("sector", _prototypes.Index(id).Name), ("faction", FactionName(neutral.Claimant))));
                });
                continue;
            }
            if (neutral.Phase != SWGlobalWarNeutralPhase.Annexing)
                continue;
            var elapsed = now - neutral.UpdatedAt;
            neutral.UpdatedAt = now;
            if (neutral.ActiveCaptures.Count == 0 && elapsed > TimeSpan.Zero)
                neutral.Remaining = TimeSpan.FromTicks(Math.Max(0, (neutral.Remaining - elapsed).Ticks));
            if (neutral.Remaining > TimeSpan.Zero || neutral.ActiveCaptures.Count > 0 ||
                neutral.Claimant == SWGlobalWarFaction.Neutral)
                continue;
            QueueNeutralOwner(politics, neutral, SWGlobalWarPlanetLocation.Surface,
                neutral.Claimant, Guid.NewGuid(), () =>
            {
                neutral.Phase = SWGlobalWarNeutralPhase.Integrated;
                neutral.BorderConflict = null;
                BeginWithdrawals(politics, neutral.Planet);
                _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-neutral-integrated",
                    ("sector", _prototypes.Index(id).Name), ("faction", FactionName(neutral.Claimant))));
            });
        }
    }

    private void QueueNeutralOwner(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarNeutralState neutral,
        SWGlobalWarPlanetLocation location, SWGlobalWarFaction faction, Guid operation, Action completed)
    {
        if (!TryComp<SWGlobalWarPlanetComponent>(neutral.Planet, out var planet))
            return;
        neutral.PendingOwnership = true;
        if (!_steps.TryQueueOwnership(neutral.Planet, location, faction, planet.OwnershipRevision,
                politics.Comp.Generation, operation, success =>
                {
                    neutral.PendingOwnership = false;
                    if (!success)
                        return;
                    completed();
                    neutral.Revision++;
                    politics.Comp.Revision++;
                }))
            neutral.PendingOwnership = false;
    }

    internal void CommitOwnershipState(SWGlobalWarOwnershipChangedEvent[] changes)
    {
        foreach (var change in changes)
        {
            var ev = change;
            OnOwnershipChanged(ref ev);
        }
    }

    private void OnOwnershipChanged(ref SWGlobalWarOwnershipChangedEvent args)
    {
        if (!TryGetPolitics(out var politics) ||
            !TryComp<SWGlobalWarPlanetComponent>(args.Planet, out var planet) ||
            !politics.Comp.Neutrals.TryGetValue(planet.Prototype, out var neutral) ||
            neutral.DrawOperation == args.OperationId || neutral.Phase != SWGlobalWarNeutralPhase.Annexing ||
            args.Location != SWGlobalWarPlanetLocation.Orbit ||
            !_campaign.TryGetCampaign(out var campaign))
            return;
        neutral.Claimant = args.Owner;
        neutral.Remaining = campaign.Comp.AnnexationDuration;
        neutral.UpdatedAt = Now();
        RetireCaptures(neutral);
        neutral.Revision++;
        politics.Comp.Revision++;
    }

    private static void RetireCaptures(SWGlobalWarNeutralState neutral)
    {
        neutral.RetiredCaptures.UnionWith(neutral.ActiveCaptures);
        neutral.ActiveCaptures.Clear();
    }
}
