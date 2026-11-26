using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// A pulsing white frame and a bouncing arrow over a control of the HUD. It sits in the popup layer and does not take
/// clicks.
/// </summary>
public sealed class SWTutorialHighlightBox : Control
{
    /// <summary>
    /// At most 2 Hz even when urgent, the limit for anything that flashes.
    /// </summary>
    private const float PulseSpeed = 5f;
    private const float UrgentPulseSpeed = 9f;
    private const int ArrowHead = 9;
    private const int ArrowStem = 8;

    private static readonly Color Mark = Color.White;
    private static readonly Color Shade = Color.FromHex("#101010");

    private float _time;

    /// <summary>
    /// 0 normally, 1 when the student has been stuck.
    /// </summary>
    public float Urgency { get; set; }

    /// <summary>
    /// No pulse and no bounce, only size.
    /// </summary>
    public bool ReducedMotion { get; set; }

    /// <summary>
    /// Points in from the left, for a control with text above it that the arrow would cover.
    /// </summary>
    public bool ArrowFromLeft { get; set; }

    public SWTutorialHighlightBox()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void Follow(Control target)
    {
        LayoutContainer.SetPosition(this, target.GlobalPosition);
        SetSize = target.Size;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var speed = PulseSpeed + Urgency * (UrgentPulseSpeed - PulseSpeed);
        var pulse = ReducedMotion ? 0.5f : (MathF.Sin(_time * speed) + 1f) / 2f;
        var size = PixelSize;
        var thick = (3f + Urgency * 2f) * UIScale;
        var grow = (2f + pulse * (3f + Urgency * 3f)) * UIScale;

        DrawFrame(handle, size.X, size.Y, grow + UIScale, thick + 2f * UIScale, Shade);
        DrawFrame(handle, size.X, size.Y, grow, thick, Mark.WithAlpha(0.7f + 0.3f * pulse));

        // Above the frame, pointing down at the control. A control at the top of the screen has no room above it.
        var bounce = pulse * (6f + Urgency * 6f) * UIScale;
        var scale = (1f + Urgency * 0.25f) * UIScale;
        if (ArrowFromLeft)
            DrawArrowSideways(handle, -grow - 4f * UIScale - bounce, size.Y / 2f, scale, -1f);
        else if (GlobalPixelPosition.Y >= grow + 4f * UIScale + bounce + (ArrowHead + ArrowStem) * 2f * scale)
            DrawArrow(handle, size.X / 2f, -grow - 4f * UIScale - bounce, scale);
        else
            DrawArrowSideways(handle, size.X + grow + 4f * UIScale + bounce, size.Y / 2f, scale, 1f);
    }

    private static void DrawFrame(DrawingHandleScreen handle, float width, float height, float grow, float thick, Color color)
    {
        var left = -grow;
        var top = -grow;
        var right = width + grow;
        var bottom = height + grow;

        handle.DrawRect(new UIBox2(left, top, right, top + thick), color);
        handle.DrawRect(new UIBox2(left, bottom - thick, right, bottom), color);
        handle.DrawRect(new UIBox2(left, top, left + thick, bottom), color);
        handle.DrawRect(new UIBox2(right - thick, top, right, bottom), color);
    }

    /// <summary>
    /// A stepped arrow, drawn upwards from its tip at (x, y).
    /// </summary>
    private static void DrawArrow(DrawingHandleScreen handle, float x, float y, float scale)
    {
        var cell = 2f * scale;

        for (var pass = 0; pass < 2; pass++)
        {
            var color = pass == 0 ? Shade : Mark;
            var pad = pass == 0 ? scale * 1.5f : 0f;

            for (var row = 0; row < ArrowHead; row++)
            {
                var half = (row + 1) * cell;
                var rowY = y - row * cell;
                handle.DrawRect(new UIBox2(x - half - pad, rowY - cell - pad, x + half + pad, rowY + pad), color);
            }

            var stemTop = y - (ArrowHead + ArrowStem) * cell;
            handle.DrawRect(new UIBox2(x - cell * 2f - pad, stemTop - pad, x + cell * 2f + pad, y - ArrowHead * cell + pad), color);
        }
    }

    /// <summary>
    /// The same arrow lying down, drawn from its tip at (x, y) rightwards with a side of 1 and leftwards with -1.
    /// </summary>
    private static void DrawArrowSideways(DrawingHandleScreen handle, float x, float y, float scale, float side)
    {
        var cell = 2f * scale;

        for (var pass = 0; pass < 2; pass++)
        {
            var color = pass == 0 ? Shade : Mark;
            var pad = pass == 0 ? scale * 1.5f : 0f;

            for (var row = 0; row < ArrowHead; row++)
            {
                var half = (row + 1) * cell;
                var rowX = x + side * row * cell;
                handle.DrawRect(Box(rowX, rowX + side * cell, y - half, y + half, pad), color);
            }

            var stemFrom = x + side * ArrowHead * cell;
            var stemTo = x + side * (ArrowHead + ArrowStem) * cell;
            handle.DrawRect(Box(stemFrom, stemTo, y - cell * 2f, y + cell * 2f, pad), color);
        }
    }

    private static UIBox2 Box(float x1, float x2, float top, float bottom, float pad)
    {
        return new UIBox2(MathF.Min(x1, x2) - pad, top - pad, MathF.Max(x1, x2) + pad, bottom + pad);
    }
}
