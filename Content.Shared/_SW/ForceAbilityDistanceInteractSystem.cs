using Content.Shared._SW.Force.Components;
using Content.Shared.Coordinates;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using System.Numerics;
using Robust.Shared.Containers;
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using Robust.Shared.Serialization;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Input.Binding;
using Content.Shared.Input;
using Robust.Shared.Player;
using Content.Shared.Stunnable;

namespace Content.Shared._SW;
public sealed partial class ForceAbilityDistanceInteractSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    public override void Initialize()
    {
        //SubscribeLocalEvent<BeingPulledByForceComponent, InteractHandEvent>(OnInteractWithPulledObject);
        SubscribeLocalEvent<ItemComponent, GetVerbsEvent<PullByForceVerb>>(OnVerbsRequested);
        SubscribeLocalEvent<BeingAttractedByForceComponent, EntGotInsertedIntoContainerMessage>(OnAttractedObjectGetInserted);
        SubscribeLocalEvent<BeingMovedByForceComponent, EntGotInsertedIntoContainerMessage>(OnMovedObjectGetInserted);
        //SubscribeLocalEvent<ForceAbilityDistanceInteractComponent, RequestPullEvent>(OnPullRequested);
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

    private void OnVerbsRequested(Entity<ItemComponent> ent, ref GetVerbsEvent<PullByForceVerb> args)
    {
        if (!HasComp<ForceAbilityDistanceInteractComponent>(args.User))
        {
            return;
        }
        // Basic examine verb.
        PullByForceVerb verb = new();
        verb.Priority = 10;
        // Center it on the entity if they use the verb instead.
        EntityUid user = args.User;
        EntityUid target = args.Target;
        verb.Act = () => StartAttractingByForce(user, target);
        verb.Text = "Притянуть силой";
        verb.Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/drop.svg.192dpi.png"));
        args.Verbs.Add(verb);
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
                MapCoordinates usercords = _transformSystem.GetMapCoordinates(uid);
                MapCoordinates targetcords = _transformSystem.GetMapCoordinates(comp.CurrentAttractingItem.Value);
                if (_interactionSystem.InRangeUnobstructed(usercords, comp.CurrentAttractingItem.Value, range: 100f) &&
                    (usercords.Position - targetcords.Position).Length() >= SharedInteractionSystem.InteractionRange)
                {
                    _physicsSystem.SetLinearVelocity(comp.CurrentAttractingItem.Value, (usercords.Position - targetcords.Position).Normalized() * comp.AttractByForceSpeed);
                }
                else
                {
                    if ((usercords.Position - targetcords.Position).Length() < SharedInteractionSystem.InteractionRange)
                    {
                        _handsSystem.TryPickup(uid, comp.CurrentAttractingItem.Value);
                    }
                    StopAttractingByForce(uid, comp);
                }
            }
        }
    }

    public void StartAttractingByForce(EntityUid forceUser, EntityUid target, ForceAbilityDistanceInteractComponent? comp = null)
    {
        if (comp == null)
        {
            comp = CompOrNull<ForceAbilityDistanceInteractComponent>(forceUser);
            if (comp == null)
            {
                return;
            }
        }

        if (HasComp<KnockedDownComponent>(forceUser)) {
            return;
        }

        comp.IsAttracting = true;
        comp.CurrentAttractingItem = target;
        EnsureComp<BeingAttractedByForceComponent>(target).User = forceUser;
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

        _physicsSystem.SetLinearVelocity(comp.CurrentAttractingItem.Value, Vector2.Zero);
        comp.IsAttracting = false;
        RemComp<BeingAttractedByForceComponent>(comp.CurrentAttractingItem.Value);
        comp.CurrentAttractingItem = null;
    }
}

[Serializable, NetSerializable]
public sealed class PullByForceVerb : Verb
{
    public override int TypePriority => 0;
}

public sealed class StopMovingByForceEvent : EntityEventArgs
{
    public EntityUid MoverId;
    public StopMovingByForceEvent(EntityUid moverId)
    {
        MoverId = moverId;
    }
}
