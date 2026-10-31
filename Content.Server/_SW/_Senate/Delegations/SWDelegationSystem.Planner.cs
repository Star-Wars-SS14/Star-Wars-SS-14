using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Shared._SW._Senate.Delegations;
using Content.Shared._SW._Senate.Planner;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Delegations;

public sealed partial class SWDelegationSystem
{
    [Dependency] private readonly SWSenatePlannerSystem _planner = default!;

    private static string VisitTarget(Visit visit) => $"delegation:{visit.Id}:{visit.PlannerOfficeRevision}";

    private bool PlannerViewer(EntityUid actor, Visit visit, out bool owner)
    {
        owner = false;
        if (visit.Declined || visit.PlannerOfficeRevision == long.MaxValue || !IsLiveOwner(visit) || !TryComp<ActorComponent>(actor, out var session)
            || session.PlayerSession.Status != SessionStatus.InGame || session.PlayerSession.AttachedEntity != actor)
            return false;
        owner = visit.Body == actor && visit.Owner == session.PlayerSession.UserId;
        return owner || _ministry.IsActiveHolder(Ministry(visit), actor);
    }

    private void OnGuestAuthorize(Entity<ActorComponent> ent, ref SWGuestRequestAuthorizeEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Delegation || _visit is not { } visit || ev.TargetKey != VisitTarget(visit)
            || !PlannerViewer(ent.Owner, visit, out var owner) || !owner)
            return;
        ev.Authorized = true;
        var target = _ministry.GetActiveHolderEntity(Ministry(visit));
        if (target is not { } body || !TryComp<ActorComponent>(body, out var actor)
            || actor.PlayerSession.UserId == visit.Owner)
            return;
        ev.TargetUserId = actor.PlayerSession.UserId;
        if (Identity.Name(body, EntityManager, ent.Owner) == Name(body)
            && Identity.Name(ent.Owner, EntityManager, body) == Name(ent.Owner))
            ev.TargetDisplayName = Name(body);
    }

    private void OnGuestRespond(Entity<ActorComponent> ent, ref SWGuestRequestRespondAuthorizeEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Delegation || _visit is not { } visit || ev.TargetKey != VisitTarget(visit)
            || !PlannerViewer(ent.Owner, visit, out var owner) || owner
            || ent.Comp.PlayerSession.UserId != ev.ExpectedTargetUserId)
            return;
        ev.Authorized = true;
    }

    private void OnGuestValidate(ref SWGuestRequestValidateEvent ev)
    {
        if (ev.Kind != SWGuestRequestKind.Delegation)
            return;
        if (_visit is not { } visit || visit.Declined || visit.PlannerOfficeRevision == long.MaxValue
            || ev.TargetKey != VisitTarget(visit)
            || visit.Owner != ev.OwnerUserId || !_ministry.IsClaimed(Ministry(visit)))
        {
            ev.Validity = SWGuestRequestValidity.Invalid;
            return;
        }
        // A disconnected owner or holder is not proof of replacement. The planner keeps Unknown requests.
        if (!IsLiveOwner(visit) || _ministry.GetActiveHolderEntity(Ministry(visit)) is not { } holder
            || !TryComp<ActorComponent>(holder, out var actor))
            return;
        ev.Validity = actor.PlayerSession.UserId == ev.ExpectedTargetUserId
            ? SWGuestRequestValidity.Valid : SWGuestRequestValidity.Invalid;
    }

    private void OnPlannerVerbs(Entity<ActorComponent> ent, ref GetVerbsEvent<AlternativeVerb> ev)
    {
        if (!ev.CanInteract || !ev.CanAccess || ev.User != ent.Owner || _visit is not { } visit
            || !PlannerViewer(ent.Owner, visit, out var owner) || owner)
            return;
        var visitId = visit.Id;
        ev.Verbs.Add(new AlternativeVerb
        {
            Text = "Встреча с представителем",
            Act = () =>
            {
                if (_visit is { } current && current.Id == visitId && PlannerViewer(ent.Owner, current, out var isOwner)
                    && !isOwner && TryComp<ActorComponent>(ent.Owner, out var actor))
                    _eui.OpenEui(new SWDelegationEui(visitId), actor.PlayerSession);
            },
        });
    }

    public void BuildPlannerState(ICommonSession player, int visitId, SWDelegationEuiState state)
    {
        if (player.AttachedEntity is not { } actor || _visit is not { } visit || visit.Id != visitId
            || !IsSessionActor(player, actor) || !PlannerViewer(actor, visit, out var owner))
            return;
        state.PlannerAvailable = true;
        state.PlannerCanCreate = owner;
        state.PlannerToday = _time.Day;
        state.PlannerEarliestSlot = _planner.EarliestStartSlot();
        state.Version = visit.Proposal.Version;
        if (!owner && !TryViewer(actor, visit, out _))
            state.Text = "Запросы встречи с представителем.";
        foreach (var item in _planner.GetGuestRequests(actor))
        {
            if (item.Kind != SWGuestRequestKind.Delegation)
                continue;
            state.Meetings.Add(new SWDelegationMeetingView
            {
                Id = item.Id, Day = item.Day, Slot = item.StartSlot, Duration = item.Duration,
                Subject = item.Subject, Location = item.Location, Status = (int) item.Status, Revision = item.Revision,
                IsOwner = item.IsOwner, IsTarget = item.IsTarget, Stale = item.Stale,
            });
        }
    }

    public string HandlePlannerAction(ICommonSession player, int visitId, SWDelegationPlannerMessage message)
    {
        if (player.AttachedEntity is not { } actor || _visit is not { } visit || visit.Id != visitId
            || !IsSessionActor(player, actor) || !PlannerViewer(actor, visit, out var owner)
            || message.Version != visit.Proposal.Version)
            return "Запрос недоступен или условия изменились.";
        string error;
        bool success;
        if (message.Action == SWDelegationPlannerAction.Create)
        {
            if (!owner) return "Только представитель может предложить встречу.";
            success = _planner.TryCreateGuestRequest(actor, SWGuestRequestKind.Delegation, VisitTarget(visit),
                message.Day, message.Slot, message.Duration, message.Subject, message.Description,
                message.Location, out _, out error);
        }
        else
        {
            var current = _planner.GetGuestRequests(actor).FirstOrDefault(item =>
                item.Kind == SWGuestRequestKind.Delegation && item.Id == message.Id);
            if (current == null || current.Day != message.ExpectedDay || current.StartSlot != message.ExpectedSlot
                || current.Duration != message.ExpectedDuration || (int) current.Status != message.ExpectedStatus
                || current.Revision != message.ExpectedRevision)
                return "Запрос встречи изменился. Обновите выбор.";
            success = message.Action switch
            {
                SWDelegationPlannerAction.Respond when !owner && current.IsTarget =>
                    _planner.TryRespondGuestRequest(actor, message.Id, message.ExpectedRevision, message.Accept, out error),
                SWDelegationPlannerAction.Cancel when owner && current.IsOwner =>
                    _planner.TryCancelGuestRequest(actor, message.Id, message.ExpectedRevision, out error),
                SWDelegationPlannerAction.Reschedule when owner && current.IsOwner =>
                    _planner.TryRescheduleGuestRequest(actor, message.Id, message.ExpectedRevision, message.Day, message.Slot, message.Duration, out error),
                _ => DenyPlanner(out error),
            };
        }
        if (success) Changed();
        return success ? "Запрос встречи обновлён." : string.IsNullOrWhiteSpace(error) ? "Запрос отклонён." : error;
    }

    private static bool DenyPlanner(out string error)
    {
        error = "Недостаточно полномочий.";
        return false;
    }
}
