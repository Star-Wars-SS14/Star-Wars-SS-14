using Content.Server.Popups;
using Content.Shared._SW._Senate;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Consoles;

public sealed class SWPlannerConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenatePlannerSystem _planner = default!;

    private const float UpdateInterval = 3f;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWPlannerConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWPlannerConsoleComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SWPlannerConsoleComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerCreateMeetingMessage>(OnCreateMeeting);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerRespondMessage>(OnRespond);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerCancelMessage>(OnCancel);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerRescheduleMessage>(OnReschedule);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerBlockMessage>(OnBlock);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerRemoveBlockMessage>(OnRemoveBlock);
        SubscribeLocalEvent<SWPlannerConsoleComponent, SWPlannerViewMessage>(OnView);
    }

    private string? GetMe(Entity<SWPlannerConsoleComponent> ent)
    {
        var card = _itemSlots.GetItemOrNull(ent.Owner, ent.Comp.IdSlot);
        if (card == null || !TryComp<IdCardComponent>(card, out var idCard))
            return null;

        return string.IsNullOrWhiteSpace(idCard.FullName) ? null : idCard.FullName;
    }

    private void OnCreateMeeting(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerCreateMeetingMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        if (_planner.CreateMeeting(me, args.Day, args.Slot, args.Duration, args.Title, args.Description,
                args.Location, args.Private, args.Invitees))
            _popup.PopupCursor("Встреча создана, приглашения разосланы.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Не удалось создать встречу: проверьте название и время.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRespond(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerRespondMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        if (_planner.Respond(me, args.MeetingId, args.Accept, args.Reason))
            _popup.PopupCursor(args.Accept ? "Приглашение принято." : "Приглашение отклонено.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnCancel(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerCancelMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        if (_planner.Cancel(me, args.MeetingId))
            _popup.PopupCursor("Встреча отменена.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnReschedule(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerRescheduleMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        if (_planner.Reschedule(me, args.MeetingId, args.Day, args.Slot, args.Duration))
            _popup.PopupCursor("Встреча перенесена, участники уведомлены.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Не удалось перенести встречу.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnBlock(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerBlockMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        if (_planner.AddBlock(me, args.Day, args.Slot, args.Duration, args.Label))
            _popup.PopupCursor("Блок добавлен в график.", args.Actor, PopupType.Medium);
        else
            _popup.PopupCursor("Не удалось добавить блок: проверьте подпись и время.", args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnRemoveBlock(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerRemoveBlockMessage args)
    {
        if (GetMe(ent) is not { } me)
            return;

        _planner.RemoveBlock(me, args.BlockId);
        UpdateUi(ent);
    }

    private void OnView(Entity<SWPlannerConsoleComponent> ent, ref SWPlannerViewMessage args)
    {
        ent.Comp.ViewPerson = args.Person;
        ent.Comp.ViewDay = args.Day;
        UpdateUi(ent);
    }

    private void OnOpened(EntityUid uid, SWPlannerConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, comp));
    }

    private void OnInserted(Entity<SWPlannerConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    private void OnRemoved(Entity<SWPlannerConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.IdSlot)
            UpdateUi(ent);
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<SWPlannerConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, SWPlannerConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    private void UpdateUi(Entity<SWPlannerConsoleComponent> ent)
    {
        var me = GetMe(ent);
        var today = _time.Day;

        var state = new SWPlannerConsoleState
        {
            HasCard = me != null,
            Me = me ?? string.Empty,
            GameTime = _time.TimeString(),
            Today = today,
            NowSlot = _planner.NowSlot(),
            Roster = _planner.BuildRoster(),
        };

        if (me != null)
        {
            state.MyBusyToday = _planner.BusyMask(me, today);
            state.MyBusyTomorrow = _planner.BusyMask(me, today + 1);
            state.MyItemsToday = _planner.BuildItems(me, me, today);
            state.MyItemsTomorrow = _planner.BuildItems(me, me, today + 1);
            state.Meetings = _planner.BuildMeetingsFor(me);
            state.Notices = _planner.GetNotices(me);

            if (ent.Comp.ViewPerson is { } target)
            {
                var day = ent.Comp.ViewDay < today ? today : ent.Comp.ViewDay;
                state.ViewPerson = target;
                state.ViewDay = day;
                state.ViewItems = _planner.BuildItems(me, target, day);
            }
        }

        _ui.SetUiState(ent.Owner, SWPlannerConsoleUiKey.Key, state);
    }
}
