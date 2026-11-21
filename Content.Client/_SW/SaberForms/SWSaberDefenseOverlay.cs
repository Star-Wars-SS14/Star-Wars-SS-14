using System.Numerics;
using Content.Shared._SW.SaberForms;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ContentDrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._SW.SaberForms;

public sealed class SWSaberDefenseOverlay : Overlay
{
    internal const float Lifetime = 0.236f;

    private const int CurlSamples = 11;
    private const int FlashSegments = 12;
    private const int VerticesPerSample = 5;
    private const int MaxBursts = 64;
    private const float ViewCullMargin = 1.13f;
    private static readonly ProtoId<ShaderPrototype> Unshaded = "unshaded";
    private static readonly ushort[] SparkIndices = BuildIndices(3);
    private static readonly ushort[] CurlIndices = BuildIndices(CurlSamples);
    private static readonly Color SparkTint = Color.FromSrgb(new Color(1f, 0.75f, 0.41f));
    private static readonly Color GlintTint = Color.FromSrgb(new Color(1f, 0.91f, 0.69f));
    private static readonly Color CurlTint = Color.FromSrgb(new Color(0.46f, 0.84f, 1f));

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transform;
    private readonly IGameTiming _timing;
    private readonly List<Burst> _bursts = new();
    private readonly DrawVertexUV2DColor[] _sparkVertices = new DrawVertexUV2DColor[3 * VerticesPerSample];
    private readonly DrawVertexUV2DColor[] _curlVertices = new DrawVertexUV2DColor[CurlSamples * VerticesPerSample];
    private readonly DrawVertexUV2DColor[] _flashVertices = new DrawVertexUV2DColor[FlashSegments + 2];

    private ShaderInstance? _unshadedShader;

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
        if (_bursts.Count == 0)
            return;

        var handle = args.WorldHandle;
        var oldShader = handle.GetShader();
        try
        {
            handle.UseShader(_unshadedShader ??= IoCManager.Resolve<IPrototypeManager>().Index(Unshaded).Instance());
            DrawBursts(args);
        }
        finally
        {
            handle.UseShader(oldShader);
        }
    }

    private void DrawBursts(in OverlayDrawArgs args)
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
        var fade = 1f - SmoothProgress((progress - 0.23f) / 0.77f);
        var travel = 1f - (1f - progress) * (1f - progress);
        switch (visual)
        {
            case SWSaberDefenseVisual.SparkFan:
                DrawFan(handle, origin, direction, travel, fade);
                DrawFlash(handle, origin, direction, progress, SparkTint);
                break;
            case SWSaberDefenseVisual.RiposteGlint:
                DrawGlint(handle, origin, direction.ToVec(), progress, travel, fade);
                DrawFlash(handle, origin, direction, progress, GlintTint);
                break;
            case SWSaberDefenseVisual.ReflectionCurl:
                DrawCurl(handle, origin, direction.ToVec(), sourceDirection.ToVec(), progress, travel, fade);
                DrawFlash(handle, origin, direction, progress, CurlTint);
                break;
        }
    }

    private void DrawFan(DrawingHandleWorld handle, Vector2 origin, Angle direction, float travel, float fade)
    {
        for (var spark = 0; spark < 9; spark++)
        {
            var angle = direction + new Angle((spark - 4) * 0.27f + 0.05f * (spark % 3 - 1));
            var ray = angle.ToVec();
            var reach = 0.30f + (0.42f + 0.09f * (spark % 4)) * travel;
            var length = (0.165f + 0.044f * (spark % 3)) * (1f - 0.20f * travel);
            var intensity = (0.76f - 0.09f * (spark % 3)) * fade;
            DrawSpark(handle, origin + ray * (reach - length), origin + ray * reach, 0.064f, intensity, SparkTint);
        }
    }

    private void DrawGlint(DrawingHandleWorld handle, Vector2 origin, Vector2 direction, float progress, float travel, float fade)
    {
        var tangent = Perpendicular(direction);
        var reach = (0.32f + 0.31f * travel) * (1f - 0.28f * progress);
        var width = 0.076f * (1f - 0.39f * progress);
        DrawSpark(handle, origin - direction * reach, origin + direction * reach, width, 0.77f * fade, GlintTint);
        // Keep the crossing ray softer so the white central glint does not become an opaque square.
        DrawSpark(handle, origin - tangent * (reach * 0.64f), origin + tangent * (reach * 0.64f),
            width * 0.82f, 0.44f * fade, GlintTint);

        var tipDistance = 0.48f + 0.31f * travel;
        DrawSpark(handle, origin + direction * (tipDistance - 0.17f), origin + direction * tipDistance,
            0.041f, 0.57f * fade, GlintTint);
    }

    private void DrawCurl(DrawingHandleWorld handle, Vector2 origin, Vector2 direction, Vector2 source, float progress,
        float travel, float fade)
    {
        var side = Perpendicular(direction);
        if (Vector2.Dot(side, source) < -0.02f)
            side = -side;

        var head = 0.43f + 0.57f * travel;
        var tail = 0.41f * SmoothProgress((progress - 0.17f) / 0.83f);
        for (var sample = 0; sample < CurlSamples; sample++)
        {
            var fraction = (float) sample / (CurlSamples - 1);
            var path = tail + (head - tail) * fraction;
            var phase = MathF.PI * path;
            var bow = 0.54f * MathF.Sin(phase) * (1f - path);
            var tangent = direction * 0.76f +
                side * (0.54f * (MathF.PI * MathF.Cos(phase) * (1f - path) - MathF.Sin(phase)));
            var point = origin + direction * (0.76f * path) + side * bow;
            var taper = MathF.Max(0f, MathF.Sin(MathF.PI * fraction));
            SetSample(_curlVertices, sample, point, Perpendicular(tangent.Normalized()), 0.115f * taper,
                0.76f * fade * taper * (0.57f + 0.43f * fraction), CurlTint);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, CurlIndices, _curlVertices);

        var tip = 0.27f + 0.64f * travel;
        DrawSpark(handle, origin + direction * (tip - 0.23f), origin + direction * tip, 0.054f, 0.70f * fade, CurlTint);
    }

    private void DrawFlash(DrawingHandleWorld handle, Vector2 origin, Angle direction, float progress, Color tint)
    {
        const float flashPortion = 0.36f;
        if (progress >= flashPortion)
            return;

        var flashAge = progress / flashPortion;
        var radius = 0.17f + 0.10f * SmoothProgress(flashAge);
        var opacity = 0.77f * (1f - SmoothProgress(flashAge));
        _flashVertices[0] = new(origin, Color.White.WithAlpha(opacity));

        var edge = tint.WithAlpha(0f);
        for (var segment = 0; segment <= FlashSegments; segment++)
        {
            var angle = segment * (MathF.Tau / FlashSegments);
            var ellipse = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.77f);
            _flashVertices[segment + 1] = new(origin + direction.RotateVec(ellipse), edge);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, _flashVertices);
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
        var index = sample * VerticesPerSample;
        var offset = normal * width;
        var edge = tint.WithAlpha(0f);
        var halo = Color.InterpolateBetween(tint, Color.White, 0.38f).WithAlpha(opacity * 0.61f);
        var core = Color.InterpolateBetween(tint, Color.White, 0.88f).WithAlpha(opacity);
        vertices[index] = new(point - offset, edge);
        vertices[index + 1] = new(point - offset * 0.39f, halo);
        vertices[index + 2] = new(point, core);
        vertices[index + 3] = new(point + offset * 0.39f, halo);
        vertices[index + 4] = new(point + offset, edge);
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
        var indices = new ushort[(samples - 1) * (VerticesPerSample - 1) * 6];
        var index = 0;
        for (var sample = 0; sample < samples - 1; sample++)
        {
            for (var lane = 0; lane < VerticesPerSample - 1; lane++)
            {
                var a = sample * VerticesPerSample + lane;
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

    private readonly record struct Burst(EntityCoordinates Contact, SWSaberDefenseVisual Visual, Angle Direction,
        Angle SourceDirection, TimeSpan StartedAt);
}
