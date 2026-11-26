using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

public sealed record SWGovernorPersonality(bool Honest, bool Cautious, bool Social);
public sealed record SWGovernorCandidate(string World, string Recipe, double AddedCyclesPerHour, int BaseCost,
    double Score, string Reason, EconomyAmount[] Materials, double WorkHours);

/// <summary>Pure evaluation of published facts. Admission, authority and spending belong to the caller.</summary>
public static class SWGovernorPolicy
{
    private static readonly HashSet<string> EssentialGroups = new(StringComparer.Ordinal)
    {
        "water", "nutrition", "care_drugs", "care_supplies", "closed_habitats",
    };

    public static double ReserveFraction(SWGovernorPersonality personality) => personality.Cautious ? .45 : .25;

    public static IReadOnlyList<SWGovernorCandidate> Evaluate(EconomyDefinition definition, EconomySnapshot snapshot,
        IReadOnlyCollection<string> worlds, SWGovernorPersonality personality)
    {
        const double hours = RepublicEconomyModel.StepHours;
        var allowed = worlds.ToHashSet(StringComparer.Ordinal);
        var stores = snapshot.Inventory.ToDictionary(i => (i.World, i.Good));
        var activity = snapshot.Goods.ToDictionary(g => (g.World, g.Good));
        var lines = snapshot.Production.ToDictionary(p => (p.World, p.Recipe));
        var capacities = snapshot.Capacities.ToDictionary(c => c.World);
        var result = new List<SWGovernorCandidate>();
        foreach (var recipe in definition.Production)
        {
            if (!allowed.Contains(recipe.World) || !definition.Worlds.Any(w => w.Id == recipe.World && w.Active)
                || !lines.TryGetValue((recipe.World, recipe.Recipe), out var line)
                || line.BaseCyclesPerHour <= 0 || line.EffectiveCyclesPerHour <= 0)
                continue;
            var remaining = line.BaseCyclesPerHour - line.CommittedExpansionCyclesPerHour;
            var added = Math.Min(remaining, line.BaseCyclesPerHour * (personality.Cautious ? .15 : .25));
            if (!double.IsFinite(added) || added <= 1e-9)
                continue;
            var world = definition.Worlds.Single(w => w.Id == recipe.World);
            capacities.TryGetValue(recipe.World, out var capacity);
            var multiplier = capacity?.ProductionMultiplier ?? 1;
            var loading = Math.Max(line.StartedCyclesLastStep / hours,
                line.WorkInProgress / recipe.Hours) / (line.EffectiveCyclesPerHour * multiplier);
            if (!double.IsFinite(loading) || loading < .65)
                continue;

            var essential = recipe.Outputs.Any(o => definition.Demands.Any(d => d.World == recipe.World
                && d.Good == o.Good && EssentialGroups.Contains(d.Group)));
            var critical = snapshot.Services.Where(s => s.World == recipe.World && EssentialGroups.Contains(s.Group)
                && s.LastRequired > 1e-9 && s.LastServed < .75 * s.LastRequired).ToArray();
            // Protect basic provisioning even under an industrial governor's priorities.
            if (critical.Length > 0 && !recipe.Outputs.Any(o => definition.Demands.Any(d => d.World == recipe.World
                    && d.Good == o.Good && critical.Any(s => s.Group == d.Group))))
                continue;

            var demand = 0d;
            var buffer = 0d;
            var shortage = 0d;
            var exports = 0d;
            var hasPressure = false;
            foreach (var output in recipe.Outputs)
            {
                if (!activity.TryGetValue((recipe.World, output.Good), out var good)
                    || !stores.TryGetValue((recipe.World, output.Good), out var stock))
                    continue;
                var used = (good.Consumed + good.Exported) / hours;
                var required = (good.Required + good.Exported) / hours;
                var pressure = Math.Max(used, required);
                if (pressure <= 1e-9)
                    continue;
                var coverage = (stock.Stock + good.Incoming) / pressure;
                if (coverage > (personality.Cautious ? 8 : 12)
                    || stock.Stock + stock.Reserved >= stock.Capacity * .9)
                    continue;
                hasPressure = true;
                demand += pressure;
                buffer = Math.Max(buffer, coverage);
                exports += good.Exported / hours;
                shortage += Math.Max(0, good.Required - good.Consumed) / hours;
            }
            if (!hasPressure || demand <= 0)
                continue;

            var extraWip = added * multiplier * recipe.Hours * definition.Headroom;
            var occupiedPower = 0d;
            var occupiedLabor = 0d;
            foreach (var other in definition.Production.Where(p => p.World == recipe.World))
            {
                if (!lines.TryGetValue((other.World, other.Recipe), out var running))
                    continue;
                // Include accepted expansions that are still under construction in planning headroom.
                var pending = Math.Max(0, running.CommittedExpansionCyclesPerHour
                    - (running.EffectiveCyclesPerHour - running.BaseCyclesPerHour));
                var occupied = running.WorkInProgress + pending * multiplier * other.Hours * definition.Headroom;
                occupiedPower += occupied * other.Power;
                occupiedLabor += occupied * other.Labor;
            }
            stores.TryGetValue((recipe.World, "reactor_fuel"), out var fuel);
            var reactor = Math.Min(capacity?.ReactorCapacityPower ?? world.ReactorCapacityPower,
                Math.Max(0, fuel?.Stock ?? 0) * definition.ReactorPowerPerFuel / 2);
            var power = world.BaseloadPower + reactor - (capacity?.DomesticPower ?? world.DomesticPower);
            var labor = capacity?.LaborCapacity ?? world.LaborCapacity;
            if (occupiedPower + extraWip * recipe.Power > power + 1e-9
                || occupiedLabor + extraWip * recipe.Labor > labor + 1e-9)
                continue;
            var fed = recipe.Inputs.All(input =>
            {
                stores.TryGetValue((recipe.World, input.Good), out var stock);
                activity.TryGetValue((recipe.World, input.Good), out var good);
                var currentUse = (good?.Consumed ?? 0) / hours;
                var pendingUse = definition.Production.Where(p => p.World == recipe.World)
                    .Sum(p => lines.TryGetValue((p.World, p.Recipe), out var state)
                        ? Math.Max(0, state.CommittedExpansionCyclesPerHour - (state.EffectiveCyclesPerHour - state.BaseCyclesPerHour))
                          * multiplier * p.Inputs.Where(i => i.Good == input.Good).Sum(i => i.Quantity) : 0);
                return (stock?.Stock ?? 0) + (good?.Incoming ?? 0)
                    >= 2 * (currentUse + pendingUse + added * multiplier * input.Quantity);
            });
            if (!fed)
                continue;
            var intermediate = recipe.Outputs.Any(o => definition.Production.Any(p => p.Inputs.Any(i => i.Good == o.Good)));
            var score = loading * 30 + 20 / (1 + buffer) + Math.Min(20, shortage) + (critical.Length > 0 ? 100 : 0)
                + (essential ? personality.Social ? 45 : 15 : 0)
                + (intermediate || exports > 0 ? personality.Social ? 8 : 40 : 0);
            var scale = 1 + Math.Min(3, added) * .2;
            result.Add(new SWGovernorCandidate(recipe.World, recipe.Recipe, added,
                (int) Math.Clamp(Math.Round(650 + 250 * added), 650, 1600), score,
                $"Загрузка {loading * 100:0}%; запас {buffer:0.0} ч; потребность {demand:0.0}/ч",
                new[] { new EconomyAmount("building_modules", .25 * scale), new EconomyAmount("machinery", .15 * scale),
                    new EconomyAmount("parts", .2 * scale) }, personality.Cautious ? 4 : 2));
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.World, StringComparer.Ordinal)
            .ThenBy(c => c.Recipe, StringComparer.Ordinal).ToArray();
    }
}
