using System;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Economy;
using Content.Client._SW._Senate.Economy;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    private SWEconomyView? _economyView;
    private SWRepublicEconomyClientSystem? _economyClient;
    private readonly OptionButton _commodityPicker = new() { Name = "TrafficCommodityFilter", Filterable = true, MinWidth = 210 };
    private readonly Button _trafficToggle = SWConsoleStyle.TabButton("Потоки");
    private readonly Label _economyStatus = SWConsoleStyle.Text("Ожидание данных", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly RichTextLabel _trafficLegend = SWStatisticsText.Wrap(string.Empty, SWConsoleStyle.TextDim);
    private readonly SWStatisticsDirectionMarker _selectedFlowDirection = new() { Visible = false };
    private SWCommodityPrototype[] _commodities = Array.Empty<SWCommodityPrototype>();
    private string? _commodityFilter;
    private bool _showTraffic = true;
    private int _detailPage;
    public string? CommodityFilter => _commodityFilter;
    public SWEconomyView? EconomyView => _economyView;

    private void InitializeEconomyToolbar()
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        _commodities = proto.EnumeratePrototypes<SWCommodityPrototype>().OrderBy(g => g.Order).ToArray();
        var bar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4, Visible = !_financeOnly };
        StylePicker(_commodityPicker);
        _commodityPicker.AddItem("Все товары", 0);
        for (var i = 0; i < _commodities.Length; i++) _commodityPicker.AddItem(_commodities[i].Name, i + 1);
        _commodityPicker.OnItemSelected += args => SelectCommodity(args.Id == 0 ? null : _commodities[args.Id - 1].ID);
        _trafficToggle.Name = "TrafficToggle";
        _trafficToggle.ToolTip = "Показать потоки грузов на карте";
        _trafficToggle.OnPressed += _ =>
        {
            _showTraffic = !_showTraffic;
            SWConsoleStyle.StyleTabButton(_trafficToggle, _showTraffic);
            Map.SetTrafficFilter(_commodityFilter, _showTraffic);
            Surface?.SetTrafficFilter(_commodityFilter, _showTraffic);
            RefreshEconomyStatus();
            if (_planetOpen) RefreshDetails();
        };
        SWConsoleStyle.StyleTabButton(_trafficToggle, true);
        bar.AddChild(_commodityPicker);
        bar.AddChild(_trafficToggle);
        if (_fullPlanetScreen) bar.AddChild(_selectedFlowDirection);
        _medicalToggle.Name = "MedicalSupplyToggle";
        _medicalToggle.Visible = !_compactEconomy && _showMedicalToggle;
        _medicalToggle.OnPressed += _ => SetMedicalSupplyVisible(!_showMedicalSupply);
        bar.AddChild(_medicalToggle);
        _economyStatus.Margin = new Thickness(8, 6);
        bar.AddChild(_economyStatus);
        AddChild(bar);
        if (_fullPlanetScreen) AddChild(_trafficLegend);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_financeOnly) return;
        _economyClient = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SWRepublicEconomyClientSystem>();
        _economyClient.Changed += OnEconomyChanged;
        SetEconomy(_economyClient.View);
        _economyClient.Request();
    }

    protected override void ExitedTree()
    {
        if (_economyClient != null) _economyClient.Changed -= OnEconomyChanged;
        _economyClient = null;
        base.ExitedTree();
    }

    private void OnEconomyChanged() => SetEconomy(_economyClient?.View);

    // Boxed primitive values retain ordinal string and numeric equality without culture or hash collisions.
    private sealed class SnapshotValues
    {
        public readonly System.Collections.Generic.List<object?> Values = new();
        public void Add(params object?[] values) => Values.AddRange(values);
        public void AddList<T>(System.Collections.Generic.IEnumerable<T>? items, Action<T> add)
        {
            if (items == null) { Add(-1); return; }
            var array = items.ToArray();
            Add(array.Length);
            foreach (var item in array) add(item);
        }
        public void Service(SWEconomyServiceView x) => Add(x.Group, x.RequiredPerHour, x.ServedPerHour);
        public void Good(SWEconomyGoodView x) => Add(x.Good, x.Stock, x.AvailableForProjects, x.Capacity, x.ProducedPerHour, x.ConsumedPerHour, x.RequiredPerHour, x.ImportedPerHour, x.ExportedPerHour, x.Incoming);
        public void Asset(SWEconomyAssetView x) => Add(x.Good, x.Installed, x.Operational, x.Target, x.Effect, x.EffectiveBonus);
        public void Order(SWEconomyOrderView x) => Add(x.Good, x.Ordered, x.Delivered, x.CompletedHour);
        public void Production(SWEconomyProductionView x) => Add(x.Recipe, x.CompletedCyclesPerHour, x.StartedCyclesPerHour, x.WorkInProgress, x.BaseCyclesPerHour, x.EffectiveCyclesPerHour);
        public void Flow(SWEconomyRouteFlow x) => Add(x.Edge, x.From, x.To, x.Good, x.UnitsPerHour, x.InTransit, x.TravelHours);
        public void LocalFlow(SWPlanetSurfaceFlow x) => Add(x.From, x.To, x.Good, x.UnitsPerHour);
        public void Medical(SWHealthSupplyFlowView x) => Add(x.Id, x.ProjectId, x.Kind, x.Payload, x.StrainId, x.TargetWorldId, x.EdgeId, x.From, x.To, x.Good, x.GoodName, x.Quantity, x.DepartAtHours, x.ArriveAtHours, x.DeliveryAtHours, x.FinalLeg, x.RouteBlocked);
        public void Diet(SWEconomyDietView? x) { Add(x != null); if (x != null) Add(x.Consumed, x.Quality); }
        public void Capacity(SWEconomyCapacityView? x) { Add(x != null); if (x != null) Add(x.ProductionMultiplier, x.LaborCapacity, x.StorageMultiplier, x.DomesticPower, x.ReactorCapacityPower, x.FleetCapacity, x.FleetOccupied); }
        public void Link(SWEconomyLinkView? x) { Add(x != null); if (x != null) Add(x.Edge, x.LoadedSlotsPerHour, x.EmptySlotsPerHour, x.CapacitySlotsPerHour); }
        public void Focused(SWEconomyRouteFlow? x) { Add(x != null); if (x != null) Flow(x); }
        public void Governor(SWGovernorView? x)
        {
            Add(x != null);
            if (x == null) return;
            Add(x.Sector, x.Name, x.Status, x.Priority, x.Project, x.World, x.WorldName, x.ProjectStatus, x.Budget, x.Reserve, x.RequestedCredits, x.ProjectProgress, x.LastDecisionHour, x.HasRequest);
            AddList(x.Traits, value => Add(value));
            AddList(x.History, value => Add(value));
        }
    }

    private System.Collections.Generic.List<object?> EconomyDetailsKey(SWEconomyView? view)
    {
        var world = SelectedWorldId;
        var sector = world != null && _worlds.TryGetValue(world, out var entry) ? entry.Sector?.Id : null;
        var edge = _route == null ? null : SWTransitLinkPrototype.LinkId(_route.From, _route.To);
        var live = view?.Worlds.FirstOrDefault(w => w.World == world);
        var surfaceControl = Surface;
        var selectedSite = surfaceControl?.SelectedSiteId;
        var site = surfaceControl?.Surface.Sites.FirstOrDefault(s => s.Id == selectedSite);
        var recipeIds = site?.Recipes.Select(r => r.Id).ToHashSet();
        var surface = surfaceControl != null;
        var needsSupply = surface ? site?.Kind == "settlement" : _detailPage == 0 && edge == null;
        var needsGoods = surface ? site?.Kind == "storage" : _detailPage == 1 && edge == null;
        var needsEquipment = !surface && _detailPage == 4 && edge == null;
        var needsFreight = edge != null || (surface ? site?.Kind is "port" or "settlement" : _detailPage is 0 or 2);
        var visibleFlows = view?.Flows.Where(f => (_commodityFilter == null || f.Good == _commodityFilter) &&
            (edge != null ? f.Edge == edge : world != null && (f.From == world || f.To == world))).ToArray();
        SWPlanetSurfaceFlow[]? localFlows = null;
        if (surfaceControl != null)
        {
            if (site != null) localFlows = surfaceControl.TrafficFlows.Where(f => f.From == site.Id || f.To == site.Id).ToArray();
            else if (surfaceControl.SelectedCell is { } cell && surfaceControl.RoadAt(cell) != null)
                localFlows = surfaceControl.FlowsAt(cell).ToArray();
        }
        var key = new SnapshotValues();
        key.Add(view != null, view?.Ready, view?.Paused, needsSupply && view?.Hour <= 0,
            live != null, live?.Goods.Count > 0, world != null && HasWorldEconomy(world, live));
        key.AddList(needsSupply ? live?.Services : null, key.Service);
        key.Diet(needsSupply ? live?.Diet : null);
        key.AddList(needsGoods ? live?.Goods.Where(g => _commodityFilter == null || g.Good == _commodityFilter) : null, key.Good);
        key.Capacity(needsEquipment || surface && (site?.Kind == "power" || site?.Kind == "settlement" || site?.Recipes.Count > 0) ? live?.Capacity : null);
        key.AddList(needsEquipment ? live?.Assets.Where(a => _commodityFilter == null || a.Good == _commodityFilter) : null, key.Asset);
        key.AddList(needsEquipment ? live?.Orders.Where(o => _commodityFilter == null || o.Good == _commodityFilter) : null, key.Order);
        key.AddList(surface && site != null ? live?.Production.Where(p => recipeIds?.Contains(p.Recipe) == true) : null, key.Production);
        key.Governor(surface && site == null || !surface && !_planetOpen && _detailPage == 3 ? view?.Governors.FirstOrDefault(g => g.Sector == sector) : null);
        key.AddList(needsFreight ? visibleFlows : null, key.Flow);
        key.AddList(localFlows, key.LocalFlow);
        key.Focused(_focusedFlow is not { } focused ? null : view?.Flows.FirstOrDefault(f => f.Edge == focused.Edge && f.From == focused.From && f.To == focused.To && f.Good == focused.Good));
        key.Link(edge == null ? null : view?.Links.FirstOrDefault(l => l.Edge == edge));
        key.AddList(_selectedMedicalShipmentId == null ? null : view?.MedicalSupplyFlows, key.Medical);
        return key.Values;
    }

    public void SetEconomy(SWEconomyView? view)
    {
        if (view != null && view.ViewerFaction != _viewerFaction) view = null;
        if (view != null && _economyView != null && view.Revision == _economyView.Revision &&
            view.Ready == _economyView.Ready && view.Paused == _economyView.Paused && view.ViewerFaction == _economyView.ViewerFaction)
            return;
        var oldDetails = _fullPlanetScreen ? EconomyDetailsKey(_economyView) : null;
        var previousFocused = _focusedFlow;
        _economyView = view;
        if (_surfaceSavedFlow is { } savedFlow)
            _surfaceSavedFlow = view?.Flows.FirstOrDefault(f => f.Edge == savedFlow.Edge && f.From == savedFlow.From && f.To == savedFlow.To && f.Good == savedFlow.Good);
        if (_focusedFlow is { } previousFlow)
        {
            _focusedFlow = view?.Flows.FirstOrDefault(f => f.Edge == previousFlow.Edge && f.From == previousFlow.From && f.To == previousFlow.To && f.Good == previousFlow.Good);
            if (_focusedFlow == null && _route == null) Map.ClearRouteHighlight();
        }
        Map.SetEconomy(view);
        Surface?.SetEconomy(view?.Worlds.FirstOrDefault(w => w.World == SelectedWorldId), view?.Paused ?? false, view?.Ready ?? false);
        RefreshEconomyStatus();
        if (_fullPlanetScreen && Surface == null && _selectedCrisisIds.Length > 0)
            return; // Crisis details are driven by their own public snapshot.
        if (_planetOpen && _detailPage == 3 && _route == null &&
            previousFocused?.UnitsPerHour == _focusedFlow?.UnitsPerHour &&
            previousFocused?.Edge == _focusedFlow?.Edge && previousFocused?.From == _focusedFlow?.From &&
            previousFocused?.To == _focusedFlow?.To && previousFocused?.Good == _focusedFlow?.Good)
        {
            RefreshPlanetPaths();
            return;
        }
        if (_fullPlanetScreen && oldDetails != null && oldDetails.SequenceEqual(EconomyDetailsKey(view)))
        {
            RefreshPlanetPaths();
            return;
        }
        RefreshDetails();
    }

    private void RefreshEconomyStatus()
    {
        _selectedFlowDirection.Visible = _fullPlanetScreen && Surface == null && _focusedFlow != null;
        _trafficLegend.Visible = _fullPlanetScreen && Surface == null && (_showTraffic || _focusedFlow != null);
        if (_trafficLegend.Visible)
            _trafficLegend.SetMessage(_focusedFlow is { } selected
                ? $"Выбранный поток: {WorldName(selected.From)} → {WorldName(selected.To)} — {GoodName(selected.Good)}"
                : _commodityFilter == null
                    ? "Стрелки поочерёдно показывают отдельные товары. Полный список доступен в досье маршрута."
                    : "Стрелка показывает поток выбранного товара, а не отдельный корабль.", defaultColor: SWConsoleStyle.TextDim);
        UpdateOverviewFlowNotice();
        _economyStatus.Text = _economyView is not { Ready: true } ? "Ожидание данных"
            : !_showTraffic ? "Потоки скрыты"
            : _economyView.Paused ? "Движение приостановлено"
            : Surface != null ? (Surface.TrafficFlows.Any(f => _commodityFilter == null || f.Good == _commodityFilter)
                ? "Местное распределение" : "Нет местных потоков выбранного товара")
            : VisibleFlows().Length == 0 ? "Нет опубликованных потоков выбранного товара" : "Межпланетные перевозки";
        _economyStatus.FontColorOverride = _showTraffic && _economyView is { Ready: true, Paused: false }
            ? SWConsoleStyle.Accent : SWConsoleStyle.TextDim;
    }

    private string? _industrialGoodsKey;
    public void SetIndustrialGoods(System.Collections.Generic.IEnumerable<string> ids)
    {
        var allowed = ids.ToHashSet();
        var key = string.Join("|", allowed.OrderBy(id => id));
        if (_industrialGoodsKey == key) return;
        _industrialGoodsKey = key;
        _commodities = IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<SWCommodityPrototype>()
            .Where(g => allowed.Contains(g.ID)).OrderBy(g => g.Order).ToArray();
        _commodityPicker.Clear();
        _commodityPicker.AddItem("Выберите товар", 0);
        _commodityPicker.SetItemDisabled(0, true);
        for (var i = 0; i < _commodities.Length; i++) _commodityPicker.AddItem(_commodities[i].Name, i + 1);
        var selected = Array.FindIndex(_commodities, g => g.ID == _commodityFilter);
        _commodityPicker.SelectId(selected + 1);
    }

    public void SelectCommodity(string? id)
    {
        var index = id == null ? -1 : Array.FindIndex(_commodities, g => g.ID == id);
        var next = index < 0 ? null : id;
        if (_fullPlanetScreen && _commodityFilter != next) _pageReveal = 0;
        _commodityFilter = next;
        if (_surfaceSavedFlow != null && _surfaceSavedFlow.Good != next) _surfaceSavedFlow = null;
        _commodityPicker.SelectId(index + 1);
        Map.SetTrafficFilter(_commodityFilter, _showTraffic);
        Map.SetEconomyMapMode(_economyMapMode, _commodityFilter);
        Surface?.SetTrafficFilter(_commodityFilter, _showTraffic);
        UpdateOverviewFlowNotice();
        if (_focusedFlow != null && _focusedFlow.Good != _commodityFilter)
        {
            _focusedFlow = null;
            if (_route == null) Map.ClearRouteHighlight();
        }
        RestoreFocusedFlow();
        RefreshDetails();
        CommodityChanged?.Invoke(_commodityFilter);
    }

    public void ShowEconomyPage(int page)
    {
        if (_planetOpen && _planetLayout?.IsTransitioning == true) return;
        var next = Math.Clamp(page, 0, 4);
        if (_fullPlanetScreen && _detailPage != next) _pageReveal = 0;
        _detailPage = next;
        RefreshDetails();
    }

    private void RefreshEconomyDetails()
    {
        if (_detailHeading == null) return;
        RefreshPlanetPaths();
        var savedScroll = _detailScroll?.GetScrollValue();
        _detailHeading.RemoveAllChildren();
        _detail.RemoveAllChildren();
        if (_fullPlanetScreen && _focusedFlow is { } focused)
            _detailHeading.AddChild(AtlasWrap($"Выбранный поток: {WorldName(focused.From)} → {WorldName(focused.To)} — {GoodName(focused.Good)} — {Number(focused.UnitsPerHour)} ед./ч", SWConsoleStyle.Brass));
        if (_route is { } route)
        {
            RouteDetails(route);
            SelectionChanged?.Invoke();
            return;
        }
        if (SelectedWorldId is not { } id) return;
        var world = _worlds[id];
        if (_fullPlanetScreen && !_planetOpen && Surface == null && !_financeOnly)
        {
            if (CurrentSectorId != null && HasSystem(world))
            {
                var openSystem = SWConsoleStyle.TabButton("Открыть систему");
                openSystem.OnPressed += _ => { if (OpenSystem(world.SystemId)) SelectWorld(id); };
                _detailHeading.AddChild(openSystem);
            }
            var openPlanet = SWConsoleStyle.TabButton("Открыть мир");
            openPlanet.OnPressed += _ => OpenPlanet(id);
            _detailHeading.AddChild(openPlanet);
        }
        _detailHeading.AddChild(AtlasWrap(world.Name, SWConsoleStyle.Brass));
        _detailHeading.AddChild(AtlasWrap(world.Sector is { } owner
            ? $"{world.Kind} / {SectorName(owner.Id)}" : world.Kind, SWConsoleStyle.TextDim));
        if (world.Sector != null && _data.GetWorldFaction(world) == "empire")
            _detailHeading.AddChild(AtlasWrap(_data.GetWorldPoliticalName(world), SWConsoleStyle.Warn));
        if (!CanReadWorldState(world) && !_planetOpen)
        {
            WorldDetails(world);
            Empty("Текущее состояние мира неизвестно");
            SelectionChanged?.Invoke();
            return;
        }
        var live = _economyView?.Worlds.FirstOrDefault(w => w.World == id);
        if (!_planetOpen && world.Sector == null && !HasWorldEconomy(world.Id, live))
        {
            WorldDetails(world);
            SelectionChanged?.Invoke();
            return;
        }
        var tabs = new WrapContainer { HorizontalExpand = true, SeparationOverride = 2, CrossSeparationOverride = 2 };
        // Keep the existing page IDs stable for the other consoles and saved selections.
        var pages = new[] { (0, "Снабжение"), (1, "Товары"), (4, "Оснащение"), (2, "Рейсы"), (3, "О мире") };
        foreach (var (page, title) in pages)
        {
            var button = SWConsoleStyle.TabButton(title);
            button.Name = $"EconomyPage_{page}";
            SWConsoleStyle.StyleTabButton(button, _detailPage == page);
            button.OnPressed += _ => ShowEconomyPage(page);
            tabs.AddChild(button);
        }
        _detailHeading.AddChild(tabs);
        if (CanOpenSurface(id))
        {
            var territory = SWConsoleStyle.TabButton("Территория");
            territory.Name = "OpenPlanetSurface";
            territory.OnPressed += _ => OpenSurface(id);
            _detailHeading.AddChild(territory);
        }
        if (_planetOpen && !CanOpenSurface(id))
            _detail.AddChild(AtlasWrap("Поверхность недоступна: данные территории не опубликованы.", SWConsoleStyle.TextDim));
        if (_planetOpen && !CanReadWorldState(world) && _detailPage != 3) Empty("Данные состояния мира недоступны");
        else if (_detailPage == 3 && _planetOpen) PlanetWorldDetails(world);
        else if (_detailPage == 3) WorldDetails(world);
        else if (_economyView is not { Ready: true }) Empty("Ожидание данных экономики");
        else if (_detailPage == 2) FreightDetails(id);
        else if (_detailPage == 4 && live != null) EquipmentDetails(live);
        else if (live == null || live.Goods.Count == 0) Empty("Вне производственной сети");
        else if (_detailPage == 0) SupplyDetails(live);
        else GoodsDetails(live);
        if (savedScroll is { } offset) _detailScroll?.SetScrollValue(offset);
        SelectionChanged?.Invoke();
    }

    private static bool HasWorldEconomy(string id, SWEconomyWorldView? live)
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (prototypes.TryIndex<SWPlanetEconomyPrototype>(id, out var profile) && profile.ActiveEconomy)
            return true;
        // Capacity snapshots also contain inactive worlds. Count actual activity instead.
        return live != null && (live.Goods.Any(g => g.Stock != 0 || g.Capacity != 0 || g.ProducedPerHour != 0
                || g.ConsumedPerHour != 0 || g.RequiredPerHour != 0 || g.ImportedPerHour != 0 || g.ExportedPerHour != 0 || g.Incoming != 0)
            || live.Services.Any(s => s.RequiredPerHour != 0 || s.ServedPerHour != 0)
            || live.Assets.Any(a => a.Installed != 0 || a.Operational != 0 || a.Target != 0)
            || live.Orders.Any(o => o.Ordered != 0 || o.Delivered != 0)
            || live.Production.Any(p => p.CompletedCyclesPerHour != 0 || p.StartedCyclesPerHour != 0 || p.WorkInProgress != 0));
    }

    private void SupplyDetails(SWEconomyWorldView world)
    {
        var services = world.Services.Where(s => s.RequiredPerHour > 0 && s.Group != "orders").ToArray();
        var required = services.Where(s => s.Group is not ("luxury" or "diet_quality" or "shadow")).ToArray();
        if (required.Length == 0)
            Empty(_economyView!.Hour <= 0 ? "Первый расчёт ещё не завершён" : "Нет текущих обязательных потребностей");
        else
        {
            var worst = required.Min(s => Math.Min(1, s.ServedPerHour / s.RequiredPerHour));
            var status = AtlasWrap(worst >= .999 ? "Потребности обеспечены" : "Дефицит снабжения",
                worst >= .999 ? SWConsoleStyle.Good : SWConsoleStyle.Warn);
            status.Name = "EconomySupplyStatus";
            _detail.AddChild(status);
            foreach (var service in required.OrderBy(s => ServiceOrder(s.Group))) ServiceRow(service, true);
        }
        var optional = services.Where(s => s.Group is "luxury" or "diet_quality").ToArray();
        if (optional.Length > 0 || world.Diet is { Consumed: > 0 })
        {
            _detail.AddChild(SWConsoleStyle.Section("Качество жизни"));
            if (world.Diet is { Consumed: > 0 } diet)
            {
                var row = VerticalStack(3);
                row.Name = "EconomyDietQuality";
                row.AddChild(Pair("Разнообразие питания", $"{Math.Clamp(diet.Quality, 0, 1) * 100:0}%", SWConsoleStyle.Accent));
                row.AddChild(new SWStatisticsBar { Value = (float) diet.Quality, Maximum = 1, Fill = SWConsoleStyle.Accent, MinHeight = 4, MaxHeight = 4 });
                row.ToolTip = "Качество уже потреблённого рациона";
                _detail.AddChild(row);
            }
            foreach (var service in optional.Where(s => s.Group != "diet_quality" || world.Diet is not { Consumed: > 0 }))
                ServiceRow(service, false);
        }
        var shadow = services.Where(s => s.Group == "shadow").ToArray();
        if (shadow.Length > 0)
        {
            _detail.AddChild(SWConsoleStyle.Section("Теневой оборот"));
            foreach (var service in shadow) ServiceRow(service, false);
        }
        _detail.AddChild(SWConsoleStyle.Section("Поставки"));
        var flows = VisibleFlows().Where(f => f.To == world.World).ToArray();
        var incoming = flows.Where(f => f.InTransit > 0).GroupBy(f => f.Good).ToArray();
        if (incoming.Length == 0) Empty("Нет грузов в пути");
        foreach (var group in incoming.OrderByDescending(g => g.Sum(f => f.InTransit)))
            _detail.AddChild(Pair(GoodName(group.Key), $"{Number(group.Sum(f => f.InTransit))} ед.", SWConsoleStyle.Accent));
    }

    private void ServiceRow(SWEconomyServiceView service, bool required)
    {
        var ratio = Math.Clamp(service.ServedPerHour / service.RequiredPerHour, 0, 1);
        var color = !required ? SWConsoleStyle.TextDim
            : ratio < .8 ? SWConsoleStyle.Bad : ratio < .999 ? SWConsoleStyle.Warn : SWConsoleStyle.Accent;
        var row = VerticalStack(3);
        row.Name = $"EconomyService_{service.Group}";
        row.AddChild(Pair(ServiceName(service.Group), $"{ratio * 100:0}%", color));
        row.AddChild(new SWStatisticsBar { Value = (float) ratio, Maximum = 1, Fill = color, MinHeight = 4, MaxHeight = 4 });
        row.ToolTip = $"Обеспечено {Number(service.ServedPerHour)} из {Number(service.RequiredPerHour)} ед./ч";
        _detail.AddChild(row);
    }

    private void EquipmentDetails(SWEconomyWorldView world)
    {
        if (world.Capacity is { } capacity && (capacity.LaborCapacity > 0 || capacity.DomesticPower > 0
            || capacity.ReactorCapacityPower > 0 || capacity.FleetCapacity > 0 || world.Assets.Any(a => a.Installed > 0)))
        {
            var block = VerticalStack(4);
            block.Name = "EconomyOperatingCapacity";
            block.AddChild(SWConsoleStyle.Section("Действующие мощности"));
            block.AddChild(Pair("Производство", $"×{Number(capacity.ProductionMultiplier)}", SWConsoleStyle.Accent));
            block.AddChild(Pair("Труд", Number(capacity.LaborCapacity), SWConsoleStyle.TextBright));
            block.AddChild(Pair("Складская ёмкость", $"×{Number(capacity.StorageMultiplier)}", SWConsoleStyle.TextBright));
            block.AddChild(Pair("Базовое потребление энергии", Number(capacity.DomesticPower), SWConsoleStyle.TextBright));
            block.AddChild(Pair("Реакторная мощность", Number(capacity.ReactorCapacityPower), SWConsoleStyle.TextBright));
            if (capacity.FleetCapacity > 0)
            {
                block.AddChild(Pair("Флот, занято / мест", $"{Number(capacity.FleetOccupied)} / {Number(capacity.FleetCapacity)}", SWConsoleStyle.Accent));
                block.AddChild(new SWStatisticsBar
                {
                    Name = "EconomyFleetCapacity", Value = (float) capacity.FleetOccupied,
                    Maximum = (float) capacity.FleetCapacity, Fill = SWConsoleStyle.Accent, MinHeight = 4, MaxHeight = 4,
                });
            }
            _detail.AddChild(block);
        }
        _detail.AddChild(SWConsoleStyle.Section("В эксплуатации"));
        var assets = world.Assets.Where(a => a.Installed > 0 && (_commodityFilter == null || a.Good == _commodityFilter))
            .OrderBy(a => _commodities.FirstOrDefault(c => c.ID == a.Good)?.Order ?? 999).ToArray();
        if (assets.Length == 0) Empty(_commodityFilter == null ? "Оснащение не введено" : "Нет оснащения выбранного типа");
        foreach (var asset in assets)
        {
            var row = VerticalStack(3);
            row.Name = $"EconomyAsset_{asset.Good}";
            row.Margin = new Thickness(0, 4);
            row.AddChild(AtlasWrap(GoodName(asset.Good), SWConsoleStyle.TextBright));
            row.AddChild(Pair("Введено", $"{Number(asset.Installed)} ед.", SWConsoleStyle.TextBright));
            var ratio = Math.Clamp(asset.Operational / asset.Installed, 0, 1);
            var color = ratio >= .999 ? SWConsoleStyle.Accent : SWConsoleStyle.Warn;
            row.AddChild(Pair("Работает", $"{Number(asset.Operational)} ед.", color));
            row.AddChild(new SWStatisticsBar
            {
                Name = $"EconomyAssetReadiness_{asset.Good}", Value = (float) ratio, Maximum = 1,
                Fill = color, MinHeight = 4, MaxHeight = 4, ToolTip = $"Обеспеченность эксплуатации: {ratio * 100:0}%",
            });
            row.AddChild(AtlasWrap(AssetEffect(asset), color));
            _detail.AddChild(row);
        }
        _detail.AddChild(SWConsoleStyle.Section("Проекты оснащения"));
        var orders = world.Orders.Where(o => o.Ordered > 0 && (_commodityFilter == null || o.Good == _commodityFilter))
            .OrderBy(o => o.CompletedHour.HasValue)
            .ThenBy(o => _commodities.FirstOrDefault(c => c.ID == o.Good)?.Order ?? 999).ToArray();
        if (orders.Length == 0) Empty(_commodityFilter == null ? "Нет проектов оснащения" : "Нет проектов выбранного типа");
        foreach (var order in orders)
        {
            var row = VerticalStack(3);
            row.Name = $"EconomyOrder_{order.Good}";
            row.Margin = new Thickness(0, 4);
            var color = order.CompletedHour.HasValue ? SWConsoleStyle.Good : SWConsoleStyle.Accent;
            row.AddChild(Pair(GoodName(order.Good), $"{Number(order.Delivered)} / {Number(order.Ordered)}", color));
            row.AddChild(new SWStatisticsBar
            {
                Value = (float) order.Delivered, Maximum = (float) order.Ordered,
                Fill = color, MinHeight = 4, MaxHeight = 4,
            });
            if (order.CompletedHour is { } completed)
                row.AddChild(AtlasWrap($"Завершён: {Number(completed)} ч", SWConsoleStyle.TextDim));
            _detail.AddChild(row);
        }
    }

    private static string AssetEffect(SWEconomyAssetView asset)
    {
        var name = asset.Effect switch
        {
            "production" => "Производственная мощность", "labor" => "Трудовая мощность",
            "storage" => "Складская ёмкость", "domestic_power" => "Энергоэффективность",
            "transport" => "Пропускная способность", "healthcare" => "Здравоохранение",
            "education" => "Образование", "society" => "Общество", "fleet" => "Ёмкость флота",
            "defense" => "Оборона", "reactor" => "Реакторная мощность", _ => "Эффект оснащения",
        };
        var fractional = asset.Effect is "production" or "labor" or "storage" or "domestic_power" or "transport";
        var value = Number(asset.EffectiveBonus * (fractional ? 100 : 1));
        var unit = fractional ? "%" : asset.Effect == "fleet" ? " мест" : string.Empty;
        return $"{name}: +{value}{unit}";
    }

    private void GoodsDetails(SWEconomyWorldView world)
    {
        if (_planetOpen)
        {
            var goods = world.Goods.Where(g => _commodityFilter == null || g.Good == _commodityFilter)
                .Where(g => g.Stock > 0 || g.Capacity > 0 || g.ProducedPerHour > 0 || g.ConsumedPerHour > 0 || g.Incoming > 0)
                .OrderBy(g => _commodities.FirstOrDefault(c => c.ID == g.Good)?.Order ?? 999).ToArray();
            if (goods.Length == 0) Empty("Нет опубликованных запасов выбранного товара");
            foreach (var good in goods)
            {
                var row = VerticalStack(3);
                var choose = new SWConsoleButton { HorizontalExpand = true };
                choose.Label.Visible = false;
                choose.AddChild(AtlasWrap(GoodName(good.Good), SWConsoleStyle.TextBright, 400));
                choose.SetAppearance(SWConsoleStyle.TextBright, selected: _commodityFilter == good.Good, quiet: true);
                choose.OnPressed += _ => SelectCommodity(good.Good);
                row.AddChild(choose);
                row.AddChild(AtlasWrap($"Запас: {Number(good.Stock)} / {Number(good.Capacity)} — выпуск {Number(good.ProducedPerHour)} ед./ч — расход {Number(good.ConsumedPerHour)} ед./ч", SWConsoleStyle.TextDim, 400));
                _detail.AddChild(row);
            }
            return;
        }
        var rows = world.Goods.Where(g => _commodityFilter == null || g.Good == _commodityFilter)
            .Where(g => g.Stock > 0 || g.ProducedPerHour > 0 || g.ConsumedPerHour > 0 || g.RequiredPerHour > 0 || g.Incoming > 0)
            .OrderByDescending(g => g.ProducedPerHour > 0).ThenBy(g => _commodities.FirstOrDefault(c => c.ID == g.Good)?.Order ?? 999).ToArray();
        var columns = new BoxContainer { HorizontalExpand = true, SeparationOverride = 0 };
        columns.AddChild(NumberCell("Запас", SWConsoleStyle.TextDim, 58));
        columns.AddChild(NumberCell("Выпуск/ч", SWConsoleStyle.Accent, 72));
        columns.AddChild(NumberCell("Расход/ч", SWConsoleStyle.TextDim, 72));
        _detail.AddChild(new SWConsoleColumns(AtlasWrap("Товар", SWConsoleStyle.TextDim), columns, 202, 0));
        if (rows.Length == 0) Empty("Нет запасов и операций");
        foreach (var good in rows)
        {
            var values = new BoxContainer { HorizontalExpand = true, SeparationOverride = 0 };
            values.AddChild(NumberCell(Number(good.Stock), SWConsoleStyle.TextBright, 58));
            values.AddChild(NumberCell(Number(good.ProducedPerHour), SWConsoleStyle.Accent, 72));
            values.AddChild(NumberCell(Number(good.ConsumedPerHour), SWConsoleStyle.TextDim, 72));
            var title = AtlasWrap(GoodName(good.Good), SWConsoleStyle.TextBright);
            var row = new SWConsoleColumns(title, values, 202, 0) { Name = $"EconomyGood_{good.Good}", Margin = new Thickness(0, 4) };
            row.ToolTip = $"{GoodName(good.Good)}\nЗапас: {Number(good.Stock)} ед.\nВместимость: {Number(good.Capacity)} ед.\nПотребность: {Number(good.RequiredPerHour)} ед./ч\nПрибыло: {Number(good.ImportedPerHour)} ед./ч\nОтправлено: {Number(good.ExportedPerHour)} ед./ч\nВ пути сюда: {Number(good.Incoming)} ед.";
            _detail.AddChild(row);
        }
    }

    private SWEconomyRouteFlow[] VisibleFlows() => _economyView?.Flows
        .Where(f => (f.UnitsPerHour > 0 || f.InTransit > 0) && (_commodityFilter == null || f.Good == _commodityFilter)).ToArray()
        ?? Array.Empty<SWEconomyRouteFlow>();

    private void FreightDetails(string world)
    {
        _detail.AddChild(SWConsoleStyle.Section("Межпланетные перевозки"));
        var flows = VisibleFlows().Where(f => f.From == world || f.To == world).ToArray();
        if (flows.Length == 0) { Empty("Нет перевозок выбранных товаров"); return; }
        foreach (var inbound in new[] { true, false })
        {
            var group = flows.Where(f => inbound ? f.To == world : f.From == world).OrderByDescending(f => f.UnitsPerHour).ToArray();
            if (group.Length == 0) continue;
            _detail.AddChild(SWConsoleStyle.Section(inbound ? "Входящие" : "Исходящие"));
            foreach (var flow in group) FlowRow(flow, inbound ? $"Из {WorldName(flow.From)}" : $"В {WorldName(flow.To)}");
        }
    }

    private void RouteDetails(SWGalacticMapRoute route)
    {
        if (_fullPlanetScreen && IsPlannedRoute(route))
        {
            _detailHeading.AddChild(AtlasWrap("Маршрут проектируется", SWConsoleStyle.Brass));
            _detailHeading.AddChild(AtlasWrap($"{WorldName(route.From)} / {WorldName(route.To)}", SWConsoleStyle.TextBright));
            Empty("Статус пути на карте: проектируется. Подтверждение действующей связи не опубликовано.");
            WorldButton(route.From);
            WorldButton(route.To);
            return;
        }
        var corridor = _data.Corridors.FirstOrDefault(c => c.Id == route.Corridor);
        _detailHeading.AddChild(AtlasWrap(corridor?.Name ?? "Местный гиперпуть", SWConsoleStyle.Brass));
        _detailHeading.AddChild(AtlasWrap($"{WorldName(route.From)} / {WorldName(route.To)}", SWConsoleStyle.TextBright));
        var edge = SWTransitLinkPrototype.LinkId(route.From, route.To);
        if (!CanReadWorldState(_worlds[route.From]) || !CanReadWorldState(_worlds[route.To]))
        {
            // A publicly closed crossing is geography, not private traffic telemetry.
            if (_transitLinks.TryGetValue(edge, out var publicLink) && !publicLink.Accessible)
                _detail.AddChild(new Label { Name = "TransitClosed", Text = "Маршрут закрыт", FontColorOverride = SWConsoleStyle.Bad });
            Empty("Данные о перевозках недоступны");
            _detail.AddChild(SWConsoleStyle.Section("Конечные точки"));
            WorldButton(route.From);
            WorldButton(route.To);
            return;
        }
        RouteCrisisDetails(edge);
        if (!_transitLinks.TryGetValue(edge, out var link)) return;
        if (!link.Accessible)
        {
            var closed = AtlasWrap("Маршрут закрыт", SWConsoleStyle.Bad);
            closed.Name = "TransitClosed";
            _detail.AddChild(closed);
        }
        else
        {
            var live = _economyView?.Links.FirstOrDefault(l => l.Edge == edge);
            if (_economyView is not { Ready: true } || live == null) Empty("Ожидание данных перевозок");
            else
            {
                var used = live.LoadedSlotsPerHour + live.EmptySlotsPerHour;
                var ratio = live.CapacitySlotsPerHour > 0 ? used / live.CapacitySlotsPerHour : 0;
                var color = ratio >= .95 ? SWConsoleStyle.Warn : SWConsoleStyle.Accent;
                var title = AtlasWrap($"Загрузка {ratio * 100:0}%", color);
                title.Name = "TransitLiveLoad";
                _detail.AddChild(title);
                _detail.AddChild(new SWStatisticsBar { Name = "TransitCapacityBar", Value = (float) used, Maximum = (float) live.CapacitySlotsPerHour, Fill = color });
                _detail.AddChild(Pair("Занято / ёмкость", $"{Number(used)} / {Number(live.CapacitySlotsPerHour)}", SWConsoleStyle.TextBright));
                _detail.AddChild(Pair("Порожние, мест/ч", Number(live.EmptySlotsPerHour), SWConsoleStyle.TextDim));
            }
            _detail.AddChild(Pair("Переход", $"{Number(link.TravelHours)} ч", SWConsoleStyle.TextBright));
            var flows = VisibleFlows().Where(f => f.Edge == edge).ToArray();
            foreach (var direction in flows.GroupBy(f => f.From))
            {
                var first = direction.First();
                _detail.AddChild(SWConsoleStyle.Section($"В {WorldName(first.To)}"));
                foreach (var flow in direction.OrderByDescending(f => f.UnitsPerHour)) FlowRow(flow, string.Empty);
            }
            if (_economyView is { Ready: true } && flows.Length == 0) Empty("Нет перевозок выбранных товаров");
        }
        _detail.AddChild(SWConsoleStyle.Section("Конечные точки"));
        WorldButton(route.From);
        WorldButton(route.To);
    }

    private void FlowRow(SWEconomyRouteFlow flow, string location)
    {
        var box = VerticalStack(2);
        box.Margin = new Thickness(0, 4);
        box.ToolTip = "Отправлено по маршруту за игровой час; в пути — ещё не доставленный груз.";
        if (location.Length > 0) box.AddChild(AtlasWrap(location, SWConsoleStyle.TextDim));
        var button = SWConsoleStyle.TabButton(GoodName(flow.Good));
        button.TextAlign = Label.AlignMode.Left;
        button.Label.ClipText = true;
        button.OnPressed += _ =>
        {
            SelectCommodity(flow.Good);
            _focusedFlow = flow;
            RestoreFocusedFlow();
            RefreshDetails();
        };
        box.AddChild(button);
        box.AddChild(Pair($"{Number(flow.UnitsPerHour)} ед./ч", $"В пути {Number(flow.InTransit)}", SWConsoleStyle.Accent));
        _detail.AddChild(box);
    }

    private void WorldDetails(SWGalacticMapWorld world)
    {
        if (CanReadWorldState(world) && _viewerFaction == "republic") AddGovernorCard(world.Sector?.Id);
        _detail.AddChild(AtlasWrap(_data.GetWorldPoliticalName(world), SWConsoleStyle.TextDim));
        if (world.Description.Length > 0) _detail.AddChild(AtlasWrap(world.Description, SWConsoleStyle.TextDim));
        if (world.Sector is { } owner && CurrentSectorId != owner.Id && _lockedSector == null)
        {
            var open = SWConsoleStyle.TabButton("Открыть сектор");
            open.OnPressed += _ => { OpenSector(owner.Id); SelectWorld(world.Id); };
            _detail.AddChild(open);
        }
        if (_lockedSector == null && world.Sector == null && HasSystem(world) && CurrentSystemId != world.SystemId)
        {
            var open = SWConsoleStyle.TabButton("Открыть систему");
            open.OnPressed += _ => { if (OpenSystem(world.SystemId)) SelectWorld(world.Id); };
            _detail.AddChild(open);
        }
        if (CanReadWorldState(world) && (world.Sector != null || HasWorldEconomy(world.Id, _economyView?.Worlds.FirstOrDefault(w => w.World == world.Id))))
            _detail.AddChild(new SWPlanetResourcePanel(world.Id));
        _detail.AddChild(SWConsoleStyle.Section("Связи"));
        foreach (var edge in View.Routes.Where(r => r.From == world.Id || r.To == world.Id))
            WorldButton(edge.From == world.Id ? edge.To : edge.From);
        if (!CanReadWorldState(world)) return;
        var sector = _sectors.FirstOrDefault(s => s.Id == world.Sector?.Id);
        if (sector == null) return;
        if (SectorSummaryRequested != null)
        {
            var summary = SWConsoleStyle.TabButton("Сводка сектора");
            summary.OnPressed += _ => SectorSummaryRequested?.Invoke(sector.Id);
            _detail.AddChild(summary);
        }
        if (_planetOpen) return; // Sector statistics belong to the sector, not to this world.
        foreach (var stat in sector.Stats)
        {
            var global = _globals.FirstOrDefault(g => g.Id == stat.Id);
            _detail.AddChild(Metric(stat.Name, stat.Value, global?.Min ?? stat.Min, global?.Max ?? stat.Max,
                color: SWStatisticsPresentation.LevelColor(stat.Value, global?.Min ?? stat.Min, global?.Max ?? stat.Max), reference: global?.Actual));
        }
        foreach (var detail in _situationalStats.Where(x => x.SectorId == sector.Id))
            _detail.AddChild(SWSituationalStatDisplay.Make(detail, _situationExpanded));
    }

    private void Empty(string text) => _detail.AddChild(AtlasWrap(text, SWConsoleStyle.TextDim));
    private string GoodName(string id) => IoCManager.Resolve<IPrototypeManager>().TryIndex<SWCommodityPrototype>(id, out var good) && !string.IsNullOrWhiteSpace(good.Name) ? good.Name : "Неизвестный товар";
    public string GetWorldName(string id) => _worlds.TryGetValue(id, out var world) && !string.IsNullOrWhiteSpace(world.Name) ? world.Name : "Неизвестный мир";
    public string GetSectorName(string id) => _sectorPrototypes.FirstOrDefault(s => s.ID == id)?.Name is { Length: > 0 } name ? name : "Неизвестный сектор";
    private string WorldName(string id) => GetWorldName(id);
    private static string Number(double value) => value > 0 && value < .01 ? value.ToString("0.###") : value.ToString("0.##");
    private static Label NumberCell(string value, Color color, float width) => new()
    {
        Text = value, FontOverride = SWConsoleStyle.Small, FontColorOverride = color,
        SetWidth = width, ClipText = true, Align = Label.AlignMode.Right,
    };
    private static Control Pair(string name, string value, Color color) => new SWConsoleColumns(
        SWConsoleStyle.Wrap(name, SWConsoleStyle.TextDim), SWConsoleStyle.Text(value, color, SWConsoleStyle.Small), 105, 0);
    private static int ServiceOrder(string id) => id switch
    {
        "water" => 0, "nutrition" => 1, "care_drugs" => 2, "care_supplies" => 3,
        "regenerative_care" => 4, "implant_care" => 5, "special_medicine" => 6,
        "closed_habitats" => 7, "household" => 8, "maintenance" => 9, "military" => 10,
        "other" => 11, "diet_quality" => 12, "luxury" => 13, "shadow" => 14, _ => 15,
    };
    private static string ServiceName(string id) => id switch
    {
        "water" => "Вода", "nutrition" => "Продовольствие", "care_drugs" => "Лекарства",
        "care_supplies" => "Медицинское снабжение", "household" => "Бытовые товары", "maintenance" => "Ремонт и обслуживание",
        "regenerative_care" => "Восстановительное лечение", "implant_care" => "Протезирование",
        "closed_habitats" => "Закрытые поселения", "luxury" => "Обеспеченное потребление",
        "special_medicine" => "Специальные назначения", "shadow" => "Теневой спрос", "diet_quality" => "Разнообразие питания",
        "military" => "Военное снабжение", "other" => "Энергоячейки", _ => id,
    };
}
