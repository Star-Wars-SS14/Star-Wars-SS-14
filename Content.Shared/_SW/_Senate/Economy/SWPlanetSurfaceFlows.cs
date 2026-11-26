using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Shared._SW._Senate.Economy;

public sealed record SWPlanetSurfaceFlow(string From, string To, string Good, double UnitsPerHour);

/// <summary>Attribution of one published step to schematic endpoints. Never simulates or creates goods.</summary>
public static class SWPlanetSurfaceFlows
{
    public static IReadOnlyList<SWPlanetSurfaceFlow> Build(SWPlanetSurfacePrototype surface,
        SWEconomyWorldView? world, IReadOnlyDictionary<string, SWProductionRecipePrototype> recipes,
        IReadOnlyCollection<string> residentGoods)
    {
        if (world == null || world.World != surface.ID)
            return Array.Empty<SWPlanetSurfaceFlow>();
        var storage = surface.Sites.FirstOrDefault(s => s.Kind == "storage")?.Id;
        var port = surface.Sites.FirstOrDefault(s => s.Kind == "port")?.Id;
        var power = surface.Sites.FirstOrDefault(s => s.Kind == "power")?.Id;
        var residents = surface.Sites.Where(s => s.Kind == "settlement").Select(s => s.Id).Distinct().ToArray();
        var totals = new Dictionary<(string From, string To, string Good), double>();
        var inputs = new Dictionary<string, double>();
        var actual = world.Production.GroupBy(p => p.Recipe).ToDictionary(g => g.Key, g => g.First());
        // Count all actual recipe inputs, including lines with no schematic production endpoint.
        foreach (var line in actual.Values)
        {
            if (!recipes.TryGetValue(line.Recipe, out var recipe) || !Positive(line.StartedCyclesPerHour))
                continue;
            foreach (var (good, quantity) in recipe.Inputs)
            {
                var amount = line.StartedCyclesPerHour * quantity;
                if (Positive(amount))
                    inputs[good.Id] = inputs.GetValueOrDefault(good.Id) + amount;
            }
        }
        var seen = new HashSet<string>();
        foreach (var site in surface.Sites.Where(s => s.Kind == "production"))
        {
            foreach (var id in site.Recipes)
            {
                if (!seen.Add(id.Id) || !actual.TryGetValue(id.Id, out var line) || !recipes.TryGetValue(id.Id, out var recipe))
                    continue;
                foreach (var (good, quantity) in recipe.Inputs)
                    Add(storage, site.Id, good.Id, Positive(line.StartedCyclesPerHour) ? line.StartedCyclesPerHour * quantity : 0);
                foreach (var (good, quantity) in recipe.Outputs)
                    Add(site.Id, storage, good.Id, Positive(line.CompletedCyclesPerHour) ? line.CompletedCyclesPerHour * quantity : 0);
            }
        }
        foreach (var good in world.Goods.GroupBy(g => g.Good).Select(g => g.First()))
        {
            Add(port, storage, good.Good, good.ImportedPerHour);
            Add(storage, port, good.Good, good.ExportedPerHour);
            if (!Positive(good.ConsumedPerHour))
                continue;
            var residual = Math.Max(0, good.ConsumedPerHour - inputs.GetValueOrDefault(good.Good));
            if (good.Good == "reactor_fuel")
                Add(storage, power, good.Good, residual);
            else if (good.Good == "ship_fuel")
                Add(storage, port, good.Good, residual);
            else if (residentGoods.Contains(good.Good) && residents.Length > 0)
                foreach (var district in residents)
                    Add(storage, district, good.Good, residual / residents.Length);
        }
        return totals.OrderBy(p => p.Key.From).ThenBy(p => p.Key.To)
            .ThenBy(p => p.Key.Good)
            .Select(p => new SWPlanetSurfaceFlow(p.Key.From, p.Key.To, p.Key.Good, p.Value)).ToArray();

        void Add(string? from, string? to, string good, double amount)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to || !Positive(amount))
                return;
            var key = (from, to, good);
            var sum = totals.GetValueOrDefault(key) + amount;
            if (Positive(sum))
                totals[key] = sum;
        }
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
