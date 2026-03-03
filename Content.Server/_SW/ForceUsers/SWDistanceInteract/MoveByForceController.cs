using Content.Server.Database;
using Content.Shared._SW;
using Content.Shared.Input;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Content.Shared.Interaction;
using Robust.Shared.Physics.Systems;
using Content.Server.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.Physics.Events;
using Content.Server.Popups;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Server.Examine;
using Content.Server._SW.Force.Components;
using Content.Shared._SW.ForceAbilities;

namespace Content.Server._SW.Force;

public sealed partial class MoveByForceController : VirtualController
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly ExamineSystem _examineSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
           .Bind(ContentKeyFunctions.TryPullObject, new PointerInputCmdHandler(OnTryStartMove))
           .Bind(ContentKeyFunctions.MovePulledObject, new PointerInputCmdHandler(OnRequestMovePulledObject))
           .Bind(ContentKeyFunctions.ReleasePulledObject, new PointerInputCmdHandler(OnReleaseObject))
           .Register<ForceAbilityDistanceInteractSystem>();
        SubscribeLocalEvent<BeingMovedByForceComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<BeingMovedByForceComponent, StopMovingByForceEvent>(OnStopPullRequested);
        SubscribeLocalEvent<BeingMovedByForceComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<BeingMovedByForceComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.ForceUserMover == null)
        {
            return;
        }

        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
        {
            return;
        }
        ent.Comp.IsMoving = false;
    }

    private void OnStopPullRequested(Entity<BeingMovedByForceComponent> ent, ref StopMovingByForceEvent args)
    {
        if (ent.Comp.ForceUserMover == null)
        {
            return;
        }

        StopMovingAndReset(ent.Comp.ForceUserMover.Value);
    }

    private void OnPullStarted(Entity<BeingMovedByForceComponent> ent, ref PullStartedMessage args)
    {
        StopMovingAndReset(args.PullerUid);
    }

    private bool OnReleaseObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } player ||
            !player.IsValid())
        {
            return false;
        }

        if (!TryComp<ForceAbilityDistanceInteractComponent>(player, out var forceComp))
        {
            return false;
        }

        StopMovingAndReset(player);
        return false;
    }

    private void StopMoving(EntityUid mover, ForceAbilityDistanceInteractComponent? forceComp = null)
    {
        if (forceComp == null && !TryComp(mover, out forceComp))
        {
            return;
        }
        if (forceComp.CurrentMovingItem == null || !forceComp.IsUsingForceToMoveObject)
        {
            return;
        }

        if (!TryComp<PhysicsComponent>(forceComp.CurrentMovingItem.Value, out var physComp))
        {
            return;
        }

        forceComp.IsUsingForceToMoveObject = false;
        RemComp<BeingMovedByForceComponent>(forceComp.CurrentMovingItem.Value);
        _physicsSystem.SetBodyStatus(forceComp.CurrentMovingItem.Value, physComp, BodyStatus.OnGround);
        var name = Identity.Name(forceComp.CurrentMovingItem.Value, EntityManager);
        var message = Loc.GetString("force-user-object-stop-flying", ("other", name));
        _popupSystem.PopupEntity(message, forceComp.CurrentMovingItem.Value);
    }

    private void StopMovingAndReset(EntityUid mover, ForceAbilityDistanceInteractComponent? forceComp = null)
    {
        if (forceComp == null && !TryComp(mover, out forceComp))
        {
            return;
        }
        if (forceComp.CurrentMovingItem == null || !forceComp.IsUsingForceToMoveObject)
        {
            return;
        }

        if (!TryComp<PhysicsComponent>(forceComp.CurrentMovingItem.Value, out var physComp))
        {
            return;
        }
        StopMoving(mover, forceComp);
        _physicsSystem.ResetDynamics(forceComp.CurrentMovingItem.Value, physComp);
        forceComp.CurrentMovingItem = null;
    }

    private bool OnRequestMovePulledObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_interactionSystem.ValidateClientInput(session, coords, uid, out var userEntity))
        {
            Log.Info($"TryPullObject input validation failed");
            return true;
        }

        if (session?.AttachedEntity is not { } player ||
            !player.IsValid())
        {
            return false;
        }

        if (!TryComp<ForceAbilityDistanceInteractComponent>(player, out var forceComp) || forceComp.CurrentMovingItem == null)
        {
            return false;
        }

        if (!TryComp<BeingMovedByForceComponent>(forceComp.CurrentMovingItem.Value, out var comp))
        {
            return false;
        }

        if (!TryComp<PhysicsComponent>(forceComp.CurrentMovingItem.Value, out var physComp))
        {
            return false;
        }
        if (forceComp.IsUsingForceToMoveObject && comp.ForceUserMover == player)
        {
            MapCoordinates placecoords = _transformSystem.ToMapCoordinates(coords);
            if (!CanBeMovedToCoords(forceComp.CurrentMovingItem.Value, player, placecoords))
            {
                return false;
            }

            comp.MovingTo = placecoords;
            comp.IsMoving = true;
            //_physicsSystem.SetBodyStatus(forceComp.CurrentMovingItem.Value, physComp, BodyStatus.InAir);
            //_s.Debug($"{forceComp.CurrentMovingItem.Value}");
            //_s.Debug($"{player}");
            //var direction = (placecoords.Position - objectcords.Position).Normalized();
            //_physicsSystem.ApplyLinearImpulse(forceComp.CurrentMovingItem.Value, direction * forceComp.MoveByForceImpulseMultiplayer);

        }
        return false;
    }

    private bool CanBeMovedToCoords(EntityUid obj, EntityUid mover, MapCoordinates coords)
    {
       if (!CanBeMoved(obj, mover))
        {
            return false;
        }

        if (!_examineSystem.InRangeUnOccluded(obj, coords, SharedInteractionSystem.MaxRaycastRange))
        {
            return false;
        }
        return true;
    }


    private bool CanBeMoved(EntityUid obj, EntityUid mover)
    {
        //Check if entity is mob. Alive mobs cant be moved by force.
        if (TryComp<MobThresholdsComponent>(obj, out var tComp))
        {
            if (tComp.CurrentThresholdState == MobState.Alive || tComp.CurrentThresholdState == MobState.Invalid)
            {
                return false;
            }
        }

        if (TryComp<PullerComponent>(mover, out var tmpComp) && tmpComp.Pulling != null)
        {
            return false;
        }

        if (!TryComp<ForceAbilityDistanceInteractComponent>(mover, out var forceComp))
        {
            return false;
        }

        MapCoordinates objCoords = _transformSystem.GetMapCoordinates(obj);
        MapCoordinates fUserCoords = _transformSystem.GetMapCoordinates(mover);

        //Ессли мы находимся достаточно близко для обычного взаимодейтсвия,
        //то не будем начинать двигать объект при помощи силы, чтобы не мешать обычному взаимодействию
        if ((fUserCoords.Position - objCoords.Position).Length() <= SharedInteractionSystem.InteractionRange && !forceComp.IsUsingForceToMoveObject)
        {
            return false;
        }

        if (!_examineSystem.InRangeUnOccluded(mover, obj, SharedInteractionSystem.MaxRaycastRange))
        {
            return false;
        }
        return true;
    }

    private bool OnTryStartMove(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_interactionSystem.ValidateClientInput(session, coords, uid, out var userEntity))
        {
            Log.Info($"TryPullObject input validation failed");
            return true;
        }

        if (userEntity.Value == uid)
            return false;

        if (Deleted(uid))
            return false;

        if (!uid.Valid)
        {
            return false;
        }
        if (!TryComp<ForceAbilityDistanceInteractComponent>(userEntity.Value, out var forceComp))
        {
            return false;
        }

        if (!TryComp<PhysicsComponent>(uid, out var physComp))
        {
            return false;
        }

        var comp = EnsureComp<BeingMovedByForceComponent>(uid);
        if (forceComp.CurrentMovingItem == uid && forceComp.IsUsingForceToMoveObject)
        {
            OnReleaseObject(session, coords, uid);
            return false;
        }

        if (!CanBeMoved(uid, userEntity.Value))
        {
            return false;
        }

        if (forceComp.IsUsingForceToMoveObject)
        {
            StopMovingAndReset(userEntity.Value);
        }

        forceComp.IsUsingForceToMoveObject = true;
        forceComp.CurrentMovingItem = uid;
        comp.ForceUserMover = userEntity.Value;
        _physicsSystem.SetBodyStatus(uid, physComp, BodyStatus.InAir);
        var name = Identity.Name(uid, EntityManager);
        var message = Loc.GetString("force-user-object-start-flying", ("other", name));
        _popupSystem.PopupEntity(message, uid);
        return false;
    }

    public override void UpdateAfterSolve(bool prediction, float frameTime)
    {
        var movingQuery = EntityQueryEnumerator<BeingMovedByForceComponent>();

        while (movingQuery.MoveNext(out var ent, out var comp))
        {
            if (comp.ForceUserMover == null || !TryComp<PhysicsComponent>(ent, out var physComp))
            {
                continue;
            }

            if (!TryComp<ForceAbilityDistanceInteractComponent>(comp.ForceUserMover, out var forceComp))
            {
                continue;
            }

            MapCoordinates objCoords = _transformSystem.GetMapCoordinates(ent);
            MapCoordinates fUserCoords = _transformSystem.GetMapCoordinates(comp.ForceUserMover.Value);
            if (!CanBeMoved(ent, comp.ForceUserMover.Value))
            {
                StopMovingAndReset(comp.ForceUserMover.Value);
                continue;
            }

            if (comp.IsMoving && (comp.MovingTo.Position - objCoords.Position).Length() > 0.1f)
            {
                var direction = comp.MovingTo.Position - objCoords.Position;
                _physicsSystem.SetLinearVelocity(ent, direction.Normalized() * forceComp.MoveByForceImpulseMultiplayer / physComp.Mass);


            }
            else
            {
                comp.IsMoving = false;
                _physicsSystem.ResetDynamics(ent, physComp);
            }
        }
    }
}
