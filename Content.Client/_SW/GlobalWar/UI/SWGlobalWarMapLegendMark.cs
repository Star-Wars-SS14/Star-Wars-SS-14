using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A legend's sample of what the map draws: corner marks, or a dark disc when <see cref="Disc"/> is set.</summary>
public sealed class SWGlobalWarMapLegendMark : Control
{
    public Color Color = Color.White;
    public bool Disc;

    public SWGlobalWarMapLegendMark()
    {
        SetSize = new Vector2(14);
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var center = PixelSize / 2;
        var half = MathF.Min(PixelWidth, PixelHeight) / 2f - UIScale;
        if (Disc)
        {
            handle.DrawCircle(center, half * .85f, Color.ToSrgb(Color));
            return;
        }

        SWGlobalWarMapControl.DrawCornerMarks(handle, center, half, Color, UIScale);
    }
}
