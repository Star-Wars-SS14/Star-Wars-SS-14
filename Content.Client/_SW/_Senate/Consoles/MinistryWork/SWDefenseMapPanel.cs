using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Statistics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>A military atlas keeps its camera, target and draft order while live telemetry changes.</summary>
public sealed class SWDefenseMapPanel : BoxContainer
{
    private readonly SWGalacticMapPrototype _source;
    private readonly BoxContainer _mapHost = Stack();
    private readonly WrapContainer _fleetList = new() { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
    private readonly BoxContainer _details = Stack();
    private readonly Label _title = SWConsoleStyle.Text("Галактика", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Button _galaxy = SWConsoleStyle.TabButton("Галактика");
    private readonly Label _fleetName = SWConsoleStyle.Text("Флот не выбран", SWConsoleStyle.Accent, SWConsoleStyle.Strong);
    private readonly RichTextLabel _fleetStatus = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly Label _fleetStats = SWConsoleStyle.Text("", SWConsoleStyle.TextBright, SWConsoleStyle.Small);
    private readonly RichTextLabel _fleetEta = SWConsoleStyle.Wrap("", SWConsoleStyle.Warn);
    private readonly RichTextLabel _target = SWConsoleStyle.Wrap("Выберите сектор на карте", SWConsoleStyle.TextBright);
    private readonly Label _stages = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _cost = SWConsoleStyle.Text("", SWConsoleStyle.Brass, SWConsoleStyle.Small);
    private readonly RichTextLabel _risk = SWConsoleStyle.Wrap("", SWConsoleStyle.Warn);
    private readonly RichTextLabel _reason = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly RichTextLabel _coverage = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly RichTextLabel _yardStatus = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly BoxContainer _selectedDetails = Stack();
    private readonly BoxContainer _taskDetails = Stack();
    private readonly BoxContainer _yardDetails = Stack();
    private readonly WrapContainer _tasks = new() { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 4 };
    private readonly Dictionary<SWDefenseFleetOrder, Button> _taskButtons = new();
    private readonly RichTextLabel _selectionHint = SWConsoleStyle.Wrap("Выберите флот на карте или в списке", SWConsoleStyle.TextDim);
    private readonly Button _openSector = SWConsoleStyle.TabButton("Открыть сектор");
    private readonly Button _locate = SWConsoleStyle.TabButton("К флоту");
    private readonly Button _supplies = SWConsoleStyle.TabButton("Поставки");
    private readonly Button _issue = SWConsoleStyle.Primary("Выберите флот", true);
    private readonly Button _recruit = SWConsoleStyle.Action("Сформировать флот", SWConsoleStyle.Good, true);
    private readonly Dictionary<int, (Button Button, Label Status, BoxContainer Row)> _fleetRows = new();
    private readonly Label _empty = SWConsoleStyle.Text("Флоты отсутствуют", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private SWDefenseMapState? _state;
    private SWDefenseOrderOptionView? _preview;
    private string? _targetWorld;
    private string? _targetEdge;
    private int? _eventId;
    private SWDefenseFleetOrder _draftOrder = SWDefenseFleetOrder.Move;
    private bool _submitted;
    public SWGalacticMapControl Map { get; private set; } = default!;
    public SWGalacticMapPrototype View { get; private set; } = default!;
    public int? SelectedFleetId { get; private set; }
    public string? CurrentSectorId { get; private set; }
    public string? TargetWorldId => _targetWorld;
    public event Action<int, SWDefenseFleetOrder, string, int>? OrderRequested;
    public event Action<string>? RecruitRequested;

    public SWDefenseMapPanel()
    {
        Name = "DefenseAtlas";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        SeparationOverride = 8;
        _source = IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic");
        var toolbar = new WrapContainer { SeparationOverride = 6, CrossSeparationOverride = 4, HorizontalExpand = true };
        _galaxy.OnPressed += _ => ShowGalaxy();
        toolbar.AddChild(_galaxy);
        toolbar.AddChild(_title);
        var minus = SWConsoleStyle.TabButton("−");
        minus.ToolTip = "Отдалить";
        minus.OnPressed += _ => Map.ChangeZoom(1 / 1.2f);
        toolbar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+");
        plus.ToolTip = "Приблизить";
        plus.OnPressed += _ => Map.ChangeZoom(1.2f);
        toolbar.AddChild(plus);
        toolbar.AddChild(_zoom);
        var reset = SWConsoleStyle.TabButton("Сбросить масштаб");
        reset.OnPressed += _ => Map.ResetView();
        toolbar.AddChild(reset);
        _openSector.OnPressed += _ => { if (TargetSector() is { } sector) OpenSector(sector); };
        toolbar.AddChild(_openSector);
        _locate.OnPressed += _ => LocateFleet();
        toolbar.AddChild(_locate);
        _supplies.Name = "DefenseSupplyToggle";
        _supplies.ToggleMode = true;
        _supplies.Pressed = true;
        _supplies.ToolTip = "Поставки снабжения, ремонта и верфей";
        SWConsoleStyle.StyleTabButton(_supplies, true);
        _supplies.OnToggled += args => { Map.SetDefenseSupplyVisible(args.Pressed); SWConsoleStyle.StyleTabButton(_supplies, args.Pressed); };
        toolbar.AddChild(_supplies);
        AddChild(toolbar);
        AddChild(_fleetList);
        _fleetList.AddChild(_empty);

        var scroll = new ScrollContainer { Name = "DefenseOrdersScroll", HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        _details.AddChild(_selectionHint);
        _details.AddChild(_selectedDetails);
        _selectedDetails.AddChild(_fleetName);
        _selectedDetails.AddChild(_fleetEta);
        var orderCard = SWConsoleStyle.Row(SWConsoleStyle.Navigation);
        var orderContent = Stack();
        orderContent.AddChild(_target);
        orderContent.AddChild(_stages);
        orderContent.AddChild(_cost);
        _issue.Name = "DefenseIssueOrder";
        _issue.Label.ClipText = true;
        orderContent.AddChild(_issue);
        orderContent.AddChild(_reason);
        orderCard.AddChild(orderContent);
        _selectedDetails.AddChild(orderCard);
        _selectedDetails.AddChild(SWConsoleStyle.Section("Задача по прибытии"));
        _selectedDetails.AddChild(_tasks);
        foreach (var order in new[] { SWDefenseFleetOrder.Move, SWDefenseFleetOrder.Guard, SWDefenseFleetOrder.Patrol,
            SWDefenseFleetOrder.Border, SWDefenseFleetOrder.Return, SWDefenseFleetOrder.Repair, SWDefenseFleetOrder.Train })
        {
            var value = order;
            var button = SWConsoleStyle.TabButton(TaskName(value));
            button.Name = $"DefenseTask{value}";
            button.OnPressed += _ => ChooseTask(value);
            _taskButtons.Add(value, button);
            _tasks.AddChild(button);
        }
        _selectedDetails.AddChild(_taskDetails);
        _taskDetails.AddChild(_coverage);
        _risk.ToolTip = "Оценка риска приказа";
        _taskDetails.AddChild(_risk);
        _selectedDetails.AddChild(SWConsoleStyle.Section("Состояние соединения"));
        _selectedDetails.AddChild(_fleetStats);
        _selectedDetails.AddChild(_fleetStatus);
        _issue.OnPressed += _ => SubmitOrder();
        _details.AddChild(_yardDetails);
        _yardDetails.AddChild(SWConsoleStyle.Section("Верфь"));
        _yardDetails.AddChild(_yardStatus);
        _recruit.OnPressed += _ => { if (_targetWorld != null) RecruitRequested?.Invoke(_targetWorld); _recruit.Disabled = true; };
        _yardDetails.AddChild(_recruit);
        scroll.AddChild(_details);
        _mapHost.VerticalExpand = true;
        AddChild(new SWAtlasLayout(_mapHost, scroll, stackBelow: 520, minDetails: 230, maxDetails: 330));
        SwitchView(null);
    }

    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };

    public void SetState(SWDefenseMapState? state)
    {
        _state = state;
        _submitted = false;
        var active = state?.Fleets.Where(f => f.Phase != SWDefenseOperationPhase.Lost).ToArray() ?? Array.Empty<SWDefenseFleetView>();
        var ids = active.Select(f => f.Id).ToHashSet();
        foreach (var id in _fleetRows.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _fleetRows[id].Row.Orphan();
            _fleetRows.Remove(id);
        }
        if (SelectedFleetId != null && !ids.Contains(SelectedFleetId.Value)) SelectedFleetId = null;
        foreach (var fleet in active)
        {
            if (!_fleetRows.TryGetValue(fleet.Id, out var row))
            {
                var id = fleet.Id;
                var box = Stack();
                box.SetWidth = 124;
                box.HorizontalExpand = false;
                var button = SWConsoleStyle.Action($"Флот {fleet.Id}", SWConsoleStyle.Accent);
                button.Name = $"DefenseFleet{id}";
                button.Label.ClipText = true;
                button.OnPressed += _ => SelectFleet(id);
                var status = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
                status.ClipText = true;
                box.AddChild(button);
                box.AddChild(status);
                _fleetList.AddChild(box);
                _fleetRows[id] = row = (button, status, box);
            }
            row.Button.Text = fleet.Name;
            row.Button.Disabled = fleet.Phase == SWDefenseOperationPhase.Lost;
            row.Status.Text = fleet.Phase == SWDefenseOperationPhase.Travel ? $"К {WorldName(fleet.TargetWorldId)}" : WorldName(fleet.CurrentWorldId);
            row.Status.FontColorOverride = fleet.Phase == SWDefenseOperationPhase.Travel ? SWConsoleStyle.Accent : SWConsoleStyle.TextDim;
            row.Status.ToolTip = fleet.Status;
            row.Button.ToolTip = $"{fleet.Name}\n{row.Status.Text}\n{fleet.Status}";
            SWConsoleStyle.StyleTabButton(row.Button, SelectedFleetId == fleet.Id);
        }
        _empty.Visible = ids.Count == 0;
        Map.SetDefense(state, SelectedFleetId);
        RefreshDetails();
    }

    public void SelectFleet(int id, bool locate = true)
    {
        if (_state?.Fleets.All(f => f.Id != id || f.Phase == SWDefenseOperationPhase.Lost) != false) return;
        SelectedFleetId = id;
        _submitted = false;
        if (_draftOrder == SWDefenseFleetOrder.Return)
            _targetWorld = _state!.Fleets.First(f => f.Id == id).HomeWorldId;
        Map.SetDefense(_state, id);
        foreach (var (fleetId, row) in _fleetRows) SWConsoleStyle.StyleTabButton(row.Button, fleetId == id);
        if (locate) LocateFleet();
        RefreshDetails();
    }

    public void SelectEvent(int id)
    {
        var incident = _state?.Events.FirstOrDefault(e => e.Id == id);
        if (incident == null) return;
        SelectWorld(incident.WorldId);
        _submitted = false;
        _draftOrder = SWDefenseFleetOrder.Respond;
        _eventId = id;
        _targetEdge = string.IsNullOrEmpty(incident.EdgeId) ? null : incident.EdgeId;
        RefreshDetails();
    }

    public void SelectWorld(string id)
    {
        var world = _source.Worlds.FirstOrDefault(w => w.Id == id);
        if (world == null) return;
        if (!View.Worlds.Any(w => w.Id == id)) SwitchView(world.Sector?.Id);
        _targetWorld = id;
        _targetEdge = null;
        _eventId = null;
        _submitted = false;
        _draftOrder = SWDefenseFleetOrder.Move;
        Map.SelectWorld(id);
        RefreshDetails();
    }

    public void ChooseTask(SWDefenseFleetOrder order)
    {
        _draftOrder = order;
        _submitted = false;
        _eventId = null;
        if (order != SWDefenseFleetOrder.Patrol) _targetEdge = null;
        var fleet = _state?.Fleets.FirstOrDefault(f => f.Id == SelectedFleetId);
        if (fleet != null)
        {
            if (order == SWDefenseFleetOrder.Return) _targetWorld = fleet.HomeWorldId;
            else if (_targetWorld == null && order is SWDefenseFleetOrder.Train or SWDefenseFleetOrder.Guard)
                _targetWorld = fleet.CurrentWorldId;
        }
        RefreshDetails();
    }

    public void SubmitOrder()
    {
        if (_submitted || _issue.Disabled || _preview is not { Available: true } preview || SelectedFleetId is not { } id) return;
        _submitted = true;
        OrderRequested?.Invoke(id, preview.Order, preview.Target, preview.EventId);
        RefreshDetails();
    }

    private string? TargetSector() => _source.Worlds.FirstOrDefault(w => w.Id == _targetWorld)?.Sector?.Id;

    private void LocateFleet()
    {
        if (SelectedFleetId is not { } id) return;
        if (Map.DefenseFleetPosition(id) == null) ShowGalaxy();
        Map.FocusDefenseFleet(id);
    }

    public void OpenSector(string sectorId) => SwitchView(sectorId);
    public void ShowGalaxy() => SwitchView(null);

    private void SwitchView(string? sectorId)
    {
        if (Map != null && sectorId == CurrentSectorId) return;
        var previous = Map;
        var throughSector = sectorId ?? CurrentSectorId;
        var anchor = _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == throughSector)?.Id;
        CurrentSectorId = sectorId;
        View = sectorId == null ? SWGalacticMapViews.Overview(_source) : SWGalacticMapViews.Sector(_source, sectorId);
        _mapHost.RemoveAllChildren();
        Map = new SWGalacticMapControl(View, sectorId);
        Map.MinHeight = 260;
        Map.SetDefenseSupplyVisible(_supplies.Pressed);
        Map.SetDefense(_state, SelectedFleetId);
        Map.WorldSelected += SelectWorld;
        Map.WorldActivated += id => { var world = _source.Worlds.First(w => w.Id == id); if (world.Sector is { } sector) { OpenSector(sector.Id); SelectWorld(id); } };
        Map.RouteSelected += route =>
        {
            _targetEdge = SWTransitLinkPrototype.LinkId(route.From, route.To);
            _targetWorld = route.To;
            _eventId = null;
            _submitted = false;
            _draftOrder = SWDefenseFleetOrder.Patrol;
            RefreshDetails();
        };
        Map.DefenseFleetSelected += id => SelectFleet(id, false);
        Map.DefenseEventSelected += SelectEvent;
        Map.ViewChanged += () => _zoom.Text = $"{Map.Zoom * 100:0}%";
        _mapHost.AddChild(Map);
        _title.Text = sectorId == null ? "Галактика" : _source.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sectorId)?.Name ?? sectorId;
        _galaxy.Visible = sectorId != null;
        _zoom.Text = "100%";
        if (_targetWorld != null) Map.SelectWorld(_targetWorld);
        if (previous != null && anchor != null) Map.TransitionFrom(previous, anchor, sectorId != null);
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var fleet = _state?.Fleets.FirstOrDefault(f => f.Id == SelectedFleetId);
        _selectedDetails.Visible = fleet != null;
        _selectionHint.Visible = fleet == null;
        _locate.Disabled = fleet == null;
        _openSector.Disabled = TargetSector() == null || TargetSector() == CurrentSectorId;
        _fleetName.Text = fleet?.Name ?? "Флот не выбран";
        _fleetName.ClipText = true;
        SetWrappedText(_fleetStatus, fleet == null ? "" : $"{PhaseName(fleet.Phase)}   {WorldName(fleet.CurrentWorldId)}\n{fleet.Status}", SWConsoleStyle.TextDim);
        _fleetStats.Text = fleet == null ? "" : $"Сила {fleet.Strength:0}   Урон {fleet.Damage:0}\nСнабжение {fleet.Supply:0}   Подготовка {fleet.Training:0}";
        SetWrappedText(_fleetEta, FleetLocation(fleet), SWConsoleStyle.Accent);
        var edge = _state?.Links.FirstOrDefault(l => l.Id == _targetEdge);
        var incident = _state?.Events.FirstOrDefault(e => e.Id == _eventId);
        var targetText = incident != null ? incident.Title + "\n" + WorldName(_targetWorld)
            : edge != null ? $"{edge.RouteName}\n{WorldName(edge.From)} — {WorldName(edge.To)}"
            : _targetWorld == null ? "Куда отправить флот?" : $"Назначение: {WorldName(_targetWorld)}";
        SetWrappedText(_target, targetText, SWConsoleStyle.TextBright);
        var borders = _state?.Borders.Where(b => b.RepublicWorldId == _targetWorld || b.ForeignWorldId == _targetWorld || b.Id == _targetEdge).ToArray();
        SetWrappedText(_coverage, borders is { Length: > 0 } ? string.Join("\n\n", borders.Select(BorderSummary)) : "", SWConsoleStyle.TextDim);
        _preview = _state?.OrderOptions.FirstOrDefault(o => o.FleetId == SelectedFleetId && o.Order == _draftOrder
            && (_draftOrder == SWDefenseFleetOrder.Return || (_draftOrder == SWDefenseFleetOrder.Patrol && _targetEdge != null
                ? o.Target == _targetEdge : o.Target == _targetWorld || o.Target == _targetEdge
                  || _draftOrder == SWDefenseFleetOrder.Border && borders?.Any(b => b.Id == o.Target) == true))
            && (_draftOrder != SWDefenseFleetOrder.Respond || o.EventId == _eventId));
        _stages.Text = _preview == null ? "" : $"Прибытие через {SWSenateDuration.FormatHours(_preview.PreparationHours + _preview.TravelHours)}" +
            (_preview.ExecutionHours > 0 ? $"\nРаботы: {SWSenateDuration.FormatHours(_preview.ExecutionHours)}" : "");
        _cost.Text = _preview == null ? "" : _preview.Cost == 0 ? "Бесплатно" : $"Стоимость: {_preview.Cost:N0} кр";
        _risk.Visible = _draftOrder == SWDefenseFleetOrder.Respond || fleet is { Damage: >= 50 } or { Supply: < 30 };
        SetWrappedText(_risk, _preview?.Risk ?? fleet?.Risk ?? "", SWConsoleStyle.Warn);
        var reason = _preview?.Reason ?? (_targetWorld == null ? "Выберите сектор или мир на карте" : "Для этой цели приказ недоступен");
        SetWrappedText(_reason, _submitted ? "" : reason, SWConsoleStyle.TextDim);
        _reason.Visible = !_submitted && !string.IsNullOrEmpty(reason);
        _issue.Text = _submitted ? "Приказ отправлен" : IssueCaption(fleet);
        _issue.ToolTip = _issue.Text;
        _issue.Disabled = _submitted || _preview?.Available != true || _state?.MinisterActive != true;
        foreach (var (order, button) in _taskButtons)
        {
            SWConsoleStyle.StyleTabButton(button, _draftOrder == order);
            var availableTarget = order == SWDefenseFleetOrder.Return || _targetWorld != null;
            button.Visible = order is SWDefenseFleetOrder.Move or SWDefenseFleetOrder.Return
                || order == SWDefenseFleetOrder.Border && borders is { Length: > 0 }
                || order == SWDefenseFleetOrder.Repair && _state?.Shipyards.Any(y => y.WorldId == _targetWorld) == true
                || order is SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol or SWDefenseFleetOrder.Train;
            button.Disabled = fleet == null || !availableTarget && order is not (SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Train);
        }
        var resolvedWorld = !string.IsNullOrEmpty(_preview?.ResolvedWorldId) ? _preview.ResolvedWorldId : _targetWorld;
        if (resolvedWorld != null && resolvedWorld != _targetWorld)
            SetWrappedText(_target, edge == null ? $"Назначение: {WorldName(resolvedWorld)}"
                : targetText + $"\nПозиция: {WorldName(resolvedWorld)}", SWConsoleStyle.TextBright);
        Map.SetDefenseDestination(resolvedWorld, _draftOrder);
        var yard = _state?.Shipyards.FirstOrDefault(y => y.WorldId == _targetWorld);
        _yardDetails.Visible = yard != null;
        SetWrappedText(_yardStatus, yard?.Status ?? "", SWConsoleStyle.TextDim);
        _recruit.Text = yard == null ? "Выберите мир с верфью" : $"Сформировать флот: {_state!.RecruitmentCost} кр";
        _recruit.ToolTip = yard?.Status;
        _recruit.Disabled = yard == null || yard.Busy || _state?.MinisterActive != true || _state.Budget < _state.RecruitmentCost;
    }

    private string FleetLocation(SWDefenseFleetView? fleet)
    {
        if (fleet == null) return "";
        var now = _state?.NowHours ?? 0;
        if (fleet.Phase is SWDefenseOperationPhase.Preparation or SWDefenseOperationPhase.Travel)
        {
            var text = $"К {WorldName(fleet.TargetWorldId)}\nПрибытие через {SWSenateDuration.FormatHours(Math.Max(0, fleet.DestinationEtaHours - now))}";
            if (fleet.NextWorldId is { } next)
            {
                var link = _state?.Links.FirstOrDefault(l => l.From == fleet.CurrentWorldId && l.To == next || l.To == fleet.CurrentWorldId && l.From == next);
                text += $"\n{link?.RouteName ?? "Переход"}: {WorldName(fleet.CurrentWorldId)} — {WorldName(next)}";
            }
            return text;
        }
        return $"На позиции: {WorldName(fleet.CurrentWorldId)}" + (fleet.Order == SWDefenseFleetOrder.Move ? "" : $"\n{OrderName(fleet.Order)}");
    }

    private string IssueCaption(SWDefenseFleetView? fleet)
    {
        if (fleet == null) return "Выберите флот";
        if (_targetWorld == null && _draftOrder != SWDefenseFleetOrder.Return) return "Выберите назначение";
        return _draftOrder switch
        {
            SWDefenseFleetOrder.Move => $"Отправить в {WorldName(_targetWorld)}",
            SWDefenseFleetOrder.Return => "Вернуть на базу",
            SWDefenseFleetOrder.Guard => "Отправить на охрану",
            SWDefenseFleetOrder.Patrol => "Начать патрулирование",
            SWDefenseFleetOrder.Border => "Прикрыть переход",
            SWDefenseFleetOrder.Train => "Начать учения",
            SWDefenseFleetOrder.Repair => "Отправить в ремонт",
            _ => "Отправить на операцию",
        };
    }

    private string WorldName(string? id) => id == null ? "" : _source.Worlds.FirstOrDefault(w => w.Id == id)?.Name ?? id;
    private string BorderSummary(SWDefenseBorderView border)
    {
        var stationed = _state!.Fleets.Where(f => f.CurrentWorldId == border.RepublicWorldId
            && f.Phase == SWDefenseOperationPhase.Standing).Select(f => f.Name).ToArray();
        var incidents = _state.Events.Where(e => !e.Resolved && (e.EdgeId == border.Id
            || e.WorldId == border.RepublicWorldId || e.WorldId == border.ForeignWorldId)).Select(e => e.Title).ToArray();
        var situation = incidents.Length > 0 ? string.Join("; ", incidents)
            : border.Coverage > 0 && border.Coverage >= border.Pressure ? "Спокойно, переход прикрыт"
            : border.Pressure >= 50 ? "Напряжённо, прикрытие недостаточно"
            : border.Coverage > 0 ? "Событий нет, прикрытие недостаточно" : "Событий нет, переход без прикрытия";
        return $"Переход к {WorldName(border.ForeignWorldId)}\n{(stationed.Length > 0 ? string.Join(", ", stationed) : "Флота на позиции нет")}\n{situation}";
    }
    private static void SetWrappedText(RichTextLabel label, string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
    }
    public static string OrderName(SWDefenseFleetOrder order) => order switch
    {
        SWDefenseFleetOrder.Move => "Перемещение", SWDefenseFleetOrder.Guard => "Охрана мира", SWDefenseFleetOrder.Patrol => "Патрулирование",
        SWDefenseFleetOrder.Border => "Прикрытие границы", SWDefenseFleetOrder.Return => "Возврат и снабжение",
        SWDefenseFleetOrder.Repair => "Ремонт на верфи", SWDefenseFleetOrder.Train => "Учения",
        _ => "Ответ на событие",
    };
    private static string TaskName(SWDefenseFleetOrder order) => order switch
    {
        SWDefenseFleetOrder.Move => "Перемещение", SWDefenseFleetOrder.Guard => "Охрана", SWDefenseFleetOrder.Patrol => "Патруль",
        SWDefenseFleetOrder.Border => "Граница", SWDefenseFleetOrder.Return => "На базу",
        SWDefenseFleetOrder.Repair => "Ремонт", SWDefenseFleetOrder.Train => "Учения", _ => OrderName(order),
    };
    public static string PhaseName(SWDefenseOperationPhase phase) => phase switch
    {
        SWDefenseOperationPhase.Preparation => "Подготовка", SWDefenseOperationPhase.Travel => "Переход",
        SWDefenseOperationPhase.Execution => "Исполнение", SWDefenseOperationPhase.Recovery => "Восстановление",
        SWDefenseOperationPhase.Lost => "Потерян", _ => "На позиции",
    };
}
