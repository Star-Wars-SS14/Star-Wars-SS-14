using System.Numerics;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>A local outline radar. It receives silhouettes only, never rooms or entities inside enemy hulls.</summary>
public sealed class SWGlobalWarGunneryRadar : Control
{
    public Vector2 Center;
    public Vector2? Target;
    public SWGlobalWarRadarHull[] Hulls = [];
    public SWGlobalWarGunView[] Guns = [];
    public event Action<Vector2>? TargetChanged;
    private static readonly Color Scope = Color.FromHex("#061012");
    private static readonly Color Ring = Color.FromHex("#46BEA0").WithAlpha(.55f);
    private static readonly Color Grid = Color.FromHex("#46BEA0").WithAlpha(.13f);
    private static readonly Color Beam = Color.FromHex("#5AE6BE");
    private const int SweepSteps = 24;
    private const float SweepArc = 1.0f;
    private const float SweepSeconds = 4;
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private readonly Vector2[] _triangle = new Vector2[3];
    private float Scale => Math.Max(1, Math.Min(PixelWidth, PixelHeight)) / 144f;
    private Vector2 Middle => new(PixelWidth / 2f, PixelHeight / 2f);

    public SWGlobalWarGunneryRadar()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        MinSize = new Vector2(280, 280);
    }

    private Vector2 Screen(Vector2 point)
    {
        var delta = (point - Center) * Scale;
        return Middle + new Vector2(delta.X, -delta.Y);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick) return;
        var delta = (args.RelativePixelPosition - Middle) / Scale;
        Target = Center + new Vector2(delta.X, -delta.Y);
        TargetChanged?.Invoke(Target.Value);
        args.Handle();
    }

    /// <summary>The beam of the scope, fading behind itself. With reduced motion it stands still.</summary>
    private void DrawSweep(DrawingHandleScreen handle, float radius)
    {
        var moving = !_configuration.GetCVar(CCVars.ReducedMotion);
        var head = moving ? (float) _timing.RealTime.TotalSeconds / SweepSeconds * MathF.Tau : -1f;
        for (var i = 0; i < SweepSteps; i++)
        {
            var from = head - SweepArc * i / SweepSteps;
            var to = head - SweepArc * (i + 1) / SweepSteps;
            var a = Middle + new Vector2(MathF.Cos(from), MathF.Sin(from)) * radius;
            var b = Middle + new Vector2(MathF.Cos(to), MathF.Sin(to)) * radius;
            _triangle[0] = Middle;
            _triangle[1] = a;
            _triangle[2] = b;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _triangle, Beam.WithAlpha(.28f * (1f - (float) i / SweepSteps)));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(new UIBox2(Vector2.Zero, new Vector2(PixelWidth, PixelHeight)), Scope);
        var cell = 16 * Scale;
        for (var x = Middle.X % cell; x < PixelWidth; x += cell)
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, PixelHeight), Grid);
        for (var y = Middle.Y % cell; y < PixelHeight; y += cell)
            handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), Grid);
        DrawSweep(handle, 64 * Scale);
        handle.DrawCircle(Middle, 64 * Scale, Ring, false);
        handle.DrawCircle(Middle, 32 * Scale, Ring, false);
        handle.DrawLine(new Vector2(Middle.X, 0), new Vector2(Middle.X, PixelHeight), Ring);
        handle.DrawLine(new Vector2(0, Middle.Y), new Vector2(PixelWidth, Middle.Y), Ring);
        foreach (var hull in Hulls)
        {
            var color = hull.Self ? Color.White : hull.Friendly ? SWGlobalWarConsoleStyle.Good : SWGlobalWarConsoleStyle.Bad;
            for (var i = 0; i < hull.Corners.Length; i++)
                handle.DrawLine(Screen(hull.Corners[i]), Screen(hull.Corners[(i + 1) % hull.Corners.Length]), color);
        }
        foreach (var gun in Guns)
        {
            var position = Screen(gun.Position);
            handle.DrawCircle(position, 3, gun.Mine ? SWGlobalWarConsoleStyle.TextBright : gun.Occupied ? SWGlobalWarConsoleStyle.Bad : SWGlobalWarConsoleStyle.TextDim);
            if (!gun.Mine) continue;
            foreach (var offset in new[] { -gun.Arc / 2, gun.Arc / 2 })
            {
                var angle = (gun.Bearing - 90 + offset) * MathF.PI / 180;
                var end = gun.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 64;
                handle.DrawLine(position, Screen(end), SWGlobalWarConsoleStyle.TextDim);
            }
        }
        if (Target is { } target)
        {
            var point = Screen(target);
            handle.DrawLine(point - new Vector2(6, 0), point + new Vector2(6, 0), SWGlobalWarConsoleStyle.Brass);
            handle.DrawLine(point - new Vector2(0, 6), point + new Vector2(0, 6), SWGlobalWarConsoleStyle.Brass);
        }
    }
}
