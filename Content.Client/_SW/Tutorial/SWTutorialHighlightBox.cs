using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._SW.Tutorial;

/// <summary>
/// A white frame over a control of the HUD. It sits in the popup layer and does not take clicks. It pulses once a
/// second and stands still after a few seconds, because whatever keeps moving stops being noticed.
/// </summary>
public sealed class SWTutorialHighlightBox : Control
{
    private const float PulseTime = 5f;
    private const float Thickness = 2f;

    private float _time;

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

        var alpha = _time < PulseTime ? 0.6f + 0.4f * MathF.Cos(_time * MathF.Tau) : 1f;
        var color = Color.White.WithAlpha(alpha);
        var size = PixelSize;
        var t = Thickness * UIScale;

        handle.DrawRect(new UIBox2(0, 0, size.X, t), color);
        handle.DrawRect(new UIBox2(0, size.Y - t, size.X, size.Y), color);
        handle.DrawRect(new UIBox2(0, 0, t, size.Y), color);
        handle.DrawRect(new UIBox2(size.X - t, 0, size.X, size.Y), color);
    }
}
