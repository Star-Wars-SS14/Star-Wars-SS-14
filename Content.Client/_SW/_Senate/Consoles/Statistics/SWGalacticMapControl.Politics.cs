using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Sectors;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

public sealed partial class SWGalacticMapControl
{
    private readonly Dictionary<SWGalacticMapPoliticalArea, Vector2[]> _politicalTriangles = new();
    private readonly List<(SWGalacticMapPoliticalArea Area, UIBox2 Box)> _politicalLabels = new();

    private static Color PoliticalColor(string faction) => Color.FromHex(faction switch
    {
        "republic" => "#73AEC4",
        "empire" => "#D47770",
        _ => "#C3AD80",
    });

    private void BuildPoliticalGeometry()
    {
        foreach (var area in _map.PoliticalAreas)
            _politicalTriangles[area] = TriangulateArea(area.Boundary);
    }

    // Concave territory outlines cannot be filled by a triangle fan. Triangulate once;
    // camera changes only transform cached vertices, never recalculate the geography.
    private static Vector2[] TriangulateArea(IReadOnlyList<Vector2> boundary)
    {
        if (boundary.Count < 3) return Array.Empty<Vector2>();
        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        var polygon = boundary.ToList();
        var signedArea = 0f;
        for (var i = 0; i < polygon.Count; i++)
            signedArea += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        if (signedArea < 0) polygon.Reverse();
        var result = new List<Vector2>();
        while (polygon.Count > 2)
        {
            var found = false;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[(i + polygon.Count - 1) % polygon.Count];
                var b = polygon[i];
                var c = polygon[(i + 1) % polygon.Count];
                if (Cross(b - a, c - b) <= .00001f) continue;
                var occupied = polygon.Any(p => p != a && p != b && p != c
                    && Cross(b - a, p - a) >= -.00001f
                    && Cross(c - b, p - b) >= -.00001f
                    && Cross(a - c, p - c) >= -.00001f);
                if (occupied) continue;
                result.Add(a);
                result.Add(b);
                result.Add(c);
                polygon.RemoveAt(i);
                found = true;
                break;
            }
            // Invalid content must not hang the UI or leave a misleading partial fill.
            if (!found) return Array.Empty<Vector2>();
        }
        return result.ToArray();
    }

    private void DrawPoliticalAreas(DrawingHandleScreen handle, bool outlines)
    {
        foreach (var area in _map.PoliticalAreas)
        {
            var color = PoliticalColor(area.Faction);
            if (!outlines)
            {
                var points = _politicalTriangles[area].Select(p => MapToLocal(p) * UIScale).ToArray();
                if (points.Length == 0) continue;
                // Opaque, muted fills let independent enclaves replace the underlying zone.
                var fill = Color.InterpolateBetween(SWConsoleStyle.Navigation, color, .11f);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, points, fill);
                continue;
            }
            for (var i = 0; i < area.Boundary.Count; i++)
            {
                var a = MapToLocal(area.Boundary[i]) * UIScale;
                var b = MapToLocal(area.Boundary[(i + 1) % area.Boundary.Count]) * UIScale;
                if (!area.Contested)
                {
                    Stroke(handle, a, b, color.WithAlpha(.78f), 1.7f);
                    continue;
                }
                var distance = Vector2.Distance(a, b);
                if (distance < .001f) continue;
                var direction = (b - a) / distance;
                for (var step = 0f; step < distance; step += 10 * UIScale)
                    Stroke(handle, a + direction * step, a + direction * Math.Min(distance, step + 5 * UIScale), color, 1.7f);
            }
        }
    }

    private void DrawPoliticalLabels(DrawingHandleScreen handle)
    {
        _politicalLabels.Clear();
        foreach (var area in _map.PoliticalAreas)
        {
            if (area.Label == null) continue;
            var size = handle.GetDimensions(SWConsoleStyle.Small, area.Label, UIScale) / UIScale;
            var p = MapToLocal(area.LabelPosition) - size / 2;
            if (p.X < 4 || p.Y < 4 || p.X + size.X > Width - 4 || p.Y + size.Y > Height - 4) continue;
            _politicalLabels.Add((area, UIBox2.FromDimensions(p - new Vector2(5), size + new Vector2(10))));
            handle.DrawString(SWConsoleStyle.Small, p * UIScale, area.Label, UIScale, PoliticalColor(area.Faction));
        }
    }
}
