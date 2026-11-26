using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed partial class SWProductionGraphBuilder
{
    /// <summary>
    /// Condense strongly connected components before assigning longest-path columns. A recycled
    /// ingredient can then retain its back edge without infinite depth or duplicate commodity nodes.
    /// Only graph topology controls columns, so shared prerequisites precede every consuming recipe.
    /// </summary>
    private static SWProductionGraphData Arrange(Dictionary<string, SWProductionNode> nodes,
        IEnumerable<SWProductionEdge> sourceEdges, string focus)
    {
        var edges = sourceEdges.OrderBy(e => e.From).ThenBy(e => e.To).ToArray();
        var outgoing = nodes.Keys.ToDictionary(id => id, _ => new List<string>());
        var incoming = nodes.Keys.ToDictionary(id => id, _ => new List<string>());
        foreach (var edge in edges)
        {
            outgoing[edge.From].Add(edge.To);
            incoming[edge.To].Add(edge.From);
        }

        // Iterative Kosaraju traversal also avoids relying on the process stack for a modded catalogue.
        var visited = new HashSet<string>();
        var finish = new List<string>();
        foreach (var root in nodes.Keys.OrderBy(id => id))
        {
            if (!visited.Add(root))
                continue;
            var stack = new Stack<(string Id, int Next)>();
            stack.Push((root, 0));
            while (stack.TryPop(out var frame))
            {
                if (frame.Next >= outgoing[frame.Id].Count)
                {
                    finish.Add(frame.Id);
                    continue;
                }
                stack.Push((frame.Id, frame.Next + 1));
                var next = outgoing[frame.Id][frame.Next];
                if (visited.Add(next))
                    stack.Push((next, 0));
            }
        }

        var component = new Dictionary<string, int>();
        var count = 0;
        for (var i = finish.Count - 1; i >= 0; i--)
        {
            if (component.ContainsKey(finish[i]))
                continue;
            var stack = new Stack<string>();
            stack.Push(finish[i]);
            component.Add(finish[i], count);
            while (stack.TryPop(out var node))
            {
                foreach (var next in incoming[node])
                {
                    if (!component.TryAdd(next, count))
                        continue;
                    stack.Push(next);
                }
            }
            count++;
        }

        var links = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
        var dependencies = new int[count];
        foreach (var edge in edges)
        {
            var from = component[edge.From];
            var to = component[edge.To];
            if (from != to && links[from].Add(to))
                dependencies[to]++;
        }
        var columns = new int[count];
        var ready = new Queue<int>(Enumerable.Range(0, count).Where(i => dependencies[i] == 0));
        while (ready.TryDequeue(out var current))
        {
            foreach (var next in links[current].OrderBy(i => i))
            {
                columns[next] = Math.Max(columns[next], columns[current] + 1);
                if (--dependencies[next] == 0)
                    ready.Enqueue(next);
            }
        }
        var arranged = nodes.Values.Select(n => n with { Column = columns[component[n.Id]] })
            .OrderBy(n => n.Column).ThenBy(n => n.Kind).ThenBy(n => n.Title)
            .ThenBy(n => n.Id).ToArray();
        return new SWProductionGraphData(arranged, edges, focus);
    }
}
