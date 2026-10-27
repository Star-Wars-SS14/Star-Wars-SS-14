using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Ministries;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._SW._Senate.Consoles.MinistryWork;

/// <summary>A bounded workspace: persistent world selection and one task at a time.</summary>
public sealed partial class SWHealthNetworkPanel : BoxContainer
{
    private readonly OptionButton _worlds = new() { Name = "HealthCareWorld", Filterable = true, HorizontalExpand = true };
    private readonly Label _budget = SWConsoleStyle.Text("", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly WrapContainer _metrics = new() { HorizontalExpand = true, SeparationOverride = 18, CrossSeparationOverride = 2 };
    private readonly BoxContainer _host = Stack();
    private readonly SWHealthCarePanel _care = new();
    private readonly SWHealthNetworkHelpPanel _help = new(standalone: true);
    private readonly BoxContainer _development = Stack(), _condition = Stack();
    private readonly List<Button> _tabs = new();
    private readonly List<Control> _pages = new();
    private SWHealthView _health = new();
    public string SelectedWorldId { get; private set; } = "";
    public int SelectedSection { get; private set; }
    public event Action<string>? WorldSelected;
    public event Action<SWHealthActionMessage>? ActionRequested;
    public event Action? ResearchRequested;

    public SWHealthNetworkPanel()
    {
        Name = "HealthNetworkWorkspace";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        SeparationOverride = 8;
        var toolbar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 12 };
        toolbar.AddChild(_worlds);
        toolbar.AddChild(_budget);
        AddChild(toolbar);
        AddChild(_metrics);
        var navigation = new WrapContainer { HorizontalExpand = true, SeparationOverride = 4, CrossSeparationOverride = 2 };
        var titles = new[] { "Лечение", "Развитие", "Снабжение", "Состояние", "Справка" };
        for (var i = 0; i < titles.Length; i++)
        {
            var section = i;
            var tab = SWConsoleStyle.TabButton(titles[i]);
            tab.Name = "HealthNetworkTab" + i;
            tab.OnPressed += _ => SelectSection(section);
            navigation.AddChild(tab);
            _tabs.Add(tab);
        }
        AddChild(navigation);
        _host.VerticalExpand = true;
        AddChild(_host);
        AddPage(_care, "HealthTreatmentScroll");
        AddPage(_development, "HealthDevelopmentScroll");
        _pages.Add(BuildSupplyWorkspace());
        _host.AddChild(_pages[2]);
        AddPage(_condition, "HealthConditionScroll");
        AddPage(_help, "HealthHelpScroll");
        _worlds.OnItemSelected += args =>
        {
            if (_worlds.GetItemMetadata(_worlds.GetIdx(args.Id)) is string id) SelectWorld(id);
        };
        _care.ActionRequested += message => ActionRequested?.Invoke(message);
        _care.OrderSelected += id => OpenSupply("care:" + id);
        SelectSection(0);
    }

    private static BoxContainer Stack() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    private static ScrollContainer Scroll(Control content, string name) 
    {
        var scroll = new ScrollContainer { Name = name, HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(content);
        return scroll;
    }
    private void AddPage(Control content, string name)
    {
        var page = Scroll(content, name);
        _pages.Add(page);
        _host.AddChild(page);
    }
    public void SetState(SWHealthView health, string? worldId = null)
    {
        _health = health;
        if (worldId != null && health.Worlds.Any(w => w.Id == worldId)) SelectedWorldId = worldId;
        if (!health.Worlds.Any(w => w.Id == SelectedWorldId)) SelectedWorldId = health.Worlds.FirstOrDefault()?.Id ?? "";
        RefreshWorldPicker();
        Refresh();
    }
    public void SelectWorld(string id)
    {
        if (!_health.Worlds.Any(w => w.Id == id)) return;
        SelectedWorldId = id;
        _selectedSupply = "";
        RefreshWorldPicker();
        Refresh();
        WorldSelected?.Invoke(id);
    }
    public void SelectSection(int section)
    {
        if (section < 0 || section >= _pages.Count) return;
        SelectedSection = section;
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == section;
            SWConsoleStyle.StyleTabButton(_tabs[i], i == section);
        }
        Refresh();
    }
    private void RefreshWorldPicker()
    {
        // Preserve the open filtered menu. Its entries retain world IDs even when the
        // next publication reorders the catalogue or another control changes selection.
        if (_worlds.OptionsScroll.Parent?.Parent != null) return;
        var catalogue = _health.Worlds.OrderByDescending(w => _health.Care.Orders.Count(o => o.WorldId == w.Id && !o.Completed))
            .ThenByDescending(w => _health.Care.Needs.Where(n => n.WorldId == w.Id).Sum(n => Math.Max(0, n.WaitingCourses - n.ReservedCourses)))
            .ThenBy(w => w.Name).ToArray();
        _worlds.Clear();
        for (var i = 0; i < catalogue.Length; i++)
        {
            var world = catalogue[i];
            var orders = _health.Care.Orders.Count(o => o.WorldId == world.Id && !o.Completed);
            var queue = _health.Care.Needs.Where(n => n.WorldId == world.Id).Sum(n => Math.Max(0, n.WaitingCourses - n.ReservedCourses));
            _worlds.AddItem(world.Name + (orders > 0 ? $"   Лечение: {orders}" : "") + (queue >= 1 ? $"   Очередь: {queue:0.#}" : ""), i);
            _worlds.SetItemMetadata(i, world.Id);
            if (world.Id == SelectedWorldId) _worlds.SelectId(i);
        }
        _worlds.Disabled = catalogue.Length == 0;
    }
    private void Refresh()
    {
        _budget.Text = $"{_health.Budget:N0} кр";
        _budget.ToolTip = "Бюджет Министерства здравоохранения";
        _metrics.RemoveAllChildren();
        if (_health.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId) is { } world)
        {
            _metrics.AddChild(SWConsoleStyle.Text($"Заражение {world.Infection:0.#}%", world.Infection > 1 ? SWConsoleStyle.Warn : SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            _metrics.AddChild(SWConsoleStyle.Text($"Иммунитет {world.Immunity:0.#}%", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            _metrics.AddChild(SWConsoleStyle.Text($"Охват {world.CareCoverage:0.#}%", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
            if (world.Quarantined) _metrics.AddChild(SWConsoleStyle.Text("Карантин", SWConsoleStyle.Warn, SWConsoleStyle.Small));
        }
        switch (SelectedSection)
        {
            case 0: _care.SetState(_health, SelectedWorldId); break;
            case 1: BuildDevelopment(); break;
            case 2: RefreshSupply(); break;
            case 3: BuildCondition(); break;
            case 4: _help.SetState(_health.Care); break;
        }
    }
}
