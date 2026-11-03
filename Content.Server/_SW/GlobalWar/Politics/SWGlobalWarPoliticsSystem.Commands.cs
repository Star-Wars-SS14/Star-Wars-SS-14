using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Utility;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarPoliticsSystem
{
    private bool Apply(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarAssignment head,
        SWGlobalWarPoliticalCommand command, out string reason, out Guid confirmation)
    {
        reason = "sw-global-war-politics-denied";
        confirmation = Guid.Empty;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
        {
            reason = "sw-global-war-politics-denied-ending";
            return false;
        }
        var state = politics.Comp;
        var now = Now();
        if (campaign.Comp.Phase == SWGlobalWarCampaignPhase.Capitulating &&
            !(command.Action == SWGlobalWarPoliticalAction.ConfirmSurrender && state.SurrenderTick == _timing.CurTick))
        {
            reason = "sw-global-war-politics-denied-capitulating";
            return false;
        }
        // A stale relations revision means another decision landed first; the menu shows the new state.
        const string changed = "sw-global-war-politics-denied-changed";
        var success = false;
        switch (command.Action)
        {
            case SWGlobalWarPoliticalAction.DeclareWar:
                if (state.Relations != SWGlobalWarRelations.Peace || command.RelationsRevision != state.RelationsRevision ||
                    now < campaign.Comp.PeaceDuration)
                {
                    reason = "sw-global-war-politics-war-unavailable";
                    return false;
                }
                SetRelations(politics, SWGlobalWarRelations.War);
                success = true;
                break;
            case SWGlobalWarPoliticalAction.OfferTruce:
                if (command.RelationsRevision != state.RelationsRevision || state.Relations != SWGlobalWarRelations.War)
                {
                    reason = command.RelationsRevision != state.RelationsRevision ? changed : "sw-global-war-politics-denied-not-war";
                    return false;
                }
                state.TruceConsents[head.Faction] = head.Id;
                state.RelationsRevision++;
                if (state.TruceConsents.TryGetValue(Opponent(head.Faction), out var other) &&
                    CurrentHead(Opponent(head.Faction))?.Id == other)
                    SetRelations(politics, SWGlobalWarRelations.Truce);
                success = true;
                break;
            case SWGlobalWarPoliticalAction.WithdrawTruce:
                if (command.RelationsRevision != state.RelationsRevision)
                {
                    reason = changed;
                    return false;
                }
                if (!state.TruceConsents.Remove(head.Faction))
                {
                    reason = "sw-global-war-politics-denied-no-truce-offer";
                    return false;
                }
                state.RelationsRevision++;
                success = true;
                break;
            case SWGlobalWarPoliticalAction.BreakTruce:
                if (command.RelationsRevision != state.RelationsRevision || state.Relations != SWGlobalWarRelations.Truce)
                {
                    reason = command.RelationsRevision != state.RelationsRevision ? changed : "sw-global-war-politics-denied-not-truce";
                    return false;
                }
                SetRelations(politics, SWGlobalWarRelations.War);
                success = true;
                break;
            case SWGlobalWarPoliticalAction.SetSanction:
                if (command.Commodity is not { } commodity || !_prototypes.HasIndex(commodity))
                    return false;
                if (command.Enabled)
                    state.Sanctions.Add((head.Faction, commodity));
                else
                    state.Sanctions.Remove((head.Faction, commodity));
                _chat.DispatchServerAnnouncement(Loc.GetString(command.Enabled
                    ? "sw-global-war-sanction-imposed" : "sw-global-war-sanction-lifted",
                    ("faction", FactionName(head.Faction)), ("commodity", Loc.GetString(_prototypes.Index(commodity).Name))));
                success = true;
                break;
            case SWGlobalWarPoliticalAction.OpenBorderConflict:
                if (command.Sector is not { } sector || !state.Neutrals.TryGetValue(sector, out var neutral))
                    return false;
                if (state.Relations != SWGlobalWarRelations.Peace)
                {
                    reason = "sw-global-war-politics-denied-border-not-peace";
                    return false;
                }
                if (neutral.Phase != SWGlobalWarNeutralPhase.Annexing || neutral.Claimant == SWGlobalWarFaction.Neutral)
                {
                    reason = "sw-global-war-politics-denied-border-no-claim";
                    return false;
                }
                if (neutral.Claimant == head.Faction)
                {
                    reason = "sw-global-war-politics-denied-border-own-claim";
                    return false;
                }
                neutral.BorderConflict = head.Faction;
                neutral.Revision++;
                _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-border-conflict",
                    ("faction", FactionName(head.Faction)), ("sector", _prototypes.Index(sector).Name)));
                success = true;
                break;
            case SWGlobalWarPoliticalAction.Sponsor:
                if (command.Sector is not { } funded || !state.Neutrals.TryGetValue(funded, out var funding))
                    return false;
                if (funding.Phase != SWGlobalWarNeutralPhase.CivilWar)
                {
                    reason = "sw-global-war-politics-denied-sponsor-no-civil-war";
                    return false;
                }
                if (now >= campaign.Comp.NeutralFundingDuration)
                {
                    reason = "sw-global-war-politics-denied-sponsor-closed";
                    return false;
                }
                if (command.Amount <= 0)
                {
                    reason = "sw-global-war-politics-denied-amount";
                    return false;
                }
                var contribution = head.Faction == SWGlobalWarFaction.Republic
                    ? funding.RepublicContribution : funding.EmpireContribution;
                if (contribution > campaign.Comp.NeutralFundingLimit ||
                    command.Amount > campaign.Comp.NeutralFundingLimit - contribution)
                {
                    reason = "sw-global-war-politics-denied-sponsor-limit";
                    return false;
                }
                if (!_ledger.TryGetLedger(out var ledger))
                    return false;
                var debit = new SWGlobalWarLedgerCommand(state.Generation, Guid.NewGuid(), ledger.Comp.Revision,
                    SWGlobalWarLedgerAction.Debit, head.Faction, SWGlobalWarAccount.Treasury, command.Amount,
                    Purpose: "neutral-sponsorship:" + funded.Id);
                if (!_ledger.TryExecuteSystem(debit, out _, head.User))
                {
                    reason = "sw-global-war-politics-insufficient-funds";
                    return false;
                }
                if (head.Faction == SWGlobalWarFaction.Republic)
                    funding.RepublicContribution += command.Amount;
                else
                    funding.EmpireContribution += command.Amount;
                funding.Revision++;
                success = true;
                break;
            case SWGlobalWarPoliticalAction.PublicAddress:
            case SWGlobalWarPoliticalAction.StateMessage:
                var text = command.Text.Trim();
                if (text.Length is < 1 or > 1000 || text.Any(c => char.IsControl(c) && c != '\n'))
                {
                    reason = text.Length == 0 ? "sw-global-war-politics-message-empty" :
                        text.Length > 1000 ? "sw-global-war-politics-message-too-long" : "sw-global-war-politics-message-invalid";
                    return false;
                }
                if (state.LastMessage.TryGetValue(head.User, out var last) && now - last < TimeSpan.FromSeconds(1))
                {
                    reason = "sw-global-war-politics-message-rate-limit";
                    return false;
                }
                state.LastMessage[head.User] = now;
                if (command.Action == SWGlobalWarPoliticalAction.PublicAddress)
                    _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-official-address",
                        ("author", FormattedMessage.EscapeText(Name(head.Body))),
                        ("faction", FactionName(head.Faction)), ("message", FormattedMessage.EscapeText(text))));
                else
                    state.StateMessages.Add(new SWGlobalWarStateMessage(Guid.NewGuid(), head.User, Name(head.Body),
                        head.Faction, now, text));
                success = true;
                break;
            case SWGlobalWarPoliticalAction.PrepareSurrender:
                if (now >= campaign.Comp.MaximumDuration)
                {
                    reason = "sw-global-war-politics-denied-surrender-closed";
                    return false;
                }
                confirmation = Guid.NewGuid();
                state.SurrenderConfirmations[head.Faction] = new SWGlobalWarSurrenderConfirmation(confirmation,
                    head.Id, now + TimeSpan.FromSeconds(30));
                success = true;
                break;
            case SWGlobalWarPoliticalAction.ConfirmSurrender:
                reason = "sw-global-war-politics-confirmation-expired";
                if (now >= campaign.Comp.MaximumDuration ||
                    !state.SurrenderConfirmations.TryGetValue(head.Faction, out var prepared) ||
                    prepared.Id != command.Confirmation || prepared.Assignment != head.Id || now >= prepared.Expires ||
                    state.Surrenders.ContainsKey(head.Faction))
                    return false;
                state.SurrenderConfirmations.Remove(head.Faction);
                state.Surrenders.Add(head.Faction, now + campaign.Comp.CapitulationDuration);
                state.SurrenderTick ??= _timing.CurTick;
                _campaign.TryAdvancePhase(state.Generation, SWGlobalWarCampaignPhase.Capitulating);
                _chat.DispatchServerAnnouncement(Loc.GetString("sw-global-war-surrender-started",
                    ("faction", FactionName(head.Faction))));
                success = true;
                break;
        }
        if (success)
        {
            state.Revision++;
            reason = string.Empty;
        }
        return success;
    }

    private void SetRelations(Entity<SWGlobalWarPoliticsComponent> politics, SWGlobalWarRelations relations)
    {
        politics.Comp.Relations = relations;
        politics.Comp.RelationsRevision++;
        politics.Comp.TruceConsents.Clear();
        if (relations == SWGlobalWarRelations.War)
            CancelWithdrawals(politics);
        if (relations == SWGlobalWarRelations.Truce)
        {
            foreach (var neutral in politics.Comp.Neutrals.Values)
            {
                neutral.BorderConflict = null;
                RetireCaptures(neutral);
                neutral.Revision++;
            }
            BeginWithdrawals(politics);
        }
        _chat.DispatchServerAnnouncement(Loc.GetString(relations switch
        {
            SWGlobalWarRelations.War => "sw-global-war-war-declared",
            SWGlobalWarRelations.Truce => "sw-global-war-truce-started",
            _ => "sw-global-war-peace-started",
        }));
    }

    private string FactionName(SWGlobalWarFaction faction)
    {
        return Loc.GetString(faction == SWGlobalWarFaction.Republic ? "sw-global-war-republic" : "sw-global-war-empire");
    }
}
