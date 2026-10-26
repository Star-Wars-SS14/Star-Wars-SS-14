using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWPlanetSurfaceControl
{
    private sealed class SurfaceTrafficPath(string from, string to, IReadOnlyList<Vector2i> cells)
    {
        public readonly string From = from;
        public readonly string To = to;
        public readonly IReadOnlyList<Vector2i> Cells = cells;
        public Vector2[] Points = Array.Empty<Vector2>();
        public float[] Distances = Array.Empty<float>();
        public float Length;
        public float Phase;
        public double Slots;
        public Color Color;
    }

    private SWEconomyWorldView? _trafficWorld;
    private readonly Dictionary<(string From, string To), SurfaceTrafficPath> _trafficPaths = new();
    private readonly List<SurfaceTrafficPath> _activeTraffic = new();
    private readonly List<SWPlanetSurfaceFlow> _trafficFlows = new();
    private readonly HashSet<string> _trafficSites = new();
    private readonly Dictionary<string, (int Slots, Color Color)> _cargo = new();
    private string[] _residentGoods = Array.Empty<string>();
    private IConfigurationManager _configuration = default!;
    private bool _showTraffic = true;
    private bool _reduceMotion;
    private double _trafficSeconds;
    public IReadOnlyList<SWPlanetSurfaceFlow> TrafficFlows => _trafficFlows;
    public int TrafficDirectionCount => _showTraffic ? _activeTraffic.Count : 0;
    public double TrafficAnimationSeconds => _trafficSeconds;

    private void InitializeTraffic(IPrototypeManager proto)
    {
        _configuration = IoCManager.Resolve<IConfigurationManager>();
        var residentGroups = new HashSet<string>
        {
            "water", "nutrition", "care_drugs", "care_supplies", "household", "luxury",
            "closed_habitats", "implant_care", "regenerative_care", "special_medicine", "shadow",
        };
        if (proto.TryIndex<SWRepublicEconomyScenarioPrototype>("SenateRepublicEconomy", out var scenario))
        {
            var demands = scenario.Demands.Where(d => d.World.Id == Surface.ID).ToArray();
            var nonResidential = demands.Where(d => d.Group is "orders" or "maintenance").Select(d => d.Good.Id).ToHashSet();
            _residentGoods = demands.Where(d => residentGroups.Contains(d.Group) && !nonResidential.Contains(d.Good.Id))
                .Select(d => d.Good.Id).Distinct().ToArray();
        }
        foreach (var good in proto.EnumeratePrototypes<SWCommodityPrototype>())
        {
            var color = Ink(good.Category.Id switch
            {
                "food" => "#B7D794", "resources" => "#CDAF87", "materials" => "#B2C8DA",
                "energy" => "#EDC275", "components" => "#8FADE8", "civil" => "#83D6C7",
                "health" => "#7EE0CF", "defense" => "#E6A68B", "culture" => "#C7ADE6",
                "controlled" => "#D298B6", _ => "#79DEEC",
            });
            _cargo[good.ID] = (good.CargoSlots, color);
        }
    }

    public void SetTrafficFilter(string? commodity, bool showTraffic = true)
    {
        if (_commodity == commodity && _showTraffic == showTraffic) return;
        _commodity = commodity;
        _showTraffic = showTraffic;
        RebuildTraffic();
    }

    public void SetReducedMotion(bool enabled) => _reduceMotion = enabled;

    public IReadOnlyList<SWPlanetSurfaceFlow> FlowsAt(Vector2i cell) => _trafficFlows
        .Where(f => _trafficPaths.TryGetValue((f.From, f.To), out var path) && path.Cells.Contains(cell)).ToArray();

    private void RebuildTraffic()
    {
        _trafficFlows.Clear();
        _activeTraffic.Clear();
        _trafficSites.Clear();
        foreach (var path in _trafficPaths.Values) path.Slots = 0;
        if (!_ready || _trafficWorld == null) return;
        var sites = Surface.Sites.ToDictionary(s => s.Id);
        foreach (var flow in SWPlanetSurfaceFlows.Build(Surface, _trafficWorld, _recipes, _residentGoods))
        {
            if (_commodity != null && flow.Good != _commodity || !_cargo.TryGetValue(flow.Good, out var cargo) || cargo.Slots <= 0) continue;
            var key = (flow.From, flow.To);
            if (!_trafficPaths.TryGetValue(key, out var path))
            {
                var cells = _network.FindPath(sites[flow.From].Cell, sites[flow.To].Cell);
                path = new SurfaceTrafficPath(flow.From, flow.To, cells) { Phase = (_trafficPaths.Count * .381966f) % 1 };
                _trafficPaths.Add(key, path);
                BuildTrafficGeometry(path);
            }
            if (path.Length <= 0) continue;
            if (path.Slots == 0)
            {
                _activeTraffic.Add(path);
                path.Color = _commodity != null ? cargo.Color : sites[flow.To].Kind switch
                {
                    "settlement" => Ink("#B7D794"), "power" => Ink("#EDC275"),
                    "port" => Ink("#EDC275"), "storage" => Ink("#79DEEC"), _ => Ink("#C5B5E5"),
                };
            }
            path.Slots += flow.UnitsPerHour * cargo.Slots;
            _trafficFlows.Add(flow);
            _trafficSites.Add(flow.From);
            _trafficSites.Add(flow.To);
        }
    }

    private static void BuildTrafficGeometry(SurfaceTrafficPath path)
    {
        path.Points = new Vector2[path.Cells.Count];
        path.Distances = new float[path.Cells.Count];
        // Mitered right-hand lanes keep opposite traffic apart through orthogonal turns.
        for (var i = 0; i < path.Cells.Count; i++)
        {
            var center = new Vector2(path.Cells[i].X + .5f, path.Cells[i].Y + .5f);
            Vector2 Normal(int a, int b)
            {
                var d = path.Cells[b] - path.Cells[a];
                return new Vector2(-d.Y, d.X);
            }
            var before = i > 0 ? Normal(i - 1, i) : Vector2.Zero;
            var after = i + 1 < path.Cells.Count ? Normal(i, i + 1) : Vector2.Zero;
            var offset = before == after ? before : before + after;
            path.Points[i] = center + offset * .085f;
            if (i > 0) path.Length += Vector2.Distance(path.Points[i - 1], path.Points[i]);
            path.Distances[i] = path.Length;
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        AdvanceTraffic(args.DeltaSeconds);
    }

    private void AdvanceTraffic(float seconds)
    {
        if (!_ready || _paused || !_showTraffic || _reduceMotion || _configuration.GetCVar(CCVars.ReducedMotion)
            || _activeTraffic.Count == 0 || !float.IsFinite(seconds) || seconds <= 0) return;
        _trafficSeconds += Math.Min(seconds, .25f);
    }

    private static Vector2 Sample(SurfaceTrafficPath path, float distance, out Vector2 direction)
    {
        distance = Math.Clamp(distance, 0, path.Length);
        var segment = 1;
        var end = path.Points.Length - 1;
        while (segment < end)
        {
            var middle = segment + (end - segment) / 2;
            if (path.Distances[middle] < distance) segment = middle + 1;
            else end = middle;
        }
        var length = path.Distances[segment] - path.Distances[segment - 1];
        direction = (path.Points[segment] - path.Points[segment - 1]) / length;
        return path.Points[segment - 1] + direction * (distance - path.Distances[segment - 1]);
    }

    private void DrawTraffic(DrawingHandleScreen handle, float tile)
    {
        if (!_ready || !_showTraffic) return;
        var origin = Origin;
        var remaining = 160;
        foreach (var path in _activeTraffic)
        {
            var focused = SelectedSiteId == null || path.From == SelectedSiteId || path.To == SelectedSiteId;
            var color = path.Color.WithAlpha(focused ? .95f : .2f);
            var count = Math.Clamp((int) Math.Ceiling(path.Length / 4 + Math.Log(1 + path.Slots)), 1, 5);
            for (var i = 0; i < count && remaining > 0; i++, remaining--)
            {
                var distance = (float) ((_trafficSeconds * 1.35 + (path.Phase + (double) i / count) * path.Length) % path.Length);
                var p = origin + Sample(path, distance, out var direction) * tile;
                if (p.X < -tile || p.Y < -tile || p.X > Width + tile || p.Y > Height + tile) continue;
                // A short trail follows the road around corners; no chord through terrain.
                var previous = p;
                for (var tail = 1; tail <= 3; tail++)
                {
                    var d = distance - tail * .12f;
                    if (d < 0) break;
                    var next = origin + Sample(path, d, out _) * tile;
                    handle.DrawLine(previous * UIScale, next * UIScale, color.WithAlpha(color.A * (1 - tail * .23f)));
                    previous = next;
                }
                var side = new Vector2(-direction.Y, direction.X);
                var size = Math.Clamp(tile * .12f, 2.2f, 5f);
                var tip = p + direction * size;
                var left = p - direction * size + side * size * .7f;
                var right = p - direction * size - side * size * .7f;
                handle.DrawLine(left * UIScale, tip * UIScale, color);
                handle.DrawLine(tip * UIScale, right * UIScale, color);
                handle.DrawLine((p - direction * size) * UIScale, tip * UIScale, color);
            }
        }
    }
}
