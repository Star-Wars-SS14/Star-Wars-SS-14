using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._SW._Senate.Consoles.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Shared atlas for statistics, senators and the Chancellor. Navigation has no server actions.</summary>
public sealed partial class SWRepublicMapBrowser : BoxContainer
{
    private static readonly ProtoId<SWGalacticMapPrototype> MapPrototype = "SenateRepublic";
    private readonly SWGalacticMapPrototype _data;
    private readonly bool _showProductionRecipes;
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds;
    private readonly Dictionary<string, SWTransitLinkPrototype> _transitLinks;
    private readonly SWSectorPrototype[] _sectorPrototypes;
    private readonly OptionButton _search = new() { Name = "MapSearch", Filterable = true, MinWidth = 210, PrefixMargin = false };
    private readonly LineEdit _searchFilter;
    private readonly Button _galaxyButton = SWConsoleStyle.TabButton("К галактике");
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _title = SWConsoleStyle.Text("Карта галактики", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly BoxContainer _body = VerticalStack(0);
    private BoxContainer _detail = default!;
    private BoxContainer _detailHeading = default!;
    private SWAtlasLayout? _columns;
    private SWGalacticMapWorld[] _searchWorlds = Array.Empty<SWGalacticMapWorld>();
    private IReadOnlyList<SWSectorEntry> _sectors = Array.Empty<SWSectorEntry>();
    private IReadOnlyList<SWGlobalStatEntry> _globals = Array.Empty<SWGlobalStatEntry>();
    private SWGalacticMapRoute? _route;
    private string? _lockedSector;
    public SWGalacticMapControl Map { get; private set; } = default!;
    public SWGalacticMapPrototype View { get; private set; } = default!;
    public string? CurrentSectorId { get; private set; }
    public string? SelectedWorldId { get; private set; }
    public string? SelectedLiveSectorId => SelectedWorldId is { } id && _worlds[id].Sector is { } sector
        && _sectors.Any(s => s.Id == sector.Id) ? sector.Id : null;
    public event Action? SelectionChanged;
    public event Action<string>? SectorSummaryRequested;

    public SWRepublicMapBrowser(bool showProductionRecipes = false)
    {
        _showProductionRecipes = showProductionRecipes;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 8;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        _data = prototypes.Index(MapPrototype);
        _worlds = _data.Worlds.ToDictionary(w => w.Id);
        _transitLinks = prototypes.EnumeratePrototypes<SWTransitLinkPrototype>().ToDictionary(l => l.ID);
        _sectorPrototypes = prototypes.EnumeratePrototypes<SWSectorPrototype>()
            .Where(s => _data.Worlds.Any(w => w.Sector?.Id == s.ID)).OrderBy(s => s.Name).ToArray();
        var location = new WrapContainer { HorizontalExpand = true, SeparationOverride = 12, CrossSeparationOverride = 4 };
        _galaxyButton.OnPressed += _ => ShowGalaxy();
        location.AddChild(_galaxyButton);
        _title.Margin = new Thickness(0, 6);
        location.AddChild(_title);
        AddChild(location);
        var toolbar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4 };
        StylePicker(_search);
        _searchFilter = _search.OptionsScroll.Children.OfType<BoxContainer>().Single().Children.OfType<LineEdit>().Single();
        _search.OnItemSelected += args =>
        {
            if (args.Id < 0 || args.Id >= _searchWorlds.Length) return;
            var world = _searchWorlds[args.Id];
            if (_lockedSector == null)
            {
                if (world.Sector is { } sector) OpenSector(sector.Id);
                else ShowGalaxy();
            }
            SelectWorld(world.Id, true);
            _searchFilter.SetText(string.Empty, invokeEvent: true);
        };
        toolbar.AddChild(_search);
        var minus = SWConsoleStyle.TabButton("−");
        minus.ToolTip = "Отдалить";
        minus.OnPressed += _ => ChangeAtlasZoom(1 / 1.2f);
        toolbar.AddChild(minus);
        var plus = SWConsoleStyle.TabButton("+");
        plus.ToolTip = "Приблизить";
        plus.OnPressed += _ => ChangeAtlasZoom(1.2f);
        toolbar.AddChild(plus);
        _zoom.Margin = new Thickness(4, 6);
        toolbar.AddChild(_zoom);
        var resetZoom = SWConsoleStyle.TabButton("Сбросить масштаб");
        resetZoom.OnPressed += _ => { if (Surface != null) Surface.ResetView(); else Map.ResetView(); };
        toolbar.AddChild(resetZoom);
        _crisisButton.Name = "MapCrises";
        _crisisButton.Visible = false;
        _crisisButton.OnPressed += _ => SelectCrises(_mapCrises.Select(c => c.Id));
        toolbar.AddChild(_crisisButton);
        AddChild(toolbar);
        InitializeEconomyToolbar();
        _body.VerticalExpand = true;
        AddChild(_body);
        SwitchView(null);
        RebuildSearch();
    }

    private static void StylePicker(OptionButton picker)
    {
        picker.ModulateSelfOverride = Color.White;
        picker.StyleBoxOverride = new SWConsoleSurface
        {
            Background = SWConsoleStyle.PillDim,
            ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
        };
    }

    /// <summary>A senator stays in their constituency; external endpoints remain inspectable.</summary>
    public void SetScope(string? ownSector)
    {
        if (_lockedSector == ownSector) return;
        _lockedSector = ownSector;
        SwitchView(ownSector);
        RebuildSearch();
    }

    public bool ShowGalaxy()
    {
        if (_lockedSector != null) return false;
        if (CurrentSectorId != null)
        {
            var anchor = _data.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == CurrentSectorId);
            SwitchView(null, animate: true);
            if (anchor != null) SelectWorld(anchor.Id);
        }
        return true;
    }

    public bool OpenSector(string sectorId)
    {
        if (_lockedSector != null && _lockedSector != sectorId || !_sectorPrototypes.Any(s => s.ID == sectorId)) return false;
        if (CurrentSectorId != sectorId || Surface != null) SwitchView(sectorId, animate: true);
        return true;
    }

    private void SwitchView(string? sectorId, bool animate = false)
    {
        Surface = null;
        _trafficToggle.Visible = true;
        var previous = Map;
        var throughSector = sectorId ?? CurrentSectorId;
        var throughWorld = SelectedWorldId is { } selected && _worlds[selected].Sector?.Id == throughSector
            && (sectorId != null || _worlds[selected].Overview) ? selected
            : _data.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == throughSector)?.Id;
        previous?.FinishTransition();
        CurrentSectorId = sectorId;
        View = sectorId == null ? SWGalacticMapViews.Overview(_data) : SWGalacticMapViews.Sector(_data, sectorId);
        _galaxyButton.Visible = _lockedSector == null && sectorId != null;
        _body.RemoveAllChildren();
        Map = new SWGalacticMapControl(View, sectorId, _lockedSector == null);
        Map.SetState(_sectors);
        Map.SetEconomy(_economyView);
        Map.SetCrises(_mapCrises, _data.Worlds);
        Map.CrisesSelected += SelectCrises;
        Map.SetTrafficFilter(_commodityFilter, _showTraffic);
        RefreshEconomyStatus();
        Map.WorldSelected += id => SelectWorld(id);
        Map.WorldActivated += id =>
        {
            if (CurrentSectorId != null && _worlds[id].Sector?.Id == CurrentSectorId) OpenSurface(id);
            else if (_worlds[id].Sector is { } sector && OpenSector(sector.Id)) SelectWorld(id, true);
        };
        Map.RouteSelected += route =>
        {
            ClearCrisisSelection();
            _route = route;
            SelectedWorldId = null;
            RefreshDetails();
        };
        Map.ViewChanged += () => _zoom.Text = $"{Map.Zoom * 100:0}%";
        _zoom.Text = "100%";
        _detail = VerticalStack(8);
        _detailHeading = VerticalStack(6);
        var sidebar = VerticalStack(8);
        sidebar.Name = "MapDetails";
        sidebar.VerticalExpand = true;
        sidebar.AddChild(_detailHeading);
        var scroll = new ScrollContainer { Name = "MapDetailsScroll", HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(_detail);
        sidebar.AddChild(scroll);
        _columns = new SWAtlasLayout(Map, sidebar) { VerticalExpand = true };
        _body.AddChild(_columns);
        _title.Text = sectorId == null ? "Карта галактики" : $"Сектор: {SectorName(sectorId)}";
        SelectWorld(View.Worlds.FirstOrDefault(w => w.Anchor && (sectorId == null ? w.Id == "coruscant" : w.Sector?.Id == sectorId))?.Id
            ?? View.Worlds.First().Id);
        if (animate && previous != null && throughWorld != null)
            Map.TransitionFrom(previous, throughWorld, sectorId != null);
    }

    private void RebuildSearch()
    {
        // The caption remains an action, never another unlabeled selection beside the location.
        _searchFilter.Text = string.Empty;
        _search.Clear();
        var prompt = _lockedSector == null ? "Найти мир или сектор" : "Найти мир";
        _searchFilter.PlaceHolder = prompt;
        _search.AddItem(prompt, -1);
        _search.SetItemDisabled(0, true);
        _searchWorlds = (_lockedSector == null ? _data.Worlds : View.Worlds).OrderBy(w => w.Name).ToArray();
        for (var i = 0; i < _searchWorlds.Length; i++)
        {
            var world = _searchWorlds[i];
            var kind = _lockedSector == null && world.Anchor ? "сектор"
                : _lockedSector != null && world.Sector?.Id != _lockedSector ? "выход" : world.Kind.ToLowerInvariant();
            _search.AddItem($"{world.Name} ({kind})", i);
        }
    }

    public void SetState(IReadOnlyList<SWSectorEntry> sectors, IReadOnlyList<SWGlobalStatEntry> globals)
    {
        _sectors = sectors;
        _globals = globals;
        Map.SetState(sectors);
        // Frequent political updates must not rebuild the live economy table or its scroll position.
        if (Surface == null && _detailPage == 3 && _route == null) RefreshDetails();
    }

    public void SelectWorld(string id, bool focus = false)
    {
        if (!_worlds.TryGetValue(id, out var world)) return;
        if (Surface != null && SelectedWorldId != id) SwitchView(CurrentSectorId);
        if (!View.Worlds.Any(w => w.Id == id))
        {
            if (_lockedSector != null) return;
            if (world.Sector is { } sector) OpenSector(sector.Id);
            else ShowGalaxy();
        }
        SelectedWorldId = id;
        ClearCrisisSelection();
        _route = null;
        Map.SelectWorld(id, focus);
        RefreshDetails();
    }

    private string SectorName(string id) => _sectorPrototypes.FirstOrDefault(s => s.ID == id)?.Name ?? id;

    private void RefreshDetails()
    {
        if (Surface != null) RefreshSurfaceDetails();
        else if (_selectedCrisisIds.Length > 0) RefreshCrisisDetails();
        else RefreshEconomyDetails();
    }

    private void WorldButton(string id)
    {
        var button = SWConsoleStyle.TabButton(_worlds[id].Name);
        button.TextAlign = Label.AlignMode.Left;
        button.OnPressed += _ => SelectWorld(id, true);
        _detail.AddChild(button);
    }
}
