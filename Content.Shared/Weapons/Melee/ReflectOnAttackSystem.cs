using Content.Shared.Weapons.Reflect;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared.Weapons.ReflectOnAttack;
public sealed class ReflectOnAttackSystem : EntitySystem
{
    [Dependency] private readonly ReflectSystem _reflectSystem = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _meleeSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ReflectOnAttackComponent, MeleeAttackEvent>(OnMeleeAttackStarted);
        SubscribeLocalEvent<ReflectOnAttackComponent, StopAttackEvent>(OnMeleeAttackStopped);
    }

    private void OnMeleeAttackStarted(Entity<ReflectOnAttackComponent> ent, ref MeleeAttackEvent args)
    {
        if (!TryComp<ReflectComponent>(args.Weapon, out var reflectComp))
        {
            return;
        }

        reflectComp.Enabled = true;
    }
    private void OnMeleeAttackStopped(Entity<ReflectOnAttackComponent> ent, ref StopAttackEvent args)
    {
        if (!TryComp<ReflectComponent>(GetEntity(args.Weapon), out var reflectComp))
        {
            return;
        }

        reflectComp.Enabled = false;
    }
}
