using System.Linq;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Enums;

namespace Content.Server._SW.GlobalWar.Politics;

public sealed partial class SWGlobalWarAssignmentsSystem
{
    private bool _personnelTransaction;
    private Entity<SWGlobalWarAssignmentsComponent>? _pendingNotification;

    /// <summary>Executes an authenticated, revision-checked personnel intent; repeats return the original result.</summary>
    public bool TryExecute(ICommonSession session, SWGlobalWarAppointmentCommand command, out Guid offer)
    {
        return TryExecute(session, command, out offer, out _);
    }

    /// <summary>Returns the original, scoped outcome; changed payloads never inherit an old success.</summary>
    public bool TryExecute(ICommonSession session, SWGlobalWarAppointmentCommand command, out Guid offer,
        out SWGlobalWarAppointmentFailure failure)
    {
        offer = Guid.Empty;
        failure = SWGlobalWarAppointmentFailure.Denied;
        if (!TryGetRegistry(out var registry) || registry.Comp.Generation != command.Generation ||
            command.Request == Guid.Empty || !LivingActor(session, out var mind, out _))
            return false;
        var key = (session.UserId, command.Request);
        if (registry.Comp.Requests.TryGetValue(key, out var receipt))
        {
            if (receipt.Mind != mind || receipt.Body != session.AttachedEntity)
                return false;
            if (receipt.Command != command)
            {
                failure = SWGlobalWarAppointmentFailure.InvalidRequest;
                return false;
            }
            offer = receipt.Offer;
            failure = receipt.Failure;
            return receipt.Success;
        }
        if (_personnelTransaction)
            return false;
        _personnelTransaction = true;
        _pendingNotification = null;
        bool success;
        try
        {
            success = Execute(registry, session, command, out offer, out var executed);
            failure = success ? SWGlobalWarAppointmentFailure.None :
                command.Action == SWGlobalWarAppointmentAction.ClaimHead &&
                LivingActor(session, out _, out var faction) && Available(faction, SWGlobalWarRole.Head) == 0
                    ? SWGlobalWarAppointmentFailure.VacancyClosed : executed;
            if (!success && command.Action == SWGlobalWarAppointmentAction.Accept)
                failure = registry.Comp.Offers.TryGetValue(command.Offer, out var failedOffer) &&
                    failedOffer.Recipient == session.UserId && failedOffer.Body == session.AttachedEntity && failedOffer.Mind == mind &&
                    failedOffer.RecipientRevision == command.ActorRevision
                    ? OfferFailure(failedOffer, session) : SWGlobalWarAppointmentFailure.OfferUnavailable;
            if (!success && failure == SWGlobalWarAppointmentFailure.None)
                failure = SWGlobalWarAppointmentFailure.Changed;
            registry.Comp.Requests.Add(key, new SWGlobalWarAppointmentReceipt(command, success, offer, failure,
                mind, session.AttachedEntity));
        }
        finally
        {
            _personnelTransaction = false;
        }
        // Consumers see the completed appointment and receipt, including all succession phase changes.
        var pending = _pendingNotification;
        _pendingNotification = null;
        if (pending is { } changed)
            NotifyAssignments(changed);
        return success;
    }

    /// <summary>Applies one personnel intent. On refusal, failure names the first condition that failed.</summary>
    private bool Execute(Entity<SWGlobalWarAssignmentsComponent> registry, ICommonSession session,
        SWGlobalWarAppointmentCommand command, out Guid offerId, out SWGlobalWarAppointmentFailure failure)
    {
        offerId = Guid.Empty;
        failure = SWGlobalWarAppointmentFailure.Changed;
        if (!LivingActor(session, out var mind, out var faction))
            return false;
        // Admin debugging: an admin's aghost or foreign body appoints as the head of the side it acts for.
        if (!TryGetAdminActor(session.UserId, out var actor))
            registry.Comp.Assignments.TryGetValue(session.UserId, out actor);
        if ((actor?.Revision ?? 0) != command.ActorRevision ||
            actor != null && (actor.Mind != mind || actor.Body != session.AttachedEntity))
        {
            failure = SWGlobalWarAppointmentFailure.ActorChanged;
            return false;
        }
        switch (command.Action)
        {
            case SWGlobalWarAppointmentAction.Offer:
                if (actor == null)
                {
                    failure = SWGlobalWarAppointmentFailure.NoAppointment;
                    return false;
                }
                if (command.Job is not { } job ||
                    !TryGetRole(registry.Owner, job, out var targetFaction, out var role) || targetFaction != faction)
                    return false;
                if (!MayAppoint(actor, role))
                {
                    failure = SWGlobalWarAppointmentFailure.CannotAppoint;
                    return false;
                }
                if (!_players.TryGetSessionById(command.Target, out var target) ||
                    !LivingActor(target, out var targetMind, out var recipientFaction) || recipientFaction != faction)
                {
                    failure = SWGlobalWarAppointmentFailure.TargetUnavailable;
                    return false;
                }
                if (!CanAssumeCampaignJob(target, target.AttachedEntity!.Value, job))
                {
                    failure = SWGlobalWarAppointmentFailure.EligibilityChanged;
                    return false;
                }
                registry.Comp.Assignments.TryGetValue(command.Target, out var previous);
                failure = (previous?.Revision ?? 0) != command.TargetRevision ? SWGlobalWarAppointmentFailure.TargetChanged :
                    previous?.Role == role ? SWGlobalWarAppointmentFailure.SameRole :
                    previous?.Role == SWGlobalWarRole.Head ? SWGlobalWarAppointmentFailure.TargetIsHead :
                    !CanTake(faction, role) ? SWGlobalWarAppointmentFailure.RoleOccupied : SWGlobalWarAppointmentFailure.None;
                if (failure != SWGlobalWarAppointmentFailure.None)
                    return false;
                offerId = Guid.NewGuid();
                registry.Comp.Offers.Add(offerId, new SWGlobalWarAppointmentOffer(offerId, command.Generation,
                    actor.User, actor.Id, actor.Revision, target.UserId, targetMind, target.AttachedEntity!.Value,
                    previous?.Revision ?? 0, faction, role, job, Name(actor.Body), actor.Role, actor.Mind,
                    actor.Body, Name(target.AttachedEntity!.Value)));
                Changed(registry);
                return true;

            case SWGlobalWarAppointmentAction.Accept:
            case SWGlobalWarAppointmentAction.Decline:
                if (!registry.Comp.Offers.TryGetValue(command.Offer, out var pending) ||
                    pending.Recipient != session.UserId || pending.Mind != mind ||
                    pending.Body != session.AttachedEntity || pending.RecipientRevision != command.ActorRevision)
                {
                    failure = SWGlobalWarAppointmentFailure.OfferUnavailable;
                    return false;
                }
                if (command.Action == SWGlobalWarAppointmentAction.Decline)
                {
                    registry.Comp.Offers.Remove(pending.Id);
                    CompleteOffer(registry, pending, SWGlobalWarOfferStatus.Declined);
                    Changed(registry);
                    return true;
                }
                if (OfferFailure(pending, session) != SWGlobalWarAppointmentFailure.None)
                    return false;
                registry.Comp.Offers.Remove(pending.Id);
                CompleteOffer(registry, pending, SWGlobalWarOfferStatus.Accepted);
                Replace(registry, session, mind, faction, pending.Role, pending.Job);
                return true;

            case SWGlobalWarAppointmentAction.Resign:
                if (actor == null)
                {
                    failure = SWGlobalWarAppointmentFailure.NoAppointment;
                    return false;
                }
                Remove(registry, actor);
                return true;

            case SWGlobalWarAppointmentAction.Dismiss:
                if (actor == null)
                {
                    failure = SWGlobalWarAppointmentFailure.NoAppointment;
                    return false;
                }
                if (!registry.Comp.Assignments.TryGetValue(command.Target, out var dismissed) ||
                    dismissed.Revision != command.TargetRevision || dismissed.Faction != faction)
                {
                    failure = SWGlobalWarAppointmentFailure.TargetChanged;
                    return false;
                }
                if (!MayAppoint(actor, dismissed.Role))
                {
                    failure = SWGlobalWarAppointmentFailure.CannotAppoint;
                    return false;
                }
                Remove(registry, dismissed);
                return true;

            case SWGlobalWarAppointmentAction.ClaimHead:
            case SWGlobalWarAppointmentAction.DeclineSuccession:
                AdvanceSuccession(registry, faction);
                if (Available(faction, SWGlobalWarRole.Head) != 1 ||
                    !registry.Comp.Successions.TryGetValue(faction, out var succession) ||
                    succession.Id != command.Vacancy)
                {
                    failure = SWGlobalWarAppointmentFailure.VacancyClosed;
                    return false;
                }
                if (succession.Stage != command.Stage ||
                    succession.Stage < 2 && (!succession.Candidates.TryGetValue(session.UserId, out var candidate) ||
                        actor?.Id != candidate || SuccessionTier(actor) != succession.Stage))
                {
                    failure = SWGlobalWarAppointmentFailure.SuccessionStage;
                    return false;
                }
                if (command.Action == SWGlobalWarAppointmentAction.DeclineSuccession)
                {
                    if (succession.Stage == 2)
                    {
                        failure = SWGlobalWarAppointmentFailure.SuccessionStage;
                        return false;
                    }
                    succession.Candidates.Remove(session.UserId);
                    AdvanceSuccession(registry, faction);
                    Changed(registry);
                    return true;
                }
                if (!TryComp<SWGlobalWarSpawnConfigurationComponent>(registry.Owner, out var catalog))
                    return false;
                var headJob = catalog.Roles.FirstOrDefault(p => p.Value.Faction == faction &&
                    p.Value.Role == SWGlobalWarRole.Head);
                if (headJob.Value == null || !CanAssumeCampaignJob(session, session.AttachedEntity!.Value, headJob.Key))
                    return false;
                registry.Comp.Successions.Remove(faction);
                Replace(registry, session, mind, faction, SWGlobalWarRole.Head, headJob.Key);
                return true;
            default:
                return false;
        }
    }

    private void CompleteOffer(Entity<SWGlobalWarAssignmentsComponent> registry, SWGlobalWarAppointmentOffer offer,
        SWGlobalWarOfferStatus status)
    {
        registry.Comp.CompletedOffers[offer.Id] = new(offer, status);
        foreach (var old in registry.Comp.CompletedOffers.Values.Where(o => o.Offer.Sender == offer.Sender)
                     .SkipLast(32).ToArray())
            registry.Comp.CompletedOffers.Remove(old.Offer.Id);
    }

    private SWGlobalWarAppointmentFailure OfferFailure(SWGlobalWarAppointmentOffer pending, ICommonSession recipient,
        bool checkLocation = true)
    {
        if (!LivingActor(recipient, out var mind, out var faction) || pending.Recipient != recipient.UserId ||
            pending.Body != recipient.AttachedEntity || pending.Mind != mind || pending.Faction != faction ||
            (TryGetAssignment(recipient.UserId, out var current) ? current.Revision : 0) != pending.RecipientRevision)
            return SWGlobalWarAppointmentFailure.EligibilityChanged;
        if (!_players.TryGetSessionById(pending.Sender, out var sender) ||
            !TryGetActor(sender, pending.Generation, pending.SenderRevision, out var author) ||
            author.Id != pending.SenderAssignment || !MayAppoint(author, pending.Role))
            return SWGlobalWarAppointmentFailure.AuthorityChanged;
        if (!CanTake(faction, pending.Role))
            return SWGlobalWarAppointmentFailure.RoleOccupied;
        if (!CanAssumeCampaignJob(recipient, recipient.AttachedEntity!.Value, pending.Job))
            return SWGlobalWarAppointmentFailure.EligibilityChanged;
        if (checkLocation)
        {
            var check = new SWGlobalWarEvaluateAppointmentEvent(pending.Body, mind, faction, pending.Role,
                pending.Generation);
            RaiseLocalEvent(ref check);
            if (!check.Allowed)
                return SWGlobalWarAppointmentFailure.LocationForbidden;
        }
        return SWGlobalWarAppointmentFailure.None;
    }

    private bool LivingActor(ICommonSession session, out EntityUid mind, out SWGlobalWarFaction faction)
    {
        mind = default;
        faction = default;
        // Admin debugging: an admin's aghost or foreign body counts as a living actor of the side it acts for.
        if (session.Status == SessionStatus.InGame && TryGetAdminActor(session.UserId, out var admin))
        {
            mind = admin.Mind;
            faction = admin.Faction;
            return true;
        }
        return session.Status == SessionStatus.InGame && session.AttachedEntity is { } body &&
               _identity.TryGetActor(session, body, out mind, out faction) && _mobs.IsAlive(body);
    }

    private bool CanTake(SWGlobalWarFaction faction, SWGlobalWarRole role)
    {
        return role == SWGlobalWarRole.Soldier || Available(faction, role) != 0;
    }

    private bool MayAppoint(SWGlobalWarAssignment actor, SWGlobalWarRole role)
    {
        if (role is SWGlobalWarRole.Head or SWGlobalWarRole.ForceUser or SWGlobalWarRole.Civilian)
            return false;
        if (actor.Role == SWGlobalWarRole.Head)
            return true;
        return actor.Role switch
        {
            SWGlobalWarRole.WarMinister => role is SWGlobalWarRole.SeniorOfficer or SWGlobalWarRole.JuniorOfficer or
                SWGlobalWarRole.Pilot or SWGlobalWarRole.Soldier or SWGlobalWarRole.Intelligence,
            SWGlobalWarRole.SeniorOfficer => role is SWGlobalWarRole.JuniorOfficer or SWGlobalWarRole.Pilot or
                SWGlobalWarRole.Soldier or SWGlobalWarRole.Intelligence,
            SWGlobalWarRole.JuniorOfficer => role == SWGlobalWarRole.Soldier,
            SWGlobalWarRole.LogisticsMinister => role == SWGlobalWarRole.LogisticsWorker,
            SWGlobalWarRole.IndustryMinister => role == SWGlobalWarRole.IndustryWorker,
            _ => false,
        };
    }

    private static int SuccessionTier(SWGlobalWarAssignment? assignment)
    {
        return assignment?.Role switch
        {
            SWGlobalWarRole.WarMinister or SWGlobalWarRole.LogisticsMinister or SWGlobalWarRole.IndustryMinister => 0,
            SWGlobalWarRole.SeniorOfficer => 1,
            _ => 2,
        };
    }

    private void Replace(Entity<SWGlobalWarAssignmentsComponent> registry, ICommonSession session, EntityUid mind,
        SWGlobalWarFaction faction, SWGlobalWarRole role, ProtoId<JobPrototype> job)
    {
        if (registry.Comp.Assignments.TryGetValue(session.UserId, out var previous))
            registry.Comp.HeadDisconnected.Remove(previous.Id);
        // The replacement is recorded before publishing any event. No body, mind, ID or equipment is recreated.
        registry.Comp.Assignments[session.UserId] = new SWGlobalWarAssignment(Guid.NewGuid(), registry.Comp.Generation,
            session.UserId, mind, session.AttachedEntity!.Value, faction, role, job, ++registry.Comp.Revision, false);
        if (role == SWGlobalWarRole.Head)
            registry.Comp.Successions.Remove(faction);
        Changed(registry);
    }
}
