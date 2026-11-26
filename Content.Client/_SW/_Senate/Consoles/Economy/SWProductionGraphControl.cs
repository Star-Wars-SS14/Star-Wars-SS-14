using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed record SWProductionNodeLayout(SWProductionNode Node, UIBox2 Bounds, UIBox2 LocalBounds);

/// <summary>Interactive catalogue topology. Its moving marks indicate dependency direction,
/// never shipments, production rates or the simulation clock.</summary>
public sealed partial class SWProductionGraphControl : Control
{
    private const float CardWidth = 264;
    private const float CardHeight = 160;
    private const float ColumnStride = 376;
    private const float RowStride = 192;
    private const float MinimumZoom = .65f;
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly Dictionary<string, NodeLayout> _nodes = new();
    private readonly List<EdgeLayout> _edges = new();
    private readonly HashSet<string> _connected = new();
    private Vector2? _pointer;
    private Vector2? _press;
    private Vector2 _panAtPress;
    private bool _dragged;
    private bool _minimapPress;
    private bool _animation = true;
    private string? _lastClick;
    private TimeSpan _lastClickTime;
    private Vector2 _lastClickPosition;
    private EdgeLayout? _hoveredEdge;
    private bool _fitView = true;
    private bool _overviewFit;

    private sealed class NodeLayout(SWProductionNode node)
    {
        public readonly SWProductionNode Node = node;
        public UIBox2 Bounds;
        public string[] Title = Array.Empty<string>();
        public string Subtitle = string.Empty;
    }

    private sealed class EdgeLayout(SWProductionEdge edge, Vector2[] points, float[] distances)
    {
        public readonly SWProductionEdge Edge = edge;
        public readonly Vector2[] Points = points;
        public readonly float[] Distances = distances;
        public string DisplayLabel = string.Empty;
        public Vector2 LabelSize;
        public float Length => Distances[^1];
    }

    public SWProductionGraphData? Graph { get; private set; }
    public string? SelectedNodeId { get; private set; }
    public string? HoveredNodeId { get; private set; }
    public string? FromNodeId { get; private set; }
    public string? ToNodeId { get; private set; }
    public float Zoom { get; private set; } = 1;
    public bool IsOverview => Zoom < .55f;
    public Vector2 Pan { get; private set; }
    public double AnimationSeconds { get; private set; }
    public UIBox2 GraphBounds { get; private set; }
    public UIBox2? MinimapBounds => _nodes.Count != 0 && Width >= 320 && Height >= 230 &&
        (GraphBounds.Width * Zoom > Width - 48 || GraphBounds.Height * Zoom > Height - 48)
            ? new UIBox2(Width - 180, Height - 122, Width - 12, Height - 12) : null;
    public event Action<SWProductionNode>? NodeSelected;
    public event Action<SWProductionNode>? NodeActivated;
    public event Action? ViewChanged;

    public SWProductionGraphControl()
    {
        Name = "ProductionGraph";
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetGraph(SWProductionGraphData data)
    {
        Graph = data;
        _textZoom = -1;
        _nodes.Clear();
        _edges.Clear();
        _press = null;
        _lastClick = null;
        foreach (var node in data.Nodes)
            _nodes.TryAdd(node.Id, new NodeLayout(node));
        SelectedNodeId = _nodes.ContainsKey(data.FocusNodeId) ? data.FocusNodeId : _nodes.Keys.FirstOrDefault();
        LayoutGraph();
        ResetView();
        RefreshConnections();
    }

    private void LayoutGraph()
    {
        if (Graph == null || _nodes.Count == 0)
        {
            GraphBounds = UIBox2.FromDimensions(Vector2.Zero, Vector2.One);
            return;
        }
        var columns = _nodes.Values.GroupBy(n => n.Node.Column).OrderBy(g => g.Key).Select(g => g.ToList()).ToArray();
        var ranks = new Dictionary<string, float>();
        void Rank(List<NodeLayout> column)
        {
            for (var i = 0; i < column.Count; i++) ranks[column[i].Node.Id] = (i + .5f) / column.Count;
        }
        foreach (var column in columns) Rank(column);
        // Barycentric sweeps reduce crossed dependencies while keeping the supplied ranks intact.
        for (var sweep = 0; sweep < 3; sweep++)
        {
            foreach (var column in sweep % 2 == 0 ? columns : columns.Reverse())
            {
                float Score(NodeLayout node)
                {
                    var adjacent = Graph.Edges.Where(e => sweep % 2 == 0 ? e.To == node.Node.Id : e.From == node.Node.Id)
                        .Select(e => sweep % 2 == 0 ? e.From : e.To).Where(ranks.ContainsKey).ToArray();
                    return adjacent.Length == 0 ? ranks[node.Node.Id] : adjacent.Average(id => ranks[id]);
                }
                var ordered = column.OrderBy(Score).ThenBy(n => ranks[n.Node.Id]).ToArray();
                column.Clear();
                column.AddRange(ordered);
                Rank(column);
            }
        }
        var height = columns.Max(c => c.Count) * RowStride - (RowStride - CardHeight);
        for (var c = 0; c < columns.Length; c++)
        for (var r = 0; r < columns[c].Count; r++)
        {
            var columnHeight = columns[c].Count * RowStride - (RowStride - CardHeight);
            columns[c][r].Bounds = UIBox2.FromDimensions(new Vector2(c * ColumnStride, (height - columnHeight) / 2 + r * RowStride),
                new Vector2(CardWidth, CardHeight));
        }
        GraphBounds = new UIBox2(-24, -24, (columns.Length - 1) * ColumnStride + CardWidth + 24, height + 24);
        var validEdges = Graph.Edges.Where(e => _nodes.ContainsKey(e.From) && _nodes.ContainsKey(e.To)).ToArray();
        var outgoing = validEdges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.OrderBy(e => _nodes[e.To].Bounds.Center.Y).ToArray());
        var incoming = validEdges.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.OrderBy(e => _nodes[e.From].Bounds.Center.Y).ToArray());
        foreach (var edge in validEdges)
        {
            var from = _nodes[edge.From].Bounds;
            var to = _nodes[edge.To].Bounds;
            var forwards = from.Left < to.Left;
            Vector2 Port(NodeLayout layout, bool output, SWProductionEdge[] edges)
            {
                var bounds = layout.Bounds;
                var right = output == forwards;
                if (layout.Node.Kind is SWProductionNodeKind.Commodity or SWProductionNodeKind.Condition)
                    return bounds.TopLeft + new Vector2(CardWidth / 2 + (right ? 8 : -8), 130);
                var y = 34 + (Array.IndexOf(edges, edge) + 1f) / (edges.Length + 1f) * 98;
                return bounds.TopLeft + new Vector2(layout.Node.Kind == SWProductionNodeKind.Demand ? 16 : right ? CardWidth : 0, y);
            }
            var a = Port(_nodes[edge.From], true, outgoing[edge.From]);
            var b = Port(_nodes[edge.To], false, incoming[edge.To]);
            var reach = Math.Max(52, Math.Abs(b.X - a.X) * .48f);
            var ca = a + new Vector2(forwards ? reach : -reach, 0);
            var cb = b + new Vector2(forwards ? -reach : reach, 0);
            if (Math.Abs(from.Left - to.Left) < 1)
            {
                // SCC/back links travel outside the cards rather than straight through their text.
                ca = new Vector2(from.Right + 72, a.Y);
                cb = new Vector2(to.Right + 72, b.Y);
                GraphBounds = new UIBox2(GraphBounds.Left, GraphBounds.Top, Math.Max(GraphBounds.Right, from.Right + 80), GraphBounds.Bottom);
            }
            var points = new Vector2[33];
            var distances = new float[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var t = i / (float) (points.Length - 1);
                var u = 1 - t;
                points[i] = u * u * u * a + 3 * u * u * t * ca + 3 * u * t * t * cb + t * t * t * b;
                if (i > 0) distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
            _edges.Add(new EdgeLayout(edge, points, distances));
        }
    }

    public IReadOnlyList<SWProductionNodeLayout> GetLayoutSnapshot() => _nodes.Values
        .Select(n => new SWProductionNodeLayout(n.Node, n.Bounds, LocalBounds(n.Bounds))).ToArray();
    public Vector2 GraphToLocal(Vector2 point) => (point - GraphBounds.Center) * Zoom + Size / 2 + Pan;
    public Vector2 LocalToGraph(Vector2 point) => (point - Size / 2 - Pan) / Zoom + GraphBounds.Center;
    private UIBox2 LocalBounds(UIBox2 box) => new(GraphToLocal(box.TopLeft), GraphToLocal(box.BottomRight));
    public Vector2? NodeCenterLocal(string id) => _nodes.TryGetValue(id, out var node) ? GraphToLocal(node.Bounds.Center) : null;

    public void SetAnimation(bool enabled) => _animation = enabled;

    /// <summary>Explicit path endpoints are independent of the node currently inspected.</summary>
    public void SetEndpoints(string? from, string? to)
    {
        if (FromNodeId == from && ToNodeId == to) return;
        FromNodeId = from;
        ToNodeId = to;
        _textZoom = -1;
    }
    public void ResetView()
    {
        _fitView = true;
        _overviewFit = false;
        var fit = Math.Min(Math.Max(1, Width - 48) / Math.Max(1, GraphBounds.Width),
            Math.Max(1, Height - 48) / Math.Max(1, GraphBounds.Height));
        Zoom = Math.Clamp(fit, MinimumZoom, 1);
        Pan = Vector2.Zero;
        if (fit < MinimumZoom && SelectedNodeId != null && _nodes.TryGetValue(SelectedNodeId, out var selected))
            Pan = (GraphBounds.Center - selected.Bounds.Center) * Zoom;
        ViewUpdated();
    }

    /// <summary>Fit the complete topology. Small overview cards intentionally omit unreadable text.</summary>
    public void FitAll()
    {
        _fitView = true;
        _overviewFit = true;
        Zoom = Math.Clamp(Math.Min(Math.Max(1, Width - 48) / Math.Max(1, GraphBounds.Width),
            Math.Max(1, Height - 48) / Math.Max(1, GraphBounds.Height)), .01f, 1);
        Pan = Vector2.Zero;
        ViewUpdated();
    }

    public void FocusSelection()
    {
        if (SelectedNodeId == null || !_nodes.TryGetValue(SelectedNodeId, out var selected)) return;
        _fitView = false;
        Zoom = Math.Max(1, Zoom);
        Pan = (GraphBounds.Center - selected.Bounds.Center) * Zoom;
        ViewUpdated();
    }

    public bool SelectNode(string id, bool focus = false)
    {
        if (!_nodes.ContainsKey(id)) return false;
        SelectedNodeId = id;
        RefreshConnections();
        if (focus) FocusSelection();
        return true;
    }

    public void ChangeZoom(float factor, Vector2? around = null)
    {
        if (!float.IsFinite(factor) || factor <= 0) return;
        _fitView = false;
        var old = Zoom;
        Zoom = Math.Clamp(Zoom * factor, Math.Min(.05f, Zoom), 1.8f);
        var point = (around ?? Size / 2) - Size / 2;
        Pan = point - (point - Pan) * (Zoom / old);
        ClampPan();
        ViewUpdated();
    }

    public void PanBy(Vector2 amount)
    {
        if (!float.IsFinite(amount.X) || !float.IsFinite(amount.Y)) return;
        _fitView = false;
        Pan += amount;
        ClampPan();
        ViewUpdated();
    }

    private void ClampPan()
    {
        var limit = new Vector2(GraphBounds.Width, GraphBounds.Height) * Zoom / 2;
        Pan = Vector2.Clamp(Pan, -limit, limit);
    }

    private void ViewUpdated()
    {
        _lastClick = null;
        UpdateHover();
        ViewChanged?.Invoke();
    }

    protected override void Resized()
    {
        base.Resized();
        if (_fitView)
        {
            if (_overviewFit) FitAll();
            else ResetView();
        }
        else { ClampPan(); ViewUpdated(); }
    }

    public SWProductionNode? HitTestNode(Vector2 local)
    {
        if (!UIBox2.FromDimensions(Vector2.Zero, Size).Contains(local) || MinimapBounds is { } mini && mini.Contains(local)) return null;
        var point = LocalToGraph(local);
        foreach (var node in _nodes.Values)
            if (node.Bounds.Contains(point)) return node.Node;
        return null;
    }

    public void ActivateAt(Vector2 local)
    {
        if (MinimapBounds is { } mini && mini.Contains(local)) { MoveFromMinimap(local); return; }
        if (HitTestNode(local) is not { } node) { _lastClick = null; return; }
        var now = _timing.RealTime;
        var activate = _lastClick == node.Id && now - _lastClickTime <= TimeSpan.FromMilliseconds(_configuration.GetCVar(CVars.DoubleClickDelay)) &&
            Vector2.Distance(local, _lastClickPosition) * UIScale <= _configuration.GetCVar(CVars.DoubleClickRange);
        _lastClickTime = now;
        _lastClickPosition = local;
        _lastClick = activate ? null : node.Id;
        SelectNode(node.Id);
        NodeSelected?.Invoke(node);
        if (activate)
        {
            if (IsOverview) FocusSelection();
            else NodeActivated?.Invoke(node);
        }
    }

    private void RefreshConnections()
    {
        _connected.Clear();
        var focus = HoveredNodeId ?? SelectedNodeId;
        if (focus == null) return;
        _connected.Add(focus);
        foreach (var edge in _edges)
            if (edge.Edge.From == focus || edge.Edge.To == focus)
            { _connected.Add(edge.Edge.From); _connected.Add(edge.Edge.To); }
    }

    private void UpdateHover()
    {
        HoveredNodeId = _pointer is { } p ? HitTestNode(p)?.Id : null;
        _hoveredEdge = null;
        ToolTip = null;
        if (HoveredNodeId is { } id)
        {
            var node = _nodes[id].Node;
            ToolTip = node.Title + (string.IsNullOrWhiteSpace(node.Subtitle) ? "" : "\n" + node.Subtitle);
            if (IsOverview) ToolTip += "\nДвойной щелчок: приблизить";
            else if (node.Kind == SWProductionNodeKind.Commodity) ToolTip += "\nДвойной щелчок: открыть товар";
        }
        else if (_pointer is { } pointer && UIBox2.FromDimensions(Vector2.Zero, Size).Contains(pointer) &&
                 !(MinimapBounds is { } mini && mini.Contains(pointer)))
        {
            var best = 6f;
            foreach (var edge in _edges)
            for (var i = 1; i < edge.Points.Length; i++)
            {
                var a = GraphToLocal(edge.Points[i - 1]);
                var b = GraphToLocal(edge.Points[i]);
                var d = b - a;
                var closest = a + d * Math.Clamp(Vector2.Dot(pointer - a, d) / Math.Max(.001f, d.LengthSquared()), 0, 1);
                var distance = Vector2.Distance(pointer, closest);
                if (distance >= best) continue;
                best = distance;
                _hoveredEdge = edge;
            }
            if (_hoveredEdge is { } hover)
                ToolTip = _nodes[hover.Edge.From].Node.Title + " → " + _nodes[hover.Edge.To].Node.Title +
                    (string.IsNullOrWhiteSpace(hover.Edge.Label) ? "" : "\n" + hover.Edge.Label);
        }
        RefreshConnections();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) return;
        _press = args.RelativePosition;
        _panAtPress = Pan;
        _dragged = false;
        _minimapPress = MinimapBounds is { } mini && mini.Contains(args.RelativePosition);
        if (_minimapPress) MoveFromMinimap(args.RelativePosition);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _press == null) return;
        if (!_dragged && !_minimapPress && Vector2.DistanceSquared(args.RelativePosition, _press.Value) < 25) ActivateAt(args.RelativePosition);
        else _lastClick = null;
        _press = null;
        _minimapPress = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _pointer = args.RelativePosition;
        if (_press is { } press)
        {
            if (_minimapPress) MoveFromMinimap(args.RelativePosition);
            else
            {
                var offset = args.RelativePosition - press;
                _dragged |= offset.LengthSquared() >= 25;
                if (_dragged) { Pan = _panAtPress; PanBy(offset); }
            }
        }
        UpdateHover();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _press = null;
        _minimapPress = false;
        _pointer = null;
        _lastClick = null;
        UpdateHover();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        ChangeZoom(MathF.Pow(1.15f, args.Delta.Y), args.RelativePosition);
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_animation && !_configuration.GetCVar(CCVars.ReducedMotion))
            AnimationSeconds += Math.Clamp(args.DeltaSeconds, 0, .1f);
    }
}
