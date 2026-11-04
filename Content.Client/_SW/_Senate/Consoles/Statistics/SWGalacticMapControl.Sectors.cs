using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    // This layer is a bounded schematic, not political control or a game territory.
    private static readonly Color[] SchemePalette =
    {
        Color.FromHex("#235563"), Color.FromHex("#2A6672"), Color.FromHex("#275968"),
        Color.FromHex("#397782"), Color.FromHex("#315D67"), Color.FromHex("#24505B"),
        Color.FromHex("#386B74"), Color.FromHex("#2C5869"), Color.FromHex("#3A6A72"),
    };
    private sealed class SectorSchemeCell(string id, Vector2 site, Vector2[] points)
    {
        public readonly string Id = id;
        public readonly Vector2 Site = site;
        public readonly Vector2[] Points = points;
        public readonly Vector2[] Screen = new Vector2[points.Length];
        public Color Color = SchemePalette[0];
    }

    private readonly List<SectorSchemeCell> _sectorSchemeCells = new();
    private readonly List<(Vector2 A, Vector2 B)> _sectorSchemeEdges = new();
    private readonly List<(string Id, UIBox2 Box)> _sectorSchemeLabels = new();
    private readonly Dictionary<string, string> _sectorSchemeNames = new();
    private bool _sectorSchemeEnabled;
    private bool _sectorSchemeBuilt;
    private string? _selectedSchemeSector;
    private bool SectorSchemeVisible => _markExternalLabels && _sectorSchemeEnabled && !IsLocalView;
    public event Action<string>? SectorSelected;

    public void SetSectorSchemeNames(IReadOnlyDictionary<string, string> names)
    {
        _sectorSchemeNames.Clear();
        foreach (var (id, name) in names) _sectorSchemeNames[id] = name;
        _sectorSchemeNames[string.Empty] = "Спорная область";
        _labelsDirty = true;
    }

    public void SetSectorScheme(bool enabled)
    {
        _sectorSchemeEnabled = enabled;
        if (SectorSchemeVisible && !_sectorSchemeBuilt) BuildSectorScheme();
        _labelsDirty = true;
    }

    private static List<Vector2> ClipScheme(List<Vector2> polygon, Vector2 normal, float limit)
    {
        var output = new List<Vector2>();
        if (polygon.Count == 0) return output;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var da = Vector2.Dot(normal, a) - limit;
            var db = Vector2.Dot(normal, b) - limit;
            if (da <= 0) output.Add(a);
            if (da < 0 && db > 0 || da > 0 && db < 0)
                output.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        return output;
    }

    private static List<Vector2> ClipSchemeTriangle(List<Vector2> polygon, Vector2 a, Vector2 b, Vector2 c)
    {
        var signed = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(signed) < .00001f) return new List<Vector2>();
        var points = new[] { a, b, c };
        for (var i = 0; i < 3 && polygon.Count > 2; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % 3];
            var d = q - p;
            var normal = signed > 0 ? new Vector2(d.Y, -d.X) : new Vector2(-d.Y, d.X);
            polygon = ClipScheme(polygon, normal, Vector2.Dot(normal, p));
        }
        return polygon;
    }

    private static List<List<Vector2>> SubtractSchemeTriangle(List<Vector2> polygon, Vector2 a, Vector2 b, Vector2 c)
    {
        var outside = new List<List<Vector2>>();
        var signed = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (MathF.Abs(signed) < .00001f) { outside.Add(polygon); return outside; }
        var points = new[] { a, b, c };
        var inside = polygon;
        for (var i = 0; i < 3 && inside.Count > 2; i++)
        {
            var p = points[i]; var d = points[(i + 1) % 3] - p;
            var normal = signed > 0 ? new Vector2(d.Y, -d.X) : new Vector2(-d.Y, d.X);
            var limit = Vector2.Dot(normal, p);
            var part = ClipScheme(inside, -normal, -limit);
            if (part.Count >= 3 && SchemeArea(part.ToArray()) >= .0001f) outside.Add(part);
            inside = ClipScheme(inside, normal, limit);
        }
        return outside;
    }

    private void BuildSectorScheme()
    {
        _sectorSchemeBuilt = true;
        _sectorSchemeCells.Clear();
        _sectorSchemeEdges.Clear();
        // Unknown membership is not a site: the fill between known sites is only an
        // interpolation, never a change to any world's Sector. Conflicts get neutral cells.
        var sites = _map.Worlds.Where(w => float.IsFinite(w.Position.X) && float.IsFinite(w.Position.Y)
                && w.Position.X >= 0 && w.Position.Y >= 0 && w.Position.X <= _map.Size.X && w.Position.Y <= _map.Size.Y)
            .GroupBy(w => w.Position)
            .Select(group => (Position: group.Key, Owners: group.Select(w => w.Sector?.Id).Where(owner => owner != null).Distinct().Take(2).ToArray()))
            .Where(site => site.Owners.Length > 0).ToArray();
        var owners = sites.Where(site => site.Owners.Length == 1 && site.Owners[0] != null)
            .Select(site => site.Owners[0]!).Distinct().OrderBy(id => id, Comparer<string>.Create(string.CompareOrdinal)).ToArray();
        if (sites.Length == 0) return;
        var min = new Vector2(sites.Min(site => site.Position.X), sites.Min(site => site.Position.Y));
        var max = new Vector2(sites.Max(site => site.Position.X), sites.Max(site => site.Position.Y));
        var center = (min + max) / 2;
        var extent = Vector2.Max((max - min) * .65f, new Vector2(8));
        var cells = new List<SectorSchemeCell>();
        foreach (var site in sites)
        {
            var owner = site.Owners.Length == 1 ? site.Owners[0]! : string.Empty;
            var p = site.Position;
            var polygon = new List<Vector2> { Vector2.Zero, new(_map.Size.X, 0), _map.Size, new(0, _map.Size.Y) };
            for (var i = 0; i < 24 && polygon.Count > 2; i++)
            {
                var angle = MathF.Tau * i / 24;
                var normal = new Vector2(MathF.Cos(angle) / extent.X, MathF.Sin(angle) / extent.Y);
                polygon = ClipScheme(polygon, normal, Vector2.Dot(normal, center) + 1);
            }
            foreach (var other in sites)
            {
                if (other.Position == p || polygon.Count < 3) continue;
                var normal = 2 * (other.Position - p);
                polygon = ClipScheme(polygon, normal, other.Position.LengthSquared() - p.LengthSquared());
            }
            if (polygon.Count >= 3) cells.Add(new SectorSchemeCell(owner, p, polygon.ToArray()));
        }

        // Construct a non-overlapping union of the published political polygons,
        // then intersect it with each Voronoi cell and the bounded schematic coverage.
        var footprint = _map.PoliticalAreas.Where(area => !area.Contested && area.Boundary.Count >= 3)
            .OrderByDescending(area => SchemeArea(area.Boundary.ToArray())).ToArray();
        var covered = new List<(Vector2 A, Vector2 B, Vector2 C)>();
        foreach (var area in footprint)
        {
            if (!_politicalTriangles.TryGetValue(area, out var triangles)) continue;
            foreach (var cell in cells)
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var clipped = ClipSchemeTriangle(cell.Points.ToList(), triangles[i], triangles[i + 1], triangles[i + 2]);
                if (clipped.Count < 3 || SchemeArea(clipped.ToArray()) < .0001f) continue;
                var parts = new List<List<Vector2>> { clipped };
                foreach (var prior in covered)
                {
                    var next = new List<List<Vector2>>();
                    foreach (var part in parts) next.AddRange(SubtractSchemeTriangle(part, prior.A, prior.B, prior.C));
                    parts = next;
                    if (parts.Count == 0) break;
                }
                foreach (var part in parts)
                {
                    _sectorSchemeCells.Add(new SectorSchemeCell(cell.Id, cell.Site, part.ToArray()));
                    for (var j = 0; j < part.Count; j++)
                    {
                        var a = part[j]; var b = part[(j + 1) % part.Count]; var middle = (a + b) / 2;
                        // Only true cell edges, not triangulation seams, receive a border.
                        if (!Enumerable.Range(0, cell.Points.Length).Any(k => SegmentDistance(middle, cell.Points[k], cell.Points[(k + 1) % cell.Points.Length]) < .001f)) continue;
                        var sameOwner = cells.Any(other => other != cell && other.Id == cell.Id &&
                            MathF.Abs(Vector2.DistanceSquared(middle, cell.Site) - Vector2.DistanceSquared(middle, other.Site)) < .02f);
                        if (sameOwner) continue;
                        if (!_sectorSchemeEdges.Any(edge => Vector2.DistanceSquared(edge.A, b) < .0001f && Vector2.DistanceSquared(edge.B, a) < .0001f ||
                            Vector2.DistanceSquared(edge.A, a) < .0001f && Vector2.DistanceSquared(edge.B, b) < .0001f))
                            _sectorSchemeEdges.Add((a, b));
                    }
                }
            }
            for (var i = 0; i + 2 < triangles.Length; i += 3)
                covered.Add((triangles[i], triangles[i + 1], triangles[i + 2]));
        }

        var neighbours = owners.ToDictionary(id => id, _ => new HashSet<string>());
        for (var i = 0; i < cells.Count; i++)
        for (var j = i + 1; j < cells.Count; j++)
        {
            var a = cells[i]; var b = cells[j];
            if (a.Id == b.Id || a.Id.Length == 0 || b.Id.Length == 0) continue;
            var normal = 2 * (b.Site - a.Site);
            var limit = b.Site.LengthSquared() - a.Site.LengthSquared();
            if (a.Points.Count(point => MathF.Abs(Vector2.Dot(normal, point) - limit) < .01f) < 2 ||
                b.Points.Count(point => MathF.Abs(Vector2.Dot(normal, point) - limit) < .01f) < 2) continue;
            neighbours[a.Id].Add(b.Id);
            neighbours[b.Id].Add(a.Id);
        }
        var assigned = new Dictionary<string, int>();
        foreach (var owner in owners)
        {
            var used = neighbours[owner].Where(assigned.ContainsKey).Select(id => assigned[id]).ToHashSet();
            var colorIndex = 0;
            while (colorIndex < SchemePalette.Length && used.Contains(colorIndex)) colorIndex++;
            assigned[owner] = colorIndex < SchemePalette.Length ? colorIndex : assigned.Count % SchemePalette.Length;
        }
        foreach (var cell in _sectorSchemeCells)
            cell.Color = cell.Id.Length == 0 ? Color.FromHex("#626565") : SchemePalette[assigned[cell.Id]];
    }

    private void DrawSectorScheme(DrawingHandleScreen handle)
    {
        foreach (var cell in _sectorSchemeCells)
        {
            for (var i = 0; i < cell.Points.Length; i++) cell.Screen[i] = MapToLocal(cell.Points[i]) * UIScale;
            var selected = cell.Id == _selectedSchemeSector || _selectedWorld != null && _worlds[_selectedWorld].Sector?.Id == cell.Id;
            var fill = selected ? Color.InterpolateBetween(cell.Color, SWConsoleStyle.Brass, .32f) : cell.Color;
            // Cells are convex; no borders are drawn between neighbouring cells of the same sector.
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, cell.Screen, Color.ToSrgb(fill.WithAlpha(.38f)));
        }
        foreach (var (a, b) in _sectorSchemeEdges)
            handle.DrawLine(MapToLocal(a) * UIScale, MapToLocal(b) * UIScale, SWConsoleStyle.TextDim.WithAlpha(.55f));
    }

    private void LayoutSectorSchemeLabels(DrawingHandleScreen handle)
    {
        _sectorSchemeLabels.Clear();
        if (!SectorSchemeVisible) return;
        var groups = _sectorSchemeCells.GroupBy(cell => cell.Id)
            .OrderByDescending(group => group.Key == _selectedSchemeSector || _selectedWorld != null && _worlds[_selectedWorld].Sector?.Id == group.Key);
        foreach (var group in groups)
        {
            if (!_sectorSchemeNames.TryGetValue(group.Key, out var name) || string.IsNullOrWhiteSpace(name)) continue;
            var size = handle.GetDimensions(SWConsoleStyle.Small, name, UIScale) / UIScale;
            foreach (var cell in group.OrderByDescending(c => SchemeArea(c.Points)))
            {
                var center = cell.Points.Aggregate(Vector2.Zero, (sum, point) => sum + point) / cell.Points.Length;
                var p = MapToLocal(center);
                var box = UIBox2.FromDimensions(p - size / 2, size);
                if (box.Left < 4 || box.Top < 4 || box.Right > Width - 4 || box.Bottom > Height - 4) continue;
                var padded = new UIBox2(box.Left - 3, box.Top - 1, box.Right + 3, box.Bottom + 1);
                // A name may cross triangulation seams inside the same sector.
                if (!group.Any(part => SchemeContains(part, padded.TopLeft)) || !group.Any(part => SchemeContains(part, padded.BottomRight)) ||
                    !group.Any(part => SchemeContains(part, new Vector2(padded.Left, padded.Bottom))) ||
                    !group.Any(part => SchemeContains(part, new Vector2(padded.Right, padded.Top)))) continue;
                if (_sectorSchemeLabels.Any(label => label.Box.Intersects(padded)) || OverlayIntersectsLabel(padded) || CoincidentTrafficIntersects(padded)) continue;
                if (_map.Worlds.Where(DisplayWorldGlyph).Any(w =>
                {
                    var radius = w.Id == _selectedWorld ? 26f : 20f;
                    return UIBox2.FromDimensions(MapToLocal(w.Position) - new Vector2(radius), new Vector2(radius * 2)).Intersects(padded);
                })) continue;
                _sectorSchemeLabels.Add((group.Key, padded));
                break;
            }
        }
    }

    private static float SchemeArea(Vector2[] points)
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(area);
    }

    private void DrawSectorSchemeLabels(DrawingHandleScreen handle)
    {
        foreach (var (id, box) in _sectorSchemeLabels)
        {
            handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), SWConsoleStyle.Navigation);
            handle.DrawString(SWConsoleStyle.Small, (box.TopLeft + new Vector2(3, 1)) * UIScale, _sectorSchemeNames[id], UIScale, SWConsoleStyle.TextBright);
        }
    }

    public void ClearSectorSchemeSelection()
    {
        _selectedSchemeSector = null;
        _labelsDirty = true;
    }

    private string? HitTestSectorScheme(Vector2 local)
    {
        if (!SectorSchemeVisible) return null;
        var label = _sectorSchemeLabels.FirstOrDefault(label => label.Box.Contains(local));
        if (!string.IsNullOrEmpty(label.Id)) return label.Id;
        foreach (var cell in _sectorSchemeCells)
            if (cell.Id.Length != 0 && SchemeContains(cell, local)) return cell.Id;
        return null;
    }

    private bool SchemeContains(SectorSchemeCell cell, Vector2 local)
    {
        var inside = false;
        for (var i = 0; i < cell.Points.Length; i++)
        {
            var a = MapToLocal(cell.Points[i]); var b = MapToLocal(cell.Points[(i + 1) % cell.Points.Length]);
            if ((a.Y > local.Y) != (b.Y > local.Y) && local.X < (b.X - a.X) * (local.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
