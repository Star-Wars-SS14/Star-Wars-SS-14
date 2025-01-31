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
    }

    private void OnPullStarted(Entity<BeingMovedByForceComponent> ent, ref PullStartedMessage args)
    {
        StopMoving(args.PullerUid);
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

        StopMoving(player);
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

        _s.Debug("Release");
        forceComp.IsMoving = false;
        RemComp<BeingMovedByForceComponent>(forceComp.CurrentMovingItem.Value);
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

        if (forceComp.IsMoving && comp.ForceUserMover == player)
        {

            //MapCoordinates objectcords = _transformSystem.GetMapCoordinates(forceComp.CurrentMovingItem.Value);
            MapCoordinates placecoords = _transformSystem.ToMapCoordinates(coords);
            comp.MovingTo = placecoords;
            comp.IsMoving = true;
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

        if (Deleted(uid))
            return false;
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

        var comp = EnsureComp<BeingMovedByForceComponent>(uid);
        if (forceComp.CurrentMovingItem == uid && forceComp.IsMoving)
        {
            OnReleaseObject(session, coords, uid);
            return false;
        }

        if (forceComp.IsMoving)
        {
            return false;
        }

        _s.Debug("started");
        forceComp.IsMoving = true;
        forceComp.CurrentMovingItem = uid;
        comp.ForceUserMover = userEntity.Value;
        return false;
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        var movingQuery = EntityQueryEnumerator<BeingMovedByForceComponent>();

        while (movingQuery.MoveNext(out var ent, out var comp))
        {
            MapCoordinates coords = _transformSystem.GetMapCoordinates(ent);
            if (comp.IsMoving && (comp.MovingTo.Position - coords.Position).Length() > 0.1f
                && TryComp<ForceAbilityDistanceInteractComponent>(comp.ForceUserMover, out var forceComp))
            {
                var direction = comp.MovingTo.Position - coords.Position;
                _physicsSystem.ApplyLinearImpulse(ent, direction.Normalized() * forceComp.MoveByForceImpulseMultiplayer);
            }
            else
            {
                comp.IsMoving = false;
                _physicsSystem.SetLinearVelocity(ent, Vector2.Zero);
            }
        }
    }
}
