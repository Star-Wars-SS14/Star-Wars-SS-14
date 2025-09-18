using Content.Shared.Actions;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Systems;
using Content.Shared._SW;
using Content.Server.Hands.Systems;
using Content.Shared.Item;
using Content.Server._SW.Unpickuppable;

namespace Content.Server._SW;

public sealed partial class SWAbilityThrowSwordSystem : EntitySystem
{
    [Dependency] private readonly ForceAbilityDistanceInteractSystem _force = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWAbilityThrowSwordComponent, BeforeThrowEvent>(BeforeThrow);
        SubscribeLocalEvent<SWSwordThrowableComponent, LandEvent>(OnLand);
    }

    private void BeforeThrow(Entity<SWAbilityThrowSwordComponent> ent, ref BeforeThrowEvent args)
    {
        if (HasComp<SWSwordThrowableComponent>(args.ItemUid))
        {
            //We do it to avoid
            //that someone can catch sword while it is throwed
            EnsureComp<SWUnpickuppableComponent>(args.ItemUid);
        }
    }

    private void OnLand(Entity<SWSwordThrowableComponent> ent, ref LandEvent args)
    {
        if (args.User == null)
        {
            return;
        }
        RemComp<SWUnpickuppableComponent>(ent);
        _force.StartAttractingByForce(args.User.Value, ent.Owner);
        _physicsSystem.ApplyAngularImpulse(ent.Owner, 2f);
    }
}
