using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Static tile connectivity for surface routes; no second goods or travel simulation.</summary>
public sealed class SWPlanetSurfaceNetwork
{
    private static readonly Vector2i[] Directions =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
    };

    public IReadOnlyDictionary<Vector2i, SWPlanetSurfaceRoad> Roads { get; }

    public SWPlanetSurfaceNetwork(SWPlanetSurfacePrototype surface)
    {
        Roads = surface.Roads.GroupBy(r => r.Cell).ToDictionary(g => g.Key, g => g.First());
    }

    public HashSet<Vector2i> ReachableFrom(Vector2i from) => Traverse(from, null).Keys.ToHashSet();

    /// <summary>Shortest orthogonal tile path, including endpoints. Empty if either endpoint is disconnected.</summary>
    public IReadOnlyList<Vector2i> FindPath(Vector2i from, Vector2i to)
    {
        if (!Roads.ContainsKey(to)) return System.Array.Empty<Vector2i>();
        var previous = Traverse(from, to);
        if (!previous.ContainsKey(to)) return System.Array.Empty<Vector2i>();
        var path = new List<Vector2i> { to };
        while (path[^1] != from) path.Add(previous[path[^1]]);
        path.Reverse();
        return path;
    }

    private Dictionary<Vector2i, Vector2i> Traverse(Vector2i from, Vector2i? to)
    {
        var previous = new Dictionary<Vector2i, Vector2i>();
        if (!Roads.ContainsKey(from)) return previous;
        var queue = new Queue<Vector2i>();
        queue.Enqueue(from);
        previous.Add(from, from);
        while (queue.TryDequeue(out var current))
        {
            if (current == to) break;
            foreach (var direction in Directions)
            {
                var next = current + direction;
                if (!Roads.ContainsKey(next) || !previous.TryAdd(next, current)) continue;
                queue.Enqueue(next);
            }
        }
        return previous;
    }
}
