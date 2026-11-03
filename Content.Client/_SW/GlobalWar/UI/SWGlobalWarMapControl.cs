using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>
/// The campaign's star chart: real planet sprites on a galaxy, with selection and a camera that fits it all on open.
/// Where a planet stands here is a picture, not a rule: routes come from the prototypes, positions only from this file.
/// </summary>
public sealed partial class SWGlobalWarMapControl : Control
{
    private const float HitRadius = 28f;
    private const float DragThreshold = 5f;
    private const int RoutePointCount = 9;
    private const int PlanetDiameterSmall = 22;
    private const int PlanetDiameterLarge = 30;

    /// <summary>
    /// The Republic's half of the chart as fractions of the panel; the Empire mirrors it. Capitals on top, Force worlds
    /// at the bottom, border worlds inward. A planet not listed falls back to its prototype position.
    /// </summary>
    private static readonly Dictionary<string, Vector2> RepublicChart = new()
    {
        ["0"] = new(.170f, .125f), ["1"] = new(.293f, .276f), ["2"] = new(.092f, .322f), ["3"] = new(.340f, .466f),
        ["4"] = new(.210f, .489f), ["5"] = new(.040f, .480f), ["6"] = new(.209f, .712f), ["7"] = new(.337f, .699f),
        ["8"] = new(.099f, .675f), ["9"] = new(.174f, .909f),
    };

    private readonly IPrototypeManager _prototypes;
    private readonly IConfigurationManager _configuration;
    private readonly IGameTiming _timing;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarPlanetPrototype> _planets = new();
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, Vector2> _positions = new();
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarFaction> _owners = new();
    private readonly HashSet<ProtoId<SWGlobalWarPlanetPrototype>> _available = new();
    private readonly List<Route> _routes = new();
    private ProtoId<SWGlobalWarPlanetPrototype>? _current;
    private ProtoId<SWGlobalWarPlanetPrototype>? _selected;
    private ProtoId<SWGlobalWarPlanetPrototype>? _hovered;
    private Route? _hoveredRoute;
    private Route? _selectedRoute;
    private Vector2 _pan;
    private Vector2 _targetPan;
    private Vector2 _panAtPress;
    private Vector2? _press;
    private Vector2? _pointer;
    private float _drawZoom = 1f;
    private bool _dragged;

    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarFaction> _split = new();

    public float Zoom { get; private set; } = 1f;

    /// <summary>A chart to look at, not to travel by: no world steps back for being out of reach.</summary>
    public bool Overview { get; set; }
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
            _owners[destination.Id] = destination.Owner ?? _prototypes.Index(destination.Id).InitialOwner;
        }

        _current = currentPlanet;
        if (_planets.Count == destinations.Count && destinations.All(destination => _planets.ContainsKey(destination.Id)))
            return;

        _planets.Clear();
        foreach (var destination in destinations)
            _planets.Add(destination.Id, _prototypes.Index(destination.Id));

        BuildPositions();
        RebuildRoutes();
        if (_selected is { } selected && !_planets.ContainsKey(selected))
            _selected = null;
        ResetView();
        // A replacement graph must be fitted immediately rather than animated from obsolete coordinates.
        _drawZoom = Zoom;
        _pan = _targetPan;
    }

    /// <summary>Worlds whose surface belongs to another side than their station, with the side that holds the surface.</summary>
    public void SetSplit(IReadOnlyDictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarFaction> split)
    {
        _split.Clear();
        foreach (var (id, surface) in split)
            _split[id] = surface;
    }

    private static Vector2? ChartPosition(ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        var code = id.Id.Replace("SWGlobalWar", string.Empty);
        if (code.Length != 2)
            return null;

        Vector2? position = code[0] switch
        {
            'R' when RepublicChart.TryGetValue(code[1..], out var republic) => republic,
            'E' when RepublicChart.TryGetValue(code[1..], out var empire) => new Vector2(1 - empire.X, empire.Y),
            'N' when code[1] == '0' => new Vector2(.5f, .300f),
            'N' when code[1] == '1' => new Vector2(.5f, .720f),
            _ => null,
        };
        return position is { } value ? new Vector2(value.X, .075f + value.Y * .885f) : null;
    }

    /// <summary>Capitals are the biggest worlds, then the Force worlds, then the neutrals.</summary>
    private int PlanetDiameter(ProtoId<SWGlobalWarPlanetPrototype> id)
    {
        var code = id.Id.Replace("SWGlobalWar", string.Empty);
        return code switch
        {
            "R0" or "E0" => 40,
            "R9" or "E9" => PlanetDiameterLarge,
            "N0" or "N1" => 28,
            _ => PlanetDiameterSmall,
        };
    }

    private void BuildPositions()
    {
        _positions.Clear();
        if (_planets.Keys.All(id => ChartPosition(id) != null))
        {
            foreach (var id in _planets.Keys)
                _positions[id] = ChartPosition(id)!.Value;
            return;
        }

        // Test maps have their own coordinates, in their own units: fit those into the same panel.
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        foreach (var planet in _planets.Values)
        {
            minimum = Vector2.Min(minimum, planet.MapPosition);
            maximum = Vector2.Max(maximum, planet.MapPosition);
        }

        var span = Vector2.Max(maximum - minimum, Vector2.One);
        foreach (var (id, planet) in _planets)
            _positions[id] = new Vector2(.15f, .2f) + (planet.MapPosition - minimum) / span * new Vector2(.7f, .6f);
    }

    /// <summary>Selects a planet and optionally animates the camera towards it without starting a journey.</summary>
    /// <param name="id">Planet prototype to inspect.</param>
    /// <param name="focus">Center the planet and magnify the map to at least 180%.</param>
    public void SelectPlanet(ProtoId<SWGlobalWarPlanetPrototype> id, bool focus = false)
    {
        if (!_positions.TryGetValue(id, out var position))
            return;

        _selected = id;
        _selectedRoute = null;
        if (focus)
        {
            Zoom = Math.Max(Zoom, 1.8f);
            _targetPan = ClampPan(-(position - new Vector2(.5f)) * Size * Zoom);
            _press = null;
            ViewUpdated();
        }
        PlanetSelected?.Invoke(id);
    }

    private Vector2 MapToLocal(Vector2 position) =>
        (position - new Vector2(.5f)) * Size * _drawZoom + Size / 2 + _pan;

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

    /// <summary>The camera stops where the chart would leave the panel, and at the fitted view there is nowhere to go.</summary>
    private Vector2 ClampPan(Vector2 pan)
    {
        var limit = Size * (Zoom - 1) / 2;
        return Vector2.Clamp(pan, -limit, limit);
    }

    private void ViewUpdated()
    {
        if (_configuration.GetCVar(CCVars.ReducedMotion))
        {
            _drawZoom = Zoom;
            _pan = _targetPan;
        }
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
        UpdateHover();
    }

    private ProtoId<SWGlobalWarPlanetPrototype>? HitTestPlanet(Vector2 position)
    {
        ProtoId<SWGlobalWarPlanetPrototype>? best = null;
        var distance = float.PositiveInfinity;
        foreach (var (id, world) in _positions)
        {
            // The name is part of the target, so the box runs a little below the sprite.
            var radius = PlanetHalf(id) / UIScale;
            var delta = position - MapToLocal(world);
            if (Math.Abs(delta.X) > Math.Max(radius, HitRadius) || delta.Y < -radius || delta.Y > radius + 18)
                continue;
            var next = delta.LengthSquared();
            if (next >= distance)
                continue;
            distance = next;
            best = id;
        }

        return best;
    }

    private Route? HitTestRoute(Vector2 position)
    {
        Route? best = null;
        var distance = 6f;
        foreach (var route in _routes)
        {
            if (!IsRouteClickable(route))
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
        if (HitTestPlanet(position) is { } id)
        {
            SelectPlanet(id);
            return;
        }

        if (_marketState != null && HitTestRoute(position) is { } route)
        {
            _selected = null;
            _selectedRoute = route;
            RouteSelected?.Invoke(route.From, route.To);
        }
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
        _hovered = planet;
        _hoveredRoute = _pointer is { } pointer && planet == null ? HitTestRoute(pointer) : null;
        DefaultCursorShape = planet != null || _marketState != null && _hoveredRoute != null ? CursorShape.Hand : CursorShape.Arrow;
        ToolTip = planet is { } id
            ? _planets[id].Name
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
