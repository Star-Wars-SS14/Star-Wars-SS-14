using System;
using System.Linq;
using System.Collections.Generic;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Empire;

/// <summary>Separate military atlas; selection and camera are independent of the economy browser.</summary>
public sealed class SWEmpireMilitaryPanel : BoxContainer
{
    private readonly SWEmpireCouncilConsoleBoundUserInterface _owner;
    private readonly SWGalacticMapPrototype _visible;
    private readonly BoxContainer _host = Stack();
    private readonly WrapContainer _fleets = new() { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
    private readonly Label _empty = SWConsoleStyle.Text("Соединения отсутствуют", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly BoxContainer _details = Stack();
    private readonly Dictionary<int, (BoxContainer Cell, Button Button, Label Location)> _fleetRows = new();
    private SWEmpireMilitaryAction? _draftAction;
    private readonly Label _title = SWConsoleStyle.Text("Галактика", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Button _back = SWConsoleStyle.TabButton("Галактика");
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _supplies = SWConsoleStyle.TabButton("Поставки");
    private SWGalacticMapControl _map = default!;
    private SWGalacticMapPrototype _view = default!;
    private SWEmpireCouncilConsoleState? _state;
    private SWEmpireCouncilSphere? _lastSphere;
    private int? _selected;
    private string? _target, _sector, _system, _borderId;
    private string? _quoteId;
    private SWEmpireMilitaryRequest? _pending;
    private bool _submitted;
    private string? _commandId;
    private string _result = string.Empty;

    public SWEmpireMilitaryPanel(SWEmpireCouncilConsoleBoundUserInterface owner)
    {
        _owner = owner;
        Name = "EmpireMilitaryPanel";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        SeparationOverride = 5;
        var source = IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic");
        _visible = SWGalacticMapViews.ForFaction(source, "empire");
        var toolbar = new WrapContainer { SeparationOverride = 5, CrossSeparationOverride = 4, HorizontalExpand = true };
        _back.OnPressed += _ => SwitchView(null, null);
        toolbar.AddChild(_back);
        toolbar.AddChild(_title);
        var minus = SWConsoleStyle.TabButton("−");
        minus.OnPressed += _ => _map.ChangeZoom(1 / 1.2f);
        toolbar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+");
        plus.OnPressed += _ => _map.ChangeZoom(1.2f);
        toolbar.AddChild(plus);
        toolbar.AddChild(_zoom);
        var reset = SWConsoleStyle.TabButton("Сбросить масштаб");
        reset.OnPressed += _ => _map.ResetView();
        toolbar.AddChild(reset);
        var locate = SWConsoleStyle.TabButton("К флоту");
        locate.OnPressed += _ => LocateFleet();
        toolbar.AddChild(locate);
        _supplies.ToggleMode = true; _supplies.Pressed = true;
        _supplies.OnToggled += args => { _map.SetDefenseSupplyVisible(args.Pressed); SWConsoleStyle.StyleTabButton(_supplies, args.Pressed); };
        SWConsoleStyle.StyleTabButton(_supplies, true);
        toolbar.AddChild(_supplies);
        AddChild(toolbar);
        AddChild(_fleets);
        _fleets.AddChild(_empty);
        var side = Stack();
        side.AddChild(_details);
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(side);
        _host.VerticalExpand = true;
        AddChild(new SWAtlasLayout(_host, scroll, stackBelow: 520, minDetails: 245, maxDetails: 330, minMapHeight: 180) { VerticalExpand = true });
        SwitchView(null, null);
    }

    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };

    private void SwitchView(string? sector, string? system)
    {
        var zoom = _map?.Zoom ?? 1f;
        _sector = sector; _system = system;
        _view = system != null ? SWGalacticMapViews.System(_visible, system) : sector != null
            ? SWGalacticMapViews.PoliticalSector(_visible, sector) : SWGalacticMapViews.Overview(_visible);
        _host.RemoveAllChildren();
        _map = new SWGalacticMapControl(_view, focusSystem: system, focusPoliticalSector: sector) { MinHeight = 180 };
        _map.WorldSelected += id => { _target = id; _borderId = null; _draftAction = null; InvalidateQuote(); Refresh(); };
        _map.WorldActivated += id =>
        {
            var world = _view.Worlds.FirstOrDefault(w => w.Id == id);
            if (world == null || world.Id.StartsWith("public-sector:")) return;
            if (_sector == null && !string.IsNullOrEmpty(world.PoliticalSectorId)) SwitchView(world.PoliticalSectorId, null);
            else if (_system == null && !string.IsNullOrEmpty(world.SystemId)) SwitchView(_sector, world.SystemId);
        };
        _map.DefenseFleetSelected += id => SelectFleet(id);
        _map.ViewChanged += () => _zoom.Text = $"{_map.Zoom * 100:0}%";
        _map.SetDefenseSupplyVisible(_supplies.Pressed);
        if (Math.Abs(zoom - 1f) > .001f) _map.ChangeZoom(zoom);
        _zoom.Text = $"{_map.Zoom * 100:0}%";
        _host.AddChild(_map);
        _title.Text = system != null ? _visible.Worlds.FirstOrDefault(w => w.SystemId == system)?.SystemName ?? "Система"
            : sector != null ? _visible.Worlds.FirstOrDefault(w => w.PoliticalSectorId == sector)?.PoliticalSectorName ?? "Сектор" : "Галактика";
        _back.Visible = sector != null || system != null;
        UpdateMap();
    }

    public void SetState(SWEmpireCouncilConsoleState state)
    {
        if (_lastSphere != state.Sphere)
        {
            InvalidateQuote(); _draftAction = null; _commandId = null; _result = string.Empty;
        }
        _lastSphere = state.Sphere;
        _state = state;
        if (_submitted && _commandId == state.ResultRequestId) { _result = state.Result; _submitted = false; _commandId = null; _pending = null; _quoteId = null; }
        if (_selected != null && !state.Military.Fleets.Any(f => f.Id == _selected && f.Phase != "Утрачен")) { _selected = null; _draftAction = null; InvalidateQuote(); }
        UpdateMap();
        Refresh();
    }

    private void SelectFleet(int id)
    {
        if (_selected != id) { _selected = id; _draftAction = null; InvalidateQuote(); }
        UpdateMap(); Refresh();
    }

    private void ChooseAction(SWEmpireMilitaryAction action)
    {
        if (_draftAction != action) { _draftAction = action; InvalidateQuote(); }
        Refresh();
    }

    private void LocateFleet()
    {
        if (_selected is not { } id) return;
        if (_map.DefenseFleetPosition(id) == null) SwitchView(null, null);
        _map.FocusDefenseFleet(id);
    }

    private void InvalidateQuote() { _quoteId = null; _pending = null; _submitted = false; }

    private void UpdateMap()
    {
        if (_map == null || _state == null) return;
        var defense = new SWDefenseMapState { NowHours = _state.Military.NowHours, ClockRunning = _state.Military.ClockRunning, SupplyHour = _state.Military.SupplyHour, SupplyFlows = _state.Military.SupplyFlows };
        foreach (var route in _view.Routes)
            defense.Links.Add(new SWDefenseLinkView { Id = SWTransitLinkPrototype.LinkId(route.From, route.To), From = route.From, To = route.To, Traversable = true });
        string DisplayLocation(string id)
        {
            if (_view.Worlds.Any(w => w.Id == id)) return id;
            var own = _visible.Worlds.FirstOrDefault(w => w.Id == id && _visible.GetWorldFaction(w) == "empire");
            if (own == null) return id;
            return _view.Worlds.FirstOrDefault(w => w.PoliticalSectorId == own.PoliticalSectorId && w.Overview)?.Id ?? id;
        }
        foreach (var fleet in _state.Military.Fleets)
            defense.Fleets.Add(new SWDefenseFleetView { Id = fleet.Id, Name = fleet.Name, CurrentWorldId = DisplayLocation(fleet.WorldId),
                TargetWorldId = DisplayLocation(fleet.Target), Strength = (float) fleet.Strength, Damage = (float) fleet.Damage,
                Supply = (float) fleet.Supply, Training = (float) fleet.Training, Status = fleet.Status, NextWorldId = string.IsNullOrEmpty(fleet.NextWorldId) ? null : DisplayLocation(fleet.NextWorldId),
                Phase = fleet.Phase == "Переход" ? SWDefenseOperationPhase.Travel : fleet.Phase == "Ожидание ответа" ? SWDefenseOperationPhase.Preparation : fleet.Phase == "Бой" ? SWDefenseOperationPhase.Execution : fleet.Phase == "Ремонт" ? SWDefenseOperationPhase.Recovery : fleet.Phase == "Утрачен" ? SWDefenseOperationPhase.Lost : SWDefenseOperationPhase.Standing,
                Order = fleet.Phase == "Бой" ? SWDefenseFleetOrder.Respond : fleet.Phase == "Ремонт" ? SWDefenseFleetOrder.Repair : SWDefenseFleetOrder.Move,
                LegStartedAt = fleet.LegStartedAt, EtaHours = fleet.LegEndsAt });
        _map.SetDefense(defense, _selected);
        var selected = _state.Military.Fleets.FirstOrDefault(f => f.Id == _selected);
        var publishedRoute = new System.Collections.Generic.List<string>();
        if (selected != null)
        {
            publishedRoute.Add(DisplayLocation(selected.WorldId));
            if (!string.IsNullOrEmpty(selected.NextWorldId)) publishedRoute.Add(DisplayLocation(selected.NextWorldId));
            publishedRoute.AddRange(selected.Route.Select(DisplayLocation));
        }
        _map.SetPublishedDefenseRoute(publishedRoute);
        if (_target != null) _map.SelectWorld(_target);
    }

    private string TargetName(string id)
    {
        if (id.StartsWith("fleet:") && int.TryParse(id.AsSpan(6), out var fleetId))
            return _state?.Military.Fleets.FirstOrDefault(f => f.Id == fleetId)?.Name ?? "Соединение";
        return _visible.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? _state?.Military.Borders.FirstOrDefault(b => b.Id == id)?.Name ?? "Цель";
    }
    private void Refresh()
    {
        _details.RemoveAllChildren();
        if (_state == null) return;
        if (_pending != null && !_state.Military.Options.Any(o => o.FleetId == _pending.FleetId && o.Target == _pending.Target && o.Action == _pending.Action && o.Available && o.Cost > 0)) InvalidateQuote();
        var visibleFleets = _state.Military.Fleets.Where(f => f.Phase != "Утрачен").ToArray();
        var ids = visibleFleets.Select(f => f.Id).ToHashSet();
        _empty.Visible = visibleFleets.Length == 0;
        foreach (var id in _fleetRows.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _fleets.RemoveChild(_fleetRows[id].Cell);
            _fleetRows.Remove(id);
        }
        foreach (var fleet in visibleFleets)
        {
            if (!_fleetRows.TryGetValue(fleet.Id, out var row))
            {
                var id = fleet.Id;
                var cell = Stack(); cell.SetWidth = 155; cell.HorizontalExpand = false;
                var button = SWConsoleStyle.TabButton(fleet.Name); button.Name = $"EmpireFleet{id}";
                button.Label.ClipText = true; button.OnPressed += _ => SelectFleet(id);
                var location = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
                location.ClipText = true; cell.AddChild(button); cell.AddChild(location); _fleets.AddChild(cell);
                row = (cell, button, location); _fleetRows[id] = row;
            }
            row.Button.Text = fleet.Name;
            SWConsoleStyle.StyleTabButton(row.Button, fleet.Id == _selected);
            row.Location.Text = fleet.Phase == "Переход" ? $"К {TargetName(fleet.Target)}" : TargetName(fleet.WorldId);
        }
        var selected = _state.Military.Fleets.FirstOrDefault(f => f.Id == _selected);
        var stats = Stack();
        if (selected != null)
        {
        _details.AddChild(SWConsoleStyle.Section(selected.Name));
        if (selected.EtaHours > 0) _details.AddChild(SWConsoleStyle.Text($"Прибытие через  {Math.Max(0, selected.EtaHours - _state.Military.NowHours):0.0} ч", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        stats.AddChild(SWConsoleStyle.Section("Состояние соединения"));
        if (!string.IsNullOrEmpty(_result)) stats.AddChild(SWConsoleStyle.Wrap(_result, SWConsoleStyle.Warn));
        stats.AddChild(SWConsoleStyle.Text(selected.Status, SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        stats.AddChild(SWConsoleStyle.Text($"Сила  {selected.Strength:0}   Повреждения  {selected.Damage:0}%", SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        stats.AddChild(SWConsoleStyle.Text($"Снабжение  {selected.Supply:0}%   Подготовка  {selected.Training:0}%", SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        if (!string.IsNullOrEmpty(selected.SupplyStatus))
            stats.AddChild(SWConsoleStyle.Wrap(selected.SupplyStatus, SWConsoleStyle.TextDim));
        if (!string.IsNullOrEmpty(selected.SupplyDetails))
            stats.AddChild(SWConsoleStyle.Wrap(selected.SupplyDetails, SWConsoleStyle.TextDim));
        if (selected.CargoAmount > 0)
            stats.AddChild(SWConsoleStyle.Text($"Груз  {selected.CargoGoodName}  {selected.CargoAmount:0.##} ед.", SWConsoleStyle.TextBright, SWConsoleStyle.Small));
        if (selected.CargoUsedSlots > 0 || !string.IsNullOrEmpty(selected.CargoStatus))
        {
            var status = selected.CargoStatus;
            if (selected.Order == SWEmpireMilitaryAction.Return && selected.CargoAmount > 0 && selected.Phase == "Переход")
                status = "Возвращение с грузом";
            if (selected.CargoReadyAt is { } readyAt && !string.IsNullOrEmpty(status))
                status += $"  {Math.Max(0, readyAt - _state.Military.NowHours):0.00} ч";
            stats.AddChild(SWConsoleStyle.Wrap($"Трюм  {selected.CargoUsedSlots:0.##} из {selected.CargoCapacity:0.##} мест\n{status}", SWConsoleStyle.TextDim));
        }
        if (selected.Route.Count > 0)
            stats.AddChild(SWConsoleStyle.Wrap($"Маршрут  {string.Join(", ", selected.Route.Select(TargetName))}", SWConsoleStyle.TextDim));
        }
        else stats.AddChild(SWConsoleStyle.Text("Выберите соединение", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        _details.AddChild(SWConsoleStyle.Section(_target == null ? "Выберите цель" : TargetName(_target)));
        var orderCard = Stack();
        _details.AddChild(orderCard);
        var selectors = Stack();
        foreach (var escort in _state.Military.Options.Where(o => selected != null && o.FleetId == selected.Id && o.Action == SWEmpireMilitaryAction.Escort && o.Target.StartsWith("fleet:")).DistinctBy(o => o.Target))
        {
            var target = escort.Target;
            var escortTarget = SWConsoleStyle.TabButton(TargetName(target));
            escortTarget.OnPressed += _ => { _target = target; _draftAction = SWEmpireMilitaryAction.Escort; InvalidateQuote(); Refresh(); };
            selectors.AddChild(escortTarget);
        }
        foreach (var border in _state.Military.Borders.Where(b => b.OwnWorldId == _target || b.ForeignSectorId == _target))
        {
            var captured = border;
            var borderButton = SWConsoleStyle.TabButton(border.Name);
            borderButton.Name = "EmpireMilitaryBorder";
            borderButton.OnPressed += _ => { _borderId = captured.Id; _target = captured.ForeignSectorId; _draftAction = null; InvalidateQuote(); Refresh(); };
            selectors.AddChild(borderButton);
        }
        var selectedBorder = _state.Military.Borders.FirstOrDefault(b => b.Id == _borderId);
        if (selectedBorder != null)
        {
            selectors.AddChild(SWConsoleStyle.Text(selectedBorder.Status, SWConsoleStyle.TextBright, SWConsoleStyle.Small));
            if (selectedBorder.ReadyAt is { } ready) selectors.AddChild(SWConsoleStyle.Text($"Ожидание  {Math.Max(0, ready - _state.Military.NowHours):0.0} ч", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            if (!string.IsNullOrEmpty(selectedBorder.Warning)) selectors.AddChild(SWConsoleStyle.Wrap(selectedBorder.Warning, SWConsoleStyle.Warn));
        }
        var options = _state.Military.Options.Where(o => o.Target == _target &&
            (o.Action == SWEmpireMilitaryAction.Recruit && o.FleetId == 0 || selected != null && o.FleetId == selected.Id)).ToArray();
        if (_target != null && options.Length == 0) _details.AddChild(SWConsoleStyle.Text("Приказы недоступны", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        if (_draftAction == null || !options.Any(o => o.Action == _draftAction))
            _draftAction = options.FirstOrDefault(o => o.Available)?.Action ?? options.FirstOrDefault()?.Action;
        var tasks = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
        if (options.Length > 0) _details.AddChild(SWConsoleStyle.Section("Задача"));
        _details.AddChild(tasks);
        foreach (var option in options)
        {
            var action = option.Action;
            var button = SWConsoleStyle.TabButton(ActionName(action));
            button.Name = $"EmpireMilitaryAction{action}";
            SWConsoleStyle.StyleTabButton(button, action == _draftAction);
            button.OnPressed += _ => ChooseAction(action);
            tasks.AddChild(button);
        }
        var draft = options.FirstOrDefault(o => o.Action == _draftAction);
        if (draft != null)
        {
            var card = SWConsoleStyle.Row(SWConsoleStyle.Navigation);
            var content = Stack();
            content.AddChild(SWConsoleStyle.Text(ActionName(draft.Action), SWConsoleStyle.TextBright, SWConsoleStyle.Strong));
            content.AddChild(SWConsoleStyle.Text(TargetName(draft.Target), SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            if (draft.Cost > 0) content.AddChild(SWConsoleStyle.Text($"Стоимость  {draft.Cost:N0} кр", SWConsoleStyle.Brass, SWConsoleStyle.Small));
            if (draft.EtaHours is { } eta) content.AddChild(SWConsoleStyle.Text($"Время  {eta:0.0} ч", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            if (!string.IsNullOrEmpty(draft.Risk)) content.AddChild(SWConsoleStyle.Wrap(draft.Risk, SWConsoleStyle.Warn));
            if (!draft.Available && !string.IsNullOrEmpty(draft.Reason)) content.AddChild(SWConsoleStyle.Wrap(draft.Reason, SWConsoleStyle.Warn));
            var issue = SWConsoleStyle.Primary(draft.Cost > 0 ? "Рассчитать приказ" : "Отдать приказ", true);
            issue.Name = "EmpireMilitaryIssue";
            issue.Disabled = !draft.Available || _state.Sphere != SWEmpireCouncilSphere.MilitaryOffense || _submitted;
            issue.ToolTip = !draft.Available ? draft.Reason : _state.Sphere != SWEmpireCouncilSphere.MilitaryOffense ? "Приказы отдаёт военная сфера" : string.Empty;
            issue.OnPressed += _ => Request(draft);
            content.AddChild(issue); card.AddChild(content); orderCard.AddChild(card);
        }
        _details.AddChild(selectors);
        if (_state.Sphere != SWEmpireCouncilSphere.MilitaryOffense)
            _details.AddChild(SWConsoleStyle.Text("Приказы отдаёт военная сфера", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        if (_pending != null && _quoteId == _state.ResultRequestId && _state.MilitaryQuote is { } quote)
        {
            if (!string.IsNullOrEmpty(quote.Reason)) orderCard.AddChild(SWConsoleStyle.Wrap(quote.Reason, SWConsoleStyle.Warn));
            var confirm = SWConsoleStyle.Primary($"Подтвердить  {quote.Cost:N0} кр", true);
            confirm.Disabled = !quote.Allowed || _submitted || _state.Sphere != SWEmpireCouncilSphere.MilitaryOffense;
            confirm.OnPressed += _ => { if (_pending == null || _pending.Action != _draftAction || _pending.Target != _target || _quoteId != _state.ResultRequestId || !_state.MilitaryQuote.Allowed || _state.Sphere != SWEmpireCouncilSphere.MilitaryOffense || !_state.Military.Options.Any(o => o.FleetId == _pending.FleetId && o.Target == _pending.Target && o.Action == _pending.Action && o.Available)) return; _submitted = true; _commandId = _pending.RequestId; _owner.SubmitMilitary(_pending); Refresh(); };
            orderCard.AddChild(confirm);
        }
        _details.AddChild(stats);
        foreach (var observation in _state.Military.Observations.Where(o => o.SectorId == _target))
            _details.AddChild(SWConsoleStyle.Wrap(observation.Current ? observation.Summary : $"{observation.Summary}\nСведения на {observation.SeenHours:0.0} ч", SWConsoleStyle.TextDim));
    }

    private void Request(SWEmpireMilitaryOptionView option)
    {
        if (_state?.Sphere != SWEmpireCouncilSphere.MilitaryOffense || _draftAction != option.Action || !_state.Military.Options.Any(o => o.FleetId == option.FleetId && o.Target == option.Target && o.Action == option.Action && o.Available) || (option.Action != SWEmpireMilitaryAction.Recruit && option.FleetId != _selected || option.Action == SWEmpireMilitaryAction.Recruit && option.FleetId != 0) || option.Target != _target) return;
        _result = string.Empty;
        var request = new SWEmpireMilitaryRequest { FleetId = option.FleetId, Target = option.Target, Action = option.Action, RequestId = Guid.NewGuid().ToString() };
        InvalidateQuote();
        if (option.Cost <= 0) { _submitted = true; _commandId = request.RequestId; _owner.SubmitMilitary(request); }
        else { _pending = request; _quoteId = request.RequestId; _owner.QuoteMilitary(request); }
        Refresh();
    }

    private static string ActionName(SWEmpireMilitaryAction action) => action switch
    {
        SWEmpireMilitaryAction.Move => "Переместить", SWEmpireMilitaryAction.Guard => "Охранять",
        SWEmpireMilitaryAction.Patrol => "Патрулировать", SWEmpireMilitaryAction.Recon => "Разведка",
        SWEmpireMilitaryAction.Escort => "Сопровождение", SWEmpireMilitaryAction.BorderConflict => "Пограничный бой",
        SWEmpireMilitaryAction.Raid => "Рейд", SWEmpireMilitaryAction.Blockade => "Блокада",
        SWEmpireMilitaryAction.Return => "Вернуться", SWEmpireMilitaryAction.Repair => "Ремонт",
        SWEmpireMilitaryAction.Train => "Подготовка", SWEmpireMilitaryAction.Recruit => "Сформировать", _ => "Приказ",
    };
}
