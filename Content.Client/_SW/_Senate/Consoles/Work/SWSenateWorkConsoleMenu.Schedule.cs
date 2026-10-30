using System;
using System.Linq;
using Content.Shared._SW._Senate;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Work;

public sealed partial class SWSenateWorkConsoleMenu
{
    private readonly SWSenateScheduleBoard _scheduleBoard = new();
    private int? _scheduleSelectedSession;
    private bool _scheduleSubmitted;
    private string _scheduleInspectorSignature = string.Empty;
    private string _scheduleFeedback = string.Empty;

    private void InitializeScheduleBoard()
    {
        ScheduleBoardHost.AddChild(_scheduleBoard);
        _scheduleBoard.BillSelected += id =>
        {
            if (_scheduleBoard.DraftBillId is { } draftId && draftId != id)
            {
                _scheduleBoard.ClearDraft();
                _selectedStartHour = -1;
                _selectedDuration = 1;
            }
            _selectedBillId = id;
            _scheduleSelectedSession = null;
            _scheduleSubmitted = false;
            _scheduleFeedback = string.Empty;
            if (_lastState is { } state) BuildGroupedAmendments(state);
            RefreshScheduleInspector();
        };
        _scheduleBoard.DraftChanged += (id, start, duration) =>
        {
            _selectedBillId = id;
            _selectedStartHour = start;
            _selectedDuration = duration;
            _scheduleSelectedSession = null;
            _scheduleSubmitted = false;
            _scheduleFeedback = string.Empty;
            if (_lastState is { } state) BuildGroupedAmendments(state);
            RefreshScheduleInspector();
        };
        _scheduleBoard.SessionSelected += id =>
        {
            _scheduleSelectedSession = id;
            RefreshScheduleInspector();
        };
        _scheduleBoard.DraftCleared += () =>
        {
            _selectedBillId = -1;
            _selectedStartHour = -1;
            _scheduleSubmitted = false;
            _groupedBillIds.Clear();
            if (_lastState is { } state) BuildGroupedAmendments(state);
            RefreshScheduleInspector();
        };
        OnClose += () => _scheduleBoard.CancelInteraction();
    }

    private void ResetScheduleContext()
    {
        _scheduleBoard.ResetLocalState();
        _scheduleSelectedSession = null;
        _scheduleSubmitted = false;
        _scheduleFeedback = string.Empty;
        _scheduleSignature = _scheduleInspectorSignature = string.Empty;
    }

    private void BuildSchedule(SWSenateWorkConsoleState state)
    {
        var bills = state.SchedulableBills.Where(b => !b.Emergency).ToList();
        _scheduleBoard.SetData(state.Schedule, bills, state.FreeStartHours, state.CanScheduleSessions);
        var signature = string.Join("|", state.SchedulableBills.Select(b => $"{b.Id}:{b.Name}:{b.Emergency}:{b.AmendsLawId}:{b.AmendsLawName}")) +
                        ":" + state.CanScheduleSessions;
        if (_scheduleSignature != signature)
        {
            _scheduleSignature = signature;
            BuildEmergencyBills(state.SchedulableBills);
            EmergencyBillsPanel.Visible = state.CanScheduleSessions;
            BuildGroupedAmendments(state);
        }
        RefreshScheduleInspector();
    }

    private void RefreshScheduleInspector()
    {
        if (_lastState is not { } state) return;
        var session = _scheduleSelectedSession is { } id ? state.PendingSessions.FirstOrDefault(s => s.Id == id) : null;
        var row = _scheduleSelectedSession is { } selected ? state.Schedule.FirstOrDefault(r => r.SessionId == selected) : null;
        var bill = state.SchedulableBills.FirstOrDefault(b => b.Id == _selectedBillId && !b.Emergency);
        var signature = $"{_scheduleSelectedSession}:{row?.SessionTitle}:{row?.SessionStartHour}:{row?.SessionDuration}:{row?.SessionConfirmed}:{row?.SessionEmergency}:" +
                        $"{session?.BillName}:{session?.TimeRange}:{string.Join(";", session?.BillNames ?? new())}:" +
                        $"{_selectedBillId}:{bill?.Name}:{_selectedStartHour}:{_selectedDuration}:{_scheduleBoard.DraftValid}:{_scheduleSubmitted}:{_scheduleFeedback}:{state.CanScheduleSessions}:{state.CanConfirmSessions}";
        ScheduleButton.Visible = state.CanScheduleSessions && _scheduleSelectedSession == null;
        GroupedAmendmentsBox.Visible = state.CanScheduleSessions && _scheduleSelectedSession == null && GroupedAmendmentsBox.ChildCount > 0;
        UpdateScheduleAction();
        if (_scheduleInspectorSignature == signature) return;
        _scheduleInspectorSignature = signature;
        ScheduleInspector.RemoveAllChildren();
        ScheduleSessionActions.RemoveAllChildren();
        if (_scheduleSelectedSession != null)
        {
            if (row == null)
            {
                ScheduleInspector.AddChild(SWConsoleStyle.Wrap("Заседание больше не доступно.", SWConsoleStyle.TextDim));
                return;
            }
            var status = row.SessionEmergency ? "чрезвычайное" : row.SessionConfirmed ? "подтверждено" : "ожидает подтверждения";
            ScheduleInspector.AddChild(SWConsoleStyle.Wrap($"{row.SessionTitle} — {status}", SWConsoleStyle.TextBright));
            if (session != null)
            {
                ScheduleInspector.AddChild(BuildSessionDescription(session));
                var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
                var sessionId = session.Id;
                if (state.CanScheduleSessions)
                {
                    var remove = SWConsoleStyle.Action("Удалить", SWConsoleStyle.Bad);
                    remove.OnPressed += _ => _owner.RemoveSession(sessionId);
                    actions.AddChild(remove);
                }
                if (state.CanConfirmSessions)
                {
                    var confirm = SWConsoleStyle.Action("Подтвердить", SWConsoleStyle.Good);
                    confirm.OnPressed += _ => _owner.ConfirmSession(sessionId);
                    actions.AddChild(confirm);
                    var reject = SWConsoleStyle.Action("Отклонить", SWConsoleStyle.Bad);
                    reject.OnPressed += _ => _owner.RejectSession(sessionId);
                    actions.AddChild(reject);
                }
                ScheduleSessionActions.AddChild(actions);
            }
            else ScheduleInspector.AddChild(SWConsoleStyle.Wrap($"{ScheduleClock(row.SessionStartHour)}–{ScheduleClock(row.SessionStartHour + row.SessionDuration)}", SWConsoleStyle.TextDim));
            return;
        }
        if (!state.CanScheduleSessions) return;
        var title = bill == null ? (_selectedBillId < 0 ? "Выберите проект и час расписания." : $"Проект № {_selectedBillId} больше недоступен.") : $"Черновик: № {bill.Id} {bill.Name}";
        ScheduleInspector.AddChild(SWConsoleStyle.Wrap(title, SWConsoleStyle.TextBright));
        if (_scheduleBoard.DraftStartHour is not { } start)
        {
            if (bill != null) ScheduleInspector.AddChild(SWConsoleStyle.Wrap("Выберите время в расписании.", SWConsoleStyle.TextDim));
            return;
        }
        ScheduleInspector.AddChild(SWConsoleStyle.Wrap($"{start:00}:00–{start + _selectedDuration:00}:00 ({_selectedDuration} ч)", SWConsoleStyle.TextDim));
        var durationActions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var minus = SWConsoleStyle.Action("− час", SWConsoleStyle.Accent);
        minus.Disabled = _selectedDuration <= 1;
        minus.OnPressed += _ => ChangeScheduleDuration(-1);
        durationActions.AddChild(minus);
        var plus = SWConsoleStyle.Action("+ час", SWConsoleStyle.Accent);
        plus.Disabled = _selectedDuration >= 4;
        plus.OnPressed += _ => ChangeScheduleDuration(1);
        durationActions.AddChild(plus);
        ScheduleInspector.AddChild(durationActions);
        if (!_scheduleBoard.DraftValid || !string.IsNullOrEmpty(_scheduleFeedback))
            ScheduleInspector.AddChild(SWConsoleStyle.Wrap(string.IsNullOrEmpty(_scheduleFeedback) ? "Выбранное время или проект недоступны." : _scheduleFeedback, SWConsoleStyle.Bad));
        else if (_scheduleSubmitted)
            ScheduleInspector.AddChild(SWConsoleStyle.Wrap("Предложение отправлено; ожидается обновление расписания.", SWConsoleStyle.TextDim));
    }

    private void ChangeScheduleDuration(int delta)
    {
        if (_lastState is not { } state || _scheduleBoard.DraftStartHour is not { } start || _scheduleBoard.DraftBillId is not { } id) return;
        var duration = Math.Clamp(_selectedDuration + delta, 1, 4);
        if (!Enumerable.Range(start, duration).All(state.FreeStartHours.Contains))
        {
            _scheduleFeedback = "Этот интервал занят или недоступен.";
            RefreshScheduleInspector();
            return;
        }
        _selectedDuration = duration;
        _scheduleFeedback = string.Empty;
        _scheduleSubmitted = false;
        _scheduleBoard.SetDraft(id, start, duration);
        RefreshScheduleInspector();
    }

    private static string ScheduleClock(double hour)
    {
        var minutes = (int) Math.Round(hour * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
