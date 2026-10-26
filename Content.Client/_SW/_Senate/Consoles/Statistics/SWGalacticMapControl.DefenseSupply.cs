using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    private bool _defenseSupplyVisible = true;
    private double _defenseSupplyHour = double.NaN;
    private double _defenseSupplyElapsed;
    private readonly List<SWDefenseSupplyFlowView> _defenseSupplyFlows = new();
    private readonly Dictionary<string, (SWGalacticMapRoute Route, Vector2[] Points)> _defenseSupplyPaths = new();
    private static readonly Color SupplyColor = Color.FromHex("#D4B57D");
    public bool DefenseSupplyVisible => _defenseSupplyVisible;
    public int DefenseSupplyShipmentCount => _defenseSupplyFlows.Count;

    public void SetDefenseSupplyVisible(bool visible)
    {
        _defenseSupplyVisible = visible;
        UpdateHover();
    }

    private void UpdateDefenseSupply(SWDefenseMapState? state)
    {
        var hadFlows = _defenseSupplyFlows.Count > 0;
        _defenseSupplyFlows.Clear();
        _defenseSupplyPaths.Clear();
        if (state == null || !double.IsFinite(state.SupplyHour))
        {
            _defenseSupplyHour = double.NaN;
            _defenseSupplyElapsed = 0;
            return;
        }
        // Political/fleet snapshots can refresh faster than the economy worker. Do not
        // rewind a shipment every time the same economic timestamp is republished.
        if (_defenseSupplyHour != state.SupplyHour) _defenseSupplyElapsed = 0;
        _defenseSupplyHour = state.SupplyHour;
        foreach (var flow in state.SupplyFlows)
        {
            if (string.IsNullOrEmpty(flow.Id) || !double.IsFinite(flow.Quantity) || flow.Quantity <= 0
                || !double.IsFinite(flow.DepartAtHours) || !double.IsFinite(flow.ArriveAtHours)
                || flow.ArriveAtHours <= flow.DepartAtHours) continue;
            _defenseSupplyFlows.Add(flow);
            var path = _paths.FirstOrDefault(p => p.Route.From == flow.From && p.Route.To == flow.To
                || p.Route.From == flow.To && p.Route.To == flow.From);
            if (path.Route != null) _defenseSupplyPaths[flow.Id] = path;
        }
        if (!hadFlows && _defenseSupplyFlows.Count > 0) _defenseSupplyElapsed = 0;
    }

    private void AdvanceDefenseSupply(float seconds)
    {
        if (_defenseSupplyFlows.Count == 0 || _defense is not { ClockRunning: true } || _defense.RealSecondsPerHour <= 0
            || !float.IsFinite(seconds) || seconds <= 0) return;
        _defenseSupplyElapsed += Math.Min(seconds, .25f) / _defense.RealSecondsPerHour;
    }

    /// <summary>Current actual shipment leg, using the economic snapshot's own clock.</summary>
    public Vector2? DefenseSupplyPosition(string shipmentId)
    {
        var flow = _defenseSupplyFlows.FirstOrDefault(f => f.Id == shipmentId);
        if (flow == null) return null;
        if (!_defenseSupplyPaths.TryGetValue(flow.Id, out var path))
        {
            // Hidden local legs are represented at their real overview anchor. There
            // is no invented connector between unrelated projected worlds.
            var from = DefenseWorld(flow.From);
            var to = DefenseWorld(flow.To);
            return from != null && to != null && from.Id == to.Id ? MapToLocal(to.Position) : null;
        }
        return MapToLocal(SupplyPathPoint(flow, path.Points, path.Route.From != flow.From));
    }

    private Vector2 SupplyPathPoint(SWDefenseSupplyFlowView flow, Vector2[] points, bool reverse, float offset = 0)
    {
        var now = Math.Min(_defenseSupplyHour + _defenseSupplyElapsed, flow.ArriveAtHours);
        var progress = (float) Math.Clamp((now - flow.DepartAtHours) / (flow.ArriveAtHours - flow.DepartAtHours), 0, 1);
        progress = Math.Clamp(progress + offset, 0, 1);
        if (reverse) progress = 1 - progress;
        var index = progress * (points.Length - 1);
        var start = Math.Min((int) index, points.Length - 2);
        return Vector2.Lerp(points[start], points[start + 1], index - start);
    }

    private void DrawDefenseSupply(DrawingHandleScreen handle)
    {
        if (!_defenseSupplyVisible || _defenseSupplyFlows.Count == 0) return;
        var routes = _defenseSupplyPaths.Values.Select(p => p.Route).ToHashSet();
        foreach (var path in _paths.Where(p => routes.Contains(p.Route)))
            for (var i = 1; i < path.Points.Length; i++)
                Stroke(handle, MapToLocal(path.Points[i - 1]) * UIScale, MapToLocal(path.Points[i]) * UIScale,
                    SupplyColor.WithAlpha(.65f), 1.2f);

        // Goods accepted together on one leg share one cargo marker, rather than
        // stacking many identical marks. Every marker still comes from actual cargo.
        foreach (var batch in _defenseSupplyFlows.GroupBy(f => (f.From, f.To, f.DepartAtHours, f.ArriveAtHours)))
        {
            var flow = batch.First();
            if (!_defenseSupplyPaths.TryGetValue(flow.Id, out var path) || DefenseSupplyPosition(flow.Id) is not { } position) continue;
            var reverse = path.Route.From != flow.From;
            var before = MapToLocal(SupplyPathPoint(flow, path.Points, reverse, -.01f));
            var after = MapToLocal(SupplyPathPoint(flow, path.Points, reverse, .01f));
            var delta = after - before;
            if (delta.LengthSquared() < .001f) continue;
            var direction = Vector2.Normalize(delta);
            var normal = new Vector2(-direction.Y, direction.X);
            var point = position * UIScale;
            var marker = new[] { point + direction * 6 * UIScale,
                point - direction * 4 * UIScale + normal * 3 * UIScale,
                point - direction * 4 * UIScale - normal * 3 * UIScale };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, marker, SupplyColor);
        }
        foreach (var port in _defenseSupplyFlows.Select(f => DefenseWorld(f.TargetWorldId)).Where(w => w != null).DistinctBy(w => w!.Id))
        {
            var point = (MapToLocal(port!.Position) + new Vector2(-11, 11)) * UIScale;
            handle.DrawRect(UIBox2.FromDimensions(point - new Vector2(3) * UIScale, new Vector2(6) * UIScale), SupplyColor, false);
        }
    }

    private string? DefenseSupplyToolTip(Vector2 point)
    {
        if (!_defenseSupplyVisible || _defenseSupplyFlows.Count == 0) return null;
        var shipment = _defenseSupplyFlows.FirstOrDefault(f => _defenseSupplyPaths.ContainsKey(f.Id)
            && DefenseSupplyPosition(f.Id) is { } p && Vector2.DistanceSquared(p, point) <= 64);
        if (shipment != null)
        {
            var fleet = shipment.RecipientFleetId is { } id ? $", флот {id}" : "";
            return $"{SupplyKindName(shipment.Kind)}{fleet}\n{shipment.GoodName}: {shipment.Quantity:0.##}\n" +
                $"{DefenseWorldName(shipment.From)} — {DefenseWorldName(shipment.To)}\n" +
                $"Завершение перехода через {SupplyArrival(shipment)}";
        }
        var port = _defenseSupplyFlows.Select(f => DefenseWorld(f.TargetWorldId)).Where(w => w != null)
            .FirstOrDefault(w => Vector2.DistanceSquared(MapToLocal(w!.Position), point) <= 20 * 20);
        if (port == null) return null;
        var incoming = _defenseSupplyFlows.Where(f => DefenseWorld(f.TargetWorldId)?.Id == port.Id).ToArray();
        var goods = incoming.GroupBy(f => (f.Good, f.GoodName, f.Kind)).Select(g =>
            $"{g.Key.GoodName}: {g.Sum(f => f.Quantity):0.##} ({SupplyKindName(g.Key.Kind)})");
        var soonest = incoming.Min(f => Math.Max(0, f.ArriveAtHours - _defenseSupplyHour - _defenseSupplyElapsed));
        var latest = incoming.Max(f => Math.Max(0, f.ArriveAtHours - _defenseSupplyHour - _defenseSupplyElapsed));
        var arrival = SWSenateDuration.FormatHours(soonest) + (latest - soonest > .01 ? $"–{SWSenateDuration.FormatHours(latest)}" : "");
        return $"{port.Name}: военные поставки\n{string.Join("\n", goods)}\nЗавершение текущих переходов: {arrival}";
    }

    private string SupplyArrival(SWDefenseSupplyFlowView flow) =>
        SWSenateDuration.FormatHours(Math.Max(0, flow.ArriveAtHours - _defenseSupplyHour - _defenseSupplyElapsed));

    private static string SupplyKindName(SWDefenseSupplyKind kind) => kind switch
    {
        SWDefenseSupplyKind.PlanetaryConstruction => "Планетарная оборона",
        SWDefenseSupplyKind.PlanetarySupply => "Резерв обороны",
        SWDefenseSupplyKind.Repair => "Ремонт", SWDefenseSupplyKind.Construction => "Постройка флота", _ => "Снабжение",
    };
}
