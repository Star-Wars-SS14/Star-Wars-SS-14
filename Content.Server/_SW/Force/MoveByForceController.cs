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
using Content.Shared._SW.Force.Components;
using Content.Shared.Interaction;
using Robust.Shared.Physics.Systems;
using Content.Server.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._SW.Force;

public sealed partial class MoveByForceController : VirtualController
{
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        _s = Logger.GetSawmill("controller");
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
        StopMoving(ent.Comp.ForceUserMover.Value);
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
        _s.Debug("Releases");
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
        if (forceComp.CurrentMovingItem == null || !forceComp.IsMoving)
        {
            return;
        }

        if (!TryComp<PhysicsComponent>(forceComp.CurrentMovingItem.Value, out var physComp))
        {
            return;
        }

        forceComp.IsMoving = false;
        RemComp<BeingMovedByForceComponent>(forceComp.CurrentMovingItem.Value);
        _physicsSystem.SetBodyStatus(forceComp.CurrentMovingItem.Value, physComp, BodyStatus.OnGround);
    }

    private void StopMovingAndReset(EntityUid mover, ForceAbilityDistanceInteractComponent? forceComp = null)
    {
        if (forceComp == null && !TryComp(mover, out forceComp))
        {
            return;
        }
        if (forceComp.CurrentMovingItem == null || !forceComp.IsMoving)
        {
            return;
        }

        if (!TryComp<PhysicsComponent>(forceComp.CurrentMovingItem.Value, out var physComp))
        {
            return;
        }
        StopMoving(mover, forceComp);
        _physicsSystem.ResetDynamics(forceComp.CurrentMovingItem.Value, physComp);
        _s.Debug("Release");
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

        if (forceComp.IsMoving && comp.ForceUserMover == player)
        {

            MapCoordinates objectcords = _transformSystem.GetMapCoordinates(forceComp.CurrentMovingItem.Value);
            MapCoordinates placecoords = _transformSystem.ToMapCoordinates(coords);
            if (!_interactionSystem.InRangeUnobstructed(objectcords, placecoords, range: 100f))
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

    private bool OnTryStartMove(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_interactionSystem.ValidateClientInput(session, coords, uid, out var userEntity))
        {
            Log.Info($"TryPullObject input validation failed");
            return true;
        }

        if (userEntity.Value == uid)
            return false;

        //Check if entity is mob. Mobs cant be moved by force.
        if (HasComp<MobMoverComponent>(uid))
        {
            return false;
        }

        if (Deleted(uid))
            return false;

        if (!uid.Valid)
        {
            return false;
        }
        _s.Debug($"{uid}");
        _s.Debug($"{userEntity.Value}");
        if (!TryComp<ForceAbilityDistanceInteractComponent>(userEntity.Value, out var forceComp))
        {
            return false;
        }

        if (TryComp<PullerComponent>(userEntity.Value, out var tmpComp) && tmpComp.Pulling != null)
        {
            return false;
        }

        if (!TryComp<PhysicsComponent>(uid, out var physComp))
        {
            return false;
        }

        var comp = EnsureComp<BeingMovedByForceComponent>(uid);
        if (forceComp.CurrentMovingItem == uid && forceComp.IsMoving)
        {
            OnReleaseObject(session, coords, uid);
            return false;
        }

        if (forceComp.IsMoving)
        {
            StopMovingAndReset(userEntity.Value);
        }

        _s.Debug("started");
        forceComp.IsMoving = true;
        forceComp.CurrentMovingItem = uid;
        comp.ForceUserMover = userEntity.Value;
        _physicsSystem.SetBodyStatus(uid, physComp, BodyStatus.InAir);

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
            if (!_interactionSystem.InRangeUnobstructed(fUserCoords, ent, range: forceComp.Range))
            {
                StopMovingAndReset(comp.ForceUserMover.Value);
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
