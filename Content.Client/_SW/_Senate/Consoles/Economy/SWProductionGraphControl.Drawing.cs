using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Economy;

public sealed record SWProductionGraphText(string Text, Vector2 Position, Font Font, float Scale, Color Color);
public sealed record SWProductionGraphEdgeLayout(SWProductionEdge Edge, IReadOnlyList<Vector2> LocalPoints, bool Highlighted);

public sealed partial class SWProductionGraphControl
{
    private readonly Vector2[] _stroke = new Vector2[4];
    private readonly Vector2[] _arrow = new Vector2[3];
    private readonly Vector2[] _sign = new Vector2[6];
    private float _textZoom = -1;
    private float _textUiScale;
    private readonly Dictionary<string, List<TextRun>> _text = new();
    private readonly record struct TextRun(string Text, Vector2 Offset, Font Font, float Scale, bool Title, Color? Tint = null);

    private static Color NodeColor(SWProductionNodeKind kind) => kind switch
    {
        SWProductionNodeKind.Recipe => SWConsoleStyle.Brass,
        SWProductionNodeKind.Demand => SWConsoleStyle.Good,
        SWProductionNodeKind.Condition => SWConsoleStyle.TextDim,
        _ => SWConsoleStyle.Accent,
    };
    private bool Highlighted(EdgeLayout edge)
    {
        var id = HoveredNodeId ?? SelectedNodeId;
        return edge == _hoveredEdge || edge.Edge.From == id || edge.Edge.To == id;
    }
    private Color EdgeColor(EdgeLayout edge)
    {
        if (!Highlighted(edge)) return SWConsoleStyle.Edge;
        return edge.Edge.From == (HoveredNodeId ?? SelectedNodeId) ? SWConsoleStyle.Brass : SWConsoleStyle.Accent;
    }
    private bool Visible(UIBox2 box) => box.Intersects(UIBox2.FromDimensions(new Vector2(-8), Size + new Vector2(16)));

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWConsoleStyle.Navigation);
        if (_nodes.Count == 0 || Width <= 1 || Height <= 1) return;
        EnsureText();
        var pulseCount = 0;
        for (var index = 0; index < _edges.Count; index++)
        {
            var edge = _edges[index];
            var highlighted = Highlighted(edge);
            var color = EdgeColor(edge);
            var anyVisible = false;
            for (var i = 1; i < edge.Points.Length; i++)
            {
                var a = GraphToLocal(edge.Points[i - 1]);
                var b = GraphToLocal(edge.Points[i]);
                if (!Visible(new UIBox2(Vector2.Min(a, b), Vector2.Max(a, b)))) continue;
                anyVisible = true;
                Stroke(handle, a, b, color.WithAlpha(highlighted ? .82f : IsOverview ? .38f : .58f),
                    highlighted ? IsOverview ? 1.2f : 1.6f : IsOverview ? .7f : 1.1f);
            }
            if (!anyVisible) continue;
            var end = GraphToLocal(edge.Points[^1]);
            var beforeEnd = GraphToLocal(edge.Points[^2]);
            Arrow(handle, end - Vector2.Normalize(end - beforeEnd) * (IsOverview ? 2 : 4), end - beforeEnd,
                color.WithAlpha(highlighted ? .95f : .7f), IsOverview ? 2 : 5);
            // Uniform catalogue marks only. Their speed never depends on live units, ships or hours.
            if ((!IsOverview || highlighted) && pulseCount++ < 96 && edge.Length > 1)
            {
                var distance = (float) ((AnimationSeconds * 44 + index * 41) % edge.Length);
                var head = PointAt(edge, distance);
                var tail = PointAt(edge, Math.Max(0, distance - 19 / Zoom));
                var a = GraphToLocal(tail);
                var b = GraphToLocal(head);
                if (Visible(UIBox2.FromDimensions(b - new Vector2(8), new Vector2(16))))
                {
                    if (!IsOverview) Stroke(handle, a, b, color.WithAlpha(highlighted ? .13f : .05f), 7);
                    Stroke(handle, Vector2.Lerp(a, b, .2f), b, color.WithAlpha(highlighted ? .76f : .26f), IsOverview ? 1.3f : 2.4f);
                    Arrow(handle, b, b - a, color.WithAlpha(highlighted ? 1 : .35f), IsOverview ? 2 : 4);
                }
            }
            if (highlighted && Zoom >= .8f && edge.Edge.Label.Length != 0)
            {
                var middle = GraphToLocal(PointAt(edge, edge.Length * .5f));
                var label = edge.DisplayLabel;
                var dimensions = edge.LabelSize;
                var position = middle - dimensions / 2;
                handle.DrawRect(new UIBox2((position - new Vector2(5, 3)) * UIScale,
                    (position + dimensions + new Vector2(5, 3)) * UIScale), SWConsoleStyle.Navigation);
                handle.DrawString(SWConsoleStyle.Small, position * UIScale, label, UIScale * .9f, color);
            }
        }
        foreach (var layout in _nodes.Values)
        {
            var box = LocalBounds(layout.Bounds);
            if (!Visible(box)) continue;
            var node = layout.Node;
            var selected = node.Id == SelectedNodeId;
            var hovered = node.Id == HoveredNodeId;
            var related = _connected.Contains(node.Id);
            DrawNodeSymbol(handle, layout, selected || hovered, related);
            foreach (var run in _text[node.Id])
                handle.DrawString(run.Font, (box.TopLeft + run.Offset) * UIScale, run.Text, run.Scale * UIScale,
                    TextColor(run, node, selected || hovered || related));
        }
        DrawMinimap(handle);
    }

    private Color TextColor(TextRun run, SWProductionNode node, bool related) => run.Tint ??
        (run.Title && node.Kind != SWProductionNodeKind.Condition ? SWConsoleStyle.TextBright : SWConsoleStyle.TextDim);

    private void DrawNodeSymbol(DrawingHandleScreen handle, NodeLayout layout, bool active, bool related)
    {
        var box = LocalBounds(layout.Bounds);
        var color = NodeColor(layout.Node.Kind).WithAlpha(active ? 1 : related ? .8f : .6f);
        Vector2 At(float x, float y) => box.TopLeft + new Vector2(x, y) * Zoom;
        if (layout.Node.Kind == SWProductionNodeKind.Recipe)
        {
            var frame = new UIBox2(At(0, 22), At(CardWidth, 142));
            // Only a process has a technical enclosure. No filled generic cards or frame glow.
            handle.DrawRect(new UIBox2(frame.TopLeft * UIScale, frame.BottomRight * UIScale), SWConsoleStyle.Navigation);
            handle.DrawRect(new UIBox2(frame.TopLeft * UIScale, frame.BottomRight * UIScale),
                active ? color : SWConsoleStyle.Edge, false);
            Stroke(handle, At(0, 34), At(0, 132), color, active ? 2.4f : 1.5f);
            Stroke(handle, At(CardWidth - 13, 142), At(CardWidth, 142), color, 1.2f);
        }
        else if (layout.Node.Kind == SWProductionNodeKind.Demand)
        {
            Stroke(handle, At(16, 22), At(16, 142), color, active ? 2.4f : 1.5f);
            Stroke(handle, At(16, 22), At(28, 22), color, 1.2f);
            Stroke(handle, At(16, 142), At(28, 142), color, 1.2f);
        }
        else
        {
            var point = At(CardWidth / 2, 130);
            var radius = Math.Max(IsOverview ? 2 : 4, (layout.Node.Kind == SWProductionNodeKind.Condition ? 6 : 8) * Zoom);
            var count = layout.Node.Kind == SWProductionNodeKind.Condition ? 4 : 6;
            for (var i = 0; i < count; i++)
            {
                var angle = i * MathF.Tau / count;
                _sign[i] = (point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius) * UIScale;
            }
            // Hide line junctions behind the exact glyph, rather than behind a large rectangle.
            if (count == 4)
            {
                for (var i = 0; i < count; i++) _stroke[i] = _sign[i];
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _stroke, Color.ToSrgb(SWConsoleStyle.Navigation));
            }
            else handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _sign, Color.ToSrgb(SWConsoleStyle.Navigation));
            for (var i = 0; i < count; i++) handle.DrawLine(_sign[i], _sign[(i + 1) % count], color);
            if (active) Stroke(handle, point - new Vector2(radius * .4f, 0), point + new Vector2(radius * .4f, 0), color, 1.6f);
        }
    }

    private void EnsureText()
    {
        if (_textZoom == Zoom && _textUiScale == UIScale) return;
        _textZoom = Zoom;
        _textUiScale = UIScale;
        _text.Clear();
        var fontScale = Math.Clamp(Zoom, IsOverview ? .70f : .85f, 1.2f);
        foreach (var node in _nodes.Values)
        {
            var runs = new List<TextRun>(4);
            var centered = node.Node.Kind is SWProductionNodeKind.Commodity or SWProductionNodeKind.Condition;
            var inset = centered ? 7 : node.Node.Kind == SWProductionNodeKind.Demand ? Math.Max(15, 32 * Zoom) : Math.Max(10, 14 * Zoom);
            // Material labels can use the empty space around their small glyphs.
            var width = Math.Max(28, centered ? ColumnStride * Zoom - 20 : CardWidth * Zoom - inset - 9);
            var firstY = 0f;
            if (Zoom >= .25f)
            {
                var titleFont = node.Node.Kind == SWProductionNodeKind.Condition ? SWConsoleStyle.Body : SWConsoleStyle.Strong;
                var lineHeight = titleFont.GetHeight(UIScale * fontScale) / UIScale + 2;
                var titleLines = IsOverview ? 2 : 3;
                node.Title = WrapTitle(node.Node.Title, titleFont, width, UIScale * fontScale, titleLines);
                var y = centered ? Math.Max(3, 130 * Zoom - node.Title.Length * lineHeight - Math.Max(10, 16 * Zoom))
                    : IsOverview ? Math.Max(2, 80 * Zoom - node.Title.Length * lineHeight / 2) : 32 * Zoom;
                firstY = y;
                foreach (var line in node.Title)
                {
                    var x = centered ? (CardWidth * Zoom - TextWidth(line, titleFont, UIScale * fontScale) / UIScale) / 2 : inset;
                    runs.Add(new TextRun(line, new Vector2(x, y), titleFont, fontScale, true));
                    y += lineHeight;
                }
                if (!centered && !IsOverview && !string.IsNullOrWhiteSpace(node.Node.Subtitle))
                {
                    var height = SWConsoleStyle.Small.GetHeight(UIScale * fontScale * .85f) / UIScale;
                    var availableY = 142 * Zoom - height - 8;
                    if (availableY >= y + 4)
                    {
                        var subtitle = node.Node.Subtitle.Replace('\n', ' ');
                        if (node.Node.Kind == SWProductionNodeKind.Recipe)
                        {
                            var split = node.Node.Subtitle.Split('\n');
                            subtitle = split.Length > 1 ? split[0] + " / " + split[1].Split(',')[0] : split[0];
                        }
                        node.Subtitle = Ellipsize(subtitle, SWConsoleStyle.Small, width, UIScale * fontScale * .85f);
                        runs.Add(new TextRun(node.Subtitle, new Vector2(inset, availableY), SWConsoleStyle.Small, fontScale * .85f, false));
                    }
                }
            }
            if (node.Node.Id == FromNodeId || node.Node.Id == ToNodeId)
            {
                var both = node.Node.Id == FromNodeId && node.Node.Id == ToNodeId;
                var marker = both ? "НАЧАЛО / КОНЕЦ" : node.Node.Id == FromNodeId ? "НАЧАЛО" : "КОНЕЦ";
                const float markerScale = .72f;
                var x = (CardWidth * Zoom - TextWidth(marker, SWConsoleStyle.Small, UIScale * markerScale) / UIScale) / 2;
                var y = Math.Max(-12, firstY - SWConsoleStyle.Small.GetHeight(UIScale * markerScale) / UIScale - 3);
                runs.Add(new TextRun(marker, new Vector2(x, y), SWConsoleStyle.Small, markerScale, false,
                    both ? SWConsoleStyle.Brass : node.Node.Id == FromNodeId ? SWConsoleStyle.Accent : SWConsoleStyle.Good));
            }
            _text[node.Node.Id] = runs;
        }
        foreach (var edge in _edges)
        {
            edge.DisplayLabel = Ellipsize(edge.Edge.Label.Replace('\n', ' '), SWConsoleStyle.Small, 132, UIScale * .9f);
            edge.LabelSize = new Vector2(TextWidth(edge.DisplayLabel, SWConsoleStyle.Small, UIScale * .9f) / UIScale,
                SWConsoleStyle.Small.GetHeight(UIScale * .9f) / UIScale);
        }
    }

    private float TextWidth(string text, Font font, float scale)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes()) width += font.GetCharMetrics(rune, scale)?.Advance ?? 0;
        return width;
    }

    private string Ellipsize(string text, Font font, float width, float scale)
    {
        if (TextWidth(text, font, scale) <= width * UIScale) return text;
        var allowed = width * UIScale - TextWidth("…", font, scale);
        var measured = 0f;
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            measured += font.GetCharMetrics(rune, scale)?.Advance ?? 0;
            if (measured > allowed) break;
            builder.Append(rune);
        }
        return builder.ToString().TrimEnd() + "…";
    }

    private string[] WrapTitle(string title, Font font, float width, float scale, int maxLines)
    {
        title = title.Replace('\n', ' ');
        var lines = new List<string>(maxLines);
        while (title.Length != 0 && lines.Count < maxLines)
        {
            if (TextWidth(title, font, scale) <= width * UIScale)
            {
                lines.Add(title);
                break;
            }
            if (lines.Count == maxLines - 1)
            {
                lines.Add(Ellipsize(title, font, width, scale));
                break;
            }
            var split = 0;
            for (var i = 0; i < title.Length; i++)
            {
                if (title[i] != ' ') continue;
                if (TextWidth(title[..i], font, scale) > width * UIScale) break;
                split = i;
            }
            if (split == 0)
            {
                // A single long word may wrap, but must not consume the following words invisibly.
                foreach (var rune in title.EnumerateRunes())
                {
                    var next = split + rune.Utf16SequenceLength;
                    if (TextWidth(title[..next], font, scale) > width * UIScale) break;
                    split = next;
                }
                if (split == 0) { lines.Add(Ellipsize(title, font, width, scale)); break; }
            }
            lines.Add(title[..split]);
            title = title[split..].TrimStart();
        }
        return lines.ToArray();
    }

    private void Stroke(DrawingHandleScreen handle, Vector2 a, Vector2 b, Color color, float width)
    {
        var delta = b - a;
        if (delta.LengthSquared() < .001f) return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width / 2;
        _stroke[0] = (a - normal) * UIScale;
        _stroke[1] = (a + normal) * UIScale;
        _stroke[2] = (b + normal) * UIScale;
        _stroke[3] = (b - normal) * UIScale;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _stroke, Color.ToSrgb(color));
    }

    private void Arrow(DrawingHandleScreen handle, Vector2 point, Vector2 direction, Color color, float length)
    {
        if (direction.LengthSquared() < .001f) return;
        direction = Vector2.Normalize(direction);
        var normal = new Vector2(-direction.Y, direction.X);
        _arrow[0] = (point + direction * length * .55f) * UIScale;
        _arrow[1] = (point - direction * length + normal * length * .6f) * UIScale;
        _arrow[2] = (point - direction * length - normal * length * .6f) * UIScale;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrow, Color.ToSrgb(color));
    }

    private static Vector2 PointAt(EdgeLayout edge, float distance)
    {
        distance = Math.Clamp(distance, 0, edge.Length);
        var i = 1;
        while (i < edge.Distances.Length - 1 && edge.Distances[i] < distance) i++;
        var previous = edge.Distances[i - 1];
        return Vector2.Lerp(edge.Points[i - 1], edge.Points[i], (distance - previous) / Math.Max(.001f, edge.Distances[i] - previous));
    }

    private (float Scale, Vector2 Origin) MinimapTransform(UIBox2 box)
    {
        var scale = Math.Min((box.Width - 16) / GraphBounds.Width, (box.Height - 16) / GraphBounds.Height);
        return (scale, box.Center - GraphBounds.Center * scale);
    }

    private void MoveFromMinimap(Vector2 point)
    {
        if (MinimapBounds is not { } box) return;
        var (scale, origin) = MinimapTransform(box);
        var target = (Vector2.Clamp(point, box.TopLeft, box.BottomRight) - origin) / scale;
        _fitView = false;
        Pan = (GraphBounds.Center - target) * Zoom;
        ClampPan();
        ViewUpdated();
    }

    private void DrawMinimap(DrawingHandleScreen handle)
    {
        if (MinimapBounds is not { } box) return;
        handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), SWConsoleStyle.Navigation.WithAlpha(.97f));
        handle.DrawRect(new UIBox2(box.TopLeft * UIScale, box.BottomRight * UIScale), SWConsoleStyle.Edge, false);
        var (scale, origin) = MinimapTransform(box);
        foreach (var edge in _edges)
        for (var i = 1; i < edge.Points.Length; i += 4)
            handle.DrawLine((origin + edge.Points[i - 1] * scale) * UIScale,
                (origin + edge.Points[Math.Min(edge.Points.Length - 1, i + 3)] * scale) * UIScale, SWConsoleStyle.Edge.WithAlpha(.65f));
        foreach (var node in _nodes.Values)
            handle.DrawRect(new UIBox2((origin + node.Bounds.TopLeft * scale) * UIScale,
                (origin + node.Bounds.BottomRight * scale) * UIScale),
                NodeColor(node.Node.Kind).WithAlpha(node.Node.Id == SelectedNodeId ? 1 : .6f));
        var top = origin + LocalToGraph(Vector2.Zero) * scale;
        var bottom = origin + LocalToGraph(Size) * scale;
        var viewport = new UIBox2(Vector2.Clamp(top, box.TopLeft + new Vector2(2), box.BottomRight - new Vector2(2)),
            Vector2.Clamp(bottom, box.TopLeft + new Vector2(2), box.BottomRight - new Vector2(2)));
        handle.DrawRect(new UIBox2(viewport.TopLeft * UIScale, viewport.BottomRight * UIScale), SWConsoleStyle.Accent.WithAlpha(.05f));
        handle.DrawRect(new UIBox2(viewport.TopLeft * UIScale, viewport.BottomRight * UIScale), SWConsoleStyle.Accent, false);
    }

    /// <summary>Measured native text for offline preview; positions are local UI units.</summary>
    public IReadOnlyList<SWProductionGraphText> GetTextSnapshot()
    {
        EnsureText();
        var result = new List<SWProductionGraphText>();
        foreach (var node in _nodes.Values)
        {
            var box = LocalBounds(node.Bounds);
            if (!Visible(box)) continue;
            foreach (var run in _text[node.Node.Id])
                result.Add(new SWProductionGraphText(run.Text, box.TopLeft + run.Offset, run.Font, run.Scale,
                    TextColor(run, node.Node, _connected.Contains(node.Node.Id) || node.Node.Id == HoveredNodeId)));
        }
        foreach (var edge in _edges)
            if (Highlighted(edge) && Zoom >= .8f && edge.Edge.Label.Length != 0)
                result.Add(new SWProductionGraphText(edge.DisplayLabel,
                    GraphToLocal(PointAt(edge, edge.Length * .5f)) - edge.LabelSize / 2,
                    SWConsoleStyle.Small, .9f, EdgeColor(edge)));
        return result;
    }

    public IReadOnlyList<SWProductionGraphEdgeLayout> GetEdgeSnapshot() => _edges.Select(e =>
        new SWProductionGraphEdgeLayout(e.Edge, e.Points.Select(GraphToLocal).ToArray(), Highlighted(e))).ToArray();
}
