using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberSwingTrailOverlay : Overlay
{
    private const float TrailDuration = 0.22f;
    private const float FallbackRibbonSweep = 0.30f;
    private const int RibbonSegments = 12;

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly Vector2[] _ribbon = new Vector2[2 * (RibbonSegments + 1)];
    private readonly Vector2[] _thrust = new Vector2[4];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public SWSaberSwingTrailOverlay(IEntityManager entities, SharedTransformSystem transform, IGameTiming timing)
    {
        _entities = entities;
        _transform = transform;
        _timing = timing;
        ZIndex = (int) ContentDrawDepth.Effects;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var now = _timing.RealTime;
        var query = _entities.EntityQueryEnumerator<SWSaberSwingTrailVisualsComponent, TransformComponent>();
        while (query.MoveNext(out _, out var trail, out var xform))
        {
            if (xform.MapID != args.MapId || trail.Reach <= 0)
                continue;

            var progress = (float) (now - trail.StartedAt).TotalSeconds / TrailDuration;
            if (progress >= 1f)
                continue;

            var origin = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Enlarged(trail.Reach).Contains(origin))
                continue;

            var aim = _transform.GetWorldRotation(xform.ParentUid) + trail.Direction;
            var sweep = MathF.Abs((float) trail.Sweep.Theta);
            // A zero-width mesh would make crescent trails disappear.
            var ribbonSweep = sweep < 0.001f ? FallbackRibbonSweep : sweep;
            var fade = Math.Clamp((progress - 0.18f) / 0.82f, 0f, 1f);
            var color = Color.InterpolateBetween(trail.Tint, Color.White, 0.45f).WithAlpha(1f - fade * fade);
            var reach = trail.Reach;

            switch (trail.Shape)
            {
                case SWSaberSwingTrail.Crescent:
                    // Reveal the broad crescent in the direction of the stock blade swing.
                    var reveal = Math.Clamp(0.65f + 2.5f * progress, 0.65f, 1f);
                    DrawRibbon(args.WorldHandle, origin, aim + trail.Sweep * ((1f - reveal) / 2f),
                        ribbonSweep * reveal, reach, reach * 0.26f, color);
                    break;
                case SWSaberSwingTrail.Thrust:
                    DrawThrust(args.WorldHandle, origin, aim, reach, progress, color);
                    break;
                case SWSaberSwingTrail.TwinArcs:
                    DrawRibbon(args.WorldHandle, origin, aim - new Angle(ribbonSweep * 0.08f),
                        ribbonSweep * 0.84f, reach * 0.84f, reach * 0.15f, color);
                    DrawRibbon(args.WorldHandle, origin, aim + new Angle(ribbonSweep * 0.12f),
                        ribbonSweep * 0.72f, reach * 0.55f, reach * 0.13f, color);
                    break;
            }
        }
    }

    private void DrawRibbon(DrawingHandleWorld handle, Vector2 origin, Angle aim, float sweep, float radius, float width, Color color)
    {
        for (var segment = 0; segment <= RibbonSegments; segment++)
        {
            var fraction = (float) segment / RibbonSegments;
            var direction = (aim + new Angle((fraction - 0.5f) * sweep)).ToWorldVec();
            var taper = MathF.Sin(fraction * MathF.PI);
            _ribbon[segment * 2] = origin + direction * radius;
            _ribbon[segment * 2 + 1] = origin + direction * (radius - width * taper);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _ribbon, color);
    }

    private void DrawThrust(DrawingHandleWorld handle, Vector2 origin, Angle aim, float reach, float progress, Color color)
    {
        var forward = aim.ToWorldVec();
        var side = new Vector2(-forward.Y, forward.X);
        var tipDistance = reach * Math.Clamp(0.78f + 2f * progress, 0.78f, 1f);
        var shoulder = origin + forward * (reach * 0.61f);
        var halfWidth = side * (reach * 0.10f);

        _thrust[0] = origin + forward * tipDistance;
        _thrust[1] = shoulder + halfWidth;
        _thrust[2] = origin + forward * (reach * 0.25f);
        _thrust[3] = shoulder - halfWidth;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _thrust, color);
    }
}
