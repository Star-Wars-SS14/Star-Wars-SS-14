using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Shared._SW._Senate.Economy;

public static class SWPlanetSurfaceValidator
{
    public const string TerrainCodes = "pfmwdcisrvo";
    public static bool IsPassable(char terrain) => TerrainCodes.Contains(terrain) && terrain is not ('w' or 'm');

    public static IReadOnlyList<string> Validate(SWPlanetSurfacePrototype surface)
    {
        var errors = new List<string>();
        if (surface.Width != 16 || surface.Height != 12 || surface.Terrain.Count != surface.Height ||
            surface.Terrain.Any(row => row.Length != surface.Width || row.Any(c => !TerrainCodes.Contains(c))))
            errors.Add($"{surface.ID}: invalid terrain dimensions or codes.");
        if (string.IsNullOrWhiteSpace(surface.Biome))
            errors.Add($"{surface.ID}: missing biome description.");
        var cells = new HashSet<Robust.Shared.Maths.Vector2i>();
        var ids = new HashSet<string>();
        foreach (var site in surface.Sites)
        {
            if (!ids.Add(site.Id) || !cells.Add(site.Cell))
                errors.Add($"{surface.ID}/{site.Id}: duplicate site ID or cell.");
            if (site.Cell.X < 0 || site.Cell.Y < 0 || site.Cell.X >= surface.Width || site.Cell.Y >= surface.Terrain.Count ||
                site.Cell.X >= surface.Terrain[site.Cell.Y].Length || !IsPassable(surface.Terrain[site.Cell.Y][site.Cell.X]))
                errors.Add($"{surface.ID}/{site.Id}: site must occupy a passable cell.");
            if (site.Kind is not ("production" or "resource" or "port" or "power" or "storage" or "settlement"))
                errors.Add($"{surface.ID}/{site.Id}: unknown site kind.");
            if ((site.Kind == "production") != (site.Recipes.Count > 0) ||
                (site.Kind == "resource") != (site.Resource != null))
                errors.Add($"{surface.ID}/{site.Id}: site payload does not match its kind.");
        }
        var settlements = surface.Sites.Count(site => site.Kind == "settlement");
        if (settlements < 3 || settlements > 5)
            errors.Add($"{surface.ID}: expected 3–5 settlement districts.");
        var roads = new Dictionary<Vector2i, SWPlanetSurfaceRoad>();
        foreach (var road in surface.Roads)
        {
            if (!roads.TryAdd(road.Cell, road))
                errors.Add($"{surface.ID}: duplicate road cell {road.Cell}.");
            if (!InBounds(road.Cell))
            {
                errors.Add($"{surface.ID}: road outside terrain bounds.");
                continue;
            }
            var tile = surface.Terrain[road.Cell.Y][road.Cell.X];
            var expected = tile == 'w' ? "bridge" : tile == 'm' ? "tunnel" : tile == 'o' ? "orbital" : "road";
            if (road.Kind != expected && road.Kind != "transfer")
                errors.Add($"{surface.ID}: road kind {road.Kind} does not match terrain {tile}.");
        }
        if (roads.Count == 0 || roads.Count >= surface.Width * surface.Height / 2)
            errors.Add($"{surface.ID}: road network must occupy less than half the region.");
        foreach (var road in roads.Values.Where(road => InBounds(road.Cell)))
        {
            var tile = surface.Terrain[road.Cell.Y][road.Cell.X];
            var crossing = Neighbors(road.Cell).Any(cell => InBounds(cell) && roads.ContainsKey(cell) &&
                (tile == 'o') != (surface.Terrain[cell.Y][cell.X] == 'o'));
            if ((road.Kind == "transfer") != crossing)
                errors.Add($"{surface.ID}: orbital crossings need a schematic transfer node.");
        }
        var network = new SWPlanetSurfaceNetwork(surface);
        var visited = roads.Count > 0 ? network.ReachableFrom(roads.Keys.First()) : new HashSet<Vector2i>();
        if (visited.Count != roads.Count || surface.Sites.Any(site => !visited.Contains(site.Cell)))
            errors.Add($"{surface.ID}: every site must belong to one orthogonally connected road network.");
        return errors;

        bool InBounds(Vector2i cell) => cell.X >= 0 && cell.Y >= 0 && cell.Y < surface.Terrain.Count &&
            cell.X < surface.Width && cell.X < surface.Terrain[cell.Y].Length;
    }

    private static IEnumerable<Vector2i> Neighbors(Vector2i cell)
    {
        yield return new Vector2i(cell.X - 1, cell.Y);
        yield return new Vector2i(cell.X + 1, cell.Y);
        yield return new Vector2i(cell.X, cell.Y - 1);
        yield return new Vector2i(cell.X, cell.Y + 1);
    }

    public static IReadOnlyList<string> Validate(IPrototypeManager prototypes)
    {
        var errors = new List<string>();
        var surfaces = prototypes.EnumeratePrototypes<SWPlanetSurfacePrototype>().ToDictionary(s => s.ID);
        var worlds = prototypes.EnumeratePrototypes<SWPlanetEconomyPrototype>().Where(w => w.ActiveEconomy).ToArray();
        if (!surfaces.Keys.ToHashSet().SetEquals(worlds.Select(w => w.ID)))
            errors.Add("Surface catalog must cover exactly the active economy worlds.");
        var scenario = prototypes.Index<SWRepublicEconomyScenarioPrototype>("SenateRepublicEconomy");
        foreach (var world in worlds)
        {
            if (!surfaces.TryGetValue(world.ID, out var surface))
                continue;
            errors.AddRange(Validate(surface));
            var expectedSettlements = world.PopulationUnits < 1.5 ? 3 : world.PopulationUnits < 2.5 ? 4 : 5;
            if (surface.Sites.Count(site => site.Kind == "settlement") != expectedSettlements)
                errors.Add($"{world.ID}: settlement districts do not match normalized population tier.");
            var recipes = surface.Sites.SelectMany(s => s.Recipes).Select(r => r.Id).ToArray();
            var actual = scenario.Production.Where(p => p.World.Id == world.ID && p.Rate > 0).Select(p => p.Recipe.Id).ToArray();
            if (recipes.Length != recipes.Distinct().Count() || !recipes.ToHashSet().SetEquals(actual))
                errors.Add($"{world.ID}: surfaces must show each actual production recipe exactly once.");
            var resources = surface.Sites.Where(s => s.Resource != null).Select(s => s.Resource!.Value.Id).ToArray();
            if (resources.Length != resources.Distinct().Count() ||
                !resources.ToHashSet().SetEquals(world.NaturalResources.Select(r => r.Condition.Id)))
                errors.Add($"{world.ID}: resource sites must match actual natural resources.");
            foreach (var site in surface.Sites.Where(s => s.Recipes.Count > 0))
            {
                if (site.Recipes.Select(r => prototypes.Index(r).Facility.Id).Distinct().Count() != 1)
                    errors.Add($"{world.ID}/{site.Id}: production site mixes facilities.");
            }
        }
        return errors;
    }
}
