using Content.Server.Actions;
using Content.Shared._SW;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

// Namespace for the Star Wars abilities in the server
namespace Content.Server._SW.Abilities;

// DashSystem is a system that handles the dash ability for entities
public sealed class DashSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ActionsSystem _action = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    // The ID for the dash ability
    private const string DashAbilityId = "ActionJediDash";

    // Initialize the system and subscribe to the necessary events
    public override void Initialize()
    {
        SubscribeLocalEvent<DashComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<DashComponent, DashAbilityEvent>(DashAbility);
    }

    // Event handler for the MapInit event
    private void OnMapInit(Entity<DashComponent> ent, ref MapInitEvent args)
    {
        // If the entity has the dash ability, add it to the action system
        if (ent.Comp.Dash)
            _action.AddAction(ent, ref ent.Comp.Action, DashAbilityId);
    }

    // Event handler for the DashAbility event
    private void DashAbility(Entity<DashComponent> ent, ref DashAbilityEvent args)
    {
        var (_, comp) = ent;
        var user = args.Performer;
        var localPhysics = EntityManager.GetComponent<PhysicsComponent>(user);

        // If the user is on the ground, has the dash ability, and does not have a DashEventComponent, perform the dash
        if (localPhysics.BodyStatus == BodyStatus.OnGround && comp.Dash && !EntityManager.HasComponent<DashEventComponent>(user))
        {
            // If a dash sound is specified, play it
            if (!string.IsNullOrEmpty(comp.DashSound))
                _audio.PlayPvs(comp.DashSound, ent);

            // Set the user's body status to in air, set their linear velocity, and apply an impulse
            _physics.SetBodyStatus(localPhysics, BodyStatus.InAir);
            _physics.SetLinearVelocity(user, localPhysics.LinearVelocity * comp.DashSpeed, body: localPhysics);
            _physics.ApplyLinearImpulse(user, localPhysics.LinearVelocity * comp.DashImpulse, body: localPhysics);

            // Add a DashEventComponent to the user with the end time and cooldown time for the dash
            AddComp(user, new DashEventComponent
            {
                DashEndTime = _timing.CurTime + TimeSpan.FromSeconds(comp.DashTime),
                DashEndCooldown = _timing.CurTime + TimeSpan.FromSeconds(comp.DashCooldown)
            });
        }
    }

    // Update the system each frame
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Iterate over all entities with a DashEventComponent
        var query = EntityQueryEnumerator<DashEventComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var localPhysics = EntityManager.GetComponent<PhysicsComponent>(uid);
            // If the dash end time has passed, set the user's body status to on ground
            if (_timing.CurTime > comp.DashEndTime)
                _physics.SetBodyStatus(localPhysics, BodyStatus.OnGround);

            // If the dash cooldown has passed, remove the DashEventComponent from the user
            if (_timing.CurTime > comp.DashEndCooldown)
                RemCompDeferred(uid, comp);
        }
    }
}
