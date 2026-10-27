using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Native, resolution-independent Holonet map. All hit tests use UI coordinates.</summary>
public sealed partial class SWGalacticMapControl : Control
{
    private readonly SWGalacticMapPrototype _map;
    private readonly string? _focusSector;
    private readonly bool _canOpenSectors;
    private readonly IGameTiming _timing;
    private readonly IConfigurationManager _configuration;
    private readonly Dictionary<string, SWGalacticMapWorld> _worlds;
    private readonly Dictionary<string, SWGalacticMapCorridor> _corridors;
    private readonly HashSet<string> _hubs;
    private readonly List<(SWGalacticMapRoute Route, Vector2[] Points)> _paths = new();
    // Content assemblies must pass the launcher's IL verifier; stackalloc is not permitted here.
    private readonly Vector2[] _strokeVertices = new Vector2[4];
    private readonly List<(string Id, UIBox2 Box)> _labels = new();
    private readonly Dictionary<string, SWSectorEntry> _sectors = new();
    private IReadOnlyDictionary<string, SWFinanceSectorRevenue>? _financeSectors;
    public void SetFinanceSectors(IReadOnlyDictionary<string, SWFinanceSectorRevenue>? sectors) { _financeSectors = sectors; _labelsDirty = true; }
    private Vector2 _pan;
    private Vector2? _press;
    private Vector2? _pointer;
    private Vector2 _panAtPress;
    private Vector2 _labelSize;
    private float _labelScale;
    private bool _labelsDirty = true;
    private bool _dragged;
    private string? _hover;
    private string? _selectedWorld;
    private SWGalacticMapRoute? _selectedRoute;
    private string? _lastClickWorld;
    private TimeSpan _lastClickTime;
    private Vector2 _lastClickPosition;
    private SWGalacticMapControl? _transitionFrom;
    private string? _transitionWorld;
    private float _transitionTime;
    private bool _enteringSector;
    private const float TransitionDuration = 0.36f;
    public bool IsTransitioning => _transitionFrom != null;
    public float Zoom { get; private set; } = 1;
    public string? SelectedWorldId => _selectedWorld;
    public event Action<string>? WorldSelected;
    public event Action<string>? WorldActivated;
    public event Action<SWGalacticMapRoute>? RouteSelected;
    public event Action? ViewChanged;

    public SWGalacticMapControl(SWGalacticMapPrototype map, string? focusSector = null, bool canOpenSectors = true)
    {
        _map = map;
        _focusSector = focusSector;
        _canOpenSectors = canOpenSectors;
        BuildPoliticalGeometry();
        _timing = IoCManager.Resolve<IGameTiming>();
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        _worlds = map.Worlds.ToDictionary(w => w.Id);
        _corridors = map.Corridors.ToDictionary(c => c.Id);
        _hubs = map.Worlds.Where(w => map.Routes.Where(r => !r.Proposed && (r.From == w.Id || r.To == w.Id))
            .Select(r => r.Corridor).Distinct().Count() > 1).Select(w => w.Id).ToHashSet();
        foreach (var route in map.Routes)
        {
            if (!_worlds.TryGetValue(route.From, out var a) || !_worlds.TryGetValue(route.To, out var b))
                continue;
            var control = route.Control ?? (a.Position + b.Position) / 2;
            var points = new Vector2[49];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (float) (points.Length - 1);
                points[i] = (1 - t) * (1 - t) * a.Position + 2 * (1 - t) * t * control + t * t * b.Position;
            }
            _paths.Add((route, points));
        }
        MinHeight = 380;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetState(IReadOnlyList<SWSectorEntry> sectors)
    {
        _sectors.Clear();
        foreach (var sector in sectors) _sectors[sector.Id] = sector;
        UpdateHover();
    }

    public void SelectWorld(string id, bool focus = false)
    {
        if (!_worlds.TryGetValue(id, out var world)) return;
        _selectedWorld = id;
        _selectedRoute = null;
        if (focus && Zoom > 1)
        {
            _pan += Size / 2 - MapToLocal(world.Position);
            ClampPan();
            ViewUpdated();
        }
        _labelsDirty = true;
    }

    public void SelectRoute(SWGalacticMapRoute route)
    {
        _selectedRoute = route;
        _selectedWorld = null;
        _labelsDirty = true;
    }

    public Vector2 MapToLocal(Vector2 position)
    {
        var extent = Vector2.Max(Size - new Vector2(64, 56), Vector2.One);
        var fitted = new Vector2(32, 28) + position / _map.Size * extent;
        return (fitted - Size / 2) * Zoom + Size / 2 + _pan;
    }

    public void ResetView()
    {
        FinishTransition();
        Zoom = 1;
        _pan = Vector2.Zero;
        ViewUpdated();
    }

    public void ChangeZoom(float factor, Vector2? around = null)
    {
        if (!float.IsFinite(factor) || factor <= 0) return;
        FinishTransition();
        var old = Zoom;
        Zoom = Math.Clamp(Zoom * factor, 1, 4);
        var point = (around ?? Size / 2) - Size / 2;
        _pan = point - (point - _pan) * (Zoom / old);
        ClampPan();
        ViewUpdated();
    }

    private void ClampPan()
    {
        var limit = Vector2.Max(Vector2.Zero, Size * (Zoom - 1) / 2);
        _pan = Vector2.Clamp(_pan, -limit, limit);
    }

    private void ViewUpdated()
    {
        InvalidateDefenseLayout();
        _crisisLayoutDirty = true;
        _lastClickWorld = null;
        _labelsDirty = true;
        _labels.Clear();
        UpdateHover();
        ViewChanged?.Invoke();
    }

    protected override void Resized()
    {
        base.Resized();
        if (_transitionTime > 0) FinishTransition();
        ClampPan();
        ViewUpdated();
    }

    public string? HitTestWorld(Vector2 position)
    {
        if (IsTransitioning) return null;
        if (position.X < 0 || position.Y < 0 || position.X > Width || position.Y > Height) return null;
        string? best = null;
        var distance = 16f * 16;
        foreach (var world in _map.Worlds)
        {
            var next = Vector2.DistanceSquared(position, MapToLocal(world.Position));
            if (next >= distance) continue;
            distance = next;
            best = world.Id;
        }
        // Visible nodes take priority; a label takes priority over another node's padded hit area.
        if (best != null && distance <= 8 * 8) return best;
        return _labels.Where(l => l.Box.Contains(position)).Select(l => l.Id).FirstOrDefault() ?? best;
    }

    public void ActivateAt(Vector2 position)
    {
        if (IsTransitioning) return;
        if (!UIBox2.FromDimensions(Vector2.Zero, Size).Contains(position))
        {
            _lastClickWorld = null;
            return;
        }
        if (ActivateMedicalSupply(position) || ActivateCrisisAt(position) || ActivateDefenseAt(position))
        {
            _lastClickWorld = null;
            return;
        }
        if (HitTestWorld(position) is { } world)
        {
            var now = _timing.RealTime;
            var delay = TimeSpan.FromMilliseconds(_configuration.GetCVar(CVars.DoubleClickDelay));
            var range = _configuration.GetCVar(CVars.DoubleClickRange);
            var activate = _lastClickWorld == world && now - _lastClickTime <= delay
                && Vector2.Distance(position, _lastClickPosition) * UIScale <= range;
            _lastClickTime = now;
            _lastClickPosition = position;
            _lastClickWorld = activate ? null : world;
            SelectWorld(world);
            WorldSelected?.Invoke(world);
            if (activate) WorldActivated?.Invoke(world);
            return;
        }
        _lastClickWorld = null;
        SWGalacticMapRoute? best = null;
        var distance = 6f;
        foreach (var path in _paths)
        for (var i = 1; i < path.Points.Length; i++)
        {
            var next = SegmentDistance(position, MapToLocal(path.Points[i - 1]), MapToLocal(path.Points[i]));
            if (next >= distance) continue;
            distance = next;
            best = path.Route;
        }
        if (best == null) return;
        SelectRoute(best);
        RouteSelected?.Invoke(best);
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var delta = b - a;
        var t = delta.LengthSquared() < .0001f ? 0 : Math.Clamp(Vector2.Dot(p - a, delta) / delta.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + t * delta);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) return;
        if (IsTransitioning) { args.Handle(); return; }
        _press = args.RelativePosition;
        _panAtPress = _pan;
        _dragged = false;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null) return;
        if (!_dragged && Vector2.DistanceSquared(args.RelativePosition, _press.Value) < 25) ActivateAt(args.RelativePosition);
        else _lastClickWorld = null;
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
            _dragged |= offset.LengthSquared() >= 25;
            if (_dragged)
            {
                _pan = _panAtPress + offset;
                ClampPan();
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
        _lastClickWorld = null;
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
        var hovered = _pointer is { } p ? HitTestWorld(p) : null;
        if (_hover != hovered) _labelsDirty = true;
        _hover = hovered;
        ToolTip = null;
        DefaultCursorShape = _pointer is { } cursor && (HitTestDefenseFleet(cursor) != null || HitTestCrisis(cursor) != null)
            ? CursorShape.Hand : CursorShape.Arrow;
        if (_pointer is { } crisisPointer && CrisisToolTip(crisisPointer) is { } crisisTip)
        {
            ToolTip = crisisTip;
            return;
        }
        if (_pointer is { } pointer && DefenseToolTip(pointer) is { } defenseTip)
        {
            ToolTip = defenseTip;
            return;
        }
        if (_pointer is { } supplyPointer && DefenseSupplyToolTip(supplyPointer) is { } supplyTip)
        {
            ToolTip = supplyTip;
            return;
        }
        if (_pointer is { } medicalPointer && MedicalSupplyToolTip(medicalPointer) is { } medicalTip) { ToolTip = medicalTip; return; }
        if (hovered == null) return;
        var world = _worlds[hovered];
        ToolTip = world.Name;
        if (_financeSectors != null && world.Sector is { } financeSector && _financeSectors.TryGetValue(financeSector.Id, out var finance))
            ToolTip += $"\n{finance.Name}\nНалоговые поступления с начала дня {finance.Tax:N0} кр";
        if (_economyMapMode >= 0 && EconomySectorSummary(world.Id) is { } economySummary)
            ToolTip += $"\n{economySummary}";
        if (_map.PoliticalAreas.LastOrDefault(a => a.Contains(world.Position)) is { } politicalArea)
            ToolTip += $"\n{politicalArea.Name}";
        if (world.Sector is { } sectorId && _sectors.TryGetValue(sectorId.Id, out var sector))
            ToolTip += $"\n{sector.Name}\n{(sector.Claimed ? "Представлен в Сенате" : "Сенатор не назначен")}";
        if (_canOpenSectors && world.Sector is { } target && target.Id != _focusSector)
            ToolTip += "\nДвойной щелчок: открыть сектор";
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (Width <= 64 || Height <= 56) return;
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWConsoleStyle.Navigation);
        if (_transitionFrom is not { } previous || _transitionWorld is not { } world)
        {
            DrawGraph(handle);
            return;
        }

        var t = Math.Clamp(_transitionTime / TransitionDuration, 0, 1);
        var ease = t * t * (3 - 2 * t);
        var fit = (Vector2) PixelSize / (Vector2) previous.PixelSize;
        var origin = previous.MapToLocal(previous._worlds[world].Position) * previous.UIScale * fit;
        var destination = MapToLocal(_worlds[world].Position) * UIScale;
        var focus = Vector2.Lerp(origin, destination, ease);
        var outgoingScale = MathHelper.Lerp(1, _enteringSector ? 2.6f : .45f, ease);
        var incomingScale = MathHelper.Lerp(_enteringSector ? .38f : 2.2f, 1, ease);
        var outgoing = Matrix3x2.CreateScale(fit) * Matrix3x2.CreateTranslation(-origin)
            * Matrix3x2.CreateScale(outgoingScale) * Matrix3x2.CreateTranslation(focus);
        var incoming = Matrix3x2.CreateTranslation(-destination) * Matrix3x2.CreateScale(incomingScale)
            * Matrix3x2.CreateTranslation(focus);
        if (previous._focusSector != null && _focusSector != null)
        {
            // Adjacent sectors are at the same map level. Follow the exit's edge, without zooming.
            var exit = previous._worlds[world].Position / previous._map.Size - new Vector2(.5f);
            var direction = Math.Abs(exit.X) > Math.Abs(exit.Y)
                ? new Vector2(Math.Sign(exit.X), 0) : new Vector2(0, Math.Sign(exit.Y));
            var travel = (Vector2) PixelSize * direction * .7f;
            outgoing = Matrix3x2.CreateScale(fit) * Matrix3x2.CreateTranslation(-travel * ease);
            incoming = Matrix3x2.CreateTranslation(travel * (1 - ease));
        }
        var transform = handle.GetTransform();
        var modulate = handle.Modulate;
        try
        {
            handle.Modulate = modulate.WithAlpha(modulate.A * (1 - ease));
            handle.SetTransform(outgoing * transform);
            previous.DrawGraph(handle);
            handle.Modulate = modulate.WithAlpha(modulate.A * ease);
            handle.SetTransform(incoming * transform);
            DrawGraph(handle);
        }
        finally
        {
            handle.Modulate = modulate;
            handle.SetTransform(transform);
        }
    }

    /// <summary>Visual-only camera transition. The target graph and selection change immediately.</summary>
    public void TransitionFrom(SWGalacticMapControl previous, string world, bool enteringSector)
    {
        FinishTransition();
        previous.FinishTransition();
        if (previous == this || previous.Width <= 64 || previous.Height <= 56
            || !previous._worlds.ContainsKey(world) || !_worlds.ContainsKey(world)
            || _configuration.GetCVar(CCVars.ReducedMotion)) return;
        _transitionFrom = previous;
        _transitionWorld = world;
        _enteringSector = enteringSector;
        _transitionTime = 0;
        _lastClickWorld = null;
        _press = null;
        _hover = null;
        ToolTip = null;
    }

    public void FinishTransition()
    {
        _transitionFrom = null;
        _transitionWorld = null;
        _transitionTime = 0;
    }

    protected override void ExitedTree()
    {
        FinishTransition();
        base.ExitedTree();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        AdvanceTraffic(args.DeltaSeconds);
        AdvanceMedicalSupply(args.DeltaSeconds);
        AdvanceDefense(args.DeltaSeconds);
        if (!IsTransitioning || Width <= 64 || Height <= 56) return;
        _transitionTime += args.DeltaSeconds;
        if (_transitionTime < TransitionDuration && !_configuration.GetCVar(CCVars.ReducedMotion)) return;
        FinishTransition();
        UpdateHover();
    }

    private void DrawGraph(DrawingHandleScreen handle)
    {
        Vector2 Screen(Vector2 point) => MapToLocal(point) * UIScale;
        DrawPoliticalAreas(handle, false);
        if (_focusSector != null)
        {
            var boundary = new UIBox2(Screen(new Vector2(18, 16)), Screen(new Vector2(102, 84)));
            handle.DrawRect(boundary, SWConsoleStyle.Edge, false);
        }
        var sectorId = _selectedWorld != null ? _worlds[_selectedWorld].Sector : null;
        foreach (var region in _map.Regions)
        {
            var selected = sectorId.HasValue && region.Sectors.Contains(sectorId.Value);
            var points = region.Boundary.Select(Screen).ToArray();
            if (points.Length < 3) continue;
            if (selected) handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, points, Color.ToSrgb(SWConsoleStyle.Selected));
            for (var i = 0; i < points.Length; i++)
                handle.DrawLine(points[i], points[(i + 1) % points.Length], selected ? SWConsoleStyle.Accent : SWConsoleStyle.Edge);
        }
        DrawPoliticalAreas(handle, true);
        foreach (var path in _paths.OrderBy(p => !p.Route.Proposed))
        {
            var route = path.Route;
            var selected = route == _selectedRoute || _selectedWorld != null && (route.From == _selectedWorld || route.To == _selectedWorld);
            var color = route.Corridor != null && _corridors.TryGetValue(route.Corridor, out var corridor)
                ? corridor.Color : selected ? SWConsoleStyle.TextBright : _focusSector != null ? SWConsoleStyle.TextDim : SWConsoleStyle.Edge;
            color = TrafficRouteColor(route, color, selected);
            var length = 0f;
            for (var i = 1; i < path.Points.Length; i++)
            {
                var a = Screen(path.Points[i - 1]);
                var b = Screen(path.Points[i]);
                if (route.Proposed)
                {
                    var segment = Vector2.Distance(a, b);
                    if (segment < .001f) continue;
                    var direction = (b - a) / segment;
                    for (var offset = 0f; offset < segment;)
                    {
                        var phase = (length + offset) % (9 * UIScale);
                        var step = Math.Min(segment - offset, (phase < 4 * UIScale ? 4 * UIScale : 9 * UIScale) - phase);
                        step = Math.Max(step, .01f);
                        if (phase < 4 * UIScale) Stroke(handle, a + direction * offset, a + direction * Math.Min(segment, offset + step), color, selected ? 1.5f : 1);
                        offset += step;
                    }
                    length += segment;
                }
                else
                {
                    Stroke(handle, a, b, SWConsoleStyle.Navigation, selected ? 5 : 4);
                    Stroke(handle, a, b, color, selected ? 2.8f : 1.8f);
                }
            }
        }
        DrawCrisisRoutes(handle);
        DrawDefenseSupply(handle);
        DrawDefenseRoutes(handle);
        DrawTraffic(handle);
        foreach (var world in _map.Worlds)
        {
            var p = Screen(world.Position);
            var selected = world.Id == _selectedWorld;
            var hover = world.Id == _hover;
            var color = selected || hover ? SWConsoleStyle.Accent : EconomyWorldColor(world.Id) ?? SWConsoleStyle.TextBright;
            if (!selected && !hover && _financeSectors != null)
                color = world.Sector is { } financeSector && _financeSectors.TryGetValue(financeSector.Id, out var finance)
                    ? finance.Reasons.Count > 0 ? SWConsoleStyle.Warn : SWConsoleStyle.Good
                    : SWConsoleStyle.TextDim;
            if (selected || hover) handle.DrawCircle(p, (selected ? 11 : 9) * UIScale, Color.ToSrgb(color), false);
            if (_focusSector != null && world.Sector?.Id != _focusSector)
            {
                var radius = 6 * UIScale;
                handle.DrawRect(new UIBox2(p - new Vector2(radius), p + new Vector2(radius)), SWConsoleStyle.Navigation);
                handle.DrawRect(new UIBox2(p - new Vector2(radius), p + new Vector2(radius)), color, false);
                continue;
            }
            var hub = _hubs.Contains(world.Id);
            handle.DrawCircle(p, (hub ? 6 : 4) * UIScale, Color.ToSrgb(SWConsoleStyle.Navigation));
            handle.DrawCircle(p, (hub ? 6 : 4) * UIScale, Color.ToSrgb(color), world.Anchor && !hub);
            if (hub) handle.DrawCircle(p, 2 * UIScale, Color.ToSrgb(color));
        }
        DrawPoliticalLabels(handle);
        if (_labelsDirty || _labelSize != Size || _labelScale != UIScale)
            LayoutLabels(handle);
        foreach (var (id, box) in _labels)
        {
            var p = MapToLocal(_worlds[id].Position);
            var end = Vector2.Clamp(p, box.TopLeft, box.BottomRight);
            if (Vector2.Distance(p, end) > 15) handle.DrawLine(p * UIScale, end * UIScale, SWConsoleStyle.Edge);
            handle.DrawRect(new UIBox2((box.TopLeft - new Vector2(2, 1)) * UIScale,
                (box.BottomRight + new Vector2(2, 1)) * UIScale), SWConsoleStyle.Navigation);
            handle.DrawString(SWConsoleStyle.Small, box.TopLeft * UIScale, FinanceLabel(_worlds[id]), UIScale,
                id == _selectedWorld || id == _hover ? SWConsoleStyle.Accent : SWConsoleStyle.TextBright);
        }
        DrawDefense(handle);
        DrawCrisisMarkers(handle);
        DrawMedicalSupply(handle);
    }

    private void Stroke(DrawingHandleScreen handle, Vector2 a, Vector2 b, Color color, float width)
    {
        var delta = b - a;
        if (delta.LengthSquared() < .0001f) return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width * UIScale / 2;
        _strokeVertices[0] = a - normal;
        _strokeVertices[1] = a + normal;
        _strokeVertices[2] = b + normal;
        _strokeVertices[3] = b - normal;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _strokeVertices, Color.ToSrgb(color));
    }

    private string FinanceLabel(SWGalacticMapWorld world)
    {
        if (_financeSectors != null && world.Anchor && world.Sector is { } sector
            && _financeSectors.TryGetValue(sector.Id, out var revenue))
            return $"{world.Name}  {revenue.Tax:N0} кр";
        return world.Name;
    }

    private void LayoutLabels(DrawingHandleScreen handle)
    {
        var previous = _labelSize == Size && _labelScale == UIScale
            ? _labels.ToDictionary(l => l.Id, l => l.Box)
            : new Dictionary<string, UIBox2>();
        _labels.Clear();
        _labelSize = Size;
        _labelScale = UIScale;
        _labelsDirty = false;
        var points = _map.Worlds.ToDictionary(w => w.Id, w => MapToLocal(w.Position));
        foreach (var world in _map.Worlds.OrderByDescending(w => w.Id == _selectedWorld || w.Id == _hover)
                     .ThenByDescending(w => w.Anchor).ThenByDescending(w => _hubs.Contains(w.Id)))
        {
            var point = points[world.Id];
            if (point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height) continue;
            var size = handle.GetDimensions(SWConsoleStyle.Small, FinanceLabel(world), UIScale) / UIScale;
            // A selected label must stay under the pointer between the two clicks.
            // ViewUpdated clears old bounds when zoom, pan or window size changes.
            if (_financeSectors == null && (world.Id == _selectedWorld || world.Id == _hover) && previous.TryGetValue(world.Id, out var prior)
                && !OverlayIntersectsLabel(prior)
                && !_labels.Any(l => l.Box.Intersects(new UIBox2(prior.Left - 4, prior.Top - 3, prior.Right + 4, prior.Bottom + 3))))
            {
                _labels.Add((world.Id, prior));
                continue;
            }
            UIBox2? best = null;
            var cost = float.PositiveInfinity;
            foreach (var y in new[] { -size.Y - 10, 10, -size.Y - 28, 28, -size.Y - 46, 46 })
            foreach (var x in new[] { -size.X / 2, 10, -size.X - 10 })
            {
                var box = UIBox2.FromDimensions(point + new Vector2(x, y), size);
                if (box.Left < 4 || box.Right > Width - 4 || box.Top < 4 || box.Bottom > Height - 4) continue;
                if (OverlayIntersectsLabel(box)) continue;
                if (_politicalLabels.Any(l => l.Box.Intersects(box))) continue;
                if (_labels.Any(l => l.Box.Intersects(new UIBox2(box.Left - 4, box.Top - 3, box.Right + 4, box.Bottom + 3)))) continue;
                if (points.Any(p => p.Key != world.Id && new UIBox2(box.Left - 7, box.Top - 7, box.Right + 7, box.Bottom + 7).Contains(p.Value))) continue;
                var score = Math.Abs(y) + Math.Abs(x + size.X / 2) * .2f;
                if (score >= cost) continue;
                best = box;
                cost = score;
            }
            // Declutter a crowded overview. Hover and zoom reveal the same world's full label.
            if (best is { } bounds) _labels.Add((world.Id, bounds));
        }
    }
}
