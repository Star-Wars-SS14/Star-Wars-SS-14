using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._SW.BattlePass;

/// <summary>
/// Square pixels flying out of a point and falling, drawn over the window when a case gives its result.
/// </summary>
public sealed class SWConfetti : Control
{
    private const float Gravity = 520f;
    private const float Drag = 1.6f;
    private const float FadeTime = 0.5f;

    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly List<Piece> _pieces = new();

    public SWConfetti()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>
    /// Throws pieces out of a point in this control, in its own units.
    /// </summary>
    public void Burst(Vector2 origin, int count, float speed, IReadOnlyList<Color> colors)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var force = speed * (0.35f + _random.NextFloat() * 0.65f);
            _pieces.Add(new Piece
            {
                Position = origin,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * force - new Vector2(0, speed * 0.35f),
                Size = 4f + _random.NextFloat() * 5f,
                Flip = _random.NextFloat() * MathF.Tau,
                FlipSpeed = 4f + _random.NextFloat() * 8f,
                Life = 1.1f + _random.NextFloat() * 1.3f,
                Color = _random.Pick(colors),
            });
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var dt = args.DeltaSeconds;
        for (var i = _pieces.Count - 1; i >= 0; i--)
        {
            var piece = _pieces[i];
            piece.Life -= dt;
            if (piece.Life <= 0)
            {
                _pieces.RemoveAt(i);
                continue;
            }

            piece.Velocity *= MathF.Max(0f, 1f - Drag * dt);
            piece.Velocity.Y += Gravity * dt;
            piece.Position += piece.Velocity * dt;
            piece.Flip += piece.FlipSpeed * dt;
            _pieces[i] = piece;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        foreach (var piece in _pieces)
        {
            // Turning in the air: the width comes and goes, and it never gets thinner than one pixel.
            var width = MathF.Max(1f, piece.Size * MathF.Abs(MathF.Cos(piece.Flip)));
            var size = new Vector2(width, piece.Size) * UIScale;
            var corner = (piece.Position - new Vector2(width, piece.Size) / 2) * UIScale;
            var alpha = MathF.Min(1f, piece.Life / FadeTime);
            handle.DrawRect(UIBox2.FromDimensions(corner, size), piece.Color.WithAlpha(alpha));
        }
    }

    private struct Piece
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Size;
        public float Flip;
        public float FlipSpeed;
        public float Life;
        public Color Color;
    }
}
