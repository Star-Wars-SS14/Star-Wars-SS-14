using Content.Server.Actions;
using Content.Server.Lightning;
using Content.Shared._SW;
using Robust.Shared.Timing;

// Namespace for the Star Wars abilities in the server
namespace Content.Server._SW.Abilities
{
    // LightningAbilitySystem is a system that handles the lightning ability for entities
    public sealed class LightningAbilitySystem : EntitySystem
    {
        [Dependency] private readonly LightningSystem _lightning = default!;
        [Dependency] private readonly ActionsSystem _action = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        // The ID for the force lightning ability
        private const string ForceLightningAbilityId = "ActionSithForceLightning";

        // Initialize the system and subscribe to the necessary events
        public override void Initialize()
        {
            SubscribeLocalEvent<LightningAbilityComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<DashComponent, ForceLightningAbilityEvent>(Force_lightning_Ability);
        }

        // Event handler for the MapInit event
        private void OnMapInit(Entity<LightningAbilityComponent> ent, ref MapInitEvent args)
        {
            // Add the force lightning ability to the action system
            _action.AddAction(ent, ref ent.Comp.Action, ForceLightningAbilityId);
        }

        // Event handler for the ForceLightningAbility event
        private void Force_lightning_Ability(Entity<DashComponent> ent, ref ForceLightningAbilityEvent args)
        {
            var user = args.Performer;
            var lightningAbility = EntityManager.GetComponent<LightningAbilityComponent>(user);

            // If the user already has a LightningEventComponent, do nothing
            if (EntityManager.HasComponent<LightningEventComponent>(user))
                return;

            // Shoot random lightnings from the user
            _lightning.ShootRandomLightnings(user, lightningAbility.LightningRange, lightningAbility.BoltCount, arcDepth: lightningAbility.ArcDepth);

            // Add a LightningEventComponent to the user with the cooldown time for the lightning
            var cooldown = _timing.CurTime + TimeSpan.FromSeconds(lightningAbility.Cooldown);
            EntityManager.AddComponent(user, new LightningEventComponent { LightningEndCooldown = cooldown });
        }

        // Update the system each frame
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // Iterate over all entities with a LightningEventComponent
            var query = EntityQueryEnumerator<LightningEventComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                // If the lightning cooldown has passed, remove the LightningEventComponent from the user
                if (_timing.CurTime > comp.LightningEndCooldown)
                {
                    RemCompDeferred(uid, comp);
                }
            }
        }
    }
}
