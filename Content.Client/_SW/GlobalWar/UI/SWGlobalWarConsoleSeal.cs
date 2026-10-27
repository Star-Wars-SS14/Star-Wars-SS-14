using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Resolution-independent HoloNet roundel adapted from the Senate console header.</summary>
public sealed class SWGlobalWarConsoleSeal : Control
{
    private readonly Vector2[] _spoke = new Vector2[4];

    public SWGlobalWarConsoleSeal()
    {
        SetSize = new Vector2(44);
        MouseFilter = MouseFilterMode.Ignore;
        VerticalAlignment = VAlignment.Center;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var center = PixelSize / 2;
        var radius = MathF.Min(PixelWidth, PixelHeight) / 2 - UIScale;
        var ink = Color.ToSrgb(SWGlobalWarConsoleStyle.Brass);
        handle.DrawCircle(center, radius, ink);
        handle.DrawCircle(center, radius * 0.83f, Color.ToSrgb(SWGlobalWarConsoleStyle.Navigation));
        handle.DrawCircle(center, radius * 0.34f, ink);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var side = new Vector2(-direction.Y, direction.X) * radius * 0.095f;
            _spoke[0] = center + direction * radius * 0.29f - side;
            _spoke[1] = center + direction * radius * 0.88f - side;
            _spoke[2] = center + direction * radius * 0.88f + side;
            _spoke[3] = center + direction * radius * 0.29f + side;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _spoke, ink);
        }
    }
}
