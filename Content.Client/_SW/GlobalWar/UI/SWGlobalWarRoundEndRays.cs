using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Rays behind the winner's banner: three turns of the loop, then a still frame.</summary>
public sealed class SWGlobalWarRoundEndRays : Control
{
    private const int Loops = 3;

    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private float _time;

    public SWGlobalWarFx? Effect;

    /// <summary>The banner's place as a share of this control's size.</summary>
    public Vector2 Center;

    public float ArtScale = 3;

    public SWGlobalWarRoundEndRays()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (Effect == null)
            return;

        var still = _configuration.GetCVar(CCVars.ReducedMotion) || _time >= Effect.Duration * Loops;
        Effect.Draw(handle, PixelSize * Center, ArtScale * UIScale, still ? 0 : _time, loop: true);
    }
}
