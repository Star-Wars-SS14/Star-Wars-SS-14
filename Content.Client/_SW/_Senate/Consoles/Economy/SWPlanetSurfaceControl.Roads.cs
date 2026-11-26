using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWPlanetSurfaceControl
{
    // Immutable prototype geometry is indexed once. Drawing only visits cached tiles and their four arms.
    private Dictionary<Vector2i, (string Kind, int Connections)>? _roadCells;
    private string? _settlementCenter;
    private static readonly Vector2i[] RoadDirections =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
    };

    private void DrawRoads(DrawingHandleScreen handle, float tile)
    {
        if (_roadCells == null)
        {
            var kinds = Surface.Roads.ToDictionary(r => r.Cell, r => r.Kind);
            _roadCells = new();
            foreach (var (cell, kind) in kinds)
            {
                var mask = 0;
                for (var i = 0; i < RoadDirections.Length; i++)
                    if (kinds.ContainsKey(cell + RoadDirections[i])) mask |= 1 << i;
                _roadCells[cell] = (kind, mask);
            }
            _settlementCenter = Surface.Sites.FirstOrDefault(s => s.Kind == "settlement")?.Id;
        }

        foreach (var (cell, road) in _roadCells)
        {
            var center = CellToLocal(cell);
            if (center.X + tile / 2 < 0 || center.Y + tile / 2 < 0 || center.X - tile / 2 > Width || center.Y - tile / 2 > Height) continue;
            var orbital = road.Kind is "orbital" or "transfer";
            var shoulder = orbital ? Ink("#263F50") : road.Kind == "tunnel" ? Ink("#252C2D") : Ink("#817D6B");
            var pavement = orbital ? Ink("#65808B") : road.Kind == "bridge" ? Ink("#646F70") : Ink("#535858");
            var width = orbital ? .12f : .23f;
            Strip(width + .09f, shoulder);
            Strip(width, pavement);

            // Short static centre marks establish orientation without implying simulated traffic.
            for (var i = 0; i < RoadDirections.Length; i++)
            {
                if ((road.Connections & (1 << i)) == 0) continue;
                var direction = new Vector2(RoadDirections[i].X, RoadDirections[i].Y);
                var side = new Vector2(-direction.Y, direction.X);
                var a = center + direction * tile * .27f;
                var b = center + direction * tile * .43f;
                Line(a, b, orbital ? Ink("#A0B6BC") : Ink("#ABA891"));
                if (road.Kind == "bridge")
                {
                    // Raised rails and transverse supports remain visible over the water tile.
                    foreach (var sign in new[] { -1, 1 })
                    {
                        var offset = side * tile * .19f * sign;
                        Line(center + direction * tile * .12f + offset, center + direction * tile * .5f + offset, Ink("#ADB4AC"));
                        Line(center + direction * tile * .38f + offset, center + direction * tile * .38f + offset * 1.25f, Ink("#89968F"));
                    }
                }
                if (road.Kind == "tunnel" && (!_roadCells.TryGetValue(cell + RoadDirections[i], out var next) || next.Kind != "tunnel"))
                {
                    // A lintel and two jambs mark the portal rather than an exposed road on rock.
                    var entrance = center + direction * tile * .42f;
                    Line(entrance - side * tile * .22f, entrance + side * tile * .22f, Ink("#A29D89"));
                    Line(entrance - side * tile * .22f, entrance - side * tile * .22f - direction * tile * .12f, Ink("#A29D89"));
                    Line(entrance + side * tile * .22f, entrance + side * tile * .22f - direction * tile * .12f, Ink("#A29D89"));
                }
            }
            if (road.Kind == "transfer")
            {
                handle.DrawCircle(center * UIScale, tile * .25f * UIScale, Color.ToSrgb(Ink("#849FA7")), false);
                handle.DrawRect(UIBox2.FromDimensions((center - new Vector2(tile * .1f)) * UIScale,
                    new Vector2(tile * .2f) * UIScale), Ink("#A6B8B9"), false);
            }

            void Strip(float breadth, Color tint)
            {
                var half = tile * breadth / 2;
                handle.DrawRect(UIBox2.FromDimensions((center - new Vector2(half)) * UIScale, new Vector2(half * 2) * UIScale), tint);
                for (var i = 0; i < RoadDirections.Length; i++)
                {
                    if ((road.Connections & (1 << i)) == 0) continue;
                    var end = center + new Vector2(RoadDirections[i].X, RoadDirections[i].Y) * tile * .5f;
                    var minimum = Vector2.Min(center, end) - new Vector2(half);
                    var extent = Vector2.Abs(end - center) + new Vector2(half * 2);
                    // Extend to the cell seam, not beyond it, so mixed road kinds keep clean joins.
                    if (RoadDirections[i].X != 0) { minimum.X = System.MathF.Min(center.X - half, end.X); extent.X = tile * .5f + half; }
                    else { minimum.Y = System.MathF.Min(center.Y - half, end.Y); extent.Y = tile * .5f + half; }
                    handle.DrawRect(UIBox2.FromDimensions(minimum * UIScale, extent * UIScale), tint);
                }
            }
            void Line(Vector2 a, Vector2 b, Color tint) => handle.DrawLine(a * UIScale, b * UIScale, tint);
        }
    }
}
