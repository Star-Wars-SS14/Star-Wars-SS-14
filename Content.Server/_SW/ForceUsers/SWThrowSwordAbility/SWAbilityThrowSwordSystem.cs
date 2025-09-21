using Content.Shared.Actions;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Systems;
using Content.Shared._SW;
using Content.Server.Hands.Systems;

namespace Content.Server._SW;

public sealed partial class SWAbilityThrowSwordSystem : EntitySystem
{
    [Dependency] private readonly ForceAbilityDistanceInteractSystem _force = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSwordThrowableComponent, LandEvent>(OnLand);
    }

    private void OnLand(Entity<SWSwordThrowableComponent> ent, ref LandEvent args)
    {
        if (args.User == null)
        {
            return;
        }
        _force.StartAttractingByForce(args.User.Value, ent.Owner);
        _physicsSystem.ApplyAngularImpulse(ent.Owner, 2f);
    }
}
