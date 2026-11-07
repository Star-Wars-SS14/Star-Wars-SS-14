using Content.Shared._SW.Vehicle;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Sprite;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Beam;
using Content.Shared._SW;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;
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
    
    
    [Dependency] private readonly SWElectrifiedEffectSystem _electrifiedEffect = default!;
    [Dependency] private readonly SWLightningSparksSystem _lightningSparks = default!; 
    
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
                
                // Add an electricity effect for the duration of the stun.
                _electrifiedEffect.AddElectrifiedEffect(args.Target, ent.Comp.StunTimeInSeconds);
                
                // Add persistent sparks for the duration of the stun.
                _lightningSparks.AddLightningSparks(args.Target, ent.Comp.StunTimeInSeconds);
            }
        }

        args.Handled = true;
    }
}