using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private Dictionary<string, double> _transitRestrictions = new();

    /// <summary>
    /// Worker-owned replacement. Restrictions affect new departures only; accepted cargo
    /// continues to arrive, and empty project carriers retain their slots while blocked.
    /// </summary>
    public void SetTransitRestrictions(IReadOnlyDictionary<string, double> multipliers)
    {
        if (multipliers.Any(p => !_edges.ContainsKey(p.Key) || !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1))
            throw new ArgumentException("Invalid transit restriction", nameof(multipliers));
        _transitRestrictions = multipliers.ToDictionary(p => p.Key, p => p.Value);
    }

    private double EdgeCapacity(string edge) => _edges[edge].CapacitySlotsPerHour * _transitRestrictions.GetValueOrDefault(edge, 1);
}
