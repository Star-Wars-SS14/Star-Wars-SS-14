using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._SW._Senate.Consoles.Statistics;

/// <summary>A comparable range bar; an optional reference mark denotes the Republic's actual value.</summary>
public sealed class SWStatisticsBar : Control
{
    public float Value { get; set; }
    public float Minimum { get; set; }
    public float Maximum { get; set; } = 100;
    public float? Reference { get; set; }
    public Color Fill { get; set; } = SWConsoleStyle.Accent;

    public SWStatisticsBar()
    {
        MinHeight = 12;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (PixelWidth < 2 * UIScale || PixelHeight <= 4 * UIScale)
            return;
        var box = new UIBox2(0, 2 * UIScale, PixelWidth, PixelHeight - 2 * UIScale);
        handle.DrawRect(box, SWConsoleStyle.PillDim);
        if (SWStatisticsPresentation.Normalize(Value, Minimum, Maximum) is { } value)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + box.Width * value, box.Bottom), Fill);
        if (Reference is { } reference && SWStatisticsPresentation.Normalize(reference, Minimum, Maximum) is { } position)
        {
            var x = Math.Clamp(PixelWidth * position, UIScale, PixelWidth - UIScale);
            handle.DrawRect(new UIBox2(x - UIScale, 0, x + UIScale, PixelHeight), SWConsoleStyle.Brass);
        }
    }
}
