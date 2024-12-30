using Content.Shared.Weapons.Reflect;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Content.Shared.Weapons.ReflectOnAttack;
public sealed class ReflectOnAttackSystem : EntitySystem
{
    [Dependency] private readonly ReflectSystem _reflectSystem = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _meleeSystem = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ReflectOnAttackComponent, AttemptMeleeEvent>(OnMeleeAttackStarted);
        SubscribeAllEvent<StopAttackEvent>(OnMeleeAttackStopped);
        _s = Logger.GetSawmill("reflect");
        _s.Debug("init");
    }

    private void OnMeleeAttackStopped(StopAttackEvent ev)
    {
        _s.Debug("stopeed");
        if (!TryComp<ReflectComponent>(GetEntity(ev.Weapon), out var reflectComp))
        {
            return;
        }

        reflectComp.Enabled = false;
    }

    private void OnMeleeAttackStarted(Entity<ReflectOnAttackComponent> ent, ref AttemptMeleeEvent args)
    {
        _s.Debug("started");
        if (args.Cancelled)
        {
            return;
        }
        if (!TryComp<ReflectComponent>(ent.Owner, out var reflectComp))
        {
            return;
        }

        reflectComp.Enabled = true;
    }
  
}
