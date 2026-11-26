using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>One addressed quarantine request and a bounded, independently selected request list.</summary>
public sealed class SWHealthQuarantinePanel : BoxContainer
{
    private SWHealthView _health = new();
    private readonly OptionButton _worlds = new() { Name = "HealthQuarantineWorld", Filterable = true, HorizontalExpand = true };
    private readonly List<string> _worldIds = new();
    private string _worldRevision = "";
    private readonly SWConsoleButton _worldScope = Toggle("Мир", "HealthQuarantineScopeWorld");
    private readonly SWConsoleButton _systemScope = Toggle("Система", "HealthQuarantineScopeSystem");
    private readonly SWConsoleButton _sectorScope = Toggle("Весь сектор", "HealthQuarantineScopeSector");
    private readonly SWConsoleButton _corridor = Toggle("Коридор открыт", "HealthQuarantineCorridor");
    private readonly Dictionary<int, SWConsoleButton> _durations = new();
    private readonly RichTextLabel _zone = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
    private readonly RichTextLabel _stock = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly RichTextLabel _consequence = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly Button _request = SWConsoleStyle.Primary("Запросить карантин");
    private readonly Label _listTitle = SWConsoleStyle.Text("", SWConsoleStyle.Brass, SWConsoleStyle.Small);
    private readonly BoxContainer _list = Stack();
    private readonly RichTextLabel _details = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
    private readonly Button _lift = SWConsoleStyle.Action("Снять карантин", SWConsoleStyle.Warn);
    private readonly ScrollContainer _detailsScroll;
    private readonly ScrollContainer _listScroll;
    private string _scope = "";
    private bool _humanitarian = true;
    private bool _explicitQuarantineSelection;
    private int _hours = 2;
    public string SelectedWorldId { get; private set; } = "";
    public int? SelectedQuarantineId { get; private set; }
    public event Action<string>? WorldSelected;
    public event Action<SWHealthActionMessage>? ActionRequested;

    public SWHealthQuarantinePanel()
    {
        Name = "HealthQuarantinePanel";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 4;
        AddChild(SWConsoleStyle.Section("Карантин"));
        AddChild(_worlds);
        var scope = Row(); scope.AddChild(_worldScope); scope.AddChild(_systemScope); scope.AddChild(_sectorScope); AddChild(scope);
        var options = Row();
        foreach (var hours in new[] { 2, 4, 6 })
        {
            var value = hours;
            var button = Toggle($"{hours} ч", "HealthQuarantineHours" + hours);
            button.OnPressed += _ => { _hours = value; RefreshForm(); };
            _durations.Add(hours, button); options.AddChild(button);
        }
        options.AddChild(_corridor); AddChild(options);
        AddChild(_zone); AddChild(_stock); AddChild(_consequence);
        _request.Name = "HealthQuarantineRequest"; _request.HorizontalExpand = true; AddChild(_request);
        AddChild(_listTitle);
        _listScroll = new ScrollContainer { Name = "HealthQuarantineListScroll", HScrollEnabled = false, HorizontalExpand = true, MinHeight = 70, MaxHeight = 90 };
        _listScroll.AddChild(_list); AddChild(_listScroll);
        _detailsScroll = new ScrollContainer { Name = "HealthQuarantineDetailsScroll", HScrollEnabled = false, HorizontalExpand = true, MinHeight = 40, MaxHeight = 72 };
        _detailsScroll.AddChild(_details); AddChild(_detailsScroll);
        _lift.Name = "HealthQuarantineLift"; _lift.HorizontalExpand = true; AddChild(_lift);
        _worlds.OnItemSelected += args =>
        {
            if (args.Id < 0 || args.Id >= _worldIds.Count) return;
            SelectedWorldId = _health.Worlds.Any(w => w.Id == _worldIds[args.Id]) ? _worldIds[args.Id] : _health.Worlds.FirstOrDefault()?.Id ?? "";
            if (!_explicitQuarantineSelection) SelectedQuarantineId = null;
            RefreshPicker(); RefreshForm(); RefreshList();
            WorldSelected?.Invoke(SelectedWorldId);
        };
        _worldScope.OnPressed += _ => { _scope = ""; RefreshForm(); };
        _systemScope.OnPressed += _ => { _scope = "system"; RefreshForm(); };
        _sectorScope.OnPressed += _ => { _scope = "sector"; RefreshForm(); };
        _corridor.OnPressed += _ => { _humanitarian = !_humanitarian; RefreshForm(); };
        _request.OnPressed += _ => ActionRequested?.Invoke(new(SWHealthAction.RequestQuarantine, SelectedWorldId, _scope, _hours, _humanitarian));
        _lift.OnPressed += _ =>
        {
            if (SelectedQuarantineId is { } id) ActionRequested?.Invoke(new(SWHealthAction.LiftQuarantine, value: id));
        };
    }
    private static SWConsoleButton Toggle(string text, string name) => new() { Text = text, Name = name, ToggleMode = true, HorizontalExpand = true };
    private static BoxContainer Row() => new() { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 4 };
    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
    public void SetState(SWHealthView health, string? worldId = null)
    {
        _health = health;
        if (worldId != null && health.Worlds.Any(w => w.Id == worldId))
        {
            if (worldId != SelectedWorldId && !_explicitQuarantineSelection) SelectedQuarantineId = null;
            SelectedWorldId = worldId;
        }
        if (!health.Worlds.Any(w => w.Id == SelectedWorldId)) SelectedWorldId = health.Worlds.FirstOrDefault()?.Id ?? "";
        RefreshPicker(); RefreshForm(); RefreshList();
    }
    private void RefreshPicker()
    {
        // Option ids belong to this immutable open menu; server publications must not replace them underneath a filter.
        if (_worlds.OptionsScroll.Parent?.Parent != null) return;
        var signature = string.Join(";", _health.Worlds.Select(w => w.Id + ":" + w.Name));
        if (_worldRevision != signature || _worldIds.Count == 0)
        {
            _worldRevision = signature;
            _worldIds.Clear(); _worlds.Clear();
            for (var i = 0; i < _health.Worlds.Count; i++) { _worldIds.Add(_health.Worlds[i].Id); _worlds.AddItem(_health.Worlds[i].Name, i); }
        }
        var index = _worldIds.IndexOf(SelectedWorldId);
        if (index >= 0) _worlds.SelectId(index);
        _worlds.Disabled = _worldIds.Count == 0;
    }
    private void RefreshForm()
    {
        var world = _health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId);
        var hasSystem = world != null && !string.IsNullOrEmpty(world.SystemId) && _health.Worlds.Count(w => w.SystemId == world.SystemId) > 1;
        var hasSector = world != null && !string.IsNullOrEmpty(world.SectorId);
        if (_scope == "system" && !hasSystem || _scope == "sector" && !hasSector) _scope = "";
        _worldScope.Pressed = _scope == ""; _worldScope.SetAppearance(SWConsoleStyle.Accent, selected: _worldScope.Pressed);
        _systemScope.Visible = hasSystem; _systemScope.Disabled = !hasSystem;
        _systemScope.Pressed = _scope == "system"; _systemScope.ToolTip = world?.SystemName;
        _systemScope.SetAppearance(SWConsoleStyle.Accent, selected: _systemScope.Pressed);
        _sectorScope.Disabled = !hasSector; _sectorScope.Pressed = _scope == "sector";
        _sectorScope.ToolTip = world?.SectorName;
        _sectorScope.SetAppearance(SWConsoleStyle.Accent, selected: _sectorScope.Pressed);
        foreach (var (hours, button) in _durations) { button.Pressed = hours == _hours; button.SetAppearance(SWConsoleStyle.Accent, selected: hours == _hours); }
        _corridor.Pressed = _humanitarian; _corridor.Text = _humanitarian ? "Коридор открыт" : "Коридор закрыт";
        _corridor.SetAppearance(SWConsoleStyle.Accent, selected: _humanitarian);
        var zone = world == null ? Array.Empty<SWHealthWorldView>() : _scope switch
        {
            "system" => _health.Worlds.Where(w => w.SystemId == world.SystemId).ToArray(),
            "sector" => _health.Worlds.Where(w => w.SectorId == world.SectorId).ToArray(),
            _ => new[] { world },
        };
        var area = _scope == "sector" ? "Сектор: " + world?.SectorName : _scope == "system" ? world?.SystemName : world?.Name;
        _zone.SetMessage(zone.Length == 0 ? "Выберите мир" : $"{area}. Миров: {zone.Length}. Минимальный медицинский охват: {zone.Min(w => w.CareCoverage):0}%.");
        _stock.SetMessage(zone.Length == 0 ? "" : $"Минимальный запас пищи: {StockHours(zone.Select(w => w.FoodHours))}; лекарств: {StockHours(zone.Select(w => w.MedicineHours))}.");
        _consequence.SetMessage(_humanitarian ? "Обычные перевозки остановятся. Входящие пища и лекарства разрешены." : "Обычные перевозки остановятся. Конвои Минздрава проходят.");
        if (zone.Length > 1) _consequence.SetMessage("Внешние перевозки остановятся, внутренние сохранятся. " + (_humanitarian ? "Входящие пища и лекарства разрешены. " : "") + "Конвои Минздрава проходят.");
        var blocked = zone.Any(w => _health.Quarantines.Any(q => q.WorldIds.Contains(w.Id)));
        _request.Disabled = !_health.CanManage || !_health.MinisterActive || world == null || blocked || _health.Quarantines.Count >= 12;
        _request.ToolTip = !_health.CanManage || !_health.MinisterActive ? "Управление недоступно"
            : blocked ? "Сначала снимите или отзовите карантин внутри выбранной зоны"
            : _health.Quarantines.Count >= 12 ? "Лимит карантинных заявок" : null;
    }
    private static string StockHours(IEnumerable<double> values)
    {
        var hours = values.ToArray();
        return hours.Length == 0 || hours.Any(h => h < 0) ? "нет данных" : $"{hours.Min():0.#} ч";
    }
    private void RefreshList()
    {
        if (SelectedQuarantineId == null || !_health.Quarantines.Any(q => q.Id == SelectedQuarantineId))
        {
            _explicitQuarantineSelection = false;
            SelectedQuarantineId = _health.Quarantines.OrderByDescending(q => q.WorldIds.Contains(SelectedWorldId)).ThenBy(q => q.Pending).FirstOrDefault()?.Id;
        }
        _listTitle.Text = $"Активные и ожидающие карантины: {_health.Quarantines.Count}";
        _listScroll.Visible = _health.Quarantines.Count > 0;
        _list.RemoveAllChildren();
        foreach (var quarantine in _health.Quarantines.OrderByDescending(q => q.WorldIds.Contains(SelectedWorldId)).ThenBy(q => q.Pending))
        {
            var id = quarantine.Id;
            var button = Toggle($"{quarantine.Label}: {(quarantine.Pending ? "ожидает решения" : $"активен, {quarantine.HoursLeft:0.#} ч")}", "HealthQuarantineItem" + id);
            button.Pressed = id == SelectedQuarantineId; button.SetAppearance(quarantine.Pending ? SWConsoleStyle.Warn : SWConsoleStyle.Good, selected: button.Pressed);
            button.OnPressed += _ => { SelectedQuarantineId = id; _explicitQuarantineSelection = true; RefreshList(); };
            _list.AddChild(button);
        }
        var selected = _health.Quarantines.FirstOrDefault(q => q.Id == SelectedQuarantineId);
        _detailsScroll.Visible = _lift.Visible = selected != null;
        if (selected == null) { _details.SetMessage(""); return; }
        _details.SetMessage($"{selected.Label}. Миров: {selected.WorldIds.Count}. {(selected.Humanitarian ? "Коридор открыт." : "Коридор закрыт.")}\n{selected.Consequences}");
        _lift.Text = selected.Pending ? "Отозвать запрос" : "Снять карантин";
        _lift.Disabled = !_health.CanManage || !_health.MinisterActive;
    }
}
