using Content.Server.Actions;
using Content.Server.Stunnable;
using Content.Shared.Actions;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Content.Shared._SW;

namespace Content.Server._SW;

public sealed class SWPushAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWPushAbilityComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<SWPushAbilityComponent, ForceTryPushAbilityEvent>(OnTryPush);
    }

    private void OnTryPush(Entity<SWPushAbilityComponent> ent, ref ForceTryPushAbilityEvent args)
    {
        if (_tag.HasTag(args.Target, "ForceUser"))
        {
            return;
        }

        
        var userTransform = EntityManager.GetComponent<TransformComponent>(ent.Owner);
        var victimTransform = EntityManager.GetComponent<TransformComponent>(args.Target);
        
        // Direction from the Jedi to the victim
        var direction = (victimTransform.WorldPosition - userTransform.WorldPosition).Normalized();
        
        // Calculating flight time
        var flightTime = ent.Comp.PushDistance / ent.Comp.PushSpeed;
        
        // 1. Spawn the initial effect at the victim's position.
        var initialEffect = EntityManager.SpawnEntity("EffectChronoField", victimTransform.Coordinates);
        
        //  We remove the initial effect after 0.5 seconds
        Timer.Spawn(TimeSpan.FromSeconds(0.5), () =>
        {
            if (initialEffect.IsValid())
                EntityManager.DeleteEntity(initialEffect);
        });
        
        // 2. Apply velocity to the victim (knockback)
        if (TryComp<PhysicsComponent>(args.Target, out var physics))
        {
            var velocity = direction * ent.Comp.PushSpeed;
            _physicsSystem.SetLinearVelocity(args.Target, velocity, body: physics);
            _physicsSystem.SetBodyStatus(args.Target, physics, BodyStatus.InAir);
        }
        
        // Saving data for use in timers
        var target = args.Target;
        var mapId = victimTransform.MapID;
        var knockedDownDuration = ent.Comp.KnockedDownDuration;
        
        // Spawn smoke effects along the trajectory
        SpawnTrailEffects(victimTransform.WorldPosition, direction, ent.Comp.PushDistance, ent.Comp.TrailInterval, ent.Comp.PushSpeed, mapId);
        
        // 4. The victim falls and becomes stunned at the end of the flight
        Timer.Spawn(TimeSpan.FromSeconds(flightTime), () =>
        {
            if (!target.IsValid())
                return;
                
            // Stopping the victim
            if (TryComp<PhysicsComponent>(target, out var phys))
            {
                _physicsSystem.SetLinearVelocity(target, Vector2.Zero, body: phys);
                _physicsSystem.SetBodyStatus(target, phys, BodyStatus.OnGround);
            }
            
            // Adding a stan
            _stun.TryAddParalyzeDuration(target, TimeSpan.FromSeconds(knockedDownDuration));
            
            // 5. Спавним эффект пыли в точке приземления
            var landTransform = EntityManager.GetComponent<TransformComponent>(target);
            var finalFog = EntityManager.SpawnEntity("EffectFog", landTransform.Coordinates);
            
            //  remove the final dust effect after 1.0 second
            Timer.Spawn(TimeSpan.FromSeconds(1.0), () =>
            {
                if (finalFog.IsValid())
                    EntityManager.DeleteEntity(finalFog);
            });
        });
        
        args.Handled = true;
    }

    private void SpawnTrailEffects(Vector2 startPosition, Vector2 direction, float distance, float interval, float speed, MapId mapId)
    {
        var steps = (int)(distance / (speed * interval));
        steps = Math.Max(1, steps); 
        
        for (int i = 1; i <= steps; i++)
        {
            var delay = i * interval;
            var position = startPosition + direction * (speed * delay);
            
            Timer.Spawn(TimeSpan.FromSeconds(delay), () =>
            {
                var coords = new MapCoordinates(position, mapId);
                var effect = EntityManager.SpawnEntity("EffectFog", coords);
                
                // Remove the effect after 0.2 seconds
                Timer.Spawn(TimeSpan.FromSeconds(0.2), () =>
                {
                    if (effect.IsValid())
                        EntityManager.DeleteEntity(effect);
                });
            });
        }
    }

    private void OnCompInit(Entity<SWPushAbilityComponent> ent, ref ComponentInit args)
    {
        _actionsSystem.AddAction(ent.Owner, ent.Comp.ActionPrototype);
    }
}