using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>World defense projects share the senator program slots and physical material progress.</summary>
public sealed class SWDefensePlanetaryPanel : BoxContainer
{
    private readonly OptionButton _worldPicker = new() { Name = "DefenseWorldPicker", Filterable = true, MinWidth = 240, HorizontalExpand = true };
    private readonly Button _onMap = SWConsoleStyle.Action("На карте", SWConsoleStyle.Accent, true);
    private readonly Label _readOnly = SWConsoleStyle.Text("Только просмотр", SWConsoleStyle.Warn, SWConsoleStyle.Small);
    private readonly Label _needsCount = SWConsoleStyle.Text("", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Label _emptyNeeds = SWConsoleStyle.Text("Запросов нет", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly BoxContainer _needs = Stack();
    private readonly BoxContainer _programs = Stack();
    private readonly BoxContainer _worldDetails = Stack();
    private readonly RichTextLabel _worldTitle = SWConsoleStyle.Wrap("Выберите мир", SWConsoleStyle.TextBright);
    private readonly RichTextLabel _worldInfo = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly RichTextLabel _readiness = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
    private readonly ProgressBar _garrison = SWConsoleStyle.Bar(0, 0, 1, SWConsoleStyle.Good);
    private readonly ProgressBar _orbital = SWConsoleStyle.Bar(0, 0, 1, SWConsoleStyle.Accent);
    private readonly Label _garrisonTitle = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _orbitalTitle = SWConsoleStyle.Text("", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly RichTextLabel _reserve = SWConsoleStyle.Wrap("", SWConsoleStyle.Brass);
    private readonly RichTextLabel _reserveStatus = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
    private readonly Button _resupply = SWConsoleStyle.Action("Заказать резерв", SWConsoleStyle.Accent, true);
    private readonly Dictionary<string, NeedRow> _needRows = new();
    private readonly Dictionary<string, ProgramCard> _programCards = new();
    private SWDefensePlanetaryState? _state;
    private string[] _worldIds = Array.Empty<string>();
    private string? _catalog;
    private bool _canManage;
    public string? SelectedWorldId { get; private set; }
    public event Action<string, string>? ProgramRequested;
    public event Action<string>? ResupplyRequested;
    public event Action<string>? WorldMapRequested;

    public SWDefensePlanetaryPanel()
    {
        Name = "DefensePlanetaryWorkspace";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        SeparationOverride = 8;
        var toolbar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 8, CrossSeparationOverride = 4 };
        toolbar.AddChild(_worldPicker);
        _onMap.Name = "DefenseWorldOnMap";
        toolbar.AddChild(_onMap);
        toolbar.AddChild(_readOnly);
        AddChild(toolbar);
        _worldPicker.OnItemSelected += args => { if (args.Id >= 0 && args.Id < _worldIds.Length) SelectWorld(_worldIds[args.Id]); };
        _onMap.OnPressed += _ => { if (SelectedWorldId is { } id) WorldMapRequested?.Invoke(id); };

        var navigation = Stack();
        navigation.AddChild(_needsCount);
        var needsScroll = new ScrollContainer { Name = "DefenseWorldNeedsScroll", HScrollEnabled = false,
            HorizontalExpand = true, VerticalExpand = true, MinHeight = 100 };
        _needs.AddChild(_emptyNeeds);
        needsScroll.AddChild(_needs);
        navigation.AddChild(needsScroll);
        navigation.VerticalExpand = true;
        var detail = new ScrollContainer { Name = "DefenseWorldDetailsScroll", HScrollEnabled = false,
            HorizontalExpand = true, VerticalExpand = true };
        _worldDetails.AddChild(_worldTitle);
        _worldDetails.AddChild(_worldInfo);
        _worldDetails.AddChild(_readiness);
        _worldDetails.AddChild(_garrisonTitle);
        _worldDetails.AddChild(_garrison);
        _worldDetails.AddChild(_orbitalTitle);
        _worldDetails.AddChild(_orbital);
        _worldDetails.AddChild(SWConsoleStyle.Section("Материальный резерв"));
        _worldDetails.AddChild(_reserve);
        _worldDetails.AddChild(_reserveStatus);
        _resupply.Name = "DefenseWorldResupply";
        _resupply.Label.ClipText = true;
        _resupply.OnPressed += _ => Resupply();
        _worldDetails.AddChild(_resupply);
        _worldDetails.AddChild(_programs);
        detail.AddChild(_worldDetails);
        AddChild(new SWConsoleColumns(navigation, detail, 230, 600, fixedFirst: true) { VerticalExpand = true });
    }

    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };

    public void SetState(SWDefensePlanetaryState? state, bool activeContext = true)
    {
        _state = state;
        _canManage = state?.CanManage == true && activeContext;
        _readOnly.Visible = !_canManage;
        var worlds = state?.Worlds.OrderBy(w => w.Name).ToArray() ?? Array.Empty<SWDefensePlanetaryWorldView>();
        var catalog = string.Join(";", worlds.Select(w => $"{w.WorldId}:{w.Name}:{w.SectorName}"));
        if (_catalog != catalog)
        {
            _catalog = catalog;
            _worldIds = worlds.Select(w => w.WorldId).ToArray();
            _worldPicker.Clear();
            for (var i = 0; i < worlds.Length; i++) _worldPicker.AddItem($"{worlds[i].Name} ({worlds[i].SectorName})", i);
            _worldPicker.Disabled = worlds.Length == 0;
        }
        if (!worlds.Any(w => w.WorldId == SelectedWorldId)) SelectedWorldId = worlds.FirstOrDefault()?.WorldId;
        var selected = Array.IndexOf(_worldIds, SelectedWorldId);
        if (selected >= 0) _worldPicker.SelectId(selected);
        RefreshNeeds();
        RefreshWorld();
    }

    public bool SelectWorld(string id)
    {
        if (_state?.Worlds.Any(w => w.WorldId == id) != true) return false;
        SelectedWorldId = id;
        _worldPicker.SelectId(Array.IndexOf(_worldIds, id));
        RefreshNeeds();
        RefreshWorld();
        return true;
    }

    public void Resupply()
    {
        if (_resupply.Disabled || SelectedWorldId is not { } id) return;
        _resupply.Disabled = true;
        ResupplyRequested?.Invoke(id);
    }

    public void StartProgram(string programId)
    {
        if (_programCards.TryGetValue(programId, out var card)) card.Start();
    }

    private void RefreshNeeds()
    {
        var requests = _state?.Requests.OrderByDescending(r => r.Priority).ThenBy(r => r.Id).ToArray() ?? Array.Empty<SWDefenseSupportRequestView>();
        var ids = requests.Select(r => r.Id).ToHashSet();
        foreach (var id in _needRows.Keys.Where(id => !ids.Contains(id)).ToArray()) { _needRows[id].Panel.Orphan(); _needRows.Remove(id); }
        _needsCount.Text = $"Запросы губернаторов: {requests.Length}";
        _emptyNeeds.Visible = requests.Length == 0;
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            if (!_needRows.TryGetValue(request.Id, out var row))
            {
                var id = request.Id;
                row = new NeedRow();
                row.Select.OnPressed += _ =>
                {
                    var current = _state?.Requests.FirstOrDefault(r => r.Id == id);
                    if (current != null) SelectWorld(current.WorldId);
                };
                _needRows.Add(id, row);
                _needs.AddChild(row.Panel);
            }
            row.Select.Text = request.Title;
            row.Select.ToolTip = request.Title;
            SWConsoleStyle.StyleTabButton(row.Select, SelectedWorldId == request.WorldId);
            var world = _state?.Worlds.FirstOrDefault(w => w.WorldId == request.WorldId)?.Name ?? request.WorldId;
            Wrap(row.Context, $"{world}\n{request.Governor}", SWConsoleStyle.Brass);
            Wrap(row.Reason, request.Reason + (string.IsNullOrEmpty(request.Status) ? "" : "\n" + request.Status), SWConsoleStyle.TextDim);
            row.Panel.SetPositionInParent(i + 1);
        }
    }

    private void RefreshWorld()
    {
        var world = _state?.Worlds.FirstOrDefault(w => w.WorldId == SelectedWorldId);
        _worldDetails.Visible = world != null;
        _onMap.Disabled = world == null;
        if (world == null) return;
        Wrap(_worldTitle, world.Name, SWConsoleStyle.TextBright);
        Wrap(_worldInfo, $"{(string.IsNullOrEmpty(world.Governor) ? "Губернатор не назначен" : world.Governor)}" +
            (string.IsNullOrEmpty(world.ProtectedSite) ? "" : $"\nЗащищаемый объект: {world.ProtectedSite}"), SWConsoleStyle.TextDim);
        _readiness.Visible = world.ActiveThreats > 0;
        Wrap(_readiness, $"Активных угроз: {world.ActiveThreats}", SWConsoleStyle.Warn);
        _garrisonTitle.Text = $"Гарнизон: {world.GarrisonReadiness:P0}";
        _orbitalTitle.Text = $"Орбитальная оборона: {world.OrbitalReadiness:P0}";
        _garrison.Value = Math.Clamp(world.GarrisonReadiness, 0, 1);
        _orbital.Value = Math.Clamp(world.OrbitalReadiness, 0, 1);
        var ids = world.Programs.Select(p => p.Id).ToHashSet();
        foreach (var id in _programCards.Keys.Where(id => !ids.Contains(id)).ToArray()) { _programCards[id].Orphan(); _programCards.Remove(id); }
        for (var i = 0; i < world.Programs.Count; i++)
        {
            var program = world.Programs[i];
            if (!_programCards.TryGetValue(program.Id, out var card))
            {
                card = new ProgramCard(program.Id, () =>
                {
                    if (SelectedWorldId is { } selected) ProgramRequested?.Invoke(selected, program.Id);
                });
                _programCards.Add(program.Id, card);
                _programs.AddChild(card);
            }
            card.Update(world.WorldId, program, _canManage && _state!.Budget >= program.Cost);
            card.SetPositionInParent(i);
        }
        Wrap(_reserve, $"Запас обороны: {world.ReserveHours:0.##} ч\nСтоимость комплекта: {world.ResupplyCost:N0} кр", SWConsoleStyle.Brass);
        Wrap(_reserveStatus, world.ResupplyStatus, SWConsoleStyle.TextDim);
        _reserveStatus.Visible = !string.IsNullOrWhiteSpace(world.ResupplyStatus);
        _resupply.Text = "Заказать материальный резерв";
        _resupply.Disabled = !_canManage || !world.CanResupply || _state!.Budget < world.ResupplyCost;
    }

    private static void Wrap(RichTextLabel label, string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        label.SetMessage(message);
    }

    private sealed class NeedRow
    {
        public readonly BoxContainer Panel = Stack();
        public readonly Button Select = SWConsoleStyle.Action("", SWConsoleStyle.Accent);
        public readonly RichTextLabel Context = SWConsoleStyle.Wrap("", SWConsoleStyle.Brass);
        public readonly RichTextLabel Reason = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
        public NeedRow()
        {
            Select.Label.ClipText = true;
            Panel.AddChild(Select);
            Panel.AddChild(Context);
            Panel.AddChild(Reason);
        }
    }

    private sealed class ProgramCard : PanelContainer
    {
        private readonly RichTextLabel _title = SWConsoleStyle.Wrap("", SWConsoleStyle.TextBright);
        private readonly RichTextLabel _role = SWConsoleStyle.Wrap("", SWConsoleStyle.Accent);
        private readonly RichTextLabel _description = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
        private readonly RichTextLabel _location = SWConsoleStyle.Wrap("", SWConsoleStyle.Brass);
        private readonly RichTextLabel _status = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
        private readonly RichTextLabel _work = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
        private readonly BoxContainer _materials = Stack();
        private readonly Dictionary<string, RichTextLabel> _materialRows = new();
        private readonly ProgressBar _supplyBar = SWConsoleStyle.Bar(0, 0, 1, SWConsoleStyle.Accent);
        private readonly ProgressBar _workBar = SWConsoleStyle.Bar(0, 0, 1, SWConsoleStyle.Brass);
        private readonly Button _start = SWConsoleStyle.Action("Начать строительство", SWConsoleStyle.Accent, true);
        private readonly Action _requested;

        public ProgramCard(string id, Action requested)
        {
            Name = $"DefenseWorldProgram_{id}";
            HorizontalExpand = true;
            _requested = requested;
            var row = SWConsoleStyle.Row();
            var body = Stack();
            body.AddChild(_title);
            body.AddChild(_role);
            body.AddChild(_description);
            body.AddChild(_location);
            body.AddChild(_status);
            body.AddChild(_materials);
            body.AddChild(_supplyBar);
            body.AddChild(_work);
            body.AddChild(_workBar);
            _start.Name = $"DefenseWorldStart_{id}";
            _start.Label.ClipText = true;
            _start.OnPressed += _ => Start();
            body.AddChild(_start);
            row.AddChild(body);
            AddChild(row);
        }

        public void Start()
        {
            if (_start.Disabled) return;
            _start.Disabled = true;
            _requested();
        }

        public void Update(string selectedWorld, SWProgramEntry program, bool canManage)
        {
            var placed = program.Started || program.Applied;
            var elsewhere = placed && program.WorldId != selectedWorld;
            Wrap(_title, program.Name, SWConsoleStyle.TextBright);
            var role = program.Id switch
            {
                "SWProgramGarrison" => "Сдерживает местные нападения",
                "SWProgramShield" => "Снижает потери и перебои у порта",
                _ => "",
            };
            Wrap(_role, role, SWConsoleStyle.Accent);
            _role.Visible = !elsewhere && role.Length > 0;
            _role.ToolTip = program.Id == "SWProgramGarrison"
                ? "При полной готовности местный ущерб от беспорядков и вторжений снижается на 30%. Подавляет слабые угрозы."
                : "При полной готовности ущерб миру и перебои у порта снижаются на 20%. Полную блокаду не снимает.";
            Wrap(_description, program.Description, SWConsoleStyle.TextDim);
            _description.Visible = !elsewhere && !string.IsNullOrWhiteSpace(program.Description);
            var location = string.IsNullOrEmpty(program.WorldName) ? program.WorldId : program.WorldName;
            Wrap(_location, placed ? $"{(program.Applied ? "Построено" : "Строительство")}: {location}" : $"Стоимость строительства: {program.Cost:N0} кр", SWConsoleStyle.Brass);
            var status = program.Status;
            var locationPrefix = $"Размещено на {location}: ";
            if (elsewhere && status.StartsWith(locationPrefix, StringComparison.Ordinal)) status = status[locationPrefix.Length..];
            Wrap(_status, (elsewhere ? "Только просмотр\n" : "") + status, program.Applied ? SWConsoleStyle.Good : SWConsoleStyle.TextDim);
            _status.Visible = elsewhere || !string.IsNullOrWhiteSpace(status);
            _materials.Visible = !elsewhere;
            var ids = program.Materials.Select(m => m.GoodId).ToHashSet();
            foreach (var id in _materialRows.Keys.Where(id => !ids.Contains(id)).ToArray()) { _materialRows[id].Orphan(); _materialRows.Remove(id); }
            for (var i = 0; i < program.Materials.Count; i++)
            {
                var material = program.Materials[i];
                if (!_materialRows.TryGetValue(material.GoodId, out var label))
                {
                    label = SWConsoleStyle.Wrap("", SWConsoleStyle.TextDim);
                    _materialRows.Add(material.GoodId, label);
                    _materials.AddChild(label);
                }
                Wrap(label, $"{material.Name}: {material.Delivered:0.##} / {material.Required:0.##}" +
                    (material.InTransit > 0 ? $"; в пути {material.InTransit:0.##}" : ""), SWConsoleStyle.TextDim);
                label.SetPositionInParent(i);
            }
            _supplyBar.Visible = !elsewhere && placed && program.Materials.Count > 0;
            _supplyBar.Value = program.Materials.Count == 0 ? 0 : program.Materials.Average(m => m.Required > 0 ? Math.Clamp(m.Delivered / m.Required, 0, 1) : 1);
            Wrap(_work, placed ? $"Работы: {program.WorkedHours:0.##} / {program.WorkHours:0.##} ч" : $"Срок работ: {program.WorkHours:0.##} ч", SWConsoleStyle.TextDim);
            _work.Visible = !elsewhere;
            _workBar.Visible = !elsewhere && placed && program.WorkHours > 0;
            _workBar.Value = program.WorkHours > 0 ? Math.Clamp(program.WorkedHours / program.WorkHours, 0, 1) : 0;
            _start.Text = program.Applied ? "Построено" : program.Started ? "Строительство начато" : "Начать строительство";
            _start.Disabled = !canManage || !program.Affordable || placed;
            _start.Visible = !elsewhere;
        }
    }
}
