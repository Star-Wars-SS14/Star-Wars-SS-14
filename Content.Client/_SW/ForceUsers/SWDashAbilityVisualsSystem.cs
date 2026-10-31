using Content.Shared._SW.ForceAbilities;

using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._SW.ForceAbilities;

public sealed class SWDashAbilityVisualsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    // A speed threshold below which we consider the character to be standing still
    private const float StandingThreshold = 0.1f;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SWDashAbilityComponent, MobStateComponent>();
        while (query.MoveNext(out var ent, out var comp, out var mobState))
        {
            // 1. If a character is in a critical state or dead, we stop controlling the sprite entirely.
            if (_mobState.IsDead(ent, mobState) || _mobState.IsCritical(ent, mobState))
            {
                if (comp.IsDashing)
                {
                    comp.IsDashing = false;
                    comp.SwayTime = 0f;
                }
                continue;
            }

            // 2. Check whether the character is moving
            bool isMoving = false;
            if (TryComp<PhysicsComponent>(ent, out var physics))
            {
                
                isMoving = physics.LinearVelocity.LengthSquared() > StandingThreshold * StandingThreshold;
            }

            // 3. Sprint logic
            if (comp.IsDashing)
            {
                if (_gameTiming.CurTime >= comp.DashEndTime)
                {
                    // The sprint ended when the timer ran out.
                    comp.IsDashing = false;
                    comp.SwayTime = 0f;
                    _sprite.SetRotation(ent, Angle.Zero);
                }
                else if (isMoving)
                {
                    // Apply swaying ONLY if the character is moving
                    comp.SwayTime += frameTime;
                    float swayAngle = MathF.Sin(comp.SwayTime * comp.SwayFrequency) * comp.SwayAmplitude;
                    _sprite.SetRotation(ent, new Angle(swayAngle));
                }
                else
                {
                    // The character is standing still — reset the sprite to a vertical position
                    _sprite.SetRotation(ent, Angle.Zero);
                    // Do NOT reset SwayTime, so that there is no sudden jump when movement resumes.
                }
            }
            else
            {
                // Sprint is not active — reset the sprite
                if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.Rotation.Theta != 0)
                {
                    _sprite.SetRotation(ent, Angle.Zero);
                }
                comp.SwayTime = 0f;
            }
        }
    }
}