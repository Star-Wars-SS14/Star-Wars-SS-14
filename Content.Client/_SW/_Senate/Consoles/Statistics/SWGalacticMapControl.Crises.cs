using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    public sealed record CrisisMarker(string Key, Vector2 Anchor, UIBox2 Bounds, string[] CrisisIds);
    private IReadOnlyList<SWMapCrisisEntry> _crises = Array.Empty<SWMapCrisisEntry>();
    private IReadOnlyList<SWGalacticMapWorld> _crisisWorlds = Array.Empty<SWGalacticMapWorld>();
    private readonly List<CrisisMarker> _crisisMarkers = new();
    private readonly HashSet<string> _selectedCrises = new();
    private bool _crisisLayoutDirty = true;
    public event Action<string[]>? CrisesSelected;
    public IReadOnlyList<CrisisMarker> CrisisMarkers { get { LayoutCrisisMarkers(); return _crisisMarkers; } }

    public void SetCrises(IReadOnlyList<SWMapCrisisEntry> entries, IReadOnlyList<SWGalacticMapWorld> worlds)
    {
        _crises = entries;
        _crisisWorlds = worlds;
        _selectedCrises.RemoveWhere(id => !entries.Any(c => c.Id == id));
        _crisisLayoutDirty = true;
        _labelsDirty = true;
        UpdateHover();
    }

    public void SelectCrises(IEnumerable<string> ids)
    {
        _selectedCrises.Clear();
        _selectedCrises.UnionWith(ids);
    }

    private SWGalacticMapWorld? CrisisWorld(string id) => _worlds.GetValueOrDefault(id)
        ?? (_crisisWorlds.FirstOrDefault(w => w.Id == id)?.Sector is { } sector
            ? _map.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector.Id) : null);

    private IEnumerable<(string Key, Vector2 Anchor)> CrisisAnchors(SWMapCrisisEntry crisis)
    {
        var path = _paths.FirstOrDefault(p => SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To) == crisis.EdgeId);
        if (path.Route != null)
        {
            yield return ("route:" + crisis.EdgeId, MapToLocal(path.Points[path.Points.Length / 2]));
            yield break;
        }
        if (CrisisWorld(crisis.WorldId) is { } world)
        {
            yield return ("world:" + world.Id, MapToLocal(world.Position));
            yield break;
        }
        foreach (var sector in crisis.SectorIds)
        {
            var anchor = _map.Worlds.FirstOrDefault(w => w.Anchor && w.Sector?.Id == sector);
            if (anchor != null) yield return ("world:" + anchor.Id, MapToLocal(anchor.Position));
        }
        // A Republic-wide crisis may predate sector assignments. Keep a public capital marker.
        if (crisis.SectorIds.Count == 0 && crisis.WorldId.Length == 0 && crisis.EdgeId.Length == 0
            && _worlds.TryGetValue("coruscant", out var capital))
            yield return ("world:" + capital.Id, MapToLocal(capital.Position));
    }

    private void LayoutCrisisMarkers()
    {
        if (!_crisisLayoutDirty) return;
        _crisisLayoutDirty = false;
        _crisisMarkers.Clear();
        if (Width < 64 || Height < 56) return;
        var viewport = UIBox2.FromDimensions(Vector2.Zero, Size);
        var groups = _crises.SelectMany(c => CrisisAnchors(c).Select(a => (a.Key, a.Anchor, Crisis: c)))
            .Where(a => viewport.Contains(a.Anchor)).GroupBy(a => a.Key).OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            var anchor = group.First().Anchor;
            var ids = group.Select(a => a.Crisis.Id).Distinct().ToArray();
            var size = new Vector2(ids.Length > 1 ? 36 : 24, 24);
            UIBox2? best = null;
            var bestCost = float.PositiveInfinity;
            foreach (var dy in new[] { -28f, 28f, -52f, 52f, -76f, 76f, -100f, 100f })
            foreach (var dx in new[] { 26f, -26f, 52f, -52f, 0f, 80f, -80f })
            {
                var center = anchor + new Vector2(dx, dy);
                var box = UIBox2.FromDimensions(center - size / 2, size);
                if (box.Left < 4 || box.Top < 4 || box.Right > Width - 4 || box.Bottom > Height - 4) continue;
                var padded = new UIBox2(box.TopLeft - new Vector2(5), box.BottomRight + new Vector2(5));
                if (_map.Worlds.Any(w => padded.Contains(MapToLocal(w.Position)))) continue;
                if (_crisisMarkers.Any(m => m.Bounds.Intersects(padded))) continue;
                if (_defense?.Fleets.Any(f => DefenseFleetHitBox(f.Id) is { } b && b.Intersects(padded)) == true) continue;
                var cost = Vector2.DistanceSquared(center, anchor) + (dx < 0 ? 1 : 0) + (dy > 0 ? 1 : 0);
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = box;
            }
            // A crowded local cluster must not silently lose its warning. Search further out,
            // retaining a leader to the actual location and keeping nodes clickable.
            if (best == null)
            for (var y = 4f; y + size.Y < Height - 4; y += 30)
            for (var x = 4f; x + size.X < Width - 4; x += 40)
            {
                var box = UIBox2.FromDimensions(new Vector2(x, y), size);
                var padded = new UIBox2(box.TopLeft - new Vector2(5), box.BottomRight + new Vector2(5));
                if (_map.Worlds.Any(w => padded.Contains(MapToLocal(w.Position)))
                    || _crisisMarkers.Any(m => m.Bounds.Intersects(padded))) continue;
                if (_defense?.Fleets.Any(f => DefenseFleetHitBox(f.Id) is { } b && b.Intersects(padded)) == true) continue;
                var cost = Vector2.DistanceSquared(box.TopLeft + size / 2, anchor);
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = box;
            }
            if (best is { } bounds) _crisisMarkers.Add(new CrisisMarker(group.Key, anchor, bounds, ids));
        }
    }

    public CrisisMarker? HitTestCrisis(Vector2 point) => CrisisMarkers.FirstOrDefault(m => m.Bounds.Contains(point));

    private bool ActivateCrisisAt(Vector2 point)
    {
        if (HitTestCrisis(point) is not { } marker) return false;
        SelectCrises(marker.CrisisIds);
        CrisesSelected?.Invoke(marker.CrisisIds);
        return true;
    }

    private string? CrisisToolTip(Vector2 point) => HitTestCrisis(point) is { } marker
        ? string.Join("\n", _crises.Where(c => marker.CrisisIds.Contains(c.Id)).Select(c => $"{c.Title}: {c.Status}")) : null;

    private bool OverlayIntersectsLabel(UIBox2 box) => CrisisMarkers.Any(m => m.Bounds.Intersects(box))
        || _defense?.Fleets.Any(f => DefenseFleetHitBox(f.Id) is { } b && b.Intersects(box)) == true;

    private void DrawCrisisRoutes(DrawingHandleScreen handle)
    {
        var restrictions = _crises.SelectMany(c => c.Routes.Select(r => (Crisis: c, Route: r)))
            .Where(x => x.Route.CapacityMultiplier < .999).GroupBy(x => x.Route.EdgeId);
        foreach (var group in restrictions)
        {
            var path = _paths.FirstOrDefault(p => SWTransitLinkPrototype.LinkId(p.Route.From, p.Route.To) == group.Key);
            if (path.Route == null) continue;
            var selected = group.Any(x => _selectedCrises.Contains(x.Crisis.Id));
            var color = group.Min(x => x.Route.CapacityMultiplier) <= 0 ? SWConsoleStyle.Bad : SWConsoleStyle.Warn;
            for (var i = 1; i < path.Points.Length; i++)
                Stroke(handle, MapToLocal(path.Points[i - 1]) * UIScale, MapToLocal(path.Points[i]) * UIScale,
                    color.WithAlpha(selected ? .95f : .55f), selected ? 4 : 2.3f);
        }
    }

    private void DrawCrisisMarkers(DrawingHandleScreen handle)
    {
        foreach (var marker in CrisisMarkers)
        {
            var selected = marker.CrisisIds.Any(_selectedCrises.Contains);
            var severe = _crises.Any(c => marker.CrisisIds.Contains(c.Id) && (c.Severity >= 65 || c.Routes.Any(r => r.CapacityMultiplier <= 0)));
            var color = severe ? SWConsoleStyle.Bad : SWConsoleStyle.Warn;
            var box = marker.Bounds;
            var center = box.TopLeft + box.Size / 2;
            var end = Vector2.Clamp(marker.Anchor, box.TopLeft, box.BottomRight);
            handle.DrawLine(marker.Anchor * UIScale, end * UIScale, color.WithAlpha(.65f));
            handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), SWConsoleStyle.Navigation);
            handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), selected ? SWConsoleStyle.Accent : color, false);
            var x = box.Left + 12;
            // Native vector punctuation stays crisp and does not depend on a font atlas.
            Stroke(handle, new Vector2(x, center.Y - 7) * UIScale, new Vector2(x, center.Y + 2) * UIScale, color, 2.5f);
            handle.DrawCircle(new Vector2(x, center.Y + 6) * UIScale, 1.6f * UIScale, Color.ToSrgb(color));
            if (marker.CrisisIds.Length > 1)
                handle.DrawString(SWConsoleStyle.Small, (box.TopLeft + new Vector2(23, 3)) * UIScale,
                    marker.CrisisIds.Length.ToString(), UIScale, SWConsoleStyle.TextBright);
        }
    }
}
