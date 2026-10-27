using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private readonly Dictionary<string, Dictionary<string, double>> _transitSources = new();
    private readonly Dictionary<string, double> _expandedTransit = new();
    private readonly HashSet<string> _committedTransit = new();
    private EconomyQuarantineRule[] _quarantines = Array.Empty<EconomyQuarantineRule>();
    public void SetTransitRestrictions(IReadOnlyDictionary<string, double> multipliers) => SetTransitRestrictions("defense", multipliers);
    public void SetTransitRestrictions(string source, IReadOnlyDictionary<string, double> multipliers)
    {
        if (string.IsNullOrWhiteSpace(source) || multipliers.Any(p => !_edges.ContainsKey(p.Key) || !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1))
            throw new ArgumentException("Invalid transit restriction", nameof(multipliers));
        if (_transitSources.TryGetValue(source, out var previous) && previous.Count == multipliers.Count && previous.All(p => multipliers.TryGetValue(p.Key, out var value) && value == p.Value)) return;
        _transitSources[source] = multipliers.ToDictionary(p => p.Key, p => p.Value);
        _projectPaths.Clear();
    }
    public void SetQuarantineTransitRestrictions(IReadOnlyList<EconomyQuarantineRule> rules)
    {
        if (rules.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Worlds.Length == 0 || r.Worlds.Any(w => !_worlds.ContainsKey(w))))
            throw new ArgumentException("Invalid quarantine", nameof(rules));
        if (_quarantines.Length == rules.Count && _quarantines.Select((r, i) => r.Id == rules[i].Id && r.Humanitarian == rules[i].Humanitarian && r.Worlds.SequenceEqual(rules[i].Worlds)).All(v => v)) return;
        _quarantines = rules.Select(r => r with { Worlds = r.Worlds.Distinct().ToArray() }).ToArray();
        _projectPaths.Clear();
    }
    private double EdgeCapacity(string edge) => (_edges[edge].CapacitySlotsPerHour + _expandedTransit.GetValueOrDefault(edge)) *
        _transitSources.Values.Aggregate(1d, (capacity, rules) => Math.Min(capacity, rules.GetValueOrDefault(edge, 1)));
    private bool CrossingAllowed(string from, string to, string? good, bool medical = false)
    {
        // Admitted addressed medical convoys may cross any quarantine. Military restrictions,
        // physical throughput, carriers and fuel remain enforced independently at dispatch.
        if (medical) return true;
        foreach (var rule in _quarantines)
        {
            var insideFrom = rule.Worlds.Contains(from);
            var insideTo = rule.Worlds.Contains(to);
            if (insideFrom == insideTo) continue;
            if (!insideFrom && insideTo && rule.Humanitarian && (good is "water" or "fresh_food" or "synth_food" or "rations" or "drugs" or "medical_supplies")) continue;
            return false;
        }
        return true;
    }
}
