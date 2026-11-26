using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryEconomySystem
{
    private double _conditionHour = -1;
    private readonly Queue<Dictionary<string, (double Completed, double Capacity, double LaborUsed, double LaborCapacity, double Delivered)>> _conditionSteps = new();
    private Dictionary<string, string>? _conditionSectors;
    private void ResetEconomicConditions() { _conditionHour = -1; _conditionSteps.Clear(); }

    // Four active hours smooth quarter-hour batches. Zero capacity or demand has no effect.
    // This reads only physical telemetry, never the resulting Senate target.
    private void PublishEconomicConditions()
    {
        var snapshot = _economy.Snapshot;
        if (snapshot == null || snapshot.Hour <= 0 || snapshot.Hour <= _conditionHour) return;
        _conditionHour = snapshot.Hour;
        if (_conditionSectors == null)
        {
            var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
            _conditionSectors = _proto.Index(scenario.Map).Worlds.Where(w => w.Sector != null)
                .ToDictionary(w => w.Id, w => w.Sector!.Value.Id);
        }
        var step = new Dictionary<string, (double Completed, double Capacity, double LaborUsed, double LaborCapacity, double Delivered)>();
        var laborByRecipe = _economy.Definition?.Production.ToDictionary(p => (p.World, p.Recipe), p => p.Labor);
        foreach (var production in snapshot.Production)
        {
            if (!_conditionSectors.TryGetValue(production.World, out var sector)) continue;
            var row = step.GetValueOrDefault(sector);
            row.Completed += Math.Max(0, production.CompletedCyclesLastStep);
            row.Capacity += Math.Max(0, production.BaseCyclesPerHour) * SWRepublicEconomyRunner.StepHours;
            row.LaborUsed += Math.Max(0, production.WorkInProgress) * (laborByRecipe?.GetValueOrDefault((production.World, production.Recipe)) ?? 0);
            step[sector] = row;
        }
        foreach (var capacity in snapshot.Capacities)
        {
            if (!_conditionSectors.TryGetValue(capacity.World, out var sector)) continue;
            var row = step.GetValueOrDefault(sector);
            row.LaborCapacity += Math.Max(0, capacity.LaborCapacity);
            step[sector] = row;
        }
        foreach (var activity in snapshot.Goods)
        {
            if (!_conditionSectors.TryGetValue(activity.World, out var sector)) continue;
            var row = step.GetValueOrDefault(sector);
            row.Delivered += Math.Max(0, activity.Imported);
            step[sector] = row;
        }
        _conditionSteps.Enqueue(step);
        while (_conditionSteps.Count > 16) _conditionSteps.Dequeue();
        var productionEffects = new List<SWSituationalContribution>();
        var employmentEffects = new List<SWSituationalContribution>();
        var tradeEffects = new List<SWSituationalContribution>();
        foreach (var sector in _conditionSteps.SelectMany(s => s.Keys).Distinct())
        {
            var rows = _conditionSteps.Select(s => s.GetValueOrDefault(sector)).ToArray();
            var capacity = rows.Sum(r => r.Capacity);
            if (capacity > 1e-9)
            {
                var load = Math.Clamp(rows.Sum(r => r.Completed) / capacity, 0, 1);
                productionEffects.Add(new SWSituationalContribution(sector, "SWStatManufacturing", (float) ((load - .75) * 8)));
            }
            var laborCapacity = rows.Sum(r => r.LaborCapacity);
            if (laborCapacity > 1e-9 && laborByRecipe != null)
            {
                // WorkInProgress is the actual active batch cycle count; recipe Labor
                // converts it to reserved labour, divided by physical world capacity.
                var labor = Math.Clamp(rows.Sum(r => r.LaborUsed) / laborCapacity, 0, 1);
                employmentEffects.Add(new SWSituationalContribution(sector, "SWStatEmployment", (float) ((labor - .5) * 6)));
            }
            // Imported is delivered cargo in the step (Incoming is still in transit).
            var delivered = rows.Sum(r => r.Delivered);
            if (delivered > 0)
                tradeEffects.Add(new SWSituationalContribution(sector, "SWStatTrade", (float) (3 * delivered / (delivered + 40))));
        }
        _senate.ReplaceSituationalContributions("ministry.economy.production", productionEffects);
        _senate.ReplaceSituationalContributions("ministry.economy.employment", employmentEffects);
        _senate.ReplaceSituationalContributions("ministry.economy.trade", tradeEffects);
    }
}
