using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._SW._Senate.Consoles.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.UserInterface;
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
    private readonly SWGalacticMapPrototype _source;
    private SWGalacticMapPrototype _data;
    private string _viewerFaction = "republic";
    private readonly bool _showProductionRecipes;
    private readonly bool _compactEconomy;
    private readonly bool _financeOnly;
    private readonly bool _showMedicalToggle;
    private readonly bool _fullPlanetScreen;
    private IReadOnlyDictionary<string, SWFinanceSectorRevenue>? _financeSectors;
    public void SetFinanceSectors(IEnumerable<SWFinanceSectorRevenue> sectors)
    {
        _financeSectors = sectors.ToDictionary(x => x.SectorId);
        Map.SetFinanceSectors(_financeSectors);
    }
    private Dictionary<string, SWGalacticMapWorld> _worlds;
    private readonly Dictionary<string, SWTransitLinkPrototype> _transitLinks;
    private readonly SWSectorPrototype[] _sectorPrototypes;
    private readonly OptionButton _search = new() { Name = "MapSearch", Filterable = true, MinWidth = 210, PrefixMargin = false };
    private readonly LineEdit _searchFilter;
    private readonly Button _galaxyButton = SWConsoleStyle.TabButton("К галактике");
    private readonly Button _sectorSchemeButton = SWConsoleStyle.TabButton("Политический контроль");
    private readonly Label _sectorSchemeNotice = SWConsoleStyle.Text("Схематические границы секторов", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private bool _showSectorScheme = true;
    private string? _selectedSchemeSector;
    private readonly Label _zoom = SWConsoleStyle.Text("100%", SWConsoleStyle.TextDim, SWConsoleStyle.Small);
    private readonly Label _title = SWConsoleStyle.Text("Карта галактики", SWConsoleStyle.Brass, SWConsoleStyle.Strong);
    private readonly Button _systemShortcut = SWConsoleStyle.TabButton("Открыть систему");
    private string? _chosenSectorSystem;
    private string? _chosenSystemSector;
    private bool _systemSummaryActive;
    private readonly Label _schematicNotice = SWConsoleStyle.Text("Расположение условное", SWConsoleStyle.Warn, SWConsoleStyle.Small);
    private readonly Label _occupationLegend = SWConsoleStyle.Text("Зона оккупации", SWConsoleStyle.Warn, SWConsoleStyle.Small);
    private readonly BoxContainer _body = VerticalStack(0);
    private BoxContainer _detail = default!;
    private BoxContainer _detailHeading = default!;
    private SWAtlasLayout? _columns;
    private SWStatisticsAtlasLayout? _statisticsAtlas;
    private Control? _atlasSidebar;
    private SWGalacticMapWorld[] _searchWorlds = Array.Empty<SWGalacticMapWorld>();
    private IReadOnlyList<SWSectorEntry> _sectors = Array.Empty<SWSectorEntry>();
    private IReadOnlyList<SWGlobalStatEntry> _globals = Array.Empty<SWGlobalStatEntry>();
    private readonly HashSet<string> _situationExpanded = new();
    private IReadOnlyList<SWSituationalStatView> _situationalStats = Array.Empty<SWSituationalStatView>();
    private bool _worldSituationChanged;
    public void SetSituationalStats(IReadOnlyList<SWSituationalStatView> stats)
    {
        if (SelectedWorldId is { } id && _worlds.TryGetValue(id, out var world) && world.Sector is { } sector)
            _worldSituationChanged |= !SituationKey(_situationalStats, sector.Id).SequenceEqual(SituationKey(stats, sector.Id));
        _situationalStats = stats;
    }

    private static System.Collections.Generic.List<object?> SituationKey(IReadOnlyList<SWSituationalStatView> stats, string sector)
    {
        var key = new SnapshotValues();
        key.AddList(stats.Where(s => s.SectorId == sector), stat =>
        {
            key.Add(stat.SubstatId, stat.SubstatName, stat.GlobalId, stat.SectorId, stat.BaseValue, stat.RawTotal, stat.AppliedTotal, stat.EffectiveValue);
            key.AddList(stat.Sources, source => key.Add(source.Source, source.SourceName, source.SectorId, source.SectorName, source.RawValue));
        });
        return key.Values;
    }

    private static System.Collections.Generic.List<object?> WorldStatsKey(IReadOnlyList<SWSectorEntry> sectors, IReadOnlyList<SWGlobalStatEntry> globals, string? sectorId)
    {
        var sector = sectors.FirstOrDefault(s => s.Id == sectorId);
        var key = new SnapshotValues();
        key.Add(sector != null);
        key.AddList(sector?.Stats, stat =>
        {
            var global = globals.FirstOrDefault(g => g.Id == stat.Id);
            key.Add(stat.Id, stat.Name, stat.Value, global?.Min ?? stat.Min, global?.Max ?? stat.Max, global?.Actual);
        });
        return key.Values;
    }
    private SWGalacticMapRoute? _route;
    private string? _lockedSector;
    public SWGalacticMapControl Map { get; private set; } = default!;
    public SWGalacticMapPrototype View { get; private set; } = default!;
    public string? CurrentSectorId { get; private set; }
    public string? CurrentSystemId { get; private set; }
    public string? CurrentPoliticalSectorId { get; private set; }
    public string? SelectedWorldId { get; private set; }
    public string? SelectedSectorId => SelectedWorldId is { } id && _worlds.TryGetValue(id, out var world) ? world.Sector?.Id : null;
    public string? SelectedLiveSectorId => SelectedWorldId is { } id && _worlds[id].Sector is { } sector
        && _sectors.Any(s => s.Id == sector.Id) ? sector.Id : null;
    public event Action? SelectionChanged;
    public event Action<string?>? CommodityChanged;
    public Action<BoxContainer, BoxContainer, string?, SWGalacticMapRoute?>? EconomyDetailsOverride { get; set; }
    private int _economyMapMode = -1;

    public void SetEconomyMapMode(int mode)
    {
        _economyMapMode = mode;
        _trafficToggle.Visible = false;
        Map.SetEconomyMapMode(mode, _commodityFilter);
        _showTraffic = mode == 2;
        SWConsoleStyle.StyleTabButton(_trafficToggle, _showTraffic);
        Map.SetTrafficFilter(_commodityFilter, _showTraffic);
        Surface?.SetTrafficFilter(_commodityFilter, _showTraffic);
        RefreshDetails();
    }
    public event Action<string>? SectorSummaryRequested;

    public SWRepublicMapBrowser(bool showProductionRecipes = false, bool compactEconomy = false, bool financeOnly = false, bool showMedicalToggle = true, string viewerFaction = "republic", bool fullPlanetScreen = false)
    {
        _viewerFaction = viewerFaction;
        _fullPlanetScreen = fullPlanetScreen;
        _showProductionRecipes = showProductionRecipes;
        _compactEconomy = compactEconomy;
        _financeOnly = financeOnly;
        _showMedicalToggle = showMedicalToggle;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 8;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        _source = prototypes.Index(MapPrototype);
        _data = SWGalacticMapViews.ForFaction(_source, _viewerFaction, id => prototypes.TryIndex<SWSectorPrototype>(id, out var sector) ? sector.Name : id);
        _worlds = _data.Worlds.ToDictionary(w => w.Id);
        _transitLinks = prototypes.EnumeratePrototypes<SWTransitLinkPrototype>().ToDictionary(l => l.ID);
        _sectorPrototypes = prototypes.EnumeratePrototypes<SWSectorPrototype>()
            .Where(s => _source.Worlds.Any(w => w.Sector?.Id == s.ID)).OrderBy(s => s.Name).ToArray();
        var location = new WrapContainer { HorizontalExpand = true, SeparationOverride = 12, CrossSeparationOverride = 4 };
        _galaxyButton.OnPressed += _ => ShowGalaxy();
        location.AddChild(_galaxyButton);
        _title.Margin = new Thickness(0, 6);
        _title.ClipText = true;
        location.AddChild(_title);
        _systemShortcut.Visible = false;
        _systemShortcut.OnPressed += _ => { if (_chosenSectorSystem is { } id) OpenSystem(id); };
        if (_fullPlanetScreen) location.AddChild(_systemShortcut);
        _schematicNotice.Visible = false;
        location.AddChild(_schematicNotice);
        if (_fullPlanetScreen)
        {
            location.AddChild(_sectorSchemeNotice);
            location.AddChild(_occupationLegend);
        }
        if (_fullPlanetScreen)
        {
            location.AddChild(SWConsoleStyle.Text("Пунктир: маршрут проектируется", SWConsoleStyle.TextDim, SWConsoleStyle.Small));
        }
        AddChild(location);
        var toolbar = new WrapContainer { HorizontalExpand = true, SeparationOverride = 6, CrossSeparationOverride = 4 };
        StylePicker(_search);
        _searchFilter = _search.OptionsScroll.Children.OfType<BoxContainer>().Single().Children.OfType<LineEdit>().Single();
        _search.OnItemSelected += args =>
        {
            if (args.Id < 0 || args.Id >= _searchWorlds.Length) return;
            var world = _searchWorlds[args.Id];
            if (_fullPlanetScreen && _lockedSector == null)
            {
                FocusWorldImmediately(world.Id);
                _searchFilter.SetText(string.Empty, invokeEvent: true);
                return;
            }
            if (_lockedSector == null)
            {
                if (_viewerFaction == "empire" && !world.Overview && HasSystem(world)) OpenSystem(world.SystemId);
                else if (_viewerFaction == "empire" && !string.IsNullOrEmpty(world.PoliticalSectorId)) OpenPoliticalSector(world.PoliticalSectorId);
                else if (world.Sector is { } sector) OpenSector(sector.Id);
                else if (!world.Overview && HasSystem(world)) OpenSystem(world.SystemId);
                else if (!world.Overview && !string.IsNullOrEmpty(world.PoliticalSectorId)) OpenPoliticalSector(world.PoliticalSectorId);
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
        var resetZoom = SWConsoleStyle.TabButton(compactEconomy ? "Сброс" : "Сбросить масштаб");
        resetZoom.ToolTip = "Сбросить масштаб";
        resetZoom.OnPressed += _ => { if (Surface != null) Surface.ResetView(); else Map.ResetView(); };
        toolbar.AddChild(resetZoom);
        if (_fullPlanetScreen)
        {
            _sectorSchemeButton.OnPressed += _ =>
            {
                _showSectorScheme = !_showSectorScheme;
                Map.SetSectorScheme(_showSectorScheme);
                UpdateSectorSchemeControls();
            };
            toolbar.AddChild(_sectorSchemeButton);
        }
        _crisisButton.Name = "MapCrises";
        _crisisButton.Visible = false;
        _crisisButton.OnPressed += _ => SelectCrises(_mapCrises.Select(c => c.Id));
        toolbar.AddChild(_crisisButton);
        AddChild(toolbar);
        InitializeEconomyToolbar();
        _body.VerticalExpand = true;
        AddChild(_body);
        // Keep the map above its controls in the full statistics workspace.
        if (_fullPlanetScreen) _body.SetPositionInParent(1);
        SwitchView(null);
        RebuildSearch();
    }

    private void UpdateSystemShortcut()
    {
        _systemShortcut.Visible = _fullPlanetScreen && CurrentSectorId != null && CurrentSectorId == _chosenSystemSector &&
            _chosenSectorSystem != null && _data.Worlds.Any(w => w.SystemId == _chosenSectorSystem && w.Sector?.Id == CurrentSectorId);
        if (!_systemShortcut.Visible) return;
        var name = _data.Worlds.FirstOrDefault(w => w.SystemId == _chosenSectorSystem && !string.IsNullOrWhiteSpace(w.SystemName))?.SystemName;
        _systemShortcut.Text = string.IsNullOrWhiteSpace(name) ? "Открыть систему" : $"Открыть {name}";
    }

    private void UpdateSectorSchemeControls()
    {
        var available = _fullPlanetScreen && !_planetOpen && Surface == null &&
            CurrentSectorId == null && CurrentSystemId == null && CurrentPoliticalSectorId == null;
        _occupationLegend.Visible = available && _data.PoliticalAreas.Any(a => a.Contested && a.Name.Contains("оккупац", StringComparison.OrdinalIgnoreCase));
        _sectorSchemeButton.Visible = available;
        _sectorSchemeButton.Text = _showSectorScheme ? "Политический контроль" : "Схема секторов";
        _sectorSchemeButton.ToolTip = "Схематическая интерполяция по мирам с подтверждённым сектором; она не меняет их принадлежность.";
        _sectorSchemeNotice.Visible = available && _showSectorScheme;
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

    public void SetViewerFaction(string faction)
    {
        if (_viewerFaction == faction) return;
        _viewerFaction = faction;
        _chosenSectorSystem = null;
        _chosenSystemSector = null;
        _systemSummaryActive = false;
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        _data = SWGalacticMapViews.ForFaction(_source, faction, id => prototypes.TryIndex<SWSectorPrototype>(id, out var sector) ? sector.Name : id);
        _worlds = _data.Worlds.ToDictionary(w => w.Id);
        _lockedSector = null;
        SelectedWorldId = null;
        _route = null;
        _economyView = null;
        _focusedFlow = null;
        _surfaceSavedFlow = null;
        _planetOpen = false;
        _systemParentSector = null;
        _mapViews.Clear();
        _sectors = Array.Empty<SWSectorEntry>();
        _globals = Array.Empty<SWGlobalStatEntry>();
        _situationalStats = Array.Empty<SWSituationalStatView>();
        _financeSectors = null;
        _mapCrises = Array.Empty<SWMapCrisisEntry>();
        _selectedCrisisIds = Array.Empty<string>();
        SwitchView(null);
        if (_economyClient?.View is { } cached && cached.ViewerFaction == faction) SetEconomy(cached);
        RebuildSearch();
    }

    private static bool IsPublicSector(SWGalacticMapWorld world) => world.Id.StartsWith("public-sector:", StringComparison.Ordinal);

    private bool CanReadWorldState(SWGalacticMapWorld world) => _data.GetWorldFaction(world) == _viewerFaction
        || _viewerFaction == "republic" && _data.GetWorldFaction(world) == "neutral";

    public bool ShowGalaxy()
    {
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return false;
        if (_lockedSector != null) return false;
        if (Surface != null && _surfaceReturnsToPlanet) { ReturnToPlanetFromSurface(); return true; }
        if (_planetOpen) { BackFromPlanet(); return true; }
        if (_fullPlanetScreen && CurrentSystemId != null && _systemParentSector != null)
        {
            var selected = SelectedWorldId;
            var sector = _systemParentSector;
            _systemParentSector = null;
            SwitchView(sector, animate: true);
            if (selected != null && View.Worlds.Any(w => w.Id == selected)) SelectWorld(selected);
            return true;
        }
        if (CurrentSystemId != null && SelectedWorldId is { } systemWorld && _worlds.TryGetValue(systemWorld, out var current)
            && !string.IsNullOrEmpty(current.PoliticalSectorId))
        {
            var anchor = _data.Worlds.FirstOrDefault(w => w.Overview && w.SystemId == current.SystemId
                && w.PoliticalSectorId == current.PoliticalSectorId);
            if (OpenPoliticalSector(current.PoliticalSectorId))
            {
                if (anchor != null) SelectWorld(anchor.Id);
                return true;
            }
        }
        if (CurrentSectorId != null || CurrentSystemId != null || CurrentPoliticalSectorId != null || Surface != null)
        {
            var anchor = CurrentSectorId == null ? null : _fullPlanetScreen ? UniqueAnchor(CurrentSectorId)
                : _data.Worlds.FirstOrDefault(w => w.Overview && w.Anchor && w.Sector?.Id == CurrentSectorId);
            anchor ??= _data.Worlds.FirstOrDefault(w => w.Overview && w.SystemId == CurrentSystemId);
            SwitchView(null, animate: true);
            if (anchor != null) SelectWorld(anchor.Id);
        }
        return true;
    }

    public bool OpenSector(string sectorId)
    {
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return false;
        if (_lockedSector != null && _lockedSector != sectorId || !_sectorPrototypes.Any(s => s.ID == sectorId)) return false;
        if (CurrentSectorId != sectorId || Surface != null) SwitchView(sectorId, animate: true);
        return true;
    }

    public bool OpenPoliticalSector(string id)
    {
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return false;
        if (_lockedSector != null || !_data.Worlds.Any(w => w.PoliticalSectorId == id)) return false;
        if (CurrentPoliticalSectorId != id || Surface != null) SwitchView(null, animate: true, politicalSectorId: id);
        return true;
    }

    private bool HasSystem(SWGalacticMapWorld world) => !string.IsNullOrEmpty(world.SystemId)
        && (_fullPlanetScreen || _data.Worlds.Count(w => w.SystemId == world.SystemId) > 1);

    public bool OpenSystem(string systemId)
    {
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return false;
        if (_lockedSector != null || !_data.Worlds.Any(w => w.SystemId == systemId && HasSystem(w))) return false;
        if (_fullPlanetScreen && CurrentSectorId != null) _systemParentSector = CurrentSectorId;
        if (CurrentSystemId != systemId || Surface != null) SwitchView(null, animate: true, systemId: systemId);
        return true;
    }

    private void SwitchView(string? sectorId, bool animate = false, string? systemId = null, string? politicalSectorId = null)
    {
        var fromPlanet = _planetOpen;
        _planetOpen = false;
        _selectedSchemeSector = null;
        if (Surface != null)
        {
            var saved = _surfaceSavedFlow;
            _surfaceSavedFlow = null;
            _focusedFlow = saved != null && saved.Good == _commodityFilter &&
                _data.Routes.Any(r => !IsPlannedRoute(r) && SWTransitLinkPrototype.LinkId(r.From, r.To) == saved.Edge)
                ? _economyView?.Flows.FirstOrDefault(f => f.Edge == saved.Edge && f.From == saved.From && f.To == saved.To && f.Good == saved.Good)
                : null;
        }
        Surface = null;
        _trafficToggle.Visible = _economyMapMode < 0;
        var previous = Map;
        if (_fullPlanetScreen && previous != null) _mapViews[MapKey()] = previous.CaptureView();
        var throughSector = sectorId ?? CurrentSectorId;
        var throughWorld = SelectedWorldId is { } selected && _worlds[selected].Sector?.Id == throughSector
            && (sectorId != null || _worlds[selected].Overview) ? selected
            : _fullPlanetScreen ? UniqueAnchor(throughSector)?.Id
            : _data.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == throughSector)?.Id;
        var throughSystem = systemId ?? CurrentSystemId;
        if (throughSector == null && throughSystem != null)
            throughWorld = _fullPlanetScreen
                ? SelectedWorldId is { } focused && _worlds[focused].SystemId == throughSystem ? focused : null
                : _data.Worlds.FirstOrDefault(w => w.SystemId == throughSystem && w.Overview)?.Id;
        if (throughWorld == null && fromPlanet && SelectedWorldId != null) throughWorld = SelectedWorldId;
        previous?.FinishTransition();
        CurrentSectorId = sectorId;
        CurrentSystemId = systemId;
        CurrentPoliticalSectorId = politicalSectorId;
        UpdateSystemShortcut();
        UpdateSectorSchemeControls();
        View = sectorId != null ? SWGalacticMapViews.Sector(_data, sectorId)
            : systemId != null ? SWGalacticMapViews.System(_data, systemId)
            : politicalSectorId != null ? SWGalacticMapViews.PoliticalSector(_data, politicalSectorId)
            : _fullPlanetScreen ? new SWGalacticMapPrototype
            {
                Size = _data.Size, Worlds = _data.Worlds.ToList(), Routes = _data.Routes.ToList(),
                Regions = _data.Regions, Corridors = _data.Corridors, PoliticalAreas = _data.PoliticalAreas,
            } : SWGalacticMapViews.Overview(_data);
        if (sectorId == null && systemId == null && politicalSectorId == null) View = IncludeOverviewFlow(View);
        View = MakeSchematicIfNeeded(View, sectorId, systemId, politicalSectorId);
        _galaxyButton.Text = systemId != null && (_systemParentSector != null || _data.Worlds.Any(w => w.SystemId == systemId && !string.IsNullOrEmpty(w.PoliticalSectorId))) ? "К сектору" : "К галактике";
        _galaxyButton.Visible = _lockedSector == null;
        _galaxyButton.Disabled = sectorId == null && systemId == null && politicalSectorId == null;
        _body.RemoveAllChildren();
        Map = new SWGalacticMapControl(View, sectorId, _lockedSector == null, systemId, politicalSectorId);
        if (_fullPlanetScreen && previous != null) Map.RestoreTrafficAnimationSeconds(previous.TrafficAnimationSeconds);
        if (_fullPlanetScreen && _mapViews.TryGetValue(MapKey(), out var savedView)) Map.RestoreView(savedView.Zoom, savedView.Pan);
        Map.SetExternalLabels(_fullPlanetScreen);
        if (_fullPlanetScreen)
        {
            Map.SetSectorSchemeNames(_sectorPrototypes.ToDictionary(s => s.ID, s => s.Name));
            Map.SetSectorScheme(_showSectorScheme);
            Map.SectorSelected += SelectSectorScheme;
        }
        Map.SetState(_sectors);
        Map.SetFinanceSectors(_financeSectors);
        Map.SetEconomy(_economyView);
        Map.SetEconomyMapMode(_economyMapMode, _commodityFilter);
        Map.SetMedicalSupplyVisible(_showMedicalSupply);
        Map.MedicalSupplySelected += SelectMedicalShipment;
        Map.SetCrises(_mapCrises, _data.Worlds);
        Map.CrisesSelected += ids => { SelectCrises(ids); _statisticsAtlas?.ShowDetails(); };
        Map.SetTrafficFilter(_commodityFilter, _showTraffic);
        RefreshEconomyStatus();
        Map.SystemSelected += systemId =>
        {
            var sectors = _data.Worlds.Where(w => w.SystemId == systemId).Select(w => w.Sector?.Id).Distinct().ToArray();
            if (sectors.Length == 1 && sectors[0] is { } systemSector && OpenSector(systemSector))
            {
                _chosenSectorSystem = systemId;
                _chosenSystemSector = systemSector;
                _systemSummaryActive = true;
                SelectedWorldId = null;
                _route = null;
                Map.ClearWorldSelection();
                UpdateSystemShortcut();
                RefreshDetails();
                _statisticsAtlas?.ShowDetailsWhenReady();
                return;
            }
            OpenSystem(systemId);
        };
        Map.WorldSelected += id =>
        {
            if (CurrentSectorId == _chosenSystemSector && _worlds[id].SystemId != _chosenSectorSystem)
            {
                _chosenSectorSystem = null;
                _chosenSystemSector = null;
                UpdateSystemShortcut();
            }
            _selectedSchemeSector = null;
            SelectWorld(id);
            _statisticsAtlas?.ShowDetails();
        };
        Map.WorldActivated += id =>
        {
            if (_fullPlanetScreen && !_financeOnly && _lockedSector == null)
            {
                var activated = _worlds[id];
                if (CurrentSystemId != null && activated.SystemId == CurrentSystemId) { OpenPlanet(id); return; }
                if (CurrentSectorId != null && activated.Sector?.Id == CurrentSectorId)
                {
                    if (HasSystem(activated) && OpenSystem(activated.SystemId)) SelectWorld(id, true);
                    else OpenPlanet(id);
                    return;
                }
            }
            if (_viewerFaction == "empire" && CurrentSystemId != null && _worlds[id].SystemId == CurrentSystemId) { if (!_financeOnly) OpenSurface(id); }
            else if (_viewerFaction == "empire" && CurrentPoliticalSectorId != null && _worlds[id].PoliticalSectorId == CurrentPoliticalSectorId && !HasSystem(_worlds[id])) { if (!_financeOnly) OpenSurface(id); }
            else if (_viewerFaction == "empire" && CurrentSystemId == null && CurrentPoliticalSectorId == null && !string.IsNullOrEmpty(_worlds[id].PoliticalSectorId) && OpenPoliticalSector(_worlds[id].PoliticalSectorId)) SelectWorld(id, true);
            else if (_viewerFaction == "empire" && CurrentSystemId == null && HasSystem(_worlds[id]) && OpenSystem(_worlds[id].SystemId)) SelectWorld(id, true);
            else if (CurrentSectorId != null && _worlds[id].Sector?.Id == CurrentSectorId) { if (!_financeOnly) OpenSurface(id); }
            else if (_worlds[id].Sector is { } sector && OpenSector(sector.Id)) SelectWorld(id, true);
            else if (CurrentSystemId == null && CurrentPoliticalSectorId == null && !string.IsNullOrEmpty(_worlds[id].PoliticalSectorId) && OpenPoliticalSector(_worlds[id].PoliticalSectorId)) SelectWorld(id, true);
            else if (CurrentSystemId != _worlds[id].SystemId && HasSystem(_worlds[id]) && OpenSystem(_worlds[id].SystemId)) SelectWorld(id, true);
            else if (CurrentSystemId != null && _worlds[id].SystemId != CurrentSystemId && _worlds[id].Overview && ShowGalaxy()) SelectWorld(id, true);
        };
        Map.RouteSelected += route =>
        {
            _systemSummaryActive = false;
            _selectedSchemeSector = null;
            if (_focusedFlow != null && (_fullPlanetScreen && IsPlannedRoute(route) || _focusedFlow.Edge != SWTransitLinkPrototype.LinkId(route.From, route.To)))
            {
                _focusedFlow = null;
                Map.SetFocusedTraffic(null, null, null, null);
            }
            _selectedMedicalShipmentId = null;
            ClearCrisisSelection();
            _route = route;
            SelectedWorldId = null;
            RefreshDetails();
            _statisticsAtlas?.ShowDetails();
        };
        Map.ViewChanged += () => _zoom.Text = $"{Map.Zoom * 100:0}%";
        _zoom.Text = "100%";
        _detail = VerticalStack(8);
        _detailHeading = VerticalStack(6);
        var sidebar = VerticalStack(8);
        sidebar.Name = "MapDetails";
        _atlasSidebar = sidebar;
        sidebar.VerticalExpand = true;
        sidebar.AddChild(_detailHeading);
        var scroll = new ScrollContainer { Name = "MapDetailsScroll", HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(_detail);
        _detailScroll = scroll;
        sidebar.AddChild(scroll);
        if (_fullPlanetScreen)
        {
            _statisticsAtlas = new SWStatisticsAtlasLayout(Map, sidebar, deferHeight: true);
            _body.AddChild(_statisticsAtlas);
        }
        else
        {
            _statisticsAtlas = null;
            _columns = new SWAtlasLayout(Map, sidebar, stackBelow: _compactEconomy ? 0 : 680, minDetails: _compactEconomy ? 220 : 300, minMapHeight: _compactEconomy ? 150 : 220) { VerticalExpand = true };
            _body.AddChild(_columns);
        }
        _title.Text = sectorId != null ? (_financeOnly ? SectorName(sectorId) : $"Сектор: {SectorName(sectorId)}")
            : systemId != null ? _data.Worlds.First(w => w.SystemId == systemId).SystemName
            : politicalSectorId != null ? _data.Worlds.First(w => w.PoliticalSectorId == politicalSectorId).PoliticalSectorName : "Карта галактики";
        _schematicNotice.Visible = _schematicPlacement;
        var initial = _fullPlanetScreen && sectorId != null ? UniqueAnchor(sectorId)
            : _fullPlanetScreen && systemId != null ? View.Worlds.FirstOrDefault(w => w.Id == SelectedWorldId && w.SystemId == systemId)
            : View.Worlds.FirstOrDefault(w => w.Anchor && (sectorId == null ? w.Id == "coruscant" : w.Sector?.Id == sectorId))
              ?? View.Worlds.FirstOrDefault(w => !IsPublicSector(w));
        if (initial != null) SelectWorld(initial.Id);
        else if (_fullPlanetScreen)
        {
            SelectedWorldId = null;
            _route = null;
            RefreshDetails();
        }
        RestoreFocusedFlow();
        if (animate && previous != null && throughWorld != null)
            Map.TransitionFrom(previous, throughWorld, !fromPlanet && (sectorId != null || systemId != null || politicalSectorId != null));
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
        _searchWorlds = (_lockedSector == null ? _data.Worlds.Where(w => !IsPublicSector(w)) : View.Worlds.Where(w => !IsPublicSector(w))).OrderBy(w => w.Name).ToArray();
        for (var i = 0; i < _searchWorlds.Length; i++)
        {
            var world = _searchWorlds[i];
            var kind = _lockedSector == null && world.Anchor && world.Sector != null ? "сектор"
                : _lockedSector != null && world.Sector?.Id != _lockedSector ? "выход" : world.Kind.ToLowerInvariant();
            _search.AddItem($"{world.Name} ({kind})", i);
        }
    }

    public void SetState(IReadOnlyList<SWSectorEntry> sectors, IReadOnlyList<SWGlobalStatEntry> globals)
    {
        var sectorId = SelectedWorldId is { } id && _worlds.TryGetValue(id, out var world) ? world.Sector?.Id : null;
        var selectedBefore = _sectors.FirstOrDefault(s => s.Id == _selectedSchemeSector);
        var selectedAfter = sectors.FirstOrDefault(s => s.Id == _selectedSchemeSector);
        var schemeChanged = selectedBefore?.Name != selectedAfter?.Name || selectedBefore?.Description != selectedAfter?.Description ||
            selectedBefore?.Representative != selectedAfter?.Representative || selectedBefore?.Claimed != selectedAfter?.Claimed || selectedBefore?.Shipyard != selectedAfter?.Shipyard;
        var summaryChanged = sectorId != null && _sectors.Any(s => s.Id == sectorId) != sectors.Any(s => s.Id == sectorId);
        var detailsChanged = _worldSituationChanged || !WorldStatsKey(_sectors, _globals, sectorId).SequenceEqual(WorldStatsKey(sectors, globals, sectorId));
        _worldSituationChanged = false;
        _sectors = sectors;
        _globals = globals;
        Map.SetState(sectors);
        if (_selectedSchemeSector != null && schemeChanged) RefreshDetails();
        if (_selectedSchemeSector == null && Surface == null && _detailPage == 3 && _route == null && _selectedCrisisIds.Length == 0 &&
            (_planetOpen ? summaryChanged : detailsChanged)) RefreshDetails();
    }

    // External navigation replaces an in-flight transition atomically, without a delayed callback.
    public bool FocusWorldImmediately(string id)
    {
        if (!_fullPlanetScreen || _lockedSector != null || !_worlds.TryGetValue(id, out var world) || IsPublicSector(world))
            return false;
        Map.FinishTransition();
        _systemParentSector = null;
        if (_viewerFaction == "empire" && HasSystem(world)) SwitchView(null, systemId: world.SystemId);
        else if (_viewerFaction == "empire" && !string.IsNullOrEmpty(world.PoliticalSectorId)) SwitchView(null, politicalSectorId: world.PoliticalSectorId);
        else if (world.Sector is { } sector) SwitchView(sector.Id);
        else if (!world.Overview && HasSystem(world)) SwitchView(null, systemId: world.SystemId);
        else if (!world.Overview && !string.IsNullOrEmpty(world.PoliticalSectorId)) SwitchView(null, politicalSectorId: world.PoliticalSectorId);
        else SwitchView(null);
        if (!View.Worlds.Any(w => w.Id == id)) return false;
        SelectWorld(id, true);
        return true;
    }

    public void SelectWorld(string id, bool focus = false)
    {
        _systemSummaryActive = false;
        _selectedSchemeSector = null;
        _selectedMedicalShipmentId = null;
        if (!_worlds.TryGetValue(id, out var world) || IsPublicSector(world)) return;
        if (Surface != null && SelectedWorldId != id) SwitchView(CurrentSectorId);
        if (!View.Worlds.Any(w => w.Id == id))
        {
            if (_lockedSector != null) return;
            if (_viewerFaction == "empire" && HasSystem(world)) OpenSystem(world.SystemId);
            else if (_viewerFaction == "empire" && !string.IsNullOrEmpty(world.PoliticalSectorId)) OpenPoliticalSector(world.PoliticalSectorId);
            else if (world.Sector is { } sector) OpenSector(sector.Id);
            else if (!world.Overview && HasSystem(world)) OpenSystem(world.SystemId);
            else if (!world.Overview && !string.IsNullOrEmpty(world.PoliticalSectorId)) OpenPoliticalSector(world.PoliticalSectorId);
            else ShowGalaxy();
            if (!View.Worlds.Any(w => w.Id == id)) return;
        }
        if (SelectedWorldId != id) _detailScroll?.SetScrollValue(default);
        SelectedWorldId = id;
        ClearCrisisSelection();
        _route = null;
        Map.SelectWorld(id, focus);
        RestoreFocusedFlow();
        RefreshDetails();
    }

    private bool IsPlannedRoute(SWGalacticMapRoute route) => route.Proposed &&
        (_economyView is not { Ready: true } || !_economyView.Links.Any(link => link.Edge == SWTransitLinkPrototype.LinkId(route.From, route.To)));

    private void SelectSectorScheme(string sectorId)
    {
        _systemSummaryActive = false;
        _selectedSchemeSector = sectorId;
        SelectedWorldId = null;
        _route = null;
        _focusedFlow = null;
        Map.SetFocusedTraffic(null, null, null, null);
        ClearCrisisSelection();
        _detailScroll?.SetScrollValue(default);
        RefreshDetails();
        _statisticsAtlas?.ShowDetails();
    }

    private void RefreshSectorSchemeDetails(string sectorId)
    {
        _detailHeading.RemoveAllChildren();
        _detail.RemoveAllChildren();
        _detailHeading.AddChild(AtlasWrap(SectorName(sectorId), SWConsoleStyle.Brass));
        _detail.AddChild(AtlasWrap("Схематические границы секторов", SWConsoleStyle.TextDim));
        var anchor = UniqueAnchor(sectorId);
        if (anchor != null)
        {
            var image = new SWStatisticsPlanetSymbol(anchor) { SetSize = new Vector2(64, 64), HorizontalAlignment = Control.HAlignment.Center };
            _detail.AddChild(image);
            _detail.AddChild(AtlasWrap(image.HasTexture ? $"Опорный мир: {anchor.Name}" : $"Опорный мир: {anchor.Name}. Изображение недоступно.", SWConsoleStyle.TextDim));
        }
        else _detail.AddChild(AtlasWrap("Единственный опорный мир не определён.", SWConsoleStyle.TextDim));
        var sector = _sectors.FirstOrDefault(s => s.Id == sectorId);
        if (sector != null)
        {
            if (!string.IsNullOrWhiteSpace(sector.Description)) _detail.AddChild(AtlasWrap(sector.Description, SWConsoleStyle.TextBright));
            _detail.AddChild(AtlasWrap(sector.Claimed ? "Представлен в Сенате" : "Сенатор не назначен", SWConsoleStyle.TextDim));
            if (!string.IsNullOrWhiteSpace(sector.Representative)) _detail.AddChild(AtlasWrap($"Представитель: {sector.Representative}", SWConsoleStyle.TextBright));
        }
        else _detail.AddChild(AtlasWrap("Сведения о представительстве недоступны.", SWConsoleStyle.TextDim));
        var open = SWConsoleStyle.TabButton("Открыть сектор");
        open.OnPressed += _ => OpenSector(sectorId);
        _detail.AddChild(open);
        if (_viewerFaction == "republic" && sector != null && SectorSummaryRequested != null)
        {
            var summary = SWConsoleStyle.TabButton("Сводка сектора");
            summary.OnPressed += _ => SectorSummaryRequested?.Invoke(sectorId);
            _detail.AddChild(summary);
        }
    }

    public bool SelectEconomyRoute(string id)
    {
        var route = _data.Routes.FirstOrDefault(r => SWTransitLinkPrototype.LinkId(r.From, r.To) == id);
        if (route == null || _fullPlanetScreen && IsPlannedRoute(route) || !_worlds.TryGetValue(route.From, out var world)) return false;
        if (_focusedFlow != null && _focusedFlow.Edge != id) _focusedFlow = null;
        if (world.Sector is { } sector) { if (!OpenSector(sector.Id)) return false; }
        else if (!ShowGalaxy()) return false;
        route = View.Routes.FirstOrDefault(r => SWTransitLinkPrototype.LinkId(r.From, r.To) == id);
        if (route == null) return false;
        _selectedMedicalShipmentId = null;
        ClearCrisisSelection();
        _route = route;
        SelectedWorldId = null;
        Map.ResetView();
        Map.SelectRoute(route);
        RefreshDetails();
        return true;
    }

    public string? SelectedSectorName => SelectedWorldId is { } id && _worlds[id].Sector is { } sector ? SectorName(sector.Id) : null;

    private string SectorName(string id) => GetSectorName(id);

    public void RefreshEconomySelection() => RefreshDetails();

    private void RefreshDetails()
    {
        var scroll = _detailScroll;
        var position = scroll?.GetScrollValue();
        try { RefreshDetailsCore(); }
        finally
        {
            if (scroll == _detailScroll && position is { } saved) scroll?.SetScrollValue(saved);
            if (_fullPlanetScreen) RefreshEconomyStatus();
        }
    }

    private void RefreshDetailsCore()
    {
        if (_systemSummaryActive && CurrentSectorId == _chosenSystemSector && _chosenSectorSystem is { } systemId && Surface == null && !_planetOpen)
        {
            _detailHeading.RemoveAllChildren();
            _detail.RemoveAllChildren();
            var members = _data.Worlds.Where(w => w.SystemId == systemId && w.Sector?.Id == CurrentSectorId).ToArray();
            var name = members.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.SystemName))?.SystemName;
            _detailHeading.AddChild(AtlasWrap(string.IsNullOrWhiteSpace(name) ? "Система" : name, SWConsoleStyle.Brass));
            _detailHeading.AddChild(AtlasWrap($"Сектор: {SectorName(CurrentSectorId!)}", SWConsoleStyle.TextDim));
            var open = SWConsoleStyle.TabButton("Открыть систему");
            open.OnPressed += _ => OpenSystem(systemId);
            _detailHeading.AddChild(open);
            foreach (var world in members) WorldButton(world.Id);
            return;
        }
        if (_selectedSchemeSector is { } sectorId && Surface == null && !_planetOpen)
        {
            RefreshSectorSchemeDetails(sectorId);
            return;
        }
        if (EconomyDetailsOverride != null && Surface == null
            && (SelectedWorldId is not { } selected || CanReadWorldState(_worlds[selected]))
            && (_route == null || CanReadWorldState(_worlds[_route.From]) && CanReadWorldState(_worlds[_route.To]))
            && (_selectedCrisisIds.Length == 0 || _selectedCrisisIds.All(id => id.StartsWith("economy:event:", StringComparison.Ordinal))))
        {
            _detailHeading.RemoveAllChildren();
            _detail.RemoveAllChildren();
            EconomyDetailsOverride(_detailHeading, _detail, SelectedWorldId, _route);
            SelectionChanged?.Invoke();
            return;
        }
        if (RefreshMedicalShipment()) return;
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
