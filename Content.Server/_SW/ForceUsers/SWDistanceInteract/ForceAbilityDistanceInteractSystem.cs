using Content.Shared._SW;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using System.Numerics;
using Robust.Shared.Containers;
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using Content.Shared.Stunnable;
using Content.Server.Actions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Server._SW.Unpickuppable;
using Content.Server._SW.ModularSaber;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared._SW.ForceAbilities;

namespace Content.Server._SW;
public sealed partial class ForceAbilityDistanceInteractSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionsSystem _actionsSystem = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<ForceAbilityDistanceInteractComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<BeingAttractedByForceComponent, EntGotInsertedIntoContainerMessage>(OnAttractedObjectGetInserted);
        SubscribeLocalEvent<BeingMovedByForceComponent, EntGotInsertedIntoContainerMessage>(OnMovedObjectGetInserted);
        SubscribeLocalEvent<ForceAbilityDistanceInteractComponent, AttractByForceRequestedEvent>(OnAttractRequested);
    }

    private void OnCompInit(Entity<ForceAbilityDistanceInteractComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.GrantAttractAction)
            _actionsSystem.AddAction(ent.Owner, ent.Comp.ActionAttractPrototype);
    }

    private void OnAttractRequested(Entity<ForceAbilityDistanceInteractComponent> ent, ref AttractByForceRequestedEvent args)
    {
        StartAttractingByForce(args.Performer, args.Target, ent.Comp);
        args.Handled = true;
    }

    private void OnMovedObjectGetInserted(Entity<BeingMovedByForceComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        StopMovingObjectByForce(ent.Owner);
    }
    private void StopMovingObjectByForce(EntityUid objectToMove)
    {
        if (!TryComp<BeingMovedByForceComponent>(objectToMove, out var comp) || comp.ForceUserMover == null)
        {
            return;
        }

        RaiseLocalEvent(objectToMove, new StopMovingByForceEvent(comp.ForceUserMover.Value));
    }
    private void OnAttractedObjectGetInserted(Entity<BeingAttractedByForceComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (ent.Comp.User == null)
        {
            return;
        }
        StopAttractingByForce(ent.Comp.User.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ForceAbilityDistanceInteractComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (HasComp<KnockedDownComponent>(uid))
            {
                StopAttractingByForce(uid, comp);
                continue;
            }

            if (comp.IsAttracting && comp.CurrentAttractingItem != null)
            {
                if (TerminatingOrDeleted(comp.CurrentAttractingItem.Value))
                {
                    StopAttractingByForce(uid, comp);
                    continue;
                }

                MapCoordinates usercords = _transformSystem.GetMapCoordinates(uid);
                MapCoordinates targetcords = _transformSystem.GetMapCoordinates(comp.CurrentAttractingItem.Value);
                if (_interactionSystem.InRangeUnobstructed(usercords, comp.CurrentAttractingItem.Value, range: 100f) &&
                    (usercords.Position - targetcords.Position).Length() >= SharedInteractionSystem.InteractionRange)
                {
                    _physicsSystem.SetLinearVelocity(comp.CurrentAttractingItem.Value, (usercords.Position - targetcords.Position).Normalized() * comp.AttractByForceSpeed);
                }
                else
                {
                    var item = comp.CurrentAttractingItem.Value;
                    var returnSaber = TryComp<SWDoubleSaberThrowVisualComponent>(item, out var visual) &&
                                      visual.ReturnUser == uid;
                    var wasActive = returnSaber && _toggle.IsActivated(item);
                    StopAttractingByForce(uid, comp);
                    if ((usercords.Position - targetcords.Position).Length() >= SharedInteractionSystem.InteractionRange)
                        continue;

                    if (returnSaber)
                    {
                        if (_handsSystem.TryPickupAnyHand(uid, item))
                            WieldReturnedSaber(uid, item, wasActive);
                    }
                    else
                    {
                        _handsSystem.TryPickup(uid, item);
                    }
                }
            }
        }
    }

    private void WieldReturnedSaber(EntityUid user, EntityUid saber, bool wasActive)
    {
        if (TerminatingOrDeleted(user) || TerminatingOrDeleted(saber) ||
            !TryComp<WieldableComponent>(saber, out var wieldable) || wieldable.Wielded ||
            !_handsSystem.IsHolding(user, saber) || !_handsSystem.TryGetEmptyHand(user, out _))
            return;

        // Never drop an unrelated held item. Roll back a partial wield if spawning a virtual hand fails.
        if (!_wieldable.TryWield(saber, wieldable, user, dropOthers: false, quiet: true))
        {
            if (wieldable.Wielded)
                _wieldable.TryUnwield(saber, wieldable, user, force: true);
            return;
        }

        // Wielding may turn the blade on automatically; preserve an intentionally inactive blade.
        if (!wasActive && _toggle.IsActivated(saber))
            _toggle.TryDeactivate(saber, user, predicted: false);
    }

    public void StartAttractingByForce(EntityUid forceUser, EntityUid target, ForceAbilityDistanceInteractComponent? comp = null)
    {
        if (!TryComp(forceUser, out comp))
        {
            return;
        }

        if (HasComp<KnockedDownComponent>(forceUser)) {
            return;
        }

        if (!HasComp<ItemComponent>(target))
        {
            return;
        }

        if (comp.IsAttracting && comp.CurrentAttractingItem == target)
            return;

        if (comp.CurrentAttractingItem != null)
            StopAttractingByForce(forceUser, comp);

        comp.IsAttracting = true;
        comp.CurrentAttractingItem = target;
        EnsureComp<BeingAttractedByForceComponent>(target).User = forceUser;
        EnsureComp<SWUnpickuppableComponent>(target);
    }

    public void StopAttractingByForce(EntityUid user, ForceAbilityDistanceInteractComponent? comp = null)
    {
        if (comp == null)
        {
            comp = CompOrNull<ForceAbilityDistanceInteractComponent>(user);
            if (comp == null)
            {
                return;
            }
        }

        if (comp.CurrentAttractingItem == null)
        {
            return;
        }

        var item = comp.CurrentAttractingItem.Value;
        comp.IsAttracting = false;
        comp.CurrentAttractingItem = null;
        if (TerminatingOrDeleted(item))
            return;

        _physicsSystem.SetLinearVelocity(item, Vector2.Zero);
        RemComp<BeingAttractedByForceComponent>(item);
        RemComp<SWUnpickuppableComponent>(item);
    }
}
