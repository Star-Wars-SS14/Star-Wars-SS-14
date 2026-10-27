using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Planet graph with selection, dragging and cursor-centered zoom, adapted from the Senate atlas.</summary>
public sealed class SWGlobalWarMapControl : Control
{
    private const float Padding = 48f;
    private const float HitRadius = 16f;
    private const float DragThreshold = 5f;
    private static readonly Color BackgroundColor = Color.FromHex("#111C28");
    private static readonly Color RouteColor = Color.FromHex("#526A80");
    private static readonly Color AvailableColor = Color.FromHex("#79DEEC");
    private static readonly Color SelectedColor = Color.FromHex("#E9C17E");
    private readonly IPrototypeManager _prototypes;
    private readonly Font _font;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, SWGlobalWarPlanetPrototype> _planets = new();
    private readonly HashSet<ProtoId<SWGlobalWarPlanetPrototype>> _available = new();
    private readonly List<(ProtoId<SWGlobalWarPlanetPrototype> From, ProtoId<SWGlobalWarPlanetPrototype> To, bool Bidirectional)> _routes = new();
    private readonly List<(ProtoId<SWGlobalWarPlanetPrototype> Id, UIBox2 Bounds)> _labels = new();
    private ProtoId<SWGlobalWarPlanetPrototype>? _selected;
    private ProtoId<SWGlobalWarPlanetPrototype>? _hovered;
    private Vector2 _mapCenter;
    private Vector2 _mapSize = Vector2.One;
    private Vector2 _pan;
    private Vector2 _panAtPress;
    private Vector2? _press;
    private bool _dragged;

    public float Zoom { get; private set; } = 1f;
    public event Action<ProtoId<SWGlobalWarPlanetPrototype>>? PlanetSelected;
    public event Action? ViewChanged;

    public SWGlobalWarMapControl()
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 12);
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    /// <summary>
    /// Updates server-provided availability; rebuilds geometry only when the set of destination planets changes.
    /// </summary>
    /// <param name="destinations">All planets in the active GlobalWarGamerule, including unavailable destinations.</param>
    public void SetState(IReadOnlyList<SWGlobalWarTravelDestination> destinations)
    {
        _available.Clear();
        foreach (var destination in destinations)
        {
            if (destination.Available)
                _available.Add(destination.Id);
        }

        if (_planets.Count == destinations.Count && destinations.All(destination => _planets.ContainsKey(destination.Id)))
            return;

        _planets.Clear();
        foreach (var destination in destinations)
            _planets.Add(destination.Id, _prototypes.Index(destination.Id));

        _routes.Clear();
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        foreach (var (id, planet) in _planets)
        {
            minimum = Vector2.Min(minimum, planet.MapPosition);
            maximum = Vector2.Max(maximum, planet.MapPosition);
            foreach (var target in planet.Connections)
            {
                if (target == id || !_planets.TryGetValue(target, out var other))
                    continue;

                var bidirectional = other.Connections.Contains(id);
                if (bidirectional && string.CompareOrdinal(id.Id, target.Id) > 0)
                    continue;

                _routes.Add((id, target, bidirectional));
            }
        }

        _mapCenter = _planets.Count == 0 ? Vector2.Zero : (minimum + maximum) / 2;
        _mapSize = _planets.Count == 0 ? Vector2.One : Vector2.Max(maximum - minimum, new Vector2(40));
        if (_selected is { } selected && !_planets.ContainsKey(selected))
            _selected = null;
        _hovered = null;
        ToolTip = null;
        ResetView();
    }

    private Vector2 MapToLocal(Vector2 position)
    {
        var extent = Vector2.Max(Size - new Vector2(Padding * 2), Vector2.One);
        var scale = Math.Min(extent.X / _mapSize.X, extent.Y / _mapSize.Y);
        return (position - _mapCenter) * scale * Zoom + Size / 2 + _pan;
    }

    public void ResetView()
    {
        Zoom = 1;
        _pan = Vector2.Zero;
        _press = null;
        ViewUpdated();
    }

    /// <summary>Changes magnification while keeping the map point under the cursor stationary.</summary>
    /// <param name="factor">Multiplier applied to the current zoom, clamped to 1–4.</param>
    /// <param name="around">Cursor position in local UI coordinates; null zooms around the control's center.</param>
    public void ChangeZoom(float factor, Vector2? around = null)
    {
        if (!float.IsFinite(factor) || factor <= 0)
            return;

        var old = Zoom;
        Zoom = Math.Clamp(Zoom * factor, 1, 4);
        var point = (around ?? Size / 2) - Size / 2;
        _pan = point - (point - _pan) * (Zoom / old);
        ClampPan();
        // Start any remaining drag from the newly zoomed camera position.
        _press = null;
        ViewUpdated();
    }

    private void ClampPan()
    {
        var limit = Size * Zoom / 2;
        _pan = Vector2.Clamp(_pan, -limit, limit);
    }

    private void ViewUpdated()
    {
        _labels.Clear();
        _hovered = null;
        ToolTip = null;
        ViewChanged?.Invoke();
    }

    protected override void Resized()
    {
        base.Resized();
        ClampPan();
        ViewUpdated();
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

        if (best != null)
            return best;
        foreach (var label in _labels)
        {
            if (label.Bounds.Contains(position))
                return label.Id;
        }
        return null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use)
            return;

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

        if (!_dragged && Vector2.DistanceSquared(args.RelativePosition, _press.Value) < DragThreshold * DragThreshold &&
            HitTestPlanet(args.RelativePosition) is { } id)
        {
            _selected = id;
            PlanetSelected?.Invoke(id);
        }
        _press = null;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_press is { } press)
        {
            var offset = args.RelativePosition - press;
            _dragged |= offset.LengthSquared() >= DragThreshold * DragThreshold;
            if (_dragged)
            {
                _pan = _panAtPress + offset;
                ClampPan();
                ViewUpdated();
            }
        }
        _hovered = HitTestPlanet(args.RelativePosition);
        ToolTip = _hovered is { } id ? _planets[id].Name : null;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _press = null;
        _hovered = null;
        ToolTip = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        ChangeZoom(MathF.Pow(1.2f, args.Delta.Y), args.RelativePosition);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), BackgroundColor);
        _labels.Clear();
        foreach (var (from, to, bidirectional) in _routes)
        {
            var a = MapToLocal(_planets[from].MapPosition) * UIScale;
            var b = MapToLocal(_planets[to].MapPosition) * UIScale;
            var color = _selected == from || _selected == to ? SelectedColor : RouteColor;
            handle.DrawLine(a, b, color);
            if (bidirectional || Vector2.DistanceSquared(a, b) < 1)
                continue;

            // A single arrow distinguishes a one-way connection from a bidirectional route.
            var direction = Vector2.Normalize(b - a);
            var normal = new Vector2(-direction.Y, direction.X);
            var tip = (a + b) / 2 + direction * 5 * UIScale;
            handle.DrawLine(tip, tip - direction * 10 * UIScale + normal * 4 * UIScale, color);
            handle.DrawLine(tip, tip - direction * 10 * UIScale - normal * 4 * UIScale, color);
        }

        foreach (var (id, planet) in _planets)
        {
            var point = MapToLocal(planet.MapPosition);
            var color = _selected == id || _hovered == id ? SelectedColor
                : _available.Contains(id) ? AvailableColor : Color.LightGray;
            if (_selected == id || _hovered == id)
                handle.DrawCircle(point * UIScale, 11 * UIScale, Color.ToSrgb(color), false);
            handle.DrawCircle(point * UIScale, 6 * UIScale, Color.ToSrgb(BackgroundColor));
            handle.DrawCircle(point * UIScale, 5 * UIScale, Color.ToSrgb(color));

            var size = handle.GetDimensions(_font, planet.Name, UIScale) / UIScale;
            var label = UIBox2.FromDimensions(point + new Vector2(-size.X / 2, 14), size);
            _labels.Add((id, label));
            handle.DrawRect(new UIBox2((label.TopLeft - new Vector2(2, 1)) * UIScale,
                (label.BottomRight + new Vector2(2, 1)) * UIScale), BackgroundColor);
            handle.DrawString(_font, label.TopLeft * UIScale, planet.Name, UIScale, color);
        }
    }
}
