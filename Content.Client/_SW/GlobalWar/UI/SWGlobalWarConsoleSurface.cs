using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A small cut corner gives primary controls a datapad silhouette without framing every label.</summary>
public sealed class SWGlobalWarConsoleSurface : StyleBox
{
    private readonly Vector2[] _shape = new Vector2[5];
    public Color Background { get; init; }
    public Color Accent { get; init; }
    public bool CutCorner { get; init; }
    public bool Selected { get; init; }

    protected override float GetDefaultContentMargin(Margin margin) => 0;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var cut = CutCorner ? MathF.Min(7 * uiScale, box.Height / 3) : 0;
        _shape[0] = new(box.Left, box.Top);
        _shape[1] = new(box.Right - cut, box.Top);
        _shape[2] = new(box.Right, box.Top + cut);
        _shape[3] = new(box.Right, box.Bottom);
        _shape[4] = new(box.Left, box.Bottom);
        // The primitive API converts sRGB vertex colors; flat UI rectangles use the supplied color directly.
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _shape, Color.ToSrgb(Background));
        if (Selected)
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - 2 * uiScale, box.Right, box.Bottom), Accent);
    }
}
