using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Client._SW._Senate.Economy;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Economy;

/// <summary>A navigable production atlas. Catalogue animation and live economic rates stay distinct.</summary>
public sealed partial class SWRepublicCommodityBrowser : BoxContainer
{
    private readonly IPrototypeManager _prototypes;
    private readonly SWCommodityPrototype[] _goods;
    private readonly Dictionary<string, int> _indices;
    private readonly SWProductionRecipePrototype[] _recipes;
    private readonly SWProductionGraphBuilder _builder;
    private readonly Dictionary<string, string> _selectedRecipes = new();
    private readonly Stack<string> _history = new();
    private readonly OptionButton _picker = new() { Name = "CommodityPicker", Filterable = true, MinWidth = 220, MaxWidth = 350 };
    private readonly OptionButton _recipePicker = new() { Name = "CommodityRecipeSelector", MinWidth = 220, MaxWidth = 420 };
    private readonly Button _back = SWConsoleStyle.TabButton("Назад");
    private readonly Button _animation = SWConsoleStyle.TabButton("Движение");
    private readonly Button[] _modes;
    private readonly Label _status = SWConsoleStyle.Text(string.Empty, SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly BoxContainer _detail = Stack(6);
    private readonly Dictionary<string, string> _worldNames;
    private SWProductionRecipePrototype[] _variants = Array.Empty<SWProductionRecipePrototype>();
    private SWRepublicEconomyClientSystem? _economyClient;
    private SWEconomyView? _economy;
    private SWProductionNode? _selectedNode;
    private bool _animate = true;
    private string? _selected;

    public SWProductionGraphControl Graph { get; } = new();
    public SWProductionGraphMode Mode { get; private set; } = SWProductionGraphMode.Chain;
    public string? SelectedCommodityId => _selected;
    public string? InspectedCommodityId => _selectedNode?.Kind == SWProductionNodeKind.Commodity
        ? _selectedNode.ReferenceId : _selected;
    public string? SelectedRecipeId => _selected is not { } id ? null
        : IsRangeView ? EndpointRecipe(id) ?? _rangeRecipes.GetValueOrDefault(id) : RecipeFor(id)?.ID;
    public event Action<string>? WorldRequested;

    public SWRepublicCommodityBrowser()
    {
        Name = "CommodityBrowser";
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        SeparationOverride = 8;
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _goods = _prototypes.EnumeratePrototypes<SWCommodityPrototype>()
            .OrderBy(g => _prototypes.Index(g.Category).Order).ThenBy(g => g.Order).ThenBy(g => g.ID).ToArray();
        _indices = _goods.Select((good, index) => (good.ID, index)).ToDictionary(g => g.ID, g => g.index);
        _recipes = _prototypes.EnumeratePrototypes<SWProductionRecipePrototype>().OrderBy(r => r.Order).ThenBy(r => r.ID).ToArray();
        _builder = new SWProductionGraphBuilder(_prototypes);
        _worldNames = _prototypes.EnumeratePrototypes<SWGalacticMapPrototype>().SelectMany(m => m.Worlds)
            .GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First().Name);

        var toolbar = Wrap();
        _back.Name = "CommodityBack";
        _back.OnPressed += _ => NavigateBack();
        toolbar.AddChild(_back);
        StylePicker(_picker);
        for (var i = 0; i < _goods.Length; i++) _picker.AddItem(_goods[i].Name, i);
        _picker.OnItemSelected += args => SelectCommodity(_goods[args.Id].ID);
        toolbar.AddChild(_picker);
        StylePicker(_recipePicker);
        _recipePicker.OnItemSelected += args =>
        {
            if (args.Id >= 0) SelectRecipe(_variants[args.Id].ID);
            else if (_selected != null) { ActiveRecipes.Remove(_selected); RefreshRecipes(); RefreshGraph(); }
        };
        toolbar.AddChild(_recipePicker);
        AddChild(toolbar);

        var controls = Wrap();
        var names = new[] { "Цепочка", "Рецепт", "Применение" };
        var modes = new[] { SWProductionGraphMode.Chain, SWProductionGraphMode.Recipe, SWProductionGraphMode.Uses };
        _modes = new Button[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var mode = modes[i];
            var button = SWConsoleStyle.TabButton(names[i]);
            button.Name = $"CommodityMode_{mode}";
            button.OnPressed += _ => SetMode(mode);
            _modes[i] = button;
            controls.AddChild(button);
        }
        _rangeButton.OnPressed += _ => BeginRangePick(true);
        controls.AddChild(_rangeButton);
        var overview = SWConsoleStyle.TabButton("Обзор");
        overview.Name = "CommodityOverview";
        overview.OnPressed += _ => Graph.FitAll();
        controls.AddChild(overview);
        var focus = SWConsoleStyle.TabButton("К выбранному");
        focus.OnPressed += _ => Graph.FocusSelection();
        controls.AddChild(focus);
        _animation.Name = "CommodityAnimation";
        _animation.ToolTip = "Движение по связям производственной схемы";
        _animation.OnPressed += _ => SetAnimation(!_animate);
        controls.AddChild(_animation);
        AddChild(controls);
        InitializeRangeControls();

        _detail.Name = "CommodityDetail";
        var inspector = new ScrollContainer
        {
            Name = "CommodityInspector", HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false,
        };
        inspector.AddChild(_detail);
        Graph.Name = "ProductionGraph";
        Graph.NodeSelected += SelectGraphNode;
        Graph.NodeActivated += ActivateNode;
        AddChild(new SWProductionWorkspace(Graph, inspector));
        var legend = Wrap();
        legend.AddChild(SWConsoleStyle.Text("Товары", SWConsoleStyle.Accent, SWConsoleStyle.Small));
        legend.AddChild(SWConsoleStyle.Text("Производство", SWConsoleStyle.Brass, SWConsoleStyle.Small));
        legend.AddChild(SWConsoleStyle.Text("Потребители", SWConsoleStyle.Good, SWConsoleStyle.Small));
        _status.Margin = new Thickness(12, 0, 0, 0);
        legend.AddChild(_status);
        AddChild(legend);
        SetAnimation(true);
        if (_indices.ContainsKey("freighters")) SelectCommodity("freighters");
        else if (_goods.Length > 0) SelectCommodity(_goods[0].ID);
        SetEconomy(null);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _economyClient = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SWRepublicEconomyClientSystem>();
        _economyClient.Changed += EconomyChanged;
        SetEconomy(_economyClient.View);
        _economyClient.Request();
    }
    protected override void ExitedTree()
    {
        if (_economyClient != null) _economyClient.Changed -= EconomyChanged;
        _economyClient = null;
        base.ExitedTree();
    }
    private void EconomyChanged() => SetEconomy(_economyClient?.View);
    public void SetEconomy(SWEconomyView? view)
    {
        _economy = view;
        // A minute sample updates the inspector, never the graph's camera or navigation history.
        RefreshStatus();
        RefreshDetail();
    }

    public bool SelectCommodity(string id) => SelectCommodity(id, true);
    private bool SelectCommodity(string id, bool remember)
    {
        if (!_indices.TryGetValue(id, out var index)) return false;
        ResetRangeSelection();
        if (remember && _selected != null && _selected != id) _history.Push(_selected);
        _selected = id;
        _picker.SelectId(index);
        _back.Disabled = _history.Count == 0;
        RefreshRecipes();
        RefreshGraph();
        return true;
    }
    public bool NavigateBack() => _history.TryPop(out var id) && SelectCommodity(id, false);

    public bool SelectRecipe(string id)
    {
        if (_selected is not { } commodity || !_recipes.Any(r => r.ID == id && r.Outputs.ContainsKey(commodity))) return false;
        ActiveRecipes[commodity] = id;
        RefreshRecipes();
        RefreshGraph();
        return true;
    }
    public bool SelectConsumer(string commodityId, string ingredientId)
    {
        var selected = RecipeFor(commodityId);
        var recipe = selected != null && selected.Inputs.ContainsKey(ingredientId) ? selected
            : _recipes.FirstOrDefault(r => r.Outputs.ContainsKey(commodityId) && r.Inputs.ContainsKey(ingredientId));
        if (recipe == null) return false;
        _selectedRecipes[commodityId] = recipe.ID;
        return SelectCommodity(commodityId);
    }
    public void SetMode(SWProductionGraphMode mode)
    {
        if (mode is not (SWProductionGraphMode.Chain or SWProductionGraphMode.Recipe or SWProductionGraphMode.Uses)) return;
        ResetRangeSelection();
        Mode = mode;
        RefreshRecipes();
        RefreshGraph();
    }
    public void SetAnimation(bool enabled)
    {
        _animate = enabled;
        Graph.SetAnimation(enabled);
        SWConsoleStyle.StyleTabButton(_animation, enabled);
    }
    private SWProductionRecipePrototype? RecipeFor(string id)
    {
        var recipes = _recipes.Where(r => r.Outputs.ContainsKey(id));
        var remembered = IsRangeView ? EndpointRecipe(id) ?? _rangeRecipes.GetValueOrDefault(id)
            : _selectedRecipes.GetValueOrDefault(id);
        return remembered != null ? recipes.FirstOrDefault(r => r.ID == remembered) ?? recipes.FirstOrDefault() : recipes.FirstOrDefault();
    }
    private void RefreshRecipes()
    {
        _variants = _recipes.Where(r => r.Outputs.ContainsKey(_selected!)).ToArray();
        _recipePicker.Clear();
        if (IsRangeView) _recipePicker.AddItem("Все способы", -1);
        for (var i = 0; i < _variants.Length; i++) _recipePicker.AddItem(_variants[i].Name, i);
        _recipePicker.Visible = _variants.Length > 1 && (IsRangeView
            ? Graph.Graph?.Nodes.Any(n => n.Id == "good:" + _selected) == true && EndpointRecipe(_selected!) == null
            : Mode != SWProductionGraphMode.Uses);
        var index = Array.FindIndex(_variants, r => r.ID == SelectedRecipeId);
        if (index >= 0 || IsRangeView) _recipePicker.SelectId(index);
    }
    private void RefreshGraph()
    {
        if (_selected == null) return;
        for (var i = 0; i < _modes.Length; i++) SWConsoleStyle.StyleTabButton(_modes[i], !IsRangeView && (int) Mode == i);
        var data = IsRangeView ? _builder.BuildRange(RangeStartId!, RangeEndId!, RangeRecipesWithEndpoints())
            : _builder.Build(_selected, _selectedRecipes, Mode);
        Graph.SetGraph(data);
        Graph.SetEndpoints(RangeStartId, RangeEndId);
        if (IsRangeView)
        {
            Graph.FitAll();
            if (Graph.Zoom < .25f) Graph.ResetView();
        }
        _selectedNode = data.Nodes.FirstOrDefault(n => n.Id == data.FocusNodeId);
        Graph.SelectNode(data.FocusNodeId, false);
        RefreshRecipes();
        RefreshRangeControls();
        RefreshStatus();
        RefreshDetail();
    }
    private void ActivateNode(SWProductionNode node)
    {
        if (IsRangeView) { Graph.FocusSelection(); return; }
        if (node.Kind == SWProductionNodeKind.Commodity)
        {
            if (_selected != null && _recipes.Any(r => r.Outputs.ContainsKey(node.ReferenceId) && r.Inputs.ContainsKey(_selected)))
                SelectConsumer(node.ReferenceId, _selected);
            else SelectCommodity(node.ReferenceId);
        }
        else if (node.Kind == SWProductionNodeKind.Recipe)
        {
            var recipe = _prototypes.Index<SWProductionRecipePrototype>(node.ReferenceId);
            if (_selected != null && recipe.Outputs.ContainsKey(_selected)) SelectRecipe(recipe.ID);
            else if (recipe.Outputs.Keys.FirstOrDefault() is { } output && SelectCommodity(output.Id)) SelectRecipe(recipe.ID);
        }
    }
    private void RefreshDetail()
    {
        _detail.RemoveAllChildren();
        if (_selectedNode is not { } node) return;
        var heading = SWConsoleStyle.Wrap(node.Title, SWConsoleStyle.TextBright);
        heading.Name = "CommodityNodeTitle";
        _detail.AddChild(heading);
        if (node.Kind == SWProductionNodeKind.Commodity)
            CommodityDetail(_prototypes.Index<SWCommodityPrototype>(node.ReferenceId));
        else if (node.Kind == SWProductionNodeKind.Recipe)
            RecipeDetail(_prototypes.Index<SWProductionRecipePrototype>(node.ReferenceId));
        else if (node.Kind == SWProductionNodeKind.Demand)
            DemandDetail(_prototypes.Index<SWCommodityDemandPrototype>(node.ReferenceId));
        else
        {
            _detail.AddChild(SWConsoleStyle.Wrap(node.Subtitle, SWConsoleStyle.TextDim));
            _detail.AddChild(SWConsoleStyle.Section("Требуется для"));
            foreach (var recipe in _recipes.Where(r => r.Requirements.Any(c => c.Id == node.ReferenceId) || r.ResourceCosts.Keys.Any(c => c.Id == node.ReferenceId)))
                _detail.AddChild(Link(recipe.Name, () => { SelectCommodity(recipe.Outputs.Keys.First().Id); SelectRecipe(recipe.ID); }));
        }
    }
    private void CommodityDetail(SWCommodityPrototype good)
    {
        _detail.AddChild(SWConsoleStyle.Wrap(good.Description, SWConsoleStyle.TextDim));
        _detail.AddChild(SWConsoleStyle.Wrap(UseLabel(good.Use), SWConsoleStyle.Brass));
        if (good.ID != _selected || Mode != SWProductionGraphMode.Chain || IsRangeView)
            _detail.AddChild(Link("Открыть цепочку", () => { Mode = SWProductionGraphMode.Chain; SelectCommodity(good.ID); }));
        if (good.Regulation != SWCommodityRegulation.Ordinary)
            _detail.AddChild(SWConsoleStyle.Wrap(good.Regulation == SWCommodityRegulation.Licensed ? "Лицензируемый оборот" : "Оборот запрещён", SWConsoleStyle.Warn));
        if (_economy is not { Ready: true }) return;
        var worlds = _economy.Worlds.Select(w => (w.World, Good: w.Goods.FirstOrDefault(g => g.Good == good.ID)))
            .Where(w => w.Good != null).ToArray();
        _detail.AddChild(SWConsoleStyle.Section("Сейчас в Республике"));
        MeasureRow("Выпуск", $"{Number(worlds.Sum(w => w.Good!.ProducedPerHour))} ед./ч", SWConsoleStyle.Accent);
        MeasureRow("Расход", $"{Number(worlds.Sum(w => w.Good!.ConsumedPerHour))} ед./ч", SWConsoleStyle.TextBright);
        MeasureRow("В пути", $"{Number(worlds.Sum(w => w.Good!.Incoming))} ед.", SWConsoleStyle.Brass);
        var producers = worlds.Where(w => w.Good!.ProducedPerHour > 1e-8).OrderByDescending(w => w.Good!.ProducedPerHour).ToArray();
        if (producers.Length > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Выпускают"));
            foreach (var world in producers) WorldRow(world.World, $"{Number(world.Good!.ProducedPerHour)} ед./ч");
        }
        var consumers = worlds.Where(w => w.Good!.ConsumedPerHour > 1e-8).OrderByDescending(w => w.Good!.ConsumedPerHour).ToArray();
        if (consumers.Length > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Используют"));
            foreach (var world in consumers) WorldRow(world.World, $"{Number(world.Good!.ConsumedPerHour)} ед./ч");
        }
        var routes = _economy.Flows.Where(f => f.Good == good.ID && (f.InTransit > 1e-8 || f.UnitsPerHour > 1e-8))
            .OrderByDescending(f => f.UnitsPerHour).ToArray();
        if (routes.Length > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Перевозки"));
            foreach (var route in routes)
            {
                var title = $"{WorldName(route.From)} — {WorldName(route.To)}";
                _detail.AddChild(WorldRequested != null ? Link(title, () => WorldRequested?.Invoke(route.To)) : SWConsoleStyle.Wrap(title, SWConsoleStyle.TextBright));
                _detail.AddChild(SWConsoleStyle.Wrap($"{Number(route.UnitsPerHour)} ед./ч    В пути {Number(route.InTransit)}", SWConsoleStyle.Accent));
            }
        }
    }
    private void RecipeDetail(SWProductionRecipePrototype recipe)
    {
        _detail.AddChild(SWConsoleStyle.Wrap(_prototypes.Index(recipe.Facility).Name, SWConsoleStyle.Brass));
        MeasureRow("Цикл", $"{Number(recipe.Hours)} ч", SWConsoleStyle.Accent);
        MeasureRow("Мощность", Number(recipe.Power), SWConsoleStyle.TextBright);
        MeasureRow("Труд", Number(recipe.Labor), SWConsoleStyle.TextBright);
        if (recipe.Inputs.Count > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("На один цикл"));
            foreach (var (id, amount) in recipe.Inputs)
                _detail.AddChild(Link($"{_prototypes.Index(id).Name}: {Number(amount)} ед.", () => SelectCommodity(id.Id)));
        }
        _detail.AddChild(SWConsoleStyle.Section("Результат"));
        foreach (var (id, amount) in recipe.Outputs)
            _detail.AddChild(Link($"{_prototypes.Index(id).Name}: {Number(amount)} ед.", () => { SelectCommodity(id.Id); SelectRecipe(recipe.ID); }));
        var conditions = recipe.Requirements.Where(id => !recipe.ResourceCosts.ContainsKey(id)).ToArray();
        if (conditions.Length > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Условия размещения"));
            foreach (var id in conditions)
                _detail.AddChild(SWConsoleStyle.Wrap(_prototypes.Index(id).Name, SWConsoleStyle.TextDim));
        }
        if (recipe.ResourceCosts.Count > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Из местного фонда за цикл"));
            foreach (var (id, amount) in recipe.ResourceCosts)
                _detail.AddChild(SWConsoleStyle.Wrap($"{_prototypes.Index(id).Name}: {Number(amount)}", SWConsoleStyle.Brass));
        }
        if (_selected != null && recipe.Outputs.ContainsKey(_selected) && SelectedRecipeId != recipe.ID)
            _detail.AddChild(Link("Выбрать этот способ", () => SelectRecipe(recipe.ID)));
    }
    private void DemandDetail(SWCommodityDemandPrototype demand)
    {
        _detail.AddChild(SWConsoleStyle.Wrap(demand.Basis, SWConsoleStyle.TextDim));
        _detail.AddChild(SWConsoleStyle.Wrap(demand.Mode switch
        {
            SWCommodityDemandMode.Bundle => "Комплект снабжения", SWCommodityDemandMode.Commission => "Оснащение вводится в эксплуатацию",
            SWCommodityDemandMode.Derived => "Оценка уже потреблённых товаров", SWCommodityDemandMode.Substitute => "Взаимозаменяемые товары",
            _ => "Текущее потребление",
        }, SWConsoleStyle.Good));
        foreach (var (id, amount) in demand.Goods)
        {
            var unit = demand.Mode == SWCommodityDemandMode.Derived ? "Вес" : demand.Mode == SWCommodityDemandMode.Bundle ? "Доля комплекта" : "Коэффициент";
            var name = _prototypes.Index(id).Name;
            _detail.AddChild(Link(demand.Mode == SWCommodityDemandMode.Commission ? name : $"{name}   {unit}: {Number(amount)}", () => SelectCommodity(id.Id)));
        }
    }
    private void WorldRow(string id, string value)
    {
        if (WorldRequested != null) _detail.AddChild(Link($"{WorldName(id)}   {value}", () => WorldRequested?.Invoke(id)));
        else MeasureRow(WorldName(id), value, SWConsoleStyle.Accent);
    }
    private string WorldName(string id) => _worldNames.GetValueOrDefault(id, id);
    private void MeasureRow(string label, string value, Color color) => _detail.AddChild(new SWConsoleColumns(
        SWConsoleStyle.Wrap(label, SWConsoleStyle.TextDim), SWConsoleStyle.Wrap(value, color), 104, 0));
    private static SWConsoleButton Link(string text, Action action)
    {
        var button = new SWConsoleButton { HorizontalExpand = true };
        button.Label.Visible = false;
        button.SetAppearance(SWConsoleStyle.Accent, quiet: true);
        button.AddChild(SWConsoleStyle.Wrap(text, SWConsoleStyle.Accent));
        button.OnPressed += _ => action();
        return button;
    }
    private static BoxContainer Stack(int gap) => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = gap };
    private static WrapContainer Wrap() => new() { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4 };
    private static string Number(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));
    private static string UseLabel(SWCommodityUse use) => use switch
    {
        SWCommodityUse.Asset => "Ввод в строй", SWCommodityUse.Durable => "Долговечное оборудование",
        SWCommodityUse.Consumable => "Расходный товар", _ => "Производственное сырьё",
    };
    private static void StylePicker(OptionButton picker) => picker.StyleBoxOverride = new SWConsoleSurface
    {
        Background = SWConsoleStyle.PillDim, ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
        ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
    };
}
