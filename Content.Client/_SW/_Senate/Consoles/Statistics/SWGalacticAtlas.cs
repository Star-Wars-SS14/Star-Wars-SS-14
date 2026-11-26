using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Shared geography for atlas overview maps; callers supply only their disclosed view.</summary>
public static class SWGalacticAtlas
{
    public static SWGalacticMapPrototype Overview(SWGalacticMapPrototype visible) => new()
    {
        Size = visible.Size,
        Worlds = visible.Worlds.ToList(),
        Routes = visible.Routes.ToList(),
        Regions = visible.Regions,
        Corridors = visible.Corridors,
        PoliticalAreas = visible.PoliticalAreas,
    };
    public static SWGalacticMapPrototype MakeSchematicIfNeeded(SWGalacticMapPrototype view, string? sector, string? system, string? political, out bool schematicPlacement)
    {
        schematicPlacement = false;
        if (sector == null && system == null && political == null) return view;
        bool Own(SWGalacticMapWorld world) => sector != null ? world.Sector?.Id == sector
            : system != null ? world.SystemId == system : world.PoliticalSectorId == political;
        var worlds = view.Worlds.Where(Own).OrderBy(w => w.Id, Comparer<string>.Create(string.CompareOrdinal)).ToArray();
        if (worlds.Length == 0 || worlds.All(w => w.LocalPosition is { } p &&
                float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X <= view.Size.X && p.Y <= view.Size.Y) &&
            worlds.Select(w => w.LocalPosition!.Value).Distinct().Count() == worlds.Length) return view;
        schematicPlacement = true;
        var columns = Math.Max(1, (int) Math.Ceiling(Math.Sqrt(worlds.Length)));
        var rows = (worlds.Length + columns - 1) / columns;
        for (var i = 0; i < worlds.Length; i++)
            worlds[i].Position = new Vector2(20 + (i % columns + .5f) * 80 / columns, 20 + (i / columns + .5f) * 60 / rows);
        var byId = view.Worlds.ToDictionary(w => w.Id);
        foreach (var route in view.Routes)
            if (byId.TryGetValue(route.From, out var a) && byId.TryGetValue(route.To, out var b))
                route.Control = (a.Position + b.Position) / 2;
        return view;
    }

}
