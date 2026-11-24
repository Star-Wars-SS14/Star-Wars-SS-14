using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A line from the first minute to the last, with a tick in the new owner's colour at every change.</summary>
public sealed class SWGlobalWarRoundEndTimeline : Control
{
    private const float TickHeight = 16;
    private static readonly Color Line = Color.FromHex("#4A505C");
    private static readonly Color Republic = Color.FromHex("#4FA3E8");
    private static readonly Color Empire = Color.FromHex("#D0303A");
    private static readonly Color Other = Color.FromHex("#8A91A0");

    public TimeSpan Duration;

    /// <summary>How much of the line shows its ticks, from the start: 0 to 1.</summary>
    public float Reveal = 1;
    public List<SWGlobalWarWarTick> Ticks = new();

    public SWGlobalWarRoundEndTimeline()
    {
        MinHeight = TickHeight;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var middle = PixelHeight / 2f;
        handle.DrawRect(new UIBox2(0, middle - UIScale, PixelWidth, middle + UIScale), Line);
        if (Duration <= TimeSpan.Zero)
            return;

        var half = TickHeight / 2f * UIScale;
        foreach (var tick in Ticks)
        {
            var share = MathF.Min(1, (float) (tick.At / Duration));
            if (share > Reveal)
                continue;
            var x = PixelWidth * share;
            var color = tick.Owner switch
            {
                SWGlobalWarFaction.Republic => Republic,
                SWGlobalWarFaction.Empire => Empire,
                _ => Other,
            };
            handle.DrawRect(new UIBox2(x - UIScale, middle - half, x + UIScale, middle + half), color);
        }
    }
}
