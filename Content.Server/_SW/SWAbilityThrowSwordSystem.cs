using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
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
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSwordThrowableComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<SWSwordThrowableComponent, ThrowRequestedActionEvent>(OnThrowRequested);
        SubscribeLocalEvent<SWSwordThrowableComponent, GetItemActionsEvent>(OnGetActions);
    }

    private void OnThrowRequested(Entity<SWSwordThrowableComponent> ent, ref ThrowRequestedActionEvent args)
    {
        ISawmill _s = Logger.GetSawmill("asdff");
        _s.Debug($"0 {args.Performer}");
        _hands.ThrowHeldItem(args.Performer, args.Target);
        args.Handled = true;
    }

    private void OnGetActions(Entity<SWSwordThrowableComponent> ent, ref GetItemActionsEvent args)
    {
        if (HasComp<SWAbilityThrowSwordComponent>(args.User))
        {
            args.AddAction(ref ent.Comp.ThrowActionEntity, "SWActionThrowSword");
        }
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
