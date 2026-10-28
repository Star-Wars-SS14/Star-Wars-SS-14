using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._SW.GlobalWar.UI;

public sealed partial class SWGlobalWarMapControl
{
    // Reuse vertex storage to keep the content assembly compatible with the launcher's IL verifier.
    private readonly Vector2[] _strokeVertices = new Vector2[4];
    private readonly List<(ProtoId<SWGlobalWarPlanetPrototype> Id, UIBox2 Bounds)> _labels = new();
    private Vector2 _labelSize;
    private float _labelScale;
    private bool _labelsDirty = true;

    /// <summary>Builds quadratic Bezier paths, choosing bends that avoid unrelated planets as in the Senate sector map.</summary>
    private void RebuildRoutes()
    {
        _routes.Clear();
        _hubs.Clear();
        foreach (var (id, planet) in _planets.OrderBy(pair => pair.Key.Id))
        foreach (var target in planet.Connections.OrderBy(target => target.Id))
        {
            if (target == id || !_planets.TryGetValue(target, out var other))
                continue;
            var bidirectional = other.Connections.Contains(id);
            if (bidirectional && string.CompareOrdinal(id.Id, target.Id) > 0)
                continue;

            var a = planet.MapPosition;
            var b = other.MapPosition;
            var delta = b - a;
            var normal = delta.LengthSquared() > .001f ? Vector2.Normalize(new Vector2(-delta.Y, delta.X)) : Vector2.Zero;
            var bend = Math.Clamp(delta.Length() * .22f, 4, 20);
            var control = (a + b) / 2;
            var bestCost = float.PositiveInfinity;
            foreach (var offset in new[] { bend, -bend, bend * 1.6f, -bend * 1.6f, bend * 2.4f, -bend * 2.4f })
            {
                var candidate = (a + b) / 2 + normal * offset;
                var cost = Math.Abs(offset) * .03f;
                foreach (var (obstacleId, obstacle) in _planets)
                {
                    if (obstacleId == id || obstacleId == target)
                        continue;
                    for (var step = 1; step < 20; step++)
                    {
                        var t = step / 20f;
                        var point = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * candidate + t * t * b;
                        cost += Math.Max(0, 9 - Vector2.Distance(point, obstacle.MapPosition));
                    }
                }
                if (cost >= bestCost)
                    continue;
                bestCost = cost;
                control = candidate;
            }

            var points = new Vector2[CurvePointCount];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (float) (points.Length - 1);
                points[i] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
            }
            _routes.Add(new Route(id, target, bidirectional, points));
        }

        foreach (var id in _planets.Keys)
        {
            if (_routes.Count(route => route.From == id || route.To == id) >= 3)
                _hubs.Add(id);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWGlobalWarConsoleStyle.Navigation);
        if (Width <= Padding * 2 || Height <= Padding * 2)
            return;

        DrawGrid(handle);
        foreach (var route in _routes)
        {
            if (!IsMarketRouteVisible(route))
                continue;
            var selected = _selectedRoute == route || _selected == route.From || _selected == route.To;
            var hovered = route == _hoveredRoute;
            var available = _current == route.From && _available.Contains(route.To)
                || route.Bidirectional && _current == route.To && _available.Contains(route.From);
            var color = hovered ? SWGlobalWarConsoleStyle.Brass : selected ? SWGlobalWarConsoleStyle.Accent
                : available ? SWGlobalWarConsoleStyle.Good.WithAlpha(.75f) : SWGlobalWarConsoleStyle.Edge;
            color = MarketRouteColor(route, color, selected || hovered);
            for (var i = 1; i < route.Points.Length; i++)
            {
                var a = MapToLocal(route.Points[i - 1]) * UIScale;
                var b = MapToLocal(route.Points[i]) * UIScale;
                // The dark underlay keeps crossing routes legible, matching the Senate atlas strokes.
                Stroke(handle, a, b, SWGlobalWarConsoleStyle.Navigation, selected || hovered ? 6 : 4);
                Stroke(handle, a, b, color, selected || hovered ? 2.8f : 1.8f);
            }
            if (!route.Bidirectional)
                DrawDirection(handle, route, color);
        }

        DrawMarketTraffic(handle);
        foreach (var (id, planet) in _planets)
        {
            var point = MapToLocal(planet.MapPosition) * UIScale;
            var selected = id == _selected;
            var hovered = id == _hovered;
            var current = id == _current;
            var color = selected || hovered ? SWGlobalWarConsoleStyle.Accent
                : current ? SWGlobalWarConsoleStyle.Brass
                : MarketPlanetColor(id) ?? (_available.Contains(id) ? SWGlobalWarConsoleStyle.Good : SWGlobalWarConsoleStyle.TextDim);
            if (selected || hovered)
                handle.DrawCircle(point, (selected ? 11 : 9) * UIScale, Color.ToSrgb(color), false);
            if (current)
            {
                var ink = SWGlobalWarConsoleStyle.Brass;
                for (var i = 0; i < 4; i++)
                {
                    var angle = i * MathF.PI / 2;
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    handle.DrawLine(point + direction * 14 * UIScale, point + direction * 18 * UIScale, ink);
                }
            }
            var hub = _hubs.Contains(id);
            var radius = (hub ? 6 : 4) * UIScale;
            handle.DrawCircle(point, radius + 2 * UIScale, Color.ToSrgb(SWGlobalWarConsoleStyle.Navigation));
            handle.DrawCircle(point, radius, Color.ToSrgb(color), !hub);
            if (hub)
                handle.DrawCircle(point, 2 * UIScale, Color.ToSrgb(color));
        }

        if (_labelsDirty || _labelSize != Size || _labelScale != UIScale)
            LayoutLabels(handle);
        foreach (var (id, box) in _labels)
        {
            var point = MapToLocal(_planets[id].MapPosition);
            var end = Vector2.Clamp(point, box.TopLeft, box.BottomRight);
            if (Vector2.Distance(point, end) > 15)
                handle.DrawLine(point * UIScale, end * UIScale, SWGlobalWarConsoleStyle.Edge);
            handle.DrawRect(new UIBox2((box.TopLeft - new Vector2(2, 1)) * UIScale,
                (box.BottomRight + new Vector2(2, 1)) * UIScale), SWGlobalWarConsoleStyle.Navigation);
            var color = id == _selected || id == _hovered ? SWGlobalWarConsoleStyle.Accent
                : id == _current ? SWGlobalWarConsoleStyle.Brass : SWGlobalWarConsoleStyle.TextBright;
            handle.DrawString(SWGlobalWarConsoleStyle.Small, box.TopLeft * UIScale, _planets[id].Name, UIScale, color);
            if (_marketLabels.TryGetValue(id, out var metric))
            {
                var height = handle.GetDimensions(SWGlobalWarConsoleStyle.Small, _planets[id].Name, UIScale).Y;
                handle.DrawString(SWGlobalWarConsoleStyle.Small, box.TopLeft * UIScale + new Vector2(0, height + 3 * UIScale),
                    metric, UIScale, MarketPlanetColor(id) ?? SWGlobalWarConsoleStyle.TextDim);
            }
        }

        if (_planets.Count == 0)
        {
            var text = Loc.GetString("sw-global-war-travel-empty-map");
            var size = handle.GetDimensions(SWGlobalWarConsoleStyle.Body, text, UIScale);
            handle.DrawString(SWGlobalWarConsoleStyle.Body, (PixelSize - size) / 2, text, UIScale, SWGlobalWarConsoleStyle.TextDim);
        }
    }

    private void DrawGrid(DrawingHandleScreen handle)
    {
        var step = 64 * UIScale * _drawZoom;
        var offset = (Size / 2 + _pan) * UIScale;
        var color = SWGlobalWarConsoleStyle.Edge.WithAlpha(.16f);
        for (var x = offset.X % step; x < PixelWidth; x += step)
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, PixelHeight), color);
        for (var y = offset.Y % step; y < PixelHeight; y += step)
            handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), color);
    }

    private void DrawDirection(DrawingHandleScreen handle, Route route, Color color)
    {
        var index = route.Points.Length / 2;
        var point = MapToLocal(route.Points[index]) * UIScale;
        var delta = MapToLocal(route.Points[index + 1]) - MapToLocal(route.Points[index - 1]);
        if (delta.LengthSquared() < .001f)
            return;
        var direction = Vector2.Normalize(delta);
        var normal = new Vector2(-direction.Y, direction.X);
        var tip = point + direction * 5 * UIScale;
        Stroke(handle, tip - direction * 9 * UIScale + normal * 4 * UIScale, tip, color, 1.8f);
        Stroke(handle, tip - direction * 9 * UIScale - normal * 4 * UIScale, tip, color, 1.8f);
    }

    private void Stroke(DrawingHandleScreen handle, Vector2 a, Vector2 b, Color color, float width)
    {
        var delta = b - a;
        if (delta.LengthSquared() < .0001f)
            return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width * UIScale / 2;
        _strokeVertices[0] = a - normal;
        _strokeVertices[1] = a + normal;
        _strokeVertices[2] = b + normal;
        _strokeVertices[3] = b - normal;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _strokeVertices, Color.ToSrgb(color));
    }

    /// <summary>Places labels around nodes without overlapping other labels or planets, preserving hovered labels under the cursor.</summary>
    /// <param name="handle">Drawing context used to measure text at the current UI scale.</param>
    private void LayoutLabels(DrawingHandleScreen handle)
    {
        var previous = _labelSize == Size && _labelScale == UIScale
            ? _labels.ToDictionary(label => label.Id, label => label.Bounds)
            : new Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, UIBox2>();
        _labels.Clear();
        _labelSize = Size;
        _labelScale = UIScale;
        _labelsDirty = false;
        var points = _planets.ToDictionary(pair => pair.Key, pair => MapToLocal(pair.Value.MapPosition));
        foreach (var (id, planet) in _planets.OrderByDescending(pair => pair.Key == _selected || pair.Key == _hovered)
                     .ThenByDescending(pair => pair.Key == _current).ThenByDescending(pair => _hubs.Contains(pair.Key)))
        {
            var point = points[id];
            if (point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height)
                continue;
            var size = handle.GetDimensions(SWGlobalWarConsoleStyle.Small, planet.Name, UIScale) / UIScale;
            if (_marketLabels.TryGetValue(id, out var metric))
            {
                var metricSize = handle.GetDimensions(SWGlobalWarConsoleStyle.Small, metric, UIScale) / UIScale;
                size = new Vector2(Math.Max(size.X, metricSize.X), size.Y + metricSize.Y + 3);
            }
            if ((id == _selected || id == _hovered) && previous.TryGetValue(id, out var prior)
                && prior.Width >= size.X && prior.Height >= size.Y
                && !_labels.Any(label => label.Bounds.Intersects(new UIBox2(prior.Left - 4, prior.Top - 3, prior.Right + 4, prior.Bottom + 3))))
            {
                _labels.Add((id, prior));
                continue;
            }

            UIBox2? best = null;
            var cost = float.PositiveInfinity;
            foreach (var y in new[] { -size.Y - 10, 10, -size.Y - 28, 28, -size.Y - 46, 46 })
            foreach (var x in new[] { -size.X / 2, 10, -size.X - 10 })
            {
                var box = UIBox2.FromDimensions(point + new Vector2(x, y), size);
                if (box.Left < 4 || box.Right > Width - 4 || box.Top < 4 || box.Bottom > Height - 4)
                    continue;
                if (_labels.Any(label => label.Bounds.Intersects(new UIBox2(box.Left - 4, box.Top - 3, box.Right + 4, box.Bottom + 3))))
                    continue;
                if (points.Any(other => other.Key != id && new UIBox2(box.Left - 7, box.Top - 7, box.Right + 7, box.Bottom + 7).Contains(other.Value)))
                    continue;
                var score = Math.Abs(y) + Math.Abs(x + size.X / 2) * .2f;
                if (score >= cost)
                    continue;
                best = box;
                cost = score;
            }
            // Dense overviews reveal additional names when hovered or zoomed, rather than stacking unreadable labels.
            if (best is { } bounds)
                _labels.Add((id, bounds));
        }
    }
}
