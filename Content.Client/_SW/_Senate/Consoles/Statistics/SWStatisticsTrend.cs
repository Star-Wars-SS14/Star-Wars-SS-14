using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>Fixed-scale time plot: actual state is solid, policy is dashed. Missing observations break the line.</summary>
public sealed class SWStatisticsTrend : Control
{
    private IReadOnlyList<SWStatisticsPlotPoint> _points = Array.Empty<SWStatisticsPlotPoint>();
    private float _min;
    private float _max = 100;
    private Vector2? _pointer;
    private int _hoverIndex = -1;

    public SWStatisticsTrend()
    {
        MinHeight = 205;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    public void SetData(IReadOnlyList<SWStatisticsPlotPoint> points, float min, float max)
    {
        _points = points;
        _min = min;
        _max = max;
        UpdateHover();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _pointer = args.RelativePosition;
        UpdateHover();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _pointer = null;
        UpdateHover();
    }

    private void UpdateHover()
    {
        _hoverIndex = -1;
        ToolTip = null;
        if (_pointer is not { } pointer || _points.Count < 2 || Width <= 48 ||
            pointer.X < 36 || pointer.X > Width - 12 || pointer.Y < 14 || pointer.Y > Height - 27)
            return;
        var minute = _points[0].Minute + (pointer.X - 36) / (Width - 48) * (_points[^1].Minute - _points[0].Minute);
        var distance = float.PositiveInfinity;
        for (var i = 0; i < _points.Count; i++)
        {
            var next = Math.Abs(_points[i].Minute - minute);
            if (next >= distance) continue;
            distance = next;
            _hoverIndex = i;
        }
        var point = _points[_hoverIndex];
        ToolTip = $"{SWStatisticsPresentation.Time(point.Minute)}\nФакт: {SWStatisticsPresentation.Number(point.Actual ?? float.NaN)}\nПолитика: {SWStatisticsPresentation.Number(point.Policy ?? float.NaN)}";
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var scale = UIScale;
        var plot = new UIBox2(36 * scale, 14 * scale, PixelWidth - 12 * scale, PixelHeight - 27 * scale);
        if (plot.Width <= 0 || plot.Height <= 0 || SWStatisticsPresentation.Normalize(_min, _min, _max) == null)
            return;
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            handle.DrawLine(new Vector2(plot.Left, y), new Vector2(plot.Right, y), SWConsoleStyle.Edge);
            handle.DrawString(SWConsoleStyle.Small, new Vector2(0, y - 9 * scale),
                SWStatisticsPresentation.Number(_min + (_max - _min) * i / 4), scale, SWConsoleStyle.TextDim);
        }
        if (_points.Count < 2)
        {
            handle.DrawString(SWConsoleStyle.Small, new Vector2(plot.Left + 12 * scale, plot.Top + 60 * scale),
                "Недостаточно записей", scale, SWConsoleStyle.TextDim);
            return;
        }
        var first = _points[0].Minute;
        var last = _points[^1].Minute;
        if (last <= first)
            return;
        Vector2 Position(int minute, float value) => new(
            plot.Left + (minute - first) / (float)(last - first) * plot.Width,
            plot.Bottom - (SWStatisticsPresentation.Normalize(value, _min, _max) ?? 0) * plot.Height);
        for (var i = 1; i < _points.Count; i++)
        {
            var a = _points[i - 1];
            var b = _points[i];
            if (a.Policy is { } pa && b.Policy is { } pb)
                Dashed(handle, Position(a.Minute, pa), Position(b.Minute, pb), scale);
            if (a.Actual is { } va && b.Actual is { } vb)
            {
                var from = Position(a.Minute, va);
                var to = Position(b.Minute, vb);
                handle.DrawLine(from, to, SWConsoleStyle.Accent);
                handle.DrawLine(from + new Vector2(0, scale), to + new Vector2(0, scale), SWConsoleStyle.Accent);
            }
        }
        if (_hoverIndex >= 0 && _hoverIndex < _points.Count)
        {
            var point = _points[_hoverIndex];
            var x = Position(point.Minute, _min).X;
            handle.DrawLine(new Vector2(x, plot.Top), new Vector2(x, plot.Bottom), SWConsoleStyle.TextDim);
        }
        var startText = SWStatisticsPresentation.Time(first);
        var endText = SWStatisticsPresentation.Time(last);
        handle.DrawString(SWConsoleStyle.Small, new Vector2(plot.Left, plot.Bottom + 8 * scale), startText, scale, SWConsoleStyle.TextDim);
        var endWidth = handle.GetDimensions(SWConsoleStyle.Small, endText, scale).X;
        handle.DrawString(SWConsoleStyle.Small, new Vector2(plot.Right - endWidth, plot.Bottom + 8 * scale), endText, scale, SWConsoleStyle.TextDim);
    }

    private static void Dashed(DrawingHandleScreen handle, Vector2 from, Vector2 to, float scale)
    {
        var length = Vector2.Distance(from, to);
        if (length < 0.01f)
            return;
        var direction = (to - from) / length;
        for (var offset = 0f; offset < length; offset += 10 * scale)
            handle.DrawLine(from + direction * offset, from + direction * Math.Min(length, offset + 6 * scale), SWConsoleStyle.Brass);
    }
}

public sealed class SWStatisticsKey : Control
{
    public bool Dashed { get; set; }
    public SWStatisticsKey()
    {
        MinWidth = 24;
        MinHeight = 12;
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var y = PixelHeight / 2f;
        if (!Dashed)
            handle.DrawRect(new UIBox2(0, y, PixelWidth, y + UIScale * 2), SWConsoleStyle.Accent);
        else
            for (var x = 0f; x < PixelWidth; x += UIScale * 9)
                handle.DrawLine(new Vector2(x, y), new Vector2(Math.Min(PixelWidth, x + UIScale * 5), y), SWConsoleStyle.Brass);
    }
}
