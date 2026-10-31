using System.Linq;
using System.Numerics;
using Content.Client.Eui;
using Content.Client.UserInterface.Controls;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Delegations;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Delegations;

[UsedImplicitly]
public sealed class SWDelegationEui : BaseEui
{
    private readonly FancyWindow _window = new() { Title = "Представительство", MinSize = new Vector2(480, 420) };
    private readonly RichTextLabel _text = new() { HorizontalExpand = true };
    private readonly Button _accept = new() { Text = "Согласиться" };
    private readonly Button _decline = new() { Text = "Отказаться" };
    private readonly Button _evacuate = new() { Text = "Эвакуироваться" };
    private readonly BoxContainer _planner = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly OptionButton _day = new() { HorizontalExpand = true };
    private readonly OptionButton _slot = new() { HorizontalExpand = true };
    private readonly OptionButton _duration = new() { HorizontalExpand = true };
    private readonly OptionButton _meetings = new() { HorizontalExpand = true };
    private readonly RichTextLabel _meetingDetails = new() { HorizontalExpand = true };
    private readonly LineEdit _subject = new() { PlaceHolder = "Тема встречи", HorizontalExpand = true };
    private readonly LineEdit _location = new() { PlaceHolder = "Место встречи", HorizontalExpand = true };
    private readonly Button _create = new() { Text = "Предложить встречу" };
    private readonly Button _respond = new() { Text = "Принять встречу" };
    private readonly Button _refuse = new() { Text = "Отклонить встречу" };
    private readonly Button _cancel = new() { Text = "Отменить встречу" };
    private readonly Button _reschedule = new() { Text = "Перенести встречу" };
    private readonly Label _notice = new();
    private SWDelegationMeetingView[] _entries = Array.Empty<SWDelegationMeetingView>();
    private string _entryFingerprint = string.Empty;
    private int _selected = -1;
    private int _today;
    private int _version;
    private bool _canCreate;
    private bool _plannerInitialized;

    public SWDelegationEui()
    {
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true };
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        content.AddChild(_text);
        content.AddChild(_planner);
        scroll.AddChild(content);
        body.AddChild(scroll);
        body.AddChild(_accept);
        body.AddChild(_decline);
        body.AddChild(_evacuate);
        _window.ContentsContainer.AddChild(body);

        _planner.AddChild(new Label { Text = "Встречи" });
        _day.AddItem("Сегодня", 0);
        _day.AddItem("Завтра", 1);
        _day.SelectId(0);
        for (var slot = 0; slot < SWPlannerSlots.PerDay; slot++) _slot.AddItem(SWPlannerSlots.Time(slot), slot);
        _slot.SelectId(0);
        for (var length = 1; length <= 4; length++) _duration.AddItem($"{length * 15} мин", length);
        _duration.SelectId(2);
        _planner.AddChild(_day);
        _planner.AddChild(_slot);
        _planner.AddChild(_duration);
        _planner.AddChild(_subject);
        _planner.AddChild(_location);
        _planner.AddChild(_create);
        _planner.AddChild(_meetings);
        _planner.AddChild(_meetingDetails);
        _planner.AddChild(_respond);
        _planner.AddChild(_refuse);
        _planner.AddChild(_cancel);
        _planner.AddChild(_reschedule);
        _planner.AddChild(_notice);
        _meetings.OnItemSelected += args => { _meetings.SelectId(args.Id); _selected = args.Id; UpdateButtons(); };
        _create.OnPressed += _ => SendPlanner(SWDelegationPlannerAction.Create);
        _respond.OnPressed += _ => SendPlanner(SWDelegationPlannerAction.Respond, true);
        _refuse.OnPressed += _ => SendPlanner(SWDelegationPlannerAction.Respond, false);
        _cancel.OnPressed += _ => SendPlanner(SWDelegationPlannerAction.Cancel);
        _reschedule.OnPressed += _ => SendPlanner(SWDelegationPlannerAction.Reschedule);
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _accept.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Accept, _version));
        _decline.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Decline, _version));
        _evacuate.OnPressed += _ => SendMessage(new SWDelegationActionMessage(SWDelegationAction.Evacuate, _version));
    }

    public override void Opened() { base.Opened(); _window.OpenCentered(); }
    public override void Closed() { base.Closed(); _window.Close(); }
    public override void HandleState(EuiStateBase state)
    {
        if (state is not SWDelegationEuiState view) return;
        _version = view.Version;
        _today = view.PlannerToday;
        _canCreate = view.PlannerCanCreate;
        var text = new FormattedMessage();
        text.AddText(view.Text);
        _text.SetMessage(text);
        _accept.Disabled = !view.CanAccept;
        _decline.Disabled = !view.CanDecline;
        _evacuate.Visible = view.CanEvacuate;
        _planner.Visible = view.PlannerAvailable;
        if (view.PlannerAvailable && !_plannerInitialized)
        {
            _plannerInitialized = true;
            if (view.PlannerEarliestSlot <= SWPlannerSlots.PerDay - 2)
                _slot.SelectId(Math.Max(0, view.PlannerEarliestSlot));
            else
            {
                _day.SelectId(1);
                _slot.SelectId(0);
            }
        }
        _notice.Text = view.PlannerNotice;
        var oldSelection = _entries.FirstOrDefault(x => x.Id == _selected);
        _entries = view.Meetings.ToArray();
        var updatedSelection = _entries.FirstOrDefault(x => x.Id == _selected);
        if (oldSelection != null && (updatedSelection == null || updatedSelection.Revision != oldSelection.Revision))
            _selected = -1; // A changed request must be selected explicitly again.
        var fingerprint = string.Join("|", _entries.Select(x => $"{x.Id}:{x.Revision}:{x.Day}:{x.Slot}:{x.Duration}:{x.Status}:{x.Stale}"));
        if (fingerprint != _entryFingerprint)
        {
            _entryFingerprint = fingerprint;
            _meetings.Clear();
            _meetings.AddItem("Выберите запрос", -1);
            foreach (var entry in _entries)
                _meetings.AddItem(entry.Stale ? $"Запрос {entry.Id}: условия недоступны" :
                    $"День {entry.Day}, {SWPlannerSlots.Time(entry.Slot)} · {entry.Duration * 15} мин", entry.Id);
            if (!_entries.Any(x => x.Id == _selected)) _selected = -1;
            _meetings.SelectId(_selected);
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = _entries.FirstOrDefault(x => x.Id == _selected);
        _create.Visible = _canCreate;
        _day.Visible = _canCreate;
        _slot.Visible = _canCreate;
        _duration.Visible = _canCreate;
        _subject.Visible = _canCreate;
        _location.Visible = _canCreate;
        _meetings.Visible = _entries.Length > 0;
        _meetingDetails.Visible = selected is { Stale: false };
        var details = new FormattedMessage();
        if (selected is { Stale: false })
            details.AddText($"{selected.Subject}\nМесто: {selected.Location}\nСтатус: " + (selected.Status switch { 0 => "ожидает ответа", 1 => "принято", 2 => "отклонено", _ => "неизвестно" }));
        _meetingDetails.SetMessage(details);
        _respond.Visible = selected is { IsTarget: true, Stale: false, Status: 0 };
        _refuse.Visible = selected is { IsTarget: true, Stale: false, Status: 0 };
        _cancel.Visible = selected is { IsOwner: true };
        _reschedule.Visible = selected is { IsOwner: true, Stale: false };
    }

    private void SendPlanner(SWDelegationPlannerAction action, bool accept = false)
    {
        var selected = _entries.FirstOrDefault(x => x.Id == _selected);
        if (action != SWDelegationPlannerAction.Create && selected == null) return;
        SendMessage(new SWDelegationPlannerMessage
        {
            Action = action, Version = _version, Id = selected?.Id ?? 0, Accept = accept,
            Day = _today + _day.SelectedId, Slot = _slot.SelectedId, Duration = _duration.SelectedId,
            Subject = _subject.Text, Location = _location.Text,
            ExpectedDay = selected?.Day ?? 0, ExpectedSlot = selected?.Slot ?? 0,
            ExpectedDuration = selected?.Duration ?? 0, ExpectedStatus = selected?.Status ?? 0,
            ExpectedRevision = selected?.Revision ?? 0,
        });
    }
}
