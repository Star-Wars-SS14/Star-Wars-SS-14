using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    private EconomyDefinition? _outbreakDistanceDefinition;
    private EconomySnapshot? _outbreakDistanceSnapshot;
    private string _outbreakDistanceTargets = "";
    private readonly Dictionary<string, (double Hours, string Target)> _outbreakDistances = new();

    /// <summary>Nominal travel on the existing open graph, excluding production, cargo queues and carrier availability.</summary>
    private void PublishOutbreakDistances(List<SWHealthWorldView> worlds)
    {
        var definition = _economy.Definition;
        var snapshot = _economy.Snapshot;
        var targets = worlds.Where(w => w.Infection > 1).Select(w => w.Id).Order(StringComparer.Ordinal).ToArray();
        var key = string.Join('\0', targets);
        if (!ReferenceEquals(definition, _outbreakDistanceDefinition) || !ReferenceEquals(snapshot, _outbreakDistanceSnapshot)
            || key != _outbreakDistanceTargets)
        {
            _outbreakDistanceDefinition = definition;
            _outbreakDistanceSnapshot = snapshot;
            _outbreakDistanceTargets = key;
            _outbreakDistances.Clear();
            foreach (var target in targets) _outbreakDistances[target] = (0, target);
            if (definition != null && targets.Length > 0)
            {
                // Effective edge capacity includes military and other physical closures.
                // Quarantines are cargo admission rules, so admitted medical convoys ignore them.
                var capacities = snapshot?.Edges.ToDictionary(e => e.Id, e => e.CapacitySlotsPerHour);
                var graph = new Dictionary<string, List<(string To, double Hours)>>();
                foreach (var edge in definition.Edges)
                {
                    if (!double.IsFinite(edge.TravelHours) || edge.TravelHours <= 0 || edge.CapacitySlotsPerHour <= 0
                        || capacities != null && capacities.GetValueOrDefault(edge.Id) <= 0) continue;
                    Connect(edge.A, edge.B, edge.TravelHours);
                    Connect(edge.B, edge.A, edge.TravelHours);
                }
                var pending = graph.Keys.Concat(targets).ToHashSet();
                while (pending.Count > 0)
                {
                    var current = pending.OrderBy(w => _outbreakDistances.TryGetValue(w, out var d) ? d.Hours : double.PositiveInfinity)
                        .ThenBy(w => w, StringComparer.Ordinal).First();
                    if (!_outbreakDistances.TryGetValue(current, out var distance)) break;
                    pending.Remove(current);
                    if (!graph.TryGetValue(current, out var neighbors)) continue;
                    foreach (var (next, hours) in neighbors)
                    {
                        if (!pending.Contains(next)) continue;
                        var candidate = distance.Hours + hours;
                        if (_outbreakDistances.TryGetValue(next, out var previous)
                            && (candidate > previous.Hours || candidate == previous.Hours
                                && string.CompareOrdinal(distance.Target, previous.Target) >= 0)) continue;
                        _outbreakDistances[next] = (candidate, distance.Target);
                    }
                }
                void Connect(string from, string to, double hours)
                {
                    if (!graph.TryGetValue(from, out var neighbors)) graph[from] = neighbors = new();
                    neighbors.Add((to, hours));
                }
            }
        }
        foreach (var world in worlds)
        {
            world.HasOutbreakTarget = targets.Length > 0;
            if (_outbreakDistances.TryGetValue(world.Id, out var distance))
            {
                world.ClosestOutbreakTravelHours = distance.Hours;
                world.ClosestOutbreakWorldId = distance.Target;
            }
            else
            {
                world.ClosestOutbreakTravelHours = null;
                world.ClosestOutbreakWorldId = "";
            }
        }
    }
}
