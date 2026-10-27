using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    // Freight is an aggregate. These bounded, repeating markers communicate direction and
    // intensity, never individual ships or authoritative positions along a route.
    private const int MaxTrafficMarkers = 256;
    private const int TrailSegments = 4;
    private readonly List<TrafficPath> _trafficPaths = new();
    private readonly Dictionary<string, TrafficPath> _trafficByEdge = new();
    private readonly Dictionary<SWGalacticMapRoute, TrafficPath> _trafficByRoute = new();
    private readonly Dictionary<string, int> _cargoSizes = new();
    private readonly Vector2[] _trailPoints = new Vector2[TrailSegments + 1];
    private DrawVertexUV2DColor[] _trafficVertices = Array.Empty<DrawVertexUV2DColor>();
    private int _trafficVertexCount;
    private SWEconomyView? _economy;
    private int _economyMapMode = -1;
    private string? _economyMapGood;
    private readonly Dictionary<string, (int Count, int Shortages, double? WorstHours, double Production)> _economySectorCache = new();

    public void SetEconomyMapMode(int mode, string? good)
    {
        _economyMapMode = mode;
        _economyMapGood = good;
        _economySectorCache.Clear();
    }

    // An overview anchor represents the entire sector, including worlds hidden at galaxy scale.
    private (int Count, int Shortages, double? WorstHours, double Production)? EconomySectorStats(string id)
    {
        if (_focusSector != null || _economy is not { Ready: true }) return null;
        if (_economySectorCache.TryGetValue(id, out var cached)) return cached;
        var geography = IoCManager.Resolve<IPrototypeManager>().Index<SWGalacticMapPrototype>("SenateRepublic");
        var anchor = geography.Worlds.FirstOrDefault(w => w.Id == id);
        if (anchor?.Sector is not { } sector) return null;
        var ids = geography.Worlds.Where(w => w.Sector?.Id == sector.Id).Select(w => w.Id).ToHashSet();
        var goods = _economy.Worlds.Where(w => ids.Contains(w.World))
            .SelectMany(w => w.Goods.Where(g => _economyMapGood == null || g.Good == _economyMapGood)
                .Select(g => (World: w.World, Good: g))).ToArray();
        var demand = goods.Where(g => g.Good.RequiredPerHour > 0).ToArray();
        var result = (goods.Length, demand.Where(g => g.Good.Stock / g.Good.RequiredPerHour < 4).Select(g => g.World).Distinct().Count(),
            demand.Length == 0 ? (double?) null : demand.Min(g => g.Good.Stock / g.Good.RequiredPerHour), goods.Sum(g => g.Good.ProducedPerHour));
        _economySectorCache[id] = result;
        return result;
    }

    public string? EconomySectorSummary(string id)
    {
        if (EconomySectorStats(id) is not { } stats) return null;
        if (stats.Count == 0) return "Сектор: нет данных по товару";
        var coverage = stats.WorstHours is { } hours ? $"{hours:0.0} ч" : "нет спроса";
        return $"Сектор: проблемных миров {stats.Shortages}\nМинимальное покрытие: {coverage}\nОбщий выпуск: {stats.Production:0.##} ед/ч";
    }

    private Color? EconomyWorldColor(string id)
    {
        if (_economyMapMode is < 0 or > 1 || _economy is not { Ready: true }) return null;
        if (EconomySectorStats(id) is { } sector)
            return _economyMapMode == 0 ? (sector.Shortages > 0 ? SWConsoleStyle.Bad : SWConsoleStyle.TextDim)
                : (sector.Production > 0 ? SWConsoleStyle.Good : SWConsoleStyle.TextDim);
        var world = _economy.Worlds.FirstOrDefault(w => w.World == id);
        if (world == null) return null;
        var goods = world.Goods.Where(g => _economyMapGood == null || g.Good == _economyMapGood).ToArray();
        if (_economyMapMode == 0)
            return goods.Any(g => g.RequiredPerHour > 0 && g.Stock / g.RequiredPerHour < 4)
                ? SWConsoleStyle.Bad : SWConsoleStyle.TextDim;
        return goods.Any(g => g.ProducedPerHour > 0) ? SWConsoleStyle.Good : SWConsoleStyle.TextDim;
    }

    private string? _trafficCommodity;
    private bool _showTraffic = true;
    private bool _hasTraffic;
    private bool _reduceTrafficMotion;
    private double _trafficSeconds;
    private Vector2 _trafficGeometrySize;
    private Vector2 _trafficGeometryPan;
    private float _trafficGeometryScale;
    private float _trafficGeometryZoom;
    private Color _commodityColor = Color.FromHex("#79DEEC");
    private Color _reverseTrafficColor = Color.FromHex("#E9C17E");
    private Color _trafficModulate = Color.White;

    public double TrafficAnimationSeconds => _trafficSeconds;
    public int TrafficDirectionCount
    {
        get
        {
            var count = 0;
            foreach (var path in _trafficPaths)
            {
                if (path.Forward.Present) count++;
                if (path.Reverse.Present) count++;
            }
            return count;
        }
    }

    private sealed class TrafficPath(SWGalacticMapRoute route, Vector2[] points, float phase)
    {
        public readonly SWGalacticMapRoute Route = route;
        public readonly Vector2[] MapPoints = points;
        public readonly Vector2[] ScreenPoints = new Vector2[points.Length];
        public readonly float[] Distances = new float[points.Length];
        public readonly TrafficDirection Forward = new();
        public readonly TrafficDirection Reverse = new();
        public readonly float Phase = phase;
        public readonly float MapLength = GetPathLength(points);
        public double Capacity;
        public float Length;
    }

    private sealed class TrafficDirection
    {
        public double Units;
        public double Transit;
        public double Slots;
        public double TransitSlots;
        public double WeightedHours;
        public bool Present => Units > .000001 || Transit > .000001;
        public void Clear() => Units = Transit = Slots = TransitSlots = WeightedHours = 0;
    }

    public void SetEconomy(SWEconomyView? view)
    {
        _economy = view;
        _economySectorCache.Clear();
        UpdateMedicalSupply();
        RebuildTraffic();
    }

    public void SetTrafficFilter(string? commodity, bool showTraffic = true)
    {
        if (_trafficCommodity == commodity && _showTraffic == showTraffic) return;
        _trafficCommodity = commodity;
        _showTraffic = showTraffic;
        RebuildTraffic();
    }

    /// <summary>Additional per-view motion preference; the global reduced-motion option also applies.</summary>
    public void SetReducedMotion(bool enabled) => _reduceTrafficMotion = enabled;

    private void RebuildTraffic()
    {
        if (_trafficPaths.Count == 0)
        {
            foreach (var path in _paths)
            {
                var traffic = new TrafficPath(path.Route, path.Points, (.17f + _trafficPaths.Count * .381966f) % 1);
                _trafficPaths.Add(traffic);
                _trafficByEdge[SWTransitLinkPrototype.LinkId(path.Route.From, path.Route.To)] = traffic;
                _trafficByRoute[path.Route] = traffic;
            }
            var prototypes = IoCManager.Resolve<IPrototypeManager>();
            foreach (var good in prototypes.EnumeratePrototypes<SWCommodityPrototype>())
                _cargoSizes[good.ID] = good.CargoSlots;
        }
        foreach (var path in _trafficPaths)
        {
            path.Forward.Clear();
            path.Reverse.Clear();
            path.Capacity = 0;
        }
        _hasTraffic = false;
        if (_economy is not { Ready: true } || !_showTraffic) return;
        foreach (var link in _economy.Links)
            if (_trafficByEdge.TryGetValue(link.Edge, out var path))
                path.Capacity = double.IsFinite(link.CapacitySlotsPerHour) ? Math.Max(0, link.CapacitySlotsPerHour) : 0;

        foreach (var flow in _economy.Flows)
        {
            if (_trafficCommodity != null && flow.Good != _trafficCommodity
                || !_trafficByEdge.TryGetValue(flow.Edge, out var path)
                || path.Capacity <= 0
                || !_cargoSizes.TryGetValue(flow.Good, out var cargo) || cargo <= 0
                || !double.IsFinite(flow.UnitsPerHour) || !double.IsFinite(flow.InTransit)
                || !double.IsFinite(flow.TravelHours)) continue;
            TrafficDirection direction;
            if (flow.From == path.Route.From && flow.To == path.Route.To) direction = path.Forward;
            else if (flow.From == path.Route.To && flow.To == path.Route.From) direction = path.Reverse;
            else continue;
            var units = Math.Max(0, flow.UnitsPerHour);
            var transit = Math.Max(0, flow.InTransit);
            if (_medicalVisible)
            {
                // Project cargo already has a finite tracked medical marker. Rendering the
                // same volume as repeating aggregate traffic made a delivered vaccine look
                // like a ship endlessly restarting its route.
                foreach (var medical in _economy.MedicalSupplyFlows.Where(m => m.EdgeId == flow.Edge
                             && m.From == flow.From && m.To == flow.To && m.Good == flow.Good))
                {
                    transit = Math.Max(0, transit - medical.Quantity);
                    if (Math.Abs(medical.DepartAtHours - _economy.Hour) < 1e-7)
                        units = Math.Max(0, units - medical.Quantity / .25);
                }
            }
            direction.Units += units;
            direction.Transit += transit;
            direction.Slots += units * cargo;
            direction.TransitSlots += transit * cargo;
            direction.WeightedHours += (units + transit) * Math.Max(.25, flow.TravelHours);
            _hasTraffic |= direction.Present;
        }
        _commodityColor = TrafficCommodityColor(_trafficCommodity);
        _reverseTrafficColor = _trafficCommodity == null ? Color.FromHex("#E9C17E") : Color.InterpolateBetween(_commodityColor, Color.White, .3f);
        if (_trafficVertices.Length == 0)
            _trafficVertices = new DrawVertexUV2DColor[MaxTrafficMarkers * (TrailSegments * 12 + 12)];
    }

    private static Color TrafficCommodityColor(string? commodity)
    {
        if (commodity == null || !IoCManager.Resolve<IPrototypeManager>().TryIndex<SWCommodityPrototype>(commodity, out var good))
            return Color.FromHex("#79DEEC");
        return Color.FromHex(good.Category.Id switch
        {
            "food" => "#B7D794",
            "resources" => "#CDAF87",
            "materials" => "#B2C8DA",
            "energy" => "#EDC275",
            "components" => "#8FADE8",
            "civil" => "#83D6C7",
            "health" => "#7EE0CF",
            "defense" => "#E6A68B",
            "culture" => "#C7ADE6",
            "controlled" => "#D298B6",
            _ => "#79DEEC",
        });
    }

    private void AdvanceTraffic(float seconds)
    {
        if (_economy is not { Ready: true, Paused: false } || !_showTraffic
            || _reduceTrafficMotion || _configuration.GetCVar(CCVars.ReducedMotion)
            || !float.IsFinite(seconds) || seconds <= 0) return;
        _trafficSeconds += Math.Min(seconds, .25f);
    }

    private Color TrafficRouteColor(SWGalacticMapRoute route, Color color, bool selected)
    {
        if (!_showTraffic || _economy is not { Ready: true }) return color;
        if (!_hasTraffic) return color.WithAlpha(selected ? .8f : .45f);
        var active = _trafficByRoute.TryGetValue(route, out var path)
            && (path.Forward.Present || path.Reverse.Present);
        if (selected) return active ? _commodityColor.WithAlpha(.65f) : color.WithAlpha(.55f);
        return color.WithAlpha(active ? .27f : .12f);
    }

    private void UpdateTrafficGeometry()
    {
        if (_trafficGeometrySize == Size && _trafficGeometryPan == _pan
            && _trafficGeometryScale == UIScale && _trafficGeometryZoom == Zoom) return;
        _trafficGeometrySize = Size;
        _trafficGeometryPan = _pan;
        _trafficGeometryScale = UIScale;
        _trafficGeometryZoom = Zoom;
        foreach (var path in _trafficPaths)
        {
            path.Length = 0;
            for (var i = 0; i < path.MapPoints.Length; i++)
            {
                path.ScreenPoints[i] = MapToLocal(path.MapPoints[i]) * UIScale;
                if (i > 0) path.Length += Vector2.Distance(path.ScreenPoints[i - 1], path.ScreenPoints[i]);
                path.Distances[i] = path.Length;
            }
        }
    }

    private bool FocusedTraffic(TrafficPath path) => path.Route == _selectedRoute
        || _selectedWorld != null && (path.Route.From == _selectedWorld || path.Route.To == _selectedWorld);

    private void DrawTraffic(DrawingHandleScreen handle)
    {
        _trafficVertexCount = 0;
        if (!_showTraffic || _economy is not { Ready: true } || _trafficVertices.Length == 0) return;
        UpdateTrafficGeometry();
        _trafficModulate = handle.Modulate;
        var remaining = MaxTrafficMarkers;
        var remainingDirections = TrafficDirectionCount;
        // Reserve marker budget for the inspected connections, even in a very dense overview.
        for (var pass = 0; pass < 2; pass++)
        foreach (var path in _trafficPaths)
        {
            var focused = FocusedTraffic(path);
            if (focused != (pass == 0) || path.Length <= 22 * UIScale) continue;
            var alpha = focused ? 1f : _selectedRoute != null || _selectedWorld != null ? .48f : .72f;
            DrawTrafficDirection(path, path.Forward, false, _commodityColor, alpha, focused, ref remaining, ref remainingDirections);
            DrawTrafficDirection(path, path.Reverse, true, _reverseTrafficColor, alpha, focused, ref remaining, ref remainingDirections);
            if (remaining == 0) break;
        }
        if (_trafficVertexCount != 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(_trafficVertices, 0, _trafficVertexCount));
    }

    private void DrawTrafficDirection(TrafficPath path, TrafficDirection direction, bool reverse,
        Color color, float alpha, bool focused, ref int remaining, ref int remainingDirections)
    {
        if (!direction.Present || remaining <= 0) return;
        var hours = direction.WeightedHours / (direction.Units + direction.Transit);
        var load = Math.Clamp(Math.Max(direction.Slots, direction.TransitSlots / hours) / path.Capacity, 0, 1);
        // Preserve at least one marker for the other visible directions before spending
        // the budget on density. A busy Core cannot hide all Outer Rim freight.
        var available = Math.Min(remaining, Math.Max(1, remaining - remainingDirections + 1));
        remainingDirections--;
        var markers = Math.Min(available, Math.Min(focused ? 4 : 2, 1 + (int) (load * 4)));
        markers = Math.Min(markers, Math.Max(1, (int) (path.Length / (70 * UIScale))));
        remaining -= markers;
        // A restrained visual cadence, independent of how often the server publishes. Longer
        // transfers move a little slower; it is deliberately not a shipment position estimate.
        var period = Math.Clamp(path.MapLength * .18, 3, 18) * Math.Sqrt(hours);
        var trim = 11 * UIScale;
        var usable = path.Length - trim * 2;
        var laneOffset = path.Forward.Present && path.Reverse.Present ? 3.6f * UIScale : 0;
        for (var marker = 0; marker < markers; marker++)
        {
            var phase = (float) ((_trafficSeconds / period + path.Phase + marker / (double) markers) % 1);
            var distance = trim + phase * usable;
            var fade = Math.Min(1, Math.Min(phase, 1 - phase) * 12);
            var intensity = alpha * fade;
            if (intensity <= .01f) continue;
            var trail = Math.Min((focused ? 27 : 20) * UIScale, usable * .24f);
            for (var segment = 0; segment <= TrailSegments; segment++)
            {
                var sample = Math.Max(trim, distance - trail * (1 - segment / (float) TrailSegments));
                _trailPoints[segment] = TrafficPoint(path, sample, reverse, laneOffset, out _);
            }
            for (var segment = 0; segment < TrailSegments; segment++)
            {
                var startAlpha = intensity * segment / TrailSegments;
                var endAlpha = intensity * (segment + 1) / TrailSegments;
                TrafficStroke(_trailPoints[segment], _trailPoints[segment + 1], color.WithAlpha(startAlpha * .10f), color.WithAlpha(endAlpha * .10f), 7);
                TrafficStroke(_trailPoints[segment], _trailPoints[segment + 1], color.WithAlpha(startAlpha * .60f), color.WithAlpha(endAlpha * .80f), 1.6f);
            }
            var head = TrafficPoint(path, distance, reverse, laneOffset, out var tangent);
            var normal = new Vector2(-tangent.Y, tangent.X);
            var tip = head + tangent * 2.5f * UIScale;
            var tail = head - tangent * 3.5f * UIScale;
            var bright = Color.InterpolateBetween(color, Color.White, .62f).WithAlpha(intensity);
            TrafficStroke(tail + normal * 2.6f * UIScale, tip, bright, bright, 1.25f);
            TrafficStroke(tail - normal * 2.6f * UIScale, tip, bright, bright, 1.25f);
        }
    }

    private static Vector2 TrafficPoint(TrafficPath path, float distance, bool reverse, float offset, out Vector2 tangent)
    {
        distance = Math.Clamp(reverse ? path.Length - distance : distance, 0, path.Length);
        var low = 1;
        var high = path.Distances.Length - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (path.Distances[mid] < distance) low = mid + 1;
            else high = mid;
        }
        var a = path.ScreenPoints[low - 1];
        var b = path.ScreenPoints[low];
        var length = path.Distances[low] - path.Distances[low - 1];
        tangent = length > .001f ? (b - a) / length : Vector2.UnitX;
        var point = Vector2.Lerp(a, b, length > .001f ? (distance - path.Distances[low - 1]) / length : 0);
        if (reverse) tangent = -tangent;
        return point + new Vector2(-tangent.Y, tangent.X) * offset;
    }

    private static float GetPathLength(Vector2[] points)
    {
        var length = 0f;
        for (var i = 1; i < points.Length; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    private void TrafficStroke(Vector2 a, Vector2 b, Color start, Color end, float width)
    {
        var delta = b - a;
        if (delta.LengthSquared() < .0001f || _trafficVertexCount + 6 > _trafficVertices.Length) return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width * UIScale / 2;
        start = Color.FromSrgb(start * _trafficModulate);
        end = Color.FromSrgb(end * _trafficModulate);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(a - normal, start);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(a + normal, start);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(b + normal, end);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(a - normal, start);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(b + normal, end);
        _trafficVertices[_trafficVertexCount++] = new DrawVertexUV2DColor(b - normal, end);
    }
}
