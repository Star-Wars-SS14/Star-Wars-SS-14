using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    /// <summary>Builds the recipient's payload before serialization, including a completely empty revoked view.</summary>
    public SWGlobalWarDiplomacyView GetDiplomacyView(ICommonSession? session, SWGlobalWarFaction consoleFaction)
    {
        if (!TryGetPolitics(out var politics) || !_campaign.TryGetCampaign(out var campaign))
            return EmptyDiplomacy(SWGlobalWarViewAvailability.Unavailable);
        var state = politics.Comp;
        if (session == null || !_appointments.TryGetAssignment(session.UserId, out var assignment) ||
            !_appointments.TryGetActor(session, state.Generation, assignment.Revision, out assignment) ||
            assignment.Faction != consoleFaction)
            return EmptyDiplomacy(SWGlobalWarViewAvailability.Forbidden, state.Generation, state.Revision);

        var now = Now();
        var head = assignment.Role == SWGlobalWarRole.Head;
        var running = campaign.Comp.Phase == SWGlobalWarCampaignPhase.Running;
        var actions = new List<SWGlobalWarPoliticalAction>();
        if (head && running)
        {
            actions.AddRange([SWGlobalWarPoliticalAction.SetSanction, SWGlobalWarPoliticalAction.PublicAddress,
                SWGlobalWarPoliticalAction.StateMessage]);
            if (state.Relations == SWGlobalWarRelations.Peace && now >= campaign.Comp.PeaceDuration)
                actions.Add(SWGlobalWarPoliticalAction.DeclareWar);
            if (state.Relations == SWGlobalWarRelations.Truce)
                actions.Add(SWGlobalWarPoliticalAction.BreakTruce);
            if (state.Relations == SWGlobalWarRelations.War)
                actions.Add(state.TruceConsents.ContainsKey(assignment.Faction)
                    ? SWGlobalWarPoliticalAction.WithdrawTruce : SWGlobalWarPoliticalAction.OfferTruce);
            if (now < campaign.Comp.MaximumDuration)
                actions.Add(SWGlobalWarPoliticalAction.PrepareSurrender);
        }
        SWGlobalWarSurrenderConfirmation? confirmation = null;
        if (head && state.SurrenderConfirmations.TryGetValue(assignment.Faction, out var pending) &&
            pending.Assignment == assignment.Id && now < pending.Expires && now < campaign.Comp.MaximumDuration &&
            !state.Surrenders.ContainsKey(assignment.Faction) &&
            (running || state.SurrenderTick == _timing.CurTick))
        {
            confirmation = pending;
            actions.Add(SWGlobalWarPoliticalAction.ConfirmSurrender);
        }
        var neutrals = state.Neutrals.Select(pair =>
        {
            var neutral = pair.Value;
            var contribution = assignment.Faction == SWGlobalWarFaction.Republic
                ? neutral.RepublicContribution : neutral.EmpireContribution;
            return new SWGlobalWarNeutralView(pair.Key, neutral.Phase, neutral.Claimant, neutral.Remaining,
                neutral.ActiveCaptures.Count != 0, head ? contribution : null,
                head ? campaign.Comp.NeutralFundingLimit : null, campaign.Comp.NeutralFundingDuration,
                head && running && neutral.Phase == SWGlobalWarNeutralPhase.CivilWar &&
                now < campaign.Comp.NeutralFundingDuration && contribution < campaign.Comp.NeutralFundingLimit,
                head && running && state.Relations == SWGlobalWarRelations.Peace &&
                neutral.Phase == SWGlobalWarNeutralPhase.Annexing && neutral.Claimant != assignment.Faction &&
                neutral.Claimant != SWGlobalWarFaction.Neutral);
        }).ToList();
        return new SWGlobalWarDiplomacyView(state.Generation, state.Revision, state.RelationsRevision, now,
            SWGlobalWarViewAvailability.Ready, assignment.Id, assignment.Revision, assignment.Faction,
            state.Relations, campaign.Comp.PeaceDuration, state.TruceConsents.ContainsKey(assignment.Faction),
            state.TruceConsents.ContainsKey(Opponent(assignment.Faction)), actions, neutrals,
            state.Sanctions.Select(s => new SWGlobalWarSanctionView(s.Item1, s.Item2)).ToList(),
            head, head && state.StateMessages.Count != 0 ? state.StateMessages[^1].Id : Guid.Empty,
            head ? state.Requests.Where(p => p.Key.Item1 == session.UserId &&
                    p.Value.Command.AssignmentRevision == assignment.Revision).TakeLast(32)
                .Select(p => new SWGlobalWarPoliticalResultView(p.Key.Item2, p.Value.Success, p.Value.Reason, p.Value.Confirmation, p.Value.RetryAfter)).ToList() : [],
            head ? _ledger.GetBalance(assignment.Faction, SWGlobalWarAccount.Treasury).Available : null,
            confirmation?.Id ?? Guid.Empty, confirmation?.Expires);
    }

    private static SWGlobalWarDiplomacyView EmptyDiplomacy(SWGlobalWarViewAvailability availability,
        Guid generation = default, long revision = 0)
    {
        return new SWGlobalWarDiplomacyView(generation, revision, 0, TimeSpan.Zero, availability,
            Guid.Empty, 0, SWGlobalWarFaction.Neutral, SWGlobalWarRelations.Peace, TimeSpan.Zero,
            false, false, [], [], [], false, Guid.Empty, [], null, Guid.Empty, null);
    }

    /// <summary>Reads at most twenty messages without copying or truncating the full government archive.</summary>
    public SWGlobalWarHistoryPage GetHistoryPage(ICommonSession? session, SWGlobalWarFaction consoleFaction,
        Guid generation, long assignmentRevision, Guid before)
    {
        var denied = new SWGlobalWarHistoryPage(generation, assignmentRevision, before, false, false, []);
        if (session == null || !TryGetPolitics(out var politics) || politics.Comp.Generation != generation ||
            !IsHead(session, generation, assignmentRevision, out var head) || head.Faction != consoleFaction)
            return denied;
        var history = politics.Comp.StateMessages;
        var end = before == Guid.Empty ? history.Count : history.FindIndex(m => m.Id == before);
        if (end < 0)
            return denied;
        var start = Math.Max(0, end - 20);
        var messages = new List<SWGlobalWarGovernmentMessageView>();
        for (var i = start; i < end; i++)
        {
            var message = history[i];
            messages.Add(new SWGlobalWarGovernmentMessageView(message.Id, message.AuthorName,
                message.Sender, message.SentAt, message.Text));
        }
        return new SWGlobalWarHistoryPage(generation, assignmentRevision, before, true, start > 0, messages);
    }
}
