using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Planner;
using Content.Shared.Access.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWSenatePlannerSystem
{
    [Dependency] private readonly IPlayerManager _guestPlayers = default!;
    [Dependency] private readonly SharedMindSystem _guestMind = default!;
    private float _guestPruneElapsed;

    private SWGuestRequestValidity GuestValidity(SWGuestRequestKind kind, string targetKey, NetUserId owner, NetUserId target)
    {
        var ev = new SWGuestRequestValidateEvent(kind, targetKey, owner, target);
        RaiseLocalEvent(ref ev);
        return ev.Validity;
    }

    private bool GuestValid(Meeting meeting) => meeting.GuestOwner is { } owner && meeting.GuestTarget is { } target
        && GuestValidity(meeting.GuestKind, meeting.GuestTargetKey, owner, target) == SWGuestRequestValidity.Valid;

    // Pending and accepted requests use the same lifecycle. Unknown preserves data and blocks access.
    private void PruneGuestRequests()
    {
        foreach (var meeting in _meetings.ToArray())
        {
            if (meeting.GuestOwner is not { } owner || meeting.GuestTarget is not { } target)
                continue;
            if (GuestValidity(meeting.GuestKind, meeting.GuestTargetKey, owner, target) == SWGuestRequestValidity.Invalid)
                _meetings.Remove(meeting);
        }
    }

    private bool TryGuestActor(EntityUid actor, out NetUserId user)
    {
        user = default;
        if (!_time.ModeActive || !TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame || player.PlayerSession.AttachedEntity != actor
            || !TryComp<MobStateComponent>(actor, out var mob) || mob.CurrentState != MobState.Alive
            || !_guestMind.TryGetMind(actor, out _, out _))
            return false;
        user = player.PlayerSession.UserId;
        return true;
    }

    private bool UniqueActiveName(EntityUid actor, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || Name(actor) != name)
            return false;
        return _guestPlayers.Sessions.Count(session => session.Status == SessionStatus.InGame
            && session.AttachedEntity is { } body && EntityManager.EntityExists(body) && Name(body) == name) == 1;
    }

    // Ambiguous names conservatively show busy for every matching active body, never details.
    private bool GuestAttends(Meeting meeting, string person)
    {
        // Stored display names are a conservative busy hint only, never authority.
        if (meeting.Host == person || meeting.Invites.TryGetValue(person, out var oldStatus) && oldStatus == SWInviteStatus.Accepted)
            return true;
        foreach (var session in _guestPlayers.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } actor
                || !EntityManager.EntityExists(actor) || !TryGuestActor(actor, out var user)
                || (Name(actor) != person && !IsAssignedMinistryCardAlias(actor, person)))
                continue;
            if (meeting.GuestOwner == user && meeting.GuestOwnerBody == actor
                || meeting.GuestTarget == user && meeting.GuestTargetBody == actor
                && meeting.Invites.Values.FirstOrDefault() == SWInviteStatus.Accepted)
                return true;
        }
        return false;
    }

    // A ministry's held card and living holder are an explicit binding, unlike two equal names.
    // This only supplies conservative busy information; it never grants access to a meeting.
    private bool IsAssignedMinistryCardAlias(EntityUid actor, string alias)
    {
        foreach (var ministry in _ministry.Holders.Keys)
        {
            if (_ministry.GetActiveHolderEntity(ministry) != actor || _ministry.GetHolderCard(ministry) is not { } card
                || !TryComp<IdCardComponent>(card, out var id) || id.FullName != alias)
                continue;
            return true;
        }
        return false;
    }

    private bool LegacyCalendarNameMatches(string storedName, string viewerName)
    {
        if (storedName == viewerName)
            return true;
        foreach (var ministry in _ministry.Holders.Keys)
        {
            if (_ministry.GetActiveHolderEntity(ministry) is not { } actor || Name(actor) != viewerName
                || !UniqueActiveName(actor, viewerName))
                continue;
            if (IsAssignedMinistryCardAlias(actor, storedName))
                return true;
        }
        return false;
    }

    // Legacy meetings keep their historical mutual-overlap behaviour. This mask contains
    // only guest meetings; identity is supplemented by a verified live actor when available.
    private ulong GuestOnlyBusyMask(string person, int day, EntityUid? verifiedActor, int excludeMeetingId = -1)
    {
        NetUserId actorUser = default;
        var hasActor = verifiedActor is { } actor && TryGuestActor(actor, out actorUser);
        ulong mask = 0;
        foreach (var meeting in _meetings)
        {
            if (meeting.Day != day || meeting.Id == excludeMeetingId || meeting.GuestOwner == null)
                continue;
            var actorAttends = hasActor && (meeting.GuestOwner == actorUser && meeting.GuestOwnerBody == verifiedActor
                || meeting.GuestTarget == actorUser && meeting.GuestTargetBody == verifiedActor
                   && meeting.Invites.Values.FirstOrDefault() == SWInviteStatus.Accepted);
            if (actorAttends || GuestAttends(meeting, person))
                mask |= SWPlannerSlots.Mask(meeting.StartSlot, meeting.Duration);
        }
        return mask;
    }

    private bool GuestValidWindow(int day, int slot, int duration) =>
        slot >= 0 && duration is >= 1 and <= 4 && slot <= SWPlannerSlots.PerDay - duration
        && ValidWindow(day, slot, duration, 4);

    private bool TryGuestPolicy(EntityUid actor, SWGuestRequestKind kind, string targetKey,
        out NetUserId target, out string targetName)
    {
        target = default; targetName = string.Empty;
        var ev = new SWGuestRequestAuthorizeEvent(kind, targetKey);
        RaiseLocalEvent(actor, ref ev);
        if (!ev.Authorized || ev.TargetUserId is not { } targetId || string.IsNullOrWhiteSpace(ev.TargetDisplayName))
            return false;
        var targetSession = _guestPlayers.Sessions.FirstOrDefault(s => s.UserId == targetId && s.Status == SessionStatus.InGame);
        if (targetSession?.AttachedEntity is not { } targetActor || !TryGuestActor(targetActor, out var verified)
            || verified != targetId || !UniqueActiveName(targetActor, ev.TargetDisplayName))
            return false;
        target = targetId; targetName = ev.TargetDisplayName;
        return true;
    }

    private bool GuestTargetAllowed(EntityUid actor, Meeting meeting)
    {
        if (!GuestValid(meeting) || meeting.GuestTarget is not { } target || meeting.GuestTargetBody != actor
            || !TryGuestActor(actor, out var user) || user != target)
            return false;
        var ev = new SWGuestRequestRespondAuthorizeEvent(meeting.GuestKind, meeting.GuestTargetKey, target);
        RaiseLocalEvent(actor, ref ev);
        return ev.Authorized;
    }

    private bool GuestOwnerAllowed(EntityUid actor, Meeting meeting)
    {
        if (!GuestValid(meeting) || meeting.GuestOwnerBody != actor || !TryGuestActor(actor, out var user) || user != meeting.GuestOwner)
            return false;
        var ev = new SWGuestRequestAuthorizeEvent(meeting.GuestKind, meeting.GuestTargetKey);
        RaiseLocalEvent(actor, ref ev);
        return ev.Authorized && ev.TargetUserId == meeting.GuestTarget;
    }

    private bool GuestOwnerMayCancel(EntityUid actor, Meeting meeting)
    {
        if (!GuestValid(meeting) || meeting.GuestOwnerBody != actor || !TryGuestActor(actor, out var user) || user != meeting.GuestOwner)
            return false;
        var ev = new SWGuestRequestAuthorizeEvent(meeting.GuestKind, meeting.GuestTargetKey);
        RaiseLocalEvent(actor, ref ev);
        return ev.Authorized;
    }

    // This view must only be sent to this actor via a targeted message; never SetUiState on a shared console.
    public IReadOnlyList<SWGuestRequestView> GetGuestRequests(EntityUid actor)
    {
        if (!TryGuestActor(actor, out var user))
            return Array.Empty<SWGuestRequestView>();
        var result = new List<SWGuestRequestView>();
        foreach (var meeting in _meetings)
        {
            if (meeting.GuestOwner == null || meeting.GuestTarget == null)
                continue;
            var owner = meeting.GuestOwner == user && GuestOwnerAllowed(actor, meeting);
            var target = meeting.GuestTarget == user && GuestTargetAllowed(actor, meeting);
            if (!owner && !target)
            {
                if (meeting.GuestOwner == user && GuestOwnerMayCancel(actor, meeting))
                    result.Add(new SWGuestRequestView(meeting.Id, -1, 0, 0, string.Empty, string.Empty,
                        string.Empty, string.Empty, string.Empty, SWInviteStatus.Pending, true, false, true, meeting.GuestKind, meeting.GuestRevision));
                continue;
            }
            var invite = meeting.Invites.First();
            result.Add(new SWGuestRequestView(meeting.Id, meeting.Day, meeting.StartSlot, meeting.Duration,
                meeting.Title, meeting.Description, meeting.Location, meeting.Host, invite.Key,
                invite.Value, owner, target, false, meeting.GuestKind, meeting.GuestRevision));
        }
        return result;
    }

    public bool TryCreateGuestRequest(EntityUid actor, SWGuestRequestKind kind, string targetKey,
        int day, int startSlot, int duration, string subject, string description, string location,
        out int meetingId, out string error)
    {
        meetingId = 0; error = "Не удалось создать запрос: проверьте полномочия, участников и время.";
        if (!Enum.IsDefined(kind) || targetKey == null || targetKey.Length is < 1 or > 100
            || subject == null || subject.Length > 120 || string.IsNullOrWhiteSpace(subject)
            || description == null || description.Length > 1000 || location == null || location.Length > 120
            || !_time.DayActive || !GuestValidWindow(day, startSlot, duration)
            || day == _time.Day && startSlot < EarliestStartSlot()
            || !TryGuestActor(actor, out var user) || !UniqueActiveName(actor, Name(actor))
            || !TryGuestPolicy(actor, kind, targetKey, out var target, out var targetName) || target == user
            || GuestValidity(kind, targetKey, user, target) != SWGuestRequestValidity.Valid
            || _meetings.Count(m => m.GuestOwner == user) >= MaxMeetingsPerHost
            || _meetings.Count(m => m.GuestOwner != null) >= 64)
            return false;
        var mask = SWPlannerSlots.Mask(startSlot, duration);
        if (((BusyMask(Name(actor), day) | BusyMask(targetName, day)) & mask) != 0)
        {
            error = "Участник занят в выбранное время или имя неоднозначно.";
            return false;
        }
        var targetSession = _guestPlayers.Sessions.FirstOrDefault(s => s.UserId == target && s.Status == SessionStatus.InGame);
        if (targetSession?.AttachedEntity is not { } targetBody) return false;
        var meeting = new Meeting
        {
            Id = _nextMeetingId++, Day = day, StartSlot = startSlot, Duration = duration,
            Host = Name(actor), Title = subject.Trim(), Description = description.Trim(), Location = location.Trim(),
            Private = true, GuestOwner = user, GuestTarget = target, GuestOwnerBody = actor, GuestTargetBody = targetBody,
            GuestKind = kind, GuestTargetKey = targetKey, GuestRevision = 1,
        };
        meeting.Invites[targetName] = SWInviteStatus.Pending;
        _meetings.Add(meeting);
        meetingId = meeting.Id; error = string.Empty;
        return true;
    }

    public bool TryRespondGuestRequest(EntityUid actor, int meetingId, int expectedRevision, bool accept, out string error)
    {
        error = "Запрос недоступен или ваши полномочия изменились.";
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId && m.GuestOwner != null);
        if (meeting == null || meeting.GuestRevision != expectedRevision || meeting.GuestRevision == int.MaxValue
            || !GuestTargetAllowed(actor, meeting) || !UniqueActiveName(actor, Name(actor))
            || meeting.Invites.Values.FirstOrDefault() != SWInviteStatus.Pending)
            return false;
        var ownerSession = _guestPlayers.Sessions.FirstOrDefault(s => s.UserId == meeting.GuestOwner && s.Status == SessionStatus.InGame);
        if (ownerSession?.AttachedEntity is not { } owner || !TryGuestActor(owner, out var ownerId)
            || ownerId != meeting.GuestOwner || owner != meeting.GuestOwnerBody || !UniqueActiveName(owner, Name(owner))
            || !_time.DayActive || !GuestValidWindow(meeting.Day, meeting.StartSlot, meeting.Duration)
            || meeting.Day == _time.Day && meeting.StartSlot < EarliestStartSlot()
            || !TryGuestPolicy(owner, meeting.GuestKind, meeting.GuestTargetKey, out var currentTarget, out var targetName)
            || currentTarget != meeting.GuestTarget || targetName != Name(actor))
            return false;
        var mask = SWPlannerSlots.Mask(meeting.StartSlot, meeting.Duration);
        if (accept && ((BusyMask(Name(actor), meeting.Day, meeting.Id) | BusyMask(Name(owner), meeting.Day, meeting.Id)) & mask) != 0)
        {
            error = "Участник занят в выбранное время."; return false;
        }
        meeting.Invites[meeting.Invites.Keys.First()] = accept ? SWInviteStatus.Accepted : SWInviteStatus.Declined;
        meeting.GuestRevision++;
        error = string.Empty; return true;
    }

    public bool TryCancelGuestRequest(EntityUid actor, int meetingId, int expectedRevision, out string error)
    {
        error = "Запрос недоступен или ваши полномочия изменились.";
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId && m.GuestOwner != null);
        if (meeting == null || meeting.GuestRevision != expectedRevision || !TryGuestActor(actor, out var user) || meeting.GuestOwner != user
            || !GuestOwnerMayCancel(actor, meeting))
            return false;
        _meetings.Remove(meeting); error = string.Empty; return true;
    }

    public bool TryRescheduleGuestRequest(EntityUid actor, int meetingId, int expectedRevision, int day, int slot, int duration, out string error)
    {
        error = "Не удалось перенести запрос: проверьте полномочия, участников и время.";
        var meeting = _meetings.FirstOrDefault(m => m.Id == meetingId && m.GuestOwner != null);
        if (meeting == null || meeting.GuestRevision != expectedRevision || meeting.GuestRevision == int.MaxValue
            || !GuestValid(meeting) || meeting.GuestOwnerBody != actor || !TryGuestActor(actor, out var user) || meeting.GuestOwner != user
            || !UniqueActiveName(actor, Name(actor)) || !_time.DayActive || !GuestValidWindow(day, slot, duration)
            || day == _time.Day && slot < EarliestStartSlot()
            || !TryGuestPolicy(actor, meeting.GuestKind, meeting.GuestTargetKey, out var target, out var targetName)
            || target != meeting.GuestTarget)
            return false;
        var mask = SWPlannerSlots.Mask(slot, duration);
        if (((BusyMask(Name(actor), day, meeting.Id) | BusyMask(targetName, day, meeting.Id)) & mask) != 0)
        {
            error = "Участник занят в выбранное время."; return false;
        }
        meeting.Day = day; meeting.StartSlot = slot; meeting.Duration = duration;
        meeting.Invites[meeting.Invites.Keys.First()] = SWInviteStatus.Pending;
        meeting.GuestRevision++;
        error = string.Empty; return true;
    }
}

public sealed record SWGuestRequestView(int Id, int Day, int StartSlot, int Duration, string Subject,
    string Description, string Location, string Host, string Target, SWInviteStatus Status, bool IsOwner, bool IsTarget, bool Stale, SWGuestRequestKind Kind, int Revision);
