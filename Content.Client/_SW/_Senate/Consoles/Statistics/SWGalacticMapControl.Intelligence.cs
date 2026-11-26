using System;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Empire;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    private SWEmpireIntelligenceState? _intelligence;
    private SWGalacticMapPrototype? _intelligenceGeography;
    private int? _intelligenceSelected;
    private double _intelligenceClock, _intelligenceSnapshotClock;
    private float _intelligenceExtrapolationSeconds;
    private const float IntelligenceMaxExtrapolation = 2.5f;
    public event Action<int>? IntelligenceGroupSelected;

    public void SetIntelligence(SWEmpireIntelligenceState? state, SWGalacticMapPrototype geography, int? selected)
    {
        var previous = _intelligence;
        if (state != null)
        {
            var reset = previous == null || state.NowHours + .01 < _intelligenceSnapshotClock;
            // A snapshot is authoritative. During normal playback tolerate only a tiny
            // interpolation lead; pause/reset always snaps to the server's frozen time.
            _intelligenceClock = !reset && state.ClockRunning && previous!.ClockRunning
                ? Math.Min(Math.Max(_intelligenceClock, state.NowHours), state.NowHours + .0005)
                : state.NowHours;
            _intelligenceSnapshotClock = state.NowHours;
            _intelligenceExtrapolationSeconds = 0;
        }
        _intelligence = state; _intelligenceGeography = geography; _intelligenceSelected = selected;
        UpdateHover();
    }
    private void AdvanceIntelligence(float seconds)
    {
        if (_intelligence is not { ClockRunning: true, RealSecondsPerHour: > 0 } state || !float.IsFinite(seconds) || seconds <= 0) return;
        var advance = Math.Min(Math.Min(seconds, .25f), Math.Max(0, IntelligenceMaxExtrapolation - _intelligenceExtrapolationSeconds));
        _intelligenceExtrapolationSeconds += advance;
        _intelligenceClock += advance / state.RealSecondsPerHour;
    }
    private Vector2? IntelligenceWorldPosition(string id)
    {
        if (_worlds.TryGetValue(id, out var exact)) return MapToLocal(exact.Position);
        var source = _intelligenceGeography?.Worlds.FirstOrDefault(w => w.Id == id);
        if (source == null) return null;
        // A hidden minor world in the overview is represented by its visible system/sector anchor.
        // Local maps only project worlds belonging to that view; remote routes use existing border nodes.
        var sameView = _focusSystem != null ? source.SystemId == _focusSystem : _focusPoliticalSector != null ? source.PoliticalSectorId == _focusPoliticalSector : _focusSector != null ? source.Sector?.Id == _focusSector : true;
        if (!sameView) return null;
        var candidates = _map.Worlds.Where(w => !IsLocalView || !IsExternalWorld(w));
        var anchor = candidates.Where(w => !string.IsNullOrEmpty(source.SystemId) && w.SystemId == source.SystemId)
            .OrderByDescending(w => w.Anchor).FirstOrDefault()
            ?? candidates.Where(w => !string.IsNullOrEmpty(source.PoliticalSectorId) && w.PoliticalSectorId == source.PoliticalSectorId).OrderByDescending(w => w.Anchor).FirstOrDefault()
            ?? candidates.Where(w => source.Sector != null && w.Sector?.Id == source.Sector?.Id).OrderByDescending(w => w.Anchor).FirstOrDefault()
            ?? candidates.OrderBy(w => Vector2.DistanceSquared(_intelligenceGeography!.Worlds.First(x => x.Id == w.Id).Position, source.Position)).FirstOrDefault();
        return anchor == null ? null : MapToLocal(anchor.Position);
    }
    private Vector2? IntelligencePosition(SWEmpireIntelligenceGroupView group)
    {
        var origin = IntelligenceWorldPosition(group.WorldId);
        var moving = !string.IsNullOrEmpty(group.NextWorldId) && group.LegEndsAt > group.LegStartedAt;
        var destination = moving ? IntelligenceWorldPosition(group.NextWorldId) : null;
        if (moving && (origin == null) != (destination == null) && _intelligenceGeography != null)
        {
            var from = _intelligenceGeography.Worlds.FirstOrDefault(w => w.Id == group.WorldId);
            var to = _intelligenceGeography.Worlds.FirstOrDefault(w => w.Id == group.NextWorldId);
            if (from != null && to != null)
            {
                if (origin is { } visibleOrigin) destination = IntelligenceBoundary(visibleOrigin, to.Position - from.Position);
                else if (destination is { } visibleDestination) origin = IntelligenceBoundary(visibleDestination, from.Position - to.Position);
            }
        }
        if (moving && origin != null && destination != null)
        {
            var fraction = (float)Math.Clamp((_intelligenceClock - group.LegStartedAt) / (group.LegEndsAt - group.LegStartedAt), 0, 1);
            var path = _paths.FirstOrDefault(p => p.Route.From == group.WorldId && p.Route.To == group.NextWorldId || p.Route.To == group.WorldId && p.Route.From == group.NextWorldId);
            if (path.Route != null)
            {
                if (path.Route.From != group.WorldId) fraction = 1 - fraction;
                var at = fraction * (path.Points.Length - 1); var i = Math.Min((int)at, path.Points.Length - 2);
                return IntelligenceMarkerPosition(group, MapToLocal(Vector2.Lerp(path.Points[i], path.Points[i + 1], at - i)));
            }
            return IntelligenceMarkerPosition(group, Vector2.Lerp(origin.Value, destination.Value, fraction));
        }
        var anchor = moving ? origin ?? destination : origin;
        return anchor is { } p ? IntelligenceMarkerPosition(group, p) : null;
    }
    private Vector2 IntelligenceMarkerPosition(SWEmpireIntelligenceGroupView group, Vector2 anchor)
    {
        // Group IDs persist throughout a match, including travel and arrival. Fixed lanes
        // therefore avoid both overlap on shared legs and jumps when the phase changes.
        var lane = (group.Id - 1) % 3;
        var offset = new Vector2((lane - 1) * 20, -17);
        var min = new Vector2(32, 29); var max = Vector2.Max(min, Size - new Vector2(32, 12));
        return Vector2.Clamp(anchor, min, max) + offset;
    }
    private Vector2 IntelligenceBoundary(Vector2 visible, Vector2 direction)
    {
        if (direction.LengthSquared() < .0001f) return visible;
        direction = Vector2.Normalize(direction);
        var min = new Vector2(12); var max = Vector2.Max(min, Size - new Vector2(12));
        visible = Vector2.Clamp(visible,min,max);
        var tx = direction.X > .0001f ? (max.X-visible.X)/direction.X : direction.X < -.0001f ? (min.X-visible.X)/direction.X : float.PositiveInfinity;
        var ty = direction.Y > .0001f ? (max.Y-visible.Y)/direction.Y : direction.Y < -.0001f ? (min.Y-visible.Y)/direction.Y : float.PositiveInfinity;
        return visible + direction * Math.Max(0,Math.Min(tx,ty));
    }
    private SWEmpireIntelligenceGroupView? HitIntelligence(Vector2 point) => _intelligence?.Groups
        .Where(g => IntelligencePosition(g) is { } p && Vector2.DistanceSquared(p, point) <= 100)
        .OrderBy(g => Vector2.DistanceSquared(IntelligencePosition(g)!.Value, point)).FirstOrDefault();
    private bool ActivateIntelligenceAt(Vector2 point)
    {
        var group = HitIntelligence(point); if (group == null) return false;
        IntelligenceGroupSelected?.Invoke(group.Id); return true;
    }
    private string? IntelligenceToolTip(Vector2 point)
    {
        var group = HitIntelligence(point);
        return group == null ? null : $"{group.Name}\n{group.Status}\nОсталось {group.EtaHours:0.0} ч";
    }
    private void DrawIntelligence(DrawingHandleScreen handle)
    {
        if (_intelligence == null) return;
        foreach (var group in _intelligence.Groups)
        {
            if (IntelligencePosition(group) is not { } point) continue;
            var p = point * UIScale; var color = group.Id == _intelligenceSelected ? SWConsoleStyle.Brass : SWConsoleStyle.Accent;
            // Diamond and central dot distinguish agents from round fleet and world markers.
            var up = p + new Vector2(0,-8) * UIScale; var right = p + new Vector2(8,0) * UIScale;
            var down = p + new Vector2(0,8) * UIScale; var left = p + new Vector2(-8,0) * UIScale;
            handle.DrawLine(up,right,color); handle.DrawLine(right,down,color); handle.DrawLine(down,left,color); handle.DrawLine(left,up,color);
            handle.DrawCircle(p, 2.5f * UIScale, color);
            Badge(handle,point + new Vector2(8,-8),group.Id.ToString(),color);
        }
    }
}
