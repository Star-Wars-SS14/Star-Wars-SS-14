using System.Numerics;
using Content.Shared._SW.SaberForms;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberDefenseOverlay : Overlay
{
    internal const float Lifetime = 0.205f;

    private const int CurlSamples = 11;
    private const int MaxBursts = 64;
    private const float ViewCullMargin = 0.9f;
    private static readonly ushort[] SparkIndices = BuildIndices(3);
    private static readonly ushort[] CurlIndices = BuildIndices(CurlSamples);
    private static readonly Color SparkTint = Color.FromSrgb(new Color(1f, 0.75f, 0.41f));
    private static readonly Color GlintTint = Color.FromSrgb(new Color(1f, 0.91f, 0.69f));
    private static readonly Color CurlTint = Color.FromSrgb(new Color(0.46f, 0.84f, 1f));

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly List<Burst> _bursts = new();
    private readonly DrawVertexUV2DColor[] _sparkVertices = new DrawVertexUV2DColor[9];
    private readonly DrawVertexUV2DColor[] _curlVertices = new DrawVertexUV2DColor[CurlSamples * 3];

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public SWSaberDefenseOverlay(IEntityManager entities, SharedTransformSystem transform, IGameTiming timing)
    {
        _entities = entities;
        _transform = transform;
        _timing = timing;
        ZIndex = (int) ContentDrawDepth.Effects;
    }

    public void Add(EntityCoordinates contact, SWSaberDefenseVisual visual, Angle direction, Angle sourceDirection)
    {
        if (_bursts.Count >= MaxBursts)
            _bursts.RemoveAt(0);

        _bursts.Add(new Burst(contact, visual, direction, sourceDirection, _timing.RealTime));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var now = _timing.RealTime;
        for (var index = _bursts.Count - 1; index >= 0; index--)
        {
            if ((now - _bursts[index].StartedAt).TotalSeconds >= Lifetime)
                _bursts.RemoveAt(index);
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var now = _timing.RealTime;
        var viewport = args.WorldAABB.Enlarged(ViewCullMargin);
        foreach (var burst in _bursts)
        {
            var progress = (float) (now - burst.StartedAt).TotalSeconds / Lifetime;
            if (progress >= 1f ||
                !_entities.TryGetComponent<TransformComponent>(burst.Contact.EntityId, out var anchor) ||
                anchor.MapID != args.MapId)
            {
                continue;
            }

            var origin = _transform.ToMapCoordinates(burst.Contact).Position;
            if (!viewport.Contains(origin))
                continue;

            var rotation = _transform.GetWorldRotation(anchor);
            DrawBurst(args.WorldHandle, burst.Visual, origin, rotation + burst.Direction,
                rotation + burst.SourceDirection, progress);
        }
    }

    internal void DrawBurst(DrawingHandleWorld handle, SWSaberDefenseVisual visual, Vector2 origin, Angle direction,
        Angle sourceDirection, float progress)
    {
        var fade = 1f - SmoothProgress((progress - 0.16f) / 0.84f);
        var travel = 1f - (1f - progress) * (1f - progress);
        switch (visual)
        {
            case SWSaberDefenseVisual.SparkFan:
                DrawFan(handle, origin, direction, travel, fade);
                break;
            case SWSaberDefenseVisual.RiposteGlint:
                DrawGlint(handle, origin, direction.ToVec(), progress, travel, fade);
                break;
            case SWSaberDefenseVisual.ReflectionCurl:
                DrawCurl(handle, origin, direction.ToVec(), sourceDirection.ToVec(), progress, travel, fade);
                break;
        }
    }

    private void DrawFan(DrawingHandleWorld handle, Vector2 origin, Angle direction, float travel, float fade)
    {
        var forward = direction.ToVec();
        DrawSpark(handle, origin - forward * 0.08f, origin + forward * 0.17f, 0.06f, 0.67f * fade, SparkTint);

        for (var spark = 0; spark < 9; spark++)
        {
            var angle = direction + new Angle((spark - 4) * 0.27f + 0.05f * (spark % 3 - 1));
            var ray = angle.ToVec();
            var reach = 0.23f + (0.32f + 0.09f * (spark % 4)) * travel;
            var length = (0.12f + 0.035f * (spark % 3)) * (1f - 0.26f * travel);
            var intensity = (0.64f - 0.08f * (spark % 3)) * fade;
            DrawSpark(handle, origin + ray * (reach - length), origin + ray * reach, 0.043f, intensity, SparkTint);
        }
    }

    private void DrawGlint(DrawingHandleWorld handle, Vector2 origin, Vector2 direction, float progress, float travel, float fade)
    {
        var tangent = Perpendicular(direction);
        var reach = (0.26f + 0.22f * travel) * (1f - 0.34f * progress);
        var width = 0.052f * (1f - 0.45f * progress);
        DrawSpark(handle, origin - direction * reach, origin + direction * reach, width, 0.69f * fade, GlintTint);
        // Keep the crossing ray softer so the white central glint does not become an opaque square.
        DrawSpark(handle, origin - tangent * (reach * 0.58f), origin + tangent * (reach * 0.58f),
            width * 0.79f, 0.36f * fade, GlintTint);

        var tipDistance = 0.39f + 0.22f * travel;
        DrawSpark(handle, origin + direction * (tipDistance - 0.12f), origin + direction * tipDistance,
            0.025f, 0.47f * fade, GlintTint);
    }

    private void DrawCurl(DrawingHandleWorld handle, Vector2 origin, Vector2 direction, Vector2 source, float progress,
        float travel, float fade)
    {
        var side = Perpendicular(direction);
        if (Vector2.Dot(side, source) < -0.02f)
            side = -side;

        var head = 0.37f + 0.63f * travel;
        var tail = 0.49f * SmoothProgress((progress - 0.14f) / 0.86f);
        for (var sample = 0; sample < CurlSamples; sample++)
        {
            var fraction = (float) sample / (CurlSamples - 1);
            var path = tail + (head - tail) * fraction;
            var phase = MathF.PI * path;
            var bow = 0.54f * MathF.Sin(phase) * (1f - path);
            var tangent = direction * 0.76f +
                side * (0.54f * (MathF.PI * MathF.Cos(phase) * (1f - path) - MathF.Sin(phase)));
            var point = origin + direction * (0.76f * path) + side * bow;
            var taper = MathF.Sin(MathF.PI * fraction);
            SetSample(_curlVertices, sample, point, Perpendicular(tangent.Normalized()), 0.079f * taper,
                0.64f * fade * taper * (0.48f + 0.52f * fraction), CurlTint);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, CurlIndices, _curlVertices);

        var tip = 0.21f + 0.61f * travel;
        DrawSpark(handle, origin + direction * (tip - 0.17f), origin + direction * tip, 0.037f, 0.59f * fade, CurlTint);
        DrawSpark(handle, origin - side * 0.10f, origin + side * 0.115f, 0.047f, 0.54f * fade * (1f - progress), CurlTint);
    }

    private void DrawSpark(DrawingHandleWorld handle, Vector2 tail, Vector2 tip, float width, float opacity, Color tint)
    {
        var normal = Perpendicular((tip - tail).Normalized());
        SetSample(_sparkVertices, 0, tail, normal, 0f, 0f, tint);
        SetSample(_sparkVertices, 1, Vector2.Lerp(tail, tip, 0.72f), normal, width, opacity, tint);
        SetSample(_sparkVertices, 2, tip, normal, 0f, opacity * 0.12f, tint);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, SparkIndices, _sparkVertices);
    }

    private static void SetSample(DrawVertexUV2DColor[] vertices, int sample, Vector2 point, Vector2 normal,
        float width, float opacity, Color tint)
    {
        var index = sample * 3;
        var offset = normal * width;
        var edge = tint.WithAlpha(0f);
        var core = Color.InterpolateBetween(tint, Color.White, 0.58f).WithAlpha(opacity);
        vertices[index] = new(point - offset, edge);
        vertices[index + 1] = new(point, core);
        vertices[index + 2] = new(point + offset, edge);
    }

    private static Vector2 Perpendicular(Vector2 direction)
    {
        return new Vector2(-direction.Y, direction.X);
    }

    private static float SmoothProgress(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static ushort[] BuildIndices(int samples)
    {
        var indices = new ushort[(samples - 1) * 12];
        var index = 0;
        for (var sample = 0; sample < samples - 1; sample++)
        {
            for (var lane = 0; lane < 2; lane++)
            {
                var a = sample * 3 + lane;
                var b = a + 3;
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

    private readonly record struct Burst(EntityCoordinates Contact, SWSaberDefenseVisual Visual, Angle Direction,
        Angle SourceDirection, TimeSpan StartedAt);
}
