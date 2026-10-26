using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Content.Shared._SW._Senate.Sectors;

/// <summary>Display projections of one graph. A border node always retains its real world ID.</summary>
public static class SWGalacticMapViews
{
    public static SWGalacticMapPrototype Overview(SWGalacticMapPrototype source) => new()
    {
        Size = source.Size,
        Worlds = source.Worlds.Where(w => w.Overview).ToList(),
        Routes = source.Routes.Where(r => !r.Local).ToList(),
        Regions = source.Regions,
        Corridors = source.Corridors,
        PoliticalAreas = source.PoliticalAreas,
    };

    public static SWGalacticMapPrototype Sector(SWGalacticMapPrototype source, string sectorId)
    {
        var owned = source.Worlds.Where(w => w.Sector?.Id == sectorId).ToArray();
        var ids = owned.Select(w => w.Id).ToHashSet();
        var routes = source.Routes.Where(r => ids.Contains(r.From) || ids.Contains(r.To)).ToArray();
        var external = routes.SelectMany(r => new[] { r.From, r.To }).Where(id => !ids.Contains(id)).ToHashSet();
        var worlds = source.Worlds.ToDictionary(w => w.Id);
        var view = new SWGalacticMapPrototype { Size = new Vector2(120, 100), Corridors = source.Corridors };
        foreach (var world in owned)
            view.Worlds.Add(Copy(world, world.LocalPosition ?? new Vector2(60, 50)));

        // Preserve the broad bearing of every actual exit. Spreading along each edge prevents
        // several routes to the same neighbouring region from producing overlapping labels.
        var sides = new List<(SWGalacticMapWorld World, float Order)>[4];
        for (var i = 0; i < sides.Length; i++) sides[i] = new();
        foreach (var id in external.Order())
        {
            var world = worlds[id];
            var connections = routes.Where(r => r.From == id || r.To == id)
                .Select(r => worlds[r.From == id ? r.To : r.From].Position).ToArray();
            var center = connections.Aggregate(Vector2.Zero, (sum, p) => sum + p) / connections.Length;
            var delta = world.Position - center;
            var side = Math.Abs(delta.X) > Math.Abs(delta.Y) ? (delta.X < 0 ? 0 : 1) : (delta.Y < 0 ? 2 : 3);
            sides[side].Add((world, side < 2 ? delta.Y : delta.X));
        }
        for (var side = 0; side < sides.Length; side++)
        {
            var ordered = sides[side].OrderBy(p => p.Order).ThenBy(p => p.World.Id).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var fraction = (i + 1f) / (ordered.Length + 1f);
                var position = side < 2
                    ? new Vector2(side == 0 ? 5 : 115, 16 + fraction * 68)
                    : new Vector2(16 + fraction * 88, side == 2 ? 5 : 95);
                view.Worlds.Add(Copy(ordered[i].World, position));
            }
        }
        var positions = view.Worlds.ToDictionary(w => w.Id, w => w.Position);
        foreach (var route in routes)
        {
            var a = positions[route.From];
            var b = positions[route.To];
            // Galaxy controls are expressed in a different projection. Choose a local bend
            // that avoids intervening nodes instead of carrying those coordinates across.
            var delta = b - a;
            var normal = delta.LengthSquared() > .001f ? Vector2.Normalize(new Vector2(-delta.Y, delta.X)) : Vector2.Zero;
            var best = (a + b) / 2;
            var bestCost = float.PositiveInfinity;
            foreach (var offset in new[] { 0f, 8, -8, 16, -16, 24, -24 })
            {
                var control = (a + b) / 2 + normal * offset;
                var cost = Math.Abs(offset) * .03f;
                foreach (var (id, p) in positions)
                {
                    if (id == route.From || id == route.To) continue;
                    for (var step = 1; step < 20; step++)
                    {
                        var t = step / 20f;
                        var point = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
                        cost += Math.Max(0, 7 - Vector2.Distance(point, p));
                    }
                }
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = control;
            }
            view.Routes.Add(new SWGalacticMapRoute
            {
                From = route.From, To = route.To, Corridor = route.Corridor,
                Proposed = route.Proposed, Local = route.Local, Control = best,
            });
        }
        return view;
    }

    private static SWGalacticMapWorld Copy(SWGalacticMapWorld world, Vector2 position) => new()
    {
        Id = world.Id, Name = world.Name, Position = position, Sector = world.Sector,
        Anchor = world.Anchor, Overview = world.Overview, LocalPosition = world.LocalPosition,
        Kind = world.Kind, Description = world.Description,
    };
}
