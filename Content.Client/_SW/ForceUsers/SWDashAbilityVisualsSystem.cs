using Content.Shared._SW.ForceAbilities;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._SW.ForceAbilities;

public sealed class SWDashAbilityVisualsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

        public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SWDashAbilityComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.IsDashing)
            {
                if (_gameTiming.CurTime >= comp.DashEndTime)
                {
                    // Dash has ended — resetting the sprite
                    _sprite.SetRotation(ent, Angle.Zero);
                    comp.SwayTime = 0f;
                }
                else
                {
                    // We apply a swaying effect to the sprite.
                    comp.SwayTime += frameTime;
                    float swayAngle = MathF.Sin(comp.SwayTime * comp.SwayFrequency) * comp.SwayAmplitude;
                    _sprite.SetRotation(ent, new Angle(swayAngle));
                }
            }
            else
            {
                // If it’s not dashing, but the sprite is rotated — reset it.
                if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.Rotation.Theta != 0)
                {
                    _sprite.SetRotation(ent, Angle.Zero);
                }
                comp.SwayTime = 0f;
            }
        }
    }
}