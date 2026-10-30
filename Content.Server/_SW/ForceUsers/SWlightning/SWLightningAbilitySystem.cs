using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Beam;
using Content.Shared.Actions;
using Content.Shared._SW;
using Robust.Shared.Prototypes;
using Content.Server.Stunnable;
using Content.Server.Actions;
using Content.Shared.Damage.Systems;

namespace Content.Server._SW;

public sealed class SWLightningAbilitySystem : EntitySystem
{
    [Dependency] private readonly BeamSystem _beam = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly StunSystem _stunSystem = default!;
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWLightningAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWLightningAbilityComponent, ForceLightningAbilityEvent>(OnLightning);        
    }

    private void OnCompInit(Entity<SWLightningAbilityComponent> ent, ref ComponentInit args)
    {
        _actionsSystem.AddAction(ent.Owner, ent.Comp.LigtningActionPrototype);
    }

    private void OnLightning(Entity<SWLightningAbilityComponent> ent, ref ForceLightningAbilityEvent args)
{
    var user = args.Performer;
    var lightningAbility = ent.Comp;

    for (var i = 0; i < lightningAbility.BoltCount; i++)
    {
        _beam.TryCreateBeam(user,
            args.Target,
            "BlueSWLightning");
        
        // Spawn a sparks effect on the victim
        var victimTransform = EntityManager.GetComponent<TransformComponent>(args.Target);
        EntityManager.SpawnEntity("EffectSparks", victimTransform.Coordinates);
        
        var ev = new HitByLightningEvent();
        RaiseLocalEvent(args.Target, ev);
        if (!ev.Cancelled)
        {
            _damageable.TryChangeDamage(args.Target, ent.Comp.Damage, true);
            _stunSystem.TryAddStunDuration(args.Target, TimeSpan.FromSeconds(ent.Comp.StunTimeInSeconds), false);
        }
    }

    args.Handled = true;
}
}
