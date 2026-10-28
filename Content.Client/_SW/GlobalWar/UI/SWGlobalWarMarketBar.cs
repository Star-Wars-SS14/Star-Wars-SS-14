using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A thin Senate-style stock or link-occupancy indicator.</summary>
public sealed class SWGlobalWarMarketBar : Control
{
    public double Value;
    public double Maximum = 1;
    public Color Fill = SWGlobalWarConsoleStyle.Accent;

    public SWGlobalWarMarketBar()
    {
        MinHeight = MaxHeight = 4;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, PixelSize), SWGlobalWarConsoleStyle.Edge);
        var ratio = Maximum > 0 ? (float) Math.Clamp(Value / Maximum, 0, 1) : 0;
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, new Vector2(PixelWidth * ratio, PixelHeight)), Fill);
    }
}
