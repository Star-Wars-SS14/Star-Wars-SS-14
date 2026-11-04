using System.Numerics;
using Content.Client._SW.UserInterface;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A thin Senate-style stock or link-occupancy indicator.</summary>
public sealed class SWGlobalWarMarketBar : Control
{
    private static readonly Color Track = Color.FromHex("#14161B");

    public double Value;
    public double Maximum = 1;
    public Color Fill = SWTerminalStyle.AccentColor(SWTerminalStyle.NeutralAccent);

    public SWGlobalWarMarketBar()
    {
        MinHeight = MaxHeight = 4;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var whole = UIBox2.FromDimensions(Vector2.Zero, PixelSize);
        handle.DrawRect(whole, Track);
        var ratio = Maximum > 0 ? (float) Math.Clamp(Value / Maximum, 0, 1) : 0;
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, new Vector2(PixelWidth * ratio, PixelHeight)), Fill);
        handle.DrawRect(whole, SWGlobalWarConsoleStyle.Edge, false);
    }
}
