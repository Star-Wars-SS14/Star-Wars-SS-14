using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWProductionGraphBuilder
{
    private RangeCatalogue? _rangeCatalogue;

    private sealed class RangeCatalogue
    {
        public readonly Dictionary<string, SWProductionNode> Nodes;
        public readonly SWProductionEdge[] Edges;
        public readonly Dictionary<string, List<SWProductionEdge>> Forward;
        public readonly Dictionary<string, List<SWProductionEdge>> Backward;

        public RangeCatalogue(Dictionary<string, SWProductionNode> nodes, SWProductionEdge[] edges)
        {
            Nodes = nodes;
            Edges = edges;
            Forward = nodes.Keys.ToDictionary(id => id, _ => new List<SWProductionEdge>());
            Backward = nodes.Keys.ToDictionary(id => id, _ => new List<SWProductionEdge>());
            foreach (var edge in edges)
            {
                Forward[edge.From].Add(edge);
                Backward[edge.To].Add(edge);
            }
        }
    }

    /// <summary>
    /// Only the directed segment between two catalogue nodes. Off-path ingredients and basket
    /// members stay in the inspector, rather than turning a selected segment into a full recipe.
    /// An explicit alternative restricts production of its output; unspecified goods retain every
    /// catalogue alternative. Unknown selections cannot suppress valid catalogue paths.
    /// </summary>
    public SWProductionGraphData BuildRange(string fromNodeId, string toNodeId,
        IReadOnlyDictionary<string, string> selectedRecipes)
    {
        var catalogue = _rangeCatalogue ??= CreateRangeCatalogue();
        var endpoints = new Dictionary<string, SWProductionNode>();
        if (catalogue.Nodes.TryGetValue(fromNodeId, out var from)) endpoints.TryAdd(from.Id, from);
        if (catalogue.Nodes.TryGetValue(toNodeId, out var to)) endpoints.TryAdd(to.Id, to);
        var focus = from?.Id ?? to?.Id ?? string.Empty;
        if (from == null || to == null || fromNodeId == toNodeId)
            return Arrange(endpoints, Array.Empty<SWProductionEdge>(), focus);

        var alternatives = new Dictionary<string, string>();
        foreach (var (good, recipe) in selectedRecipes)
        {
            if (_recipes.TryGetValue(recipe, out var definition) && definition.Outputs.ContainsKey(good))
                alternatives["good:" + good] = "recipe:" + recipe;
        }

        bool Allowed(SWProductionEdge edge) => !edge.From.StartsWith("recipe:") ||
            !alternatives.TryGetValue(edge.To, out var selected) || edge.From == selected;

        HashSet<string> Reach(string start, bool backwards)
        {
            var reached = new HashSet<string> { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            var adjacency = backwards ? catalogue.Backward : catalogue.Forward;
            while (queue.TryDequeue(out var node))
            {
                foreach (var edge in adjacency[node])
                {
                    if (!Allowed(edge)) continue;
                    var next = backwards ? edge.From : edge.To;
                    if (reached.Add(next)) queue.Enqueue(next);
                }
            }
            return reached;
        }

        var reachable = Reach(fromNodeId, false);
        if (!reachable.Contains(toNodeId))
            return Arrange(endpoints, Array.Empty<SWProductionEdge>(), fromNodeId);
        reachable.IntersectWith(Reach(toNodeId, true));
        var nodes = catalogue.Nodes.Where(p => reachable.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        var edges = catalogue.Edges.Where(e => reachable.Contains(e.From) && reachable.Contains(e.To) && Allowed(e));
        return Arrange(nodes, edges, fromNodeId);
    }

    private RangeCatalogue CreateRangeCatalogue()
    {
        var nodes = new Dictionary<string, SWProductionNode>();
        var edges = new Dictionary<(string From, string To), SWProductionEdge>();
        foreach (var good in _goods.Values.OrderBy(g => g.ID))
            nodes.Add("good:" + good.ID, new SWProductionNode("good:" + good.ID, SWProductionNodeKind.Commodity,
                good.ID, good.Name, UseLabel(good.Use), 0));
        foreach (var condition in _conditions.Values.OrderBy(c => c.ID))
            nodes.Add("condition:" + condition.ID, new SWProductionNode("condition:" + condition.ID,
                SWProductionNodeKind.Condition, condition.ID, condition.Name,
                _finiteConditions.Contains(condition.ID) ? "Конечный местный фонд" : "Условие размещения", 0));

        // Reuse the recipe-mode material and condition semantics. This runs once for the catalogue,
        // never for each endpoint click, and includes alternatives absent from a focused chain.
        foreach (var recipe in _recipes.Values.OrderBy(r => r.Order).ThenBy(r => r.ID))
        {
            if (recipe.Outputs.Count == 0) continue;
            var output = recipe.Outputs.Keys.OrderBy(g => g.Id).First().Id;
            var selection = new Dictionary<string, string> { [output] = recipe.ID };
            var graph = Build(output, selection, SWProductionGraphMode.Recipe);
            foreach (var node in graph.Nodes) nodes.TryAdd(node.Id, node);
            foreach (var edge in graph.Edges) edges.TryAdd((edge.From, edge.To), edge);
        }
        foreach (var demand in _allDemands)
        {
            var id = "demand:" + demand.ID;
            nodes.TryAdd(id, new SWProductionNode(id, SWProductionNodeKind.Demand, demand.ID,
                demand.Name, DemandLabel(demand.Mode) + "\n" + demand.Basis, 0));
            foreach (var (good, amount) in demand.Goods.OrderBy(p => p.Key.Id))
            {
                var input = "good:" + good.Id;
                if (nodes.ContainsKey(input))
                    edges.TryAdd((input, id), new SWProductionEdge(input, id, DemandCoefficient(demand.Mode, amount)));
            }
        }
        return new RangeCatalogue(nodes, edges.Values.OrderBy(e => e.From).ThenBy(e => e.To).ToArray());
    }
}
