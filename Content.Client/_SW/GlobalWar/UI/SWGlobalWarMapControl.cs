using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>HoloNet planet graph with Senate atlas rendering, selection and camera controls.</summary>
public sealed partial class SWGlobalWarMapControl : Control
{
    private const float Padding = 48f;
    private const float HitRadius = 16f;
    private const float DragThreshold = 5f;
    private const int CurvePointCount = 49;
    private readonly IPrototypeManager _prototypes;
    private readonly IConfigurationManager _configuration;
    private readonly IGameTiming _timing;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarPlanetPrototype> _planets = new();
    private readonly HashSet<ProtoId<SWGlobalWarPlanetPrototype>> _available = new();
    private readonly HashSet<ProtoId<SWGlobalWarPlanetPrototype>> _hubs = new();
    private readonly List<Route> _routes = new();
    private ProtoId<SWGlobalWarPlanetPrototype>? _current;
    private ProtoId<SWGlobalWarPlanetPrototype>? _selected;
    private ProtoId<SWGlobalWarPlanetPrototype>? _hovered;
    private Route? _hoveredRoute;
    private Route? _selectedRoute;
    private ProtoId<SWGlobalWarPlanetPrototype>? _lastClickPlanet;
    private TimeSpan _lastClickTime;
    private Vector2 _lastClickPosition;
    private Vector2 _mapCenter;
    private Vector2 _mapSize = Vector2.One;
    private Vector2 _pan;
    private Vector2 _targetPan;
    private Vector2 _panAtPress;
    private Vector2? _press;
    private Vector2? _pointer;
    private float _drawZoom = 1f;
    private bool _dragged;

    public float Zoom { get; private set; } = 1f;
    public event Action<ProtoId<SWGlobalWarPlanetPrototype>>? PlanetSelected;
    public event Action? ViewChanged;
    public event Action<ProtoId<SWGlobalWarPlanetPrototype>, ProtoId<SWGlobalWarPlanetPrototype>>? RouteSelected;

    private sealed record Route(ProtoId<SWGlobalWarPlanetPrototype> From,
        ProtoId<SWGlobalWarPlanetPrototype> To, bool Bidirectional, Vector2[] Points);

    public SWGlobalWarMapControl()
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        _timing = IoCManager.Resolve<IGameTiming>();
        MinHeight = 180;
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>Updates availability and location without rebuilding the graph or resetting its camera.</summary>
    /// <param name="destinations">All planets in the active GlobalWarGamerule, including unavailable destinations.</param>
    /// <param name="currentPlanet">Source orbit, retained during hyperspace travel.</param>
    public void SetState(IReadOnlyList<SWGlobalWarTravelDestination> destinations,
        ProtoId<SWGlobalWarPlanetPrototype>? currentPlanet = null)
    {
        _available.Clear();
        foreach (var destination in destinations)
        {
            if (destination.Available)
                _available.Add(destination.Id);
        }

        if (_current != currentPlanet)
            _labelsDirty = true;
        _current = currentPlanet;
        if (_planets.Count == destinations.Count && destinations.All(destination => _planets.ContainsKey(destination.Id)))
            return;

        _planets.Clear();
        foreach (var destination in destinations)
            _planets.Add(destination.Id, _prototypes.Index(destination.Id));

        RebuildRoutes();
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        foreach (var planet in _planets.Values)
        {
            minimum = Vector2.Min(minimum, planet.MapPosition);
            maximum = Vector2.Max(maximum, planet.MapPosition);
        }
        foreach (var route in _routes)
        foreach (var point in route.Points)
        {
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        _mapCenter = _planets.Count == 0 ? Vector2.Zero : (minimum + maximum) / 2;
        _mapSize = _planets.Count == 0 ? Vector2.One : Vector2.Max(maximum - minimum, new Vector2(40));
        if (_selected is { } selected && !_planets.ContainsKey(selected))
            _selected = null;
        ResetView();
        // A replacement graph must be fitted immediately rather than animated from obsolete coordinates.
        _drawZoom = Zoom;
        _pan = _targetPan;
    }

    /// <summary>Selects a planet and optionally animates the camera towards it without starting a journey.</summary>
    /// <param name="id">Planet prototype to inspect.</param>
    /// <param name="focus">Center the planet and magnify the map to at least 180%.</param>
    public void SelectPlanet(ProtoId<SWGlobalWarPlanetPrototype> id, bool focus = false)
    {
        if (!_planets.TryGetValue(id, out var planet))
            return;

        _selected = id;
        _selectedRoute = null;
        _labelsDirty = true;
        if (focus)
        {
            Zoom = Math.Max(Zoom, 1.8f);
            _targetPan = ClampPan(-(planet.MapPosition - _mapCenter) * FitScale * Zoom);
            _press = null;
            ViewUpdated();
        }
        PlanetSelected?.Invoke(id);
    }

    private float FitScale
    {
        get
        {
            var extent = Vector2.Max(Size - new Vector2(Padding * 2), Vector2.One);
            return Math.Min(extent.X / _mapSize.X, extent.Y / _mapSize.Y);
        }
    }

    private Vector2 MapToLocal(Vector2 position) =>
        (position - _mapCenter) * FitScale * _drawZoom + Size / 2 + _pan;

    public void ResetView()
    {
        Zoom = 1;
        _targetPan = Vector2.Zero;
        _press = null;
        ViewUpdated();
    }

    /// <summary>Zooms around a local UI point, preserving the point beneath the cursor throughout the animation.</summary>
    /// <param name="factor">Multiplier applied to the target zoom, clamped to 1–4.</param>
    /// <param name="around">Cursor position; null zooms around the center of the map.</param>
    public void ChangeZoom(float factor, Vector2? around = null)
    {
        if (!float.IsFinite(factor) || factor <= 0)
            return;

        var old = Zoom;
        Zoom = Math.Clamp(Zoom * factor, 1, 4);
        var point = (around ?? Size / 2) - Size / 2;
        _targetPan = ClampPan(point - (point - _targetPan) * (Zoom / old));
        _press = null;
        ViewUpdated();
    }

    private Vector2 ClampPan(Vector2 pan)
    {
        var limit = Size * Zoom / 2;
        return Vector2.Clamp(pan, -limit, limit);
    }

    private void ViewUpdated()
    {
        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _drawZoom = Zoom;
            _pan = _targetPan;
        }
        _lastClickPlanet = null;
        _labelsDirty = true;
        _labels.Clear();
        UpdateHover();
        ViewChanged?.Invoke();
    }

    protected override void Resized()
    {
        base.Resized();
        _targetPan = ClampPan(_targetPan);
        _pan = ClampPan(_pan);
        ViewUpdated();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_drawZoom == Zoom && _pan == _targetPan)
            return;

        var blend = _configuration.GetCVar(CCVars.ReducedMotion)
            ? 1f : 1 - MathF.Exp(-14 * Math.Clamp(args.DeltaSeconds, 0, .1f));
        _drawZoom = MathHelper.Lerp(_drawZoom, Zoom, blend);
        _pan = Vector2.Lerp(_pan, _targetPan, blend);
        if (Math.Abs(_drawZoom - Zoom) < .001f && Vector2.DistanceSquared(_pan, _targetPan) < .04f)
        {
            _drawZoom = Zoom;
            _pan = _targetPan;
        }
        _labelsDirty = true;
        _labels.Clear();
        UpdateHover();
    }

    private ProtoId<SWGlobalWarPlanetPrototype>? HitTestPlanet(Vector2 position)
    {
        if (!UIBox2.FromDimensions(Vector2.Zero, Size).Contains(position))
            return null;

        ProtoId<SWGlobalWarPlanetPrototype>? best = null;
        var distance = HitRadius * HitRadius;
        foreach (var (id, planet) in _planets)
        {
            var next = Vector2.DistanceSquared(position, MapToLocal(planet.MapPosition));
            if (next >= distance)
                continue;
            distance = next;
            best = id;
        }

        // Visible nodes take priority; labels take priority over another node's padded hit area.
        if (best != null && distance <= 8 * 8)
            return best;
        foreach (var label in _labels)
        {
            if (label.Bounds.Contains(position))
                return label.Id;
        }
        return best;
    }

    private Route? HitTestRoute(Vector2 position)
    {
        if (!UIBox2.FromDimensions(Vector2.Zero, Size).Contains(position))
            return null;

        Route? best = null;
        var distance = 6f;
        foreach (var route in _routes)
        {
            if (!IsMarketRouteVisible(route))
                continue;
            for (var i = 1; i < route.Points.Length; i++)
            {
                var a = MapToLocal(route.Points[i - 1]);
                var b = MapToLocal(route.Points[i]);
                var delta = b - a;
                var t = delta.LengthSquared() < .0001f ? 0
                    : Math.Clamp(Vector2.Dot(position - a, delta) / delta.LengthSquared(), 0, 1);
                var next = Vector2.Distance(position, a + t * delta);
                if (next >= distance)
                    continue;
                distance = next;
                best = route;
            }
        }
        return best;
    }

    private void ActivateAt(Vector2 position)
    {
        if (HitTestPlanet(position) is not { } id)
        {
            if (_marketState != null && HitTestRoute(position) is { } route)
            {
                _selected = null;
                _selectedRoute = route;
                _labelsDirty = true;
                RouteSelected?.Invoke(route.From, route.To);
            }
            return;
        }

        var now = _timing.RealTime;
        var delay = TimeSpan.FromMilliseconds(_configuration.GetCVar(CVars.DoubleClickDelay));
        var range = _configuration.GetCVar(CVars.DoubleClickRange);
        var focus = _lastClickPlanet == id && now - _lastClickTime <= delay &&
            Vector2.Distance(position, _lastClickPosition) * UIScale <= range;
        _lastClickTime = now;
        _lastClickPosition = position;
        _lastClickPlanet = id;
        SelectPlanet(id, focus);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use)
            return;

        // Take over the displayed camera immediately so a drag never jumps to an unfinished animation's target.
        Zoom = _drawZoom;
        _targetPan = _pan;
        ViewChanged?.Invoke();
        _press = args.RelativePosition;
        _panAtPress = _pan;
        _dragged = false;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null)
            return;

        if (!_dragged && Vector2.DistanceSquared(args.RelativePosition, _press.Value) < DragThreshold * DragThreshold)
            ActivateAt(args.RelativePosition);
        else
            _lastClickPlanet = null;
        _press = null;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _pointer = args.RelativePosition;
        if (_press is { } press)
        {
            var offset = args.RelativePosition - press;
            _dragged |= offset.LengthSquared() >= DragThreshold * DragThreshold;
            if (_dragged)
            {
                _pan = _targetPan = ClampPan(_panAtPress + offset);
                ViewUpdated();
            }
        }
        UpdateHover();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _press = null;
        _pointer = null;
        _lastClickPlanet = null;
        UpdateHover();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        ChangeZoom(MathF.Pow(1.2f, args.Delta.Y), args.RelativePosition);
        args.Handle();
    }

    private void UpdateHover()
    {
        var planet = _pointer is { } point ? HitTestPlanet(point) : null;
        if (_hovered != planet)
            _labelsDirty = true;
        _hovered = planet;
        _hoveredRoute = _pointer is { } pointer && planet == null ? HitTestRoute(pointer) : null;
        DefaultCursorShape = planet != null || _marketState != null && _hoveredRoute != null ? CursorShape.Hand : CursorShape.Arrow;
        ToolTip = planet is { } id
            ? _planets[id].Name + "\n" + Loc.GetString("sw-global-war-travel-focus-hint")
            : _hoveredRoute is { } route ? Loc.GetString("sw-global-war-travel-route-tooltip",
                ("from", _planets[route.From].Name), ("to", _planets[route.To].Name),
                ("direction", route.Bidirectional ? "↔" : "→")) : null;
        if (_marketState != null)
        {
            if (planet is { } marketPlanet)
                ToolTip = _planets[marketPlanet].Name + "\n" + MarketPlanetToolTip(marketPlanet);
            else if (_hoveredRoute is { } marketRoute)
                ToolTip += "\n" + MarketRouteToolTip(marketRoute);
        }
    }
}
