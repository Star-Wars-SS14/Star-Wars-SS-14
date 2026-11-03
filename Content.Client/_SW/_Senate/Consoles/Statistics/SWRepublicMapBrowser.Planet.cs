using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._SW._Senate.Consoles.Economy;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Timing;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client._SW._Senate.Consoles.Statistics.SWStatisticsWidgets;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWRepublicMapBrowser
{
    private bool _planetOpen;
    private float _pageReveal = 1;
    private SWStatisticsPlanetLayout? _planetLayout;
    private string? _systemParentSector;
    private bool _schematicPlacement;
    private bool _overviewFlowNoGeometry;
    private readonly Dictionary<string, (float Zoom, Vector2 Pan)> _mapViews = new();
    private string MapKey() => $"{CurrentSectorId}|{CurrentSystemId}|{CurrentPoliticalSectorId}";
    private bool _surfaceReturnsToPlanet;
    private string? _surfaceReturnSystem;
    private string? _surfaceReturnSector;
    private SWEconomyRouteFlow? _focusedFlow;
    private SWEconomyRouteFlow? _surfaceSavedFlow;
    private ScrollContainer? _detailScroll;
    private BoxContainer? _planetPaths;
    private ScrollContainer? _planetPathScroll;
    private string _planetPathsSignature = string.Empty;

    private SWGalacticMapPrototype IncludeOverviewFlow(SWGalacticMapPrototype view)
    {
        _overviewFlowNoGeometry = false;
        if (!_fullPlanetScreen || _focusedFlow == null) return view;
        var flow = _focusedFlow;
        var route = _data.Routes.FirstOrDefault(r => SWTransitLinkPrototype.LinkId(r.From, r.To) == flow.Edge);
        if (route == null || route.Proposed || !_worlds.TryGetValue(flow.From, out var from) || !_worlds.TryGetValue(flow.To, out var to) ||
            Vector2.DistanceSquared(from.Position, to.Position) < .0001f)
        {
            _overviewFlowNoGeometry = true;
            return view;
        }
        if (!view.Worlds.Any(w => w.Id == from.Id)) view.Worlds.Add(from);
        if (!view.Worlds.Any(w => w.Id == to.Id)) view.Worlds.Add(to);
        if (!view.Routes.Any(r => SWTransitLinkPrototype.LinkId(r.From, r.To) == flow.Edge)) view.Routes.Add(route);
        return view;
    }

    private void UpdateOverviewFlowNotice()
    {
        if (!_fullPlanetScreen || CurrentSectorId != null || CurrentSystemId != null || CurrentPoliticalSectorId != null || Surface != null || _planetOpen || !_showTraffic)
        {
            _flowGeometryNotice.Visible = false;
            return;
        }
        _flowGeometryNotice.Visible = _overviewFlowNoGeometry || _economyView is { Ready: true } view && view.Flows.Any(flow =>
            (flow.UnitsPerHour > 0 || flow.InTransit > 0) && (_commodityFilter == null || flow.Good == _commodityFilter) &&
            (!_worlds.TryGetValue(flow.From, out var from) || !_worlds.TryGetValue(flow.To, out var to) ||
             Vector2.DistanceSquared(from.Position, to.Position) < .0001f ||
             !_data.Routes.Any(route => !route.Proposed && SWTransitLinkPrototype.LinkId(route.From, route.To) == flow.Edge)));
    }

    // When a local coordinate is absent, use an explicitly schematic graph rather than
    // silently putting unrelated worlds at the same invented coordinate.
    private SWGalacticMapPrototype MakeSchematicIfNeeded(SWGalacticMapPrototype view, string? sector, string? system, string? political)
    {
        _schematicPlacement = false;
        if (!_fullPlanetScreen || sector == null && system == null && political == null) return view;
        bool Own(SWGalacticMapWorld world) => sector != null ? world.Sector?.Id == sector
            : system != null ? world.SystemId == system : world.PoliticalSectorId == political;
        var worlds = view.Worlds.Where(Own).OrderBy(w => w.Id, StringComparer.Ordinal).ToArray();
        if (worlds.Length == 0 || worlds.All(w => w.LocalPosition is { } p &&
                float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X <= view.Size.X && p.Y <= view.Size.Y) &&
            worlds.Select(w => w.LocalPosition!.Value).Distinct().Count() == worlds.Length) return view;
        _schematicPlacement = true;
        var columns = Math.Max(1, (int) Math.Ceiling(Math.Sqrt(worlds.Length)));
        var rows = (worlds.Length + columns - 1) / columns;
        for (var i = 0; i < worlds.Length; i++)
            worlds[i].Position = new Vector2(20 + (i % columns + .5f) * 80 / columns, 20 + (i / columns + .5f) * 60 / rows);
        var byId = view.Worlds.ToDictionary(w => w.Id);
        foreach (var route in view.Routes)
            if (byId.TryGetValue(route.From, out var a) && byId.TryGetValue(route.To, out var b))
                route.Control = (a.Position + b.Position) / 2;
        return view;
    }

    private RichTextLabel AtlasWrap(string text, Color color, float width = 660) => _fullPlanetScreen
        ? SWStatisticsText.Wrap(text, color, width) : SWConsoleStyle.Wrap(text, color, width);

    private SWGalacticMapWorld? UniqueAnchor(string? sector)
    {
        if (sector == null) return null;
        var anchors = _data.Worlds.Where(w => w.Anchor && w.Sector?.Id == sector).Take(2).ToArray();
        return anchors.Length == 1 ? anchors[0] : null;
    }

    private void RestoreFocusedFlow()
    {
        if (_focusedFlow is not { } flow || Map == null || Surface != null) return;
        var edge = flow.Edge;
        Map.SetFocusedTraffic(edge, flow.From, flow.To, flow.Good);
        var route = View.Routes.FirstOrDefault(r => SWTransitLinkPrototype.LinkId(r.From, r.To) == edge);
        if (route != null) Map.HighlightRoute(route);
    }

    public bool OpenPlanet(string id, bool returning = false, Vector2? originOverride = null)
    {
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return false;
        if (!_fullPlanetScreen || !_worlds.TryGetValue(id, out var world) || IsPublicSector(world)) return false;
        if (_planetOpen) return SelectedWorldId == id;
        if (!View.Worlds.Any(w => w.Id == id)) SelectWorld(id);
        if (!View.Worlds.Any(w => w.Id == id)) return false;
        var displayed = View.Worlds.First(w => w.Id == id);
        Vector2? origin = originOverride ?? (Map is { Parent: not null } map && map.Width > 0 && map.Height > 0
            ? map.GlobalPosition + map.MapToLocal(displayed.Position) - _body.GlobalPosition : null);
        SelectedWorldId = id;
        _route = null;
        Surface = null;
        _planetOpen = true;
        _galaxyButton.Visible = _lockedSector == null;
        _galaxyButton.Disabled = false;
        _galaxyButton.Text = CurrentSystemId != null ? "Назад к системе" : CurrentSectorId != null ? "Назад к сектору" : "Назад к галактике";
        _title.Text = $"{world.Name} / Мир";

        var left = VerticalStack(8);
        left.HorizontalExpand = left.VerticalExpand = true;
        left.AddChild(SWStatisticsText.Wrap(world.Name, SWConsoleStyle.Brass, 240));
        var symbol = new SWStatisticsPlanetSymbol { HorizontalExpand = true, MinHeight = 140, MaxHeight = 180 };
        left.AddChild(symbol);
        left.AddChild(SWStatisticsText.Wrap("Межпланетные перевозки", SWConsoleStyle.TextBright, 240));
        _planetPaths = VerticalStack(5);
        _planetPathsSignature = string.Empty;
        var pathsScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        pathsScroll.AddChild(_planetPaths);
        _planetPathScroll = pathsScroll;
        left.AddChild(pathsScroll);
        if (CanOpenSurface(id))
        {
            var surface = SWConsoleStyle.TabButton("Открыть поверхность");
            surface.OnPressed += _ => OpenSurface(id);
            left.AddChild(surface);
        }
        else left.AddChild(SWStatisticsText.Wrap("Данные поверхности недоступны", SWConsoleStyle.TextDim, 240));

        _detailHeading = VerticalStack(6);
        _detail = VerticalStack(8);
        var right = VerticalStack(8);
        right.HorizontalExpand = right.VerticalExpand = true;
        right.AddChild(_detailHeading);
        _detailScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        _detailScroll.AddChild(_detail);
        right.AddChild(_detailScroll);
        _body.RemoveAllChildren();
        _statisticsAtlas = null;
        _planetLayout = new SWStatisticsPlanetLayout(left, right, symbol, origin, returning ? .22f : .30f);
        _body.AddChild(_planetLayout);
        RefreshDetails();
        return true;
    }

    public bool TryNavigateBack()
    {
        if (_statisticsAtlas?.CloseDrawer() == true) return true;
        if (_fullPlanetScreen && Map != null && Map.IsTransitioning) return true;
        if (_selectedCrisisIds.Length > 0)
        {
            ClearCrisisSelection();
            RefreshDetails();
            return true;
        }
        if (!_planetOpen && Surface == null && CurrentSectorId == null && CurrentSystemId == null && CurrentPoliticalSectorId == null)
            return false;
        return ShowGalaxy();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_fullPlanetScreen || !VisibleInTree) return;
        var reduced = IoCManager.Resolve<IConfigurationManager>().GetCVar(CCVars.ReducedMotion);
        _pageReveal = reduced ? 1 : Math.Min(1, _pageReveal + Math.Max(0, args.DeltaSeconds) / .15f);
        _detail.Modulate = Color.White.WithAlpha(_pageReveal);
        var sidebar = _atlasSidebar;
        if (sidebar == null || Surface != null || _planetOpen) return;
        var phase = Map.TransitionProgress;
        var alpha = Map.IsTransitioning ? Math.Clamp((phase - .61f) / .39f, 0, 1) : 1;
        sidebar.Modulate = Color.White.WithAlpha(alpha);
    }

    private void ReturnToPlanetFromSurface()
    {
        var world = SelectedWorldId;
        Vector2? origin = Surface is { } surface ? surface.GlobalPosition + surface.Size / 2 - _body.GlobalPosition : null;
        var system = _surfaceReturnSystem;
        var sector = _surfaceReturnSector;
        _surfaceReturnsToPlanet = false;
        if (system != null) SwitchView(null, systemId: system);
        else if (sector != null) SwitchView(sector);
        else SwitchView(null);
        if (world != null) OpenPlanet(world, returning: true, originOverride: origin);
    }

    private void BackFromPlanet()
    {
        var world = SelectedWorldId;
        var system = CurrentSystemId;
        var sector = CurrentSectorId;
        var political = CurrentPoliticalSectorId;
        SwitchView(sector, animate: true, systemId: system, politicalSectorId: political);
        if (world != null) SelectWorld(world);
    }

    private void PlanetWorldDetails(SWGalacticMapWorld world)
    {
        _detail.AddChild(SWStatisticsText.Wrap($"Тип: {world.Kind}", SWConsoleStyle.TextBright, 500));
        _detail.AddChild(SWStatisticsText.Wrap($"Принадлежность: {_data.GetWorldPoliticalName(world)}", SWConsoleStyle.TextBright, 500));
        if (!string.IsNullOrEmpty(world.SystemName)) _detail.AddChild(SWStatisticsText.Wrap($"Система: {world.SystemName}", SWConsoleStyle.TextBright, 500));
        if (world.Sector is { } owner)
        {
            _detail.AddChild(SWStatisticsText.Wrap($"Представительство: {SectorName(owner.Id)}", SWConsoleStyle.TextBright, 500));
            if (SectorSummaryRequested != null && _sectors.Any(s => s.Id == owner.Id))
            {
                var summary = SWConsoleStyle.TabButton("Сводка сектора");
                summary.OnPressed += _ => SectorSummaryRequested?.Invoke(owner.Id);
                _detail.AddChild(summary);
            }
        }
        if (!string.IsNullOrWhiteSpace(world.Description))
            _detail.AddChild(SWStatisticsText.Wrap(world.Description, SWConsoleStyle.TextBright, 500));
    }

    private void RefreshPlanetPaths()
    {
        if (!_planetOpen || _planetPaths == null || SelectedWorldId is not { } id) return;
        var visibleFlows = VisibleFlows().Where(f => f.From == id || f.To == id).ToArray();
        var signature = $"{id}|{_viewerFaction}|{_commodityFilter}|{_showTraffic}|{_economyView?.Ready}|{_economyView?.Paused}|{_focusedFlow?.Edge}|{_focusedFlow?.From}|{_focusedFlow?.To}|{_focusedFlow?.Good}|" +
            string.Join(";", visibleFlows.Select(f => $"{f.Edge}:{f.From}:{f.To}:{f.Good}:{f.UnitsPerHour}:{f.InTransit}"));
        if (_planetPathsSignature == signature) return;
        _planetPathsSignature = signature;
        var position = _planetPathScroll?.GetScrollValue();
        _planetPaths.RemoveAllChildren();
        if (!_showTraffic)
        {
            _planetPaths.AddChild(SWStatisticsText.Wrap("Слой потоков выключен", SWConsoleStyle.TextDim, 240));
            return;
        }
        var economy = _economyView;
        if (economy is not { Ready: true })
        {
            _planetPaths.AddChild(SWStatisticsText.Wrap("Ожидание опубликованных перевозок", SWConsoleStyle.TextDim, 240));
            return;
        }
        if (!_worlds.TryGetValue(id, out var world) || !CanReadWorldState(world))
        {
            _planetPaths.AddChild(SWStatisticsText.Wrap("Данные перевозок недоступны", SWConsoleStyle.TextDim, 240));
            return;
        }
        var flows = visibleFlows;
        if (flows.Length == 0)
        {
            _planetPaths.AddChild(SWStatisticsText.Wrap("Нет опубликованных перевозок выбранного товара", SWConsoleStyle.TextDim, 240));
            return;
        }
        foreach (var inbound in new[] { true, false })
        {
            var direction = flows.Where(f => inbound ? f.To == id : f.From == id).ToArray();
            if (direction.Length == 0) continue;
            _planetPaths.AddChild(SWConsoleStyle.Section(inbound ? "Входящие" : "Исходящие"));
            foreach (var flow in direction)
            {
                var selected = _focusedFlow != null && _focusedFlow.Edge == flow.Edge &&
                    _focusedFlow.From == flow.From && _focusedFlow.To == flow.To && _focusedFlow.Good == flow.Good;
                var button = new SWConsoleButton { HorizontalExpand = true };
                button.Label.Visible = false;
                button.AddChild(SWStatisticsText.Wrap($"{WorldName(flow.From)} → {WorldName(flow.To)}", SWConsoleStyle.TextBright, 240));
                button.ToolTip = $"{WorldName(flow.From)} → {WorldName(flow.To)}\n{GoodName(flow.Good)}: {Number(flow.UnitsPerHour)} ед./ч; в пути {Number(flow.InTransit)} ед.";
                button.SetAppearance(SWConsoleStyle.TextBright, selected: selected, quiet: true);
                button.OnPressed += _ =>
                {
                    if (_planetLayout?.IsTransitioning == true) return;
                    SelectCommodity(flow.Good); _focusedFlow = flow; RefreshDetails();
                };
                _planetPaths.AddChild(button);
                _planetPaths.AddChild(new SWStatisticsFlowLine { Highlighted = selected, Paused = economy.Paused });
                _planetPaths.AddChild(SWStatisticsText.Wrap($"{GoodName(flow.Good)} · {Number(flow.UnitsPerHour)} ед./ч · в пути {Number(flow.InTransit)}", SWConsoleStyle.TextDim, 240));
            }
        }
        if (position is { } saved) _planetPathScroll?.SetScrollValue(saved);
    }
}

/// <summary>Neutral symbol; it does not claim to depict an actual world.</summary>
public sealed class SWStatisticsPlanetSymbol : Control
{
    public SWStatisticsPlanetSymbol() { MouseFilter = MouseFilterMode.Ignore; }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var center = PixelSize / 2;
        var radius = MathF.Max(0, MathF.Min(PixelWidth, PixelHeight) * .38f);
        handle.DrawCircle(center, radius, Color.ToSrgb(Color.FromHex("#173644")));
        handle.DrawCircle(center, radius, Color.ToSrgb(SWConsoleStyle.Edge), false);
        handle.DrawCircle(center, radius * .68f, Color.ToSrgb(Color.FromHex("#315864")), false);
    }
}

public sealed class SWStatisticsPlanetLayout : Container
{
    private const float Gap = 16;
    private readonly float _duration;
    private float _width;
    private float _elapsed;
    private readonly SWStatisticsPlanetSymbol _symbol;
    private readonly Vector2? _origin;
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    public bool IsTransitioning => _elapsed < _duration;
    public SWStatisticsPlanetLayout(Control left, Control right, SWStatisticsPlanetSymbol symbol, Vector2? origin, float duration)
    {
        _duration = duration;
        _symbol = symbol;
        _origin = origin;
        if (_configuration.GetCVar(CCVars.ReducedMotion)) _elapsed = _duration;
        else { _symbol.Modulate = Color.White.WithAlpha(0); right.Modulate = Color.White.WithAlpha(0); }
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        AddChild(left); AddChild(right);
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree || !IsTransitioning) return;
        _elapsed = _configuration.GetCVar(CCVars.ReducedMotion) ? _duration : Math.Min(_duration, _elapsed + Math.Max(0, args.DeltaSeconds));
        var t = _elapsed / _duration;
        _symbol.Modulate = Color.White.WithAlpha(Math.Clamp((t - .45f) / .55f, 0, 1));
        Children[1].Modulate = Color.White.WithAlpha(Math.Clamp((t - .35f) / .65f, 0, 1));
    }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_origin is not { } origin || !IsTransitioning || _symbol.Width <= 0 || _symbol.Height <= 0) return;
        var t = _elapsed / _duration;
        var smooth = t * t * (3 - 2 * t);
        var target = _symbol.GlobalPosition - GlobalPosition + _symbol.Size / 2;
        var center = Vector2.Lerp(origin, target, smooth) * UIScale;
        var radius = (5 + (MathF.Min(_symbol.Width, _symbol.Height) * .38f - 5) * smooth) * UIScale;
        var alpha = 1 - Math.Clamp((t - .45f) / .55f, 0, 1);
        handle.DrawCircle(center, Math.Max(0, radius), Color.ToSrgb(Color.FromHex("#315864").WithAlpha(alpha)));
        handle.DrawCircle(center, Math.Max(0, radius), Color.ToSrgb(SWConsoleStyle.Brass.WithAlpha(alpha)), false);
    }
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _width = float.IsFinite(availableSize.X) ? Math.Max(0, availableSize.X) : 1000;
        var height = float.IsFinite(availableSize.Y) ? Math.Max(0, availableSize.Y) : 500;
        var left = Math.Max(0, (_width - Gap) * .28f);
        Children[0].Measure(new Vector2(left, height));
        Children[1].Measure(new Vector2(Math.Max(0, _width - Gap - left), height));
        return new Vector2(_width, height);
    }
    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var width = Math.Min(_width, finalSize.X);
        var left = Math.Max(0, (width - Gap) * .28f);
        Children[0].Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(left, finalSize.Y)));
        Children[1].Arrange(UIBox2.FromDimensions(new Vector2(left + Gap, 0), new Vector2(Math.Max(0, width - Gap - left), finalSize.Y)));
        return finalSize;
    }
}


/// <summary>Direction and aggregate flow, not a tracked vessel position.</summary>
public sealed class SWStatisticsFlowLine : Control
{
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private float _time;
    public bool Highlighted { get; init; }
    public bool Paused { get; init; }
    public SWStatisticsFlowLine() { SetHeight = 18; HorizontalExpand = true; MouseFilter = MouseFilterMode.Ignore; }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree || Paused || _configuration.GetCVar(CCVars.ReducedMotion)) return;
        _time = (_time + Math.Max(0, args.DeltaSeconds)) % 3f;
    }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var y = PixelHeight / 2f;
        var left = 4 * UIScale;
        var right = Math.Max(left, PixelWidth - 4 * UIScale);
        var color = Highlighted ? SWConsoleStyle.Brass : SWConsoleStyle.Accent;
        handle.DrawLine(new Vector2(left, y), new Vector2(right, y), color);
        handle.DrawLine(new Vector2(right - 6 * UIScale, y - 4 * UIScale), new Vector2(right, y), color);
        handle.DrawLine(new Vector2(right - 6 * UIScale, y + 4 * UIScale), new Vector2(right, y), color);
        var fraction = Paused || _configuration.GetCVar(CCVars.ReducedMotion) ? .5f : _time / 3f;
        handle.DrawCircle(new Vector2(left + (right - left) * fraction, y), 2 * UIScale, Color.ToSrgb(color));
    }
}


/// <summary>Lifecycle-bound, motion-preference-aware appearance for a new atlas level.</summary>
public sealed class SWStatisticsReveal : Container
{
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly float _duration;
    private float _elapsed;
    public SWStatisticsReveal(Control child, float duration)
    {
        _duration = duration;
        HorizontalExpand = VerticalExpand = true;
        AddChild(child);
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!VisibleInTree) return;
        _elapsed = _configuration.GetCVar(CCVars.ReducedMotion) ? _duration : Math.Min(_duration, _elapsed + Math.Max(0, args.DeltaSeconds));
        var t = _duration <= 0 ? 1 : _elapsed / _duration;
        Modulate = Color.White.WithAlpha(t * (2 - t));
    }
}
