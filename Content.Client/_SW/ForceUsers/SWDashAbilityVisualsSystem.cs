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

    // Порог скорости, ниже которого считаем, что персонаж стоит на месте
    private const float StandingThreshold = 0.1f;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SWDashAbilityComponent, MobStateComponent>();
        while (query.MoveNext(out var ent, out var comp, out var mobState))
        {
            // 1. Если персонаж в крите или мёртв — полностью перестаём управлять спрайтом
            if (_mobState.IsDead(ent, mobState) || _mobState.IsCritical(ent, mobState))
            {
                if (comp.IsDashing)
                {
                    comp.IsDashing = false;
                    comp.SwayTime = 0f;
                }
                continue;
            }

            // 2. Проверяем, движется ли персонаж
            bool isMoving = false;
            if (TryComp<PhysicsComponent>(ent, out var physics))
            {
                // Используем LengthSquared, чтобы избежать дорогого sqrt
                isMoving = physics.LinearVelocity.LengthSquared() > StandingThreshold * StandingThreshold;
            }

            // 3. Логика спринта
            if (comp.IsDashing)
            {
                if (_gameTiming.CurTime >= comp.DashEndTime)
                {
                    // Спринт закончился по таймеру
                    comp.IsDashing = false;
                    comp.SwayTime = 0f;
                    _sprite.SetRotation(ent, Angle.Zero);
                }
                else if (isMoving)
                {
                    // Применяем покачивание ТОЛЬКО если персонаж движется
                    comp.SwayTime += frameTime;
                    float swayAngle = MathF.Sin(comp.SwayTime * comp.SwayFrequency) * comp.SwayAmplitude;
                    _sprite.SetRotation(ent, new Angle(swayAngle));
                }
                else
                {
                    // Персонаж стоит на месте — сбрасываем спрайт в вертикальное положение
                    _sprite.SetRotation(ent, Angle.Zero);
                    // НЕ сбрасываем SwayTime, чтобы при возобновлении движения не было скачка
                }
            }
            else
            {
                // Спринт не активен — сбрасываем спрайт
                if (TryComp<SpriteComponent>(ent, out var sprite) && sprite.Rotation.Theta != 0)
                {
                    _sprite.SetRotation(ent, Angle.Zero);
                }
                comp.SwayTime = 0f;
            }
        }
    }
}