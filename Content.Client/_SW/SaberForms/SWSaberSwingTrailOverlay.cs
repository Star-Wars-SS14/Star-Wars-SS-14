using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberSwingTrailOverlay : Overlay
{
    internal const float TrailDuration = 0.232f;

    private const float FallbackRibbonSweep = 0.30f;
    private const float PeakOpacity = 0.60f;
    private const int TrailSegments = 16;
    private const int VerticesPerSample = 5;

    private static readonly ushort[] Indices = BuildTrailIndices();

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[(TrailSegments + 1) * VerticesPerSample];

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

            var age = (float) (now - trail.StartedAt).TotalSeconds;
            if (age >= TrailDuration)
                continue;

            var origin = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Enlarged(trail.Reach).Contains(origin))
                continue;

            var aim = _transform.GetWorldRotation(xform.ParentUid) + trail.Direction;
            var halo = Color.FromSrgb(Color.InterpolateBetween(trail.Tint, Color.White, 0.15f));
            var core = Color.FromSrgb(Color.InterpolateBetween(trail.Tint, Color.White, 0.72f));
            var opacity = PeakOpacity * (1f - SmoothProgress(age, 0.079f, TrailDuration - 0.079f));

            if (trail.Shape == SWSaberSwingTrail.Thrust)
                FillThrust(origin, aim, trail.Reach, age, opacity, halo, core);
            else
                FillArc(origin, aim, trail.Sweep, trail.Reach, age, trail.Shape == SWSaberSwingTrail.GuardSweep, opacity, halo, core);

            args.WorldHandle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, Indices, _vertices);
        }
    }

    private void FillArc(Vector2 origin, Angle aim, Angle swing, float reach, float age, bool guard, float opacity,
        Color halo, Color core)
    {
        var signedSweep = (float) swing.Theta;
        if (MathF.Abs(signedSweep) < 0.001f)
            signedSweep = MathF.CopySign(FallbackRibbonSweep, signedSweep);

        var (tail, head) = GetArcPose(age, guard);
        var haloWidth = guard ? 0.122f : 0.110f;
        var tailLight = guard ? 0.32f : 0.46f;

        for (var sample = 0; sample <= TrailSegments; sample++)
        {
            var fraction = (float) sample / TrailSegments;
            var path = tail + (head - tail) * fraction;
            // The stock slash starts at +sweep/2 and finishes at -sweep/2, including left swings.
            var direction = (aim + new Angle(signedSweep * (0.5f - path))).ToWorldVec();
            var bow = MathF.Sin(MathF.PI * path);
            var radius = reach * (guard ? 0.56f + 0.16f * bow : 0.82f + 0.04f * bow);
            var taper = GetEndTaper(fraction);
            var leading = SmoothProgress(fraction, 0.47f, 0.40f);
            var width = MathF.Min(haloWidth * taper * (0.67f + 0.33f * leading), reach - radius);
            var light = opacity * taper * (tailLight + (1f - tailLight) * leading);

            SetSample(sample, origin + direction * radius, direction, width, light, halo, core);
        }
    }

    private void FillThrust(Vector2 origin, Angle aim, float reach, float age, float opacity, Color halo, Color core)
    {
        var (tail, head) = GetThrustPose(age);
        var forward = aim.ToWorldVec();
        var side = new Vector2(-forward.Y, forward.X);

        for (var sample = 0; sample <= TrailSegments; sample++)
        {
            var fraction = (float) sample / TrailSegments;
            var distance = reach * (tail + (head - tail) * fraction);
            var taper = GetEndTaper(fraction);
            var leading = SmoothProgress(fraction, 0.46f, 0.43f);
            var reachLimit = MathF.Sqrt(MathF.Max(0f, reach * reach - distance * distance));
            var width = MathF.Min(0.104f * taper * (0.54f + 0.46f * leading), reachLimit);
            var light = opacity * taper * (0.27f + 0.73f * leading);

            SetSample(sample, origin + forward * distance, side, width, light, halo, core);
        }
    }

    // Tail and head are normalized positions along the sweep, from its starting edge to its finish.
    internal static (float Tail, float Head) GetArcPose(float age, bool guard)
    {
        var windup = guard ? 0.011f : 0.016f;
        var duration = guard ? 0.078f : 0.086f;
        var lag = guard ? 0.047f : 0.059f;
        var head = 0.07f + 0.93f * SmoothProgress(age, windup, duration);
        var tail = SmoothProgress(age, windup + lag, duration)
            * (guard ? 0.83f : 0.70f) * (0.78f + 0.22f * SmoothProgress(age, 0.12f, 0.09f));
        return (tail, head);
    }

    // Tail and head are normalized distances from the fighter along the line of the thrust.
    internal static (float Tail, float Head) GetThrustPose(float age)
    {
        var extend = SmoothProgress(age, 0f, 0.064f);
        var retract = SmoothProgress(age, 0.063f, 0.091f);
        var settle = SmoothProgress(age, 0.129f, 0.096f);
        var head = 0.32f + 0.66f * extend - 0.37f * retract - 0.09f * settle;
        var length = (0.18f + 0.37f * extend) * (1f - 0.55f * retract);
        return (head - length, head);
    }

    private static float SmoothProgress(float value, float start, float duration)
    {
        var t = Math.Clamp((value - start) / duration, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float GetEndTaper(float fraction)
    {
        return SmoothProgress(fraction, 0f, 0.21f) * (1f - SmoothProgress(fraction, 0.83f, 0.17f));
    }

    private void SetSample(int sample, Vector2 center, Vector2 normal, float width, float opacity, Color halo, Color core)
    {
        var vertex = sample * VerticesPerSample;
        var edge = halo.WithAlpha(0f);
        var shoulder = halo.WithAlpha(opacity * 0.55f);
        var offset = normal * width;
        var inner = offset * 0.42f;

        // Shared rails give the bright center and soft edges one alpha gradient, without layered overdraw.
        _vertices[vertex] = new(center - offset, edge);
        _vertices[vertex + 1] = new(center - inner, shoulder);
        _vertices[vertex + 2] = new(center, core.WithAlpha(opacity));
        _vertices[vertex + 3] = new(center + inner, shoulder);
        _vertices[vertex + 4] = new(center + offset, edge);
    }

    private static ushort[] BuildTrailIndices()
    {
        var indices = new ushort[TrailSegments * (VerticesPerSample - 1) * 6];
        var index = 0;
        for (var segment = 0; segment < TrailSegments; segment++)
        {
            for (var lane = 0; lane < VerticesPerSample - 1; lane++)
            {
                var a = segment * VerticesPerSample + lane;
                var b = a + VerticesPerSample;
                indices[index++] = (ushort) a;
                indices[index++] = (ushort) (a + 1);
                indices[index++] = (ushort) b;
                indices[index++] = (ushort) (a + 1);
                indices[index++] = (ushort) (b + 1);
                indices[index++] = (ushort) b;
            }
        }

        return indices;
    }
}
