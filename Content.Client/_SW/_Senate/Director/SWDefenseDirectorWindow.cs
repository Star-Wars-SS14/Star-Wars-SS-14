using System;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Director;

/// <summary>Persistent event editor: routine state refreshes never replace the draft.</summary>
public sealed class SWDefenseDirectorWindow : FancyWindow
{
    public event Action<SWDefenseDirectorEventMessage>? Submitted;
    private readonly OptionButton _events = new() { Name = "DefenseDirectorEvents", HorizontalExpand = true };
    private readonly OptionButton _kind = new() { HorizontalExpand = true };
    private readonly OptionButton _world = new() { Name = "DefenseDirectorWorld", Filterable = true, HorizontalExpand = true };
    private readonly OptionButton _route = new() { Name = "DefenseDirectorRoute", Filterable = true, HorizontalExpand = true };
    private readonly LineEdit _title = new() { Name = "DefenseDirectorTitle", HorizontalExpand = true };
    private readonly OptionButton _severity = new();
    private readonly OptionButton _strength = new();
    private readonly OptionButton _duration = new();
    private readonly Label _notice = SWConsoleStyle.Text("", SWConsoleStyle.Warn, SWConsoleStyle.Small);
    private readonly Label _presence = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _save = SWConsoleStyle.Action("Создать событие", SWConsoleStyle.Accent);
    private readonly Button _resolve = SWConsoleStyle.Action("Завершить событие", SWConsoleStyle.Good);
    private SWDefenseMapState? _state;
    private int _eventId;
    private string _worldId = "";
    private string _edgeId = "";
    private SWDefenseEventKind _eventKind;
    private bool _canAct;
    private bool _draftExpired;
    private float _severityValue = 40;
    private float _strengthValue = 45;
    private double _durationValue = 4;

    public SWDefenseDirectorWindow()
    {
        Title = "Тактические события";
        MinSize = new Vector2(640, 590);
        var panel = new PanelContainer { HorizontalExpand = true, VerticalExpand = true };
        SWConsoleStyle.StyleRoot(panel);
        ContentsContainer.AddChild(panel);
        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 9,
            Margin = new Thickness(14), HorizontalExpand = true, VerticalExpand = true,
        };
        panel.AddChild(body);
        body.AddChild(_presence);
        _notice.ClipText = true;
        body.AddChild(_notice);
        AddField(body, "Событие", _events);
        AddField(body, "Название", _title);
        AddField(body, "Тип", _kind);
        foreach (var kind in Enum.GetValues<SWDefenseEventKind>())
            _kind.AddItem(KindName(kind), (int) kind);
        _kind.OnItemSelected += args => { _kind.SelectId(args.Id); _eventKind = (SWDefenseEventKind) args.Id; };
        AddField(body, "Мир", _world);
        AddField(body, "Гиперпуть", _route);
        AddField(body, "Тяжесть", _severity);
        AddField(body, "Сила противника", _strength);
        AddField(body, "Продолжительность", _duration);
        FillNumbers(_severity, new[] { 10, 25, 40, 60, 80, 100 }, 40, "%");
        FillNumbers(_strength, new[] { 10, 25, 45, 65, 85, 110, 150 }, 45, "");
        FillNumbers(_duration, new[] { 1, 2, 3, 4, 6, 8, 12 }, 4, " ч");
        _severity.OnItemSelected += args => { _severity.SelectId(args.Id); _severityValue = args.Id; };
        _strength.OnItemSelected += args => { _strength.SelectId(args.Id); _strengthValue = args.Id; };
        _duration.OnItemSelected += args => { _duration.SelectId(args.Id); _durationValue = args.Id; };
        _events.OnItemSelected += args => LoadEvent(args.Id);
        _world.OnItemSelected += args =>
        {
            if (_state == null || args.Id < 0 || args.Id >= _state.Worlds.Count) return;
            _world.SelectId(args.Id);
            _worldId = _state.Worlds[args.Id].Id;
            _edgeId = "";
            RebuildRoutes();
        };
        _route.OnItemSelected += args =>
        {
            if (_state == null) return;
            _route.SelectId(args.Id);
            _edgeId = args.Id < 0 ? "" : _state.Links[args.Id].Id;
        };
        var actions = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(0, 8, 0, 0) };
        actions.AddChild(_save);
        actions.AddChild(_resolve);
        body.AddChild(actions);
        _save.OnPressed += _ => Submit(false);
        _resolve.OnPressed += _ => Submit(true);
    }

    public void SetState(SWDefenseMapState state, bool canAct, string notice)
    {
        var first = _state == null;
        _state = state;
        _canAct = canAct;
        _presence.Text = state.MinisterActive ? "Министр обороны на службе" : "Министр обороны отсутствует";
        _notice.Text = notice;
        _notice.ToolTip = notice;
        _notice.Visible = notice.Length > 0;
        _events.Clear();
        _events.AddItem("Новое событие", 0);
        foreach (var ev in state.Events.Where(e => !e.Resolved))
            _events.AddItem($"№ {ev.Id}: {ev.Title}", ev.Id);
        if (_eventId != 0 && !state.Events.Any(e => e.Id == _eventId && !e.Resolved))
        {
            _eventId = 0;
            _draftExpired = true;
        }
        if (_draftExpired)
        {
            _notice.Text = "Событие завершено. Выберите «Новое событие» или другое действующее событие.";
            _notice.ToolTip = _notice.Text;
            _notice.Visible = true;
        }
        _events.SelectId(_eventId);
        if (first)
        {
            _world.Clear();
            for (var i = 0; i < state.Worlds.Count; i++) _world.AddItem(state.Worlds[i].Name, i);
            var initial = state.Worlds.FindIndex(w => w.Id == "coruscant");
            if (initial < 0) initial = 0;
            if (state.Worlds.Count > 0)
            {
                _world.SelectId(initial);
                _worldId = state.Worlds[initial].Id;
            }
            RebuildRoutes();
            _title.Text = "Нападение на конвой";
        }
        UpdateButtons();
    }

    private void LoadEvent(int id)
    {
        _eventId = id;
        _draftExpired = false;
        _events.SelectId(id);
        if (_state?.Events.FirstOrDefault(e => e.Id == id) is { } ev)
        {
            _title.Text = ev.Title;
            _eventKind = ev.Kind;
            _kind.SelectId((int) ev.Kind);
            _worldId = ev.WorldId;
            _edgeId = ev.EdgeId;
            var worldIndex = _state.Worlds.FindIndex(w => w.Id == ev.WorldId);
            if (worldIndex >= 0) _world.SelectId(worldIndex);
            _severityValue = ev.Severity;
            _strengthValue = ev.EnemyStrength;
            _durationValue = Math.Clamp(ev.ExpiresAt - _state.NowHours, 0.25, 12);
            SelectExact(_severity, (int) Math.Round(_severityValue), "%");
            SelectExact(_strength, (int) Math.Round(_strengthValue), "");
            SelectExact(_duration, (int) Math.Ceiling(_durationValue), " ч");
            _durationValue = Math.Ceiling(_durationValue);
            RebuildRoutes();
        }
        UpdateButtons();
    }

    private void RebuildRoutes()
    {
        _route.Clear();
        _route.AddItem("В районе мира", -1);
        if (_state != null)
            for (var i = 0; i < _state.Links.Count; i++)
            {
                var edge = _state.Links[i];
                if (edge.From != _worldId && edge.To != _worldId) continue;
                string WorldName(string id) => _state.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? id;
                _route.AddItem($"{WorldName(edge.From)} — {WorldName(edge.To)}", i);
            }
        var selected = _state?.Links.FindIndex(l => l.Id == _edgeId && (l.From == _worldId || l.To == _worldId)) ?? -1;
        _route.SelectId(selected);
    }

    private void Submit(bool resolve)
    {
        if (!_canAct || _state == null || _draftExpired) return;
        Submitted?.Invoke(new SWDefenseDirectorEventMessage(_eventId, _title.Text, _worldId, _edgeId,
            _severityValue, _strengthValue, _durationValue, resolve, _eventKind));
    }

    private void UpdateButtons()
    {
        _save.Text = _eventId == 0 ? "Создать событие" : "Сохранить изменения";
        _save.Disabled = !_canAct || _draftExpired;
        _resolve.Disabled = !_canAct || _eventId == 0;
    }

    private static void AddField(BoxContainer parent, string name, Control control)
    {
        var row = new BoxContainer { SeparationOverride = 10, HorizontalExpand = true };
        row.AddChild(new Label { Text = name, MinWidth = 175, FontOverride = SWConsoleStyle.Small });
        control.HorizontalExpand = true;
        row.AddChild(control);
        parent.AddChild(row);
    }

    private static void FillNumbers(OptionButton button, int[] values, int selected, string suffix)
    {
        foreach (var value in values) button.AddItem($"{value}{suffix}", value);
        button.SelectId(selected);
    }

    private static void SelectExact(OptionButton button, int value, string suffix)
    {
        if (!Enumerable.Range(0, button.ItemCount).Any(i => button.GetItemId(i) == value))
            button.AddItem($"{value}{suffix}", value);
        button.SelectId(value);
    }

    private static string KindName(SWDefenseEventKind kind) => kind switch
    {
        SWDefenseEventKind.Piracy => "Пиратское нападение",
        SWDefenseEventKind.Unrest => "Беспорядки",
        SWDefenseEventKind.Incursion => "Вторжение",
        SWDefenseEventKind.Blockade => "Блокада",
        _ => "Рейд за границу",
    };
}
