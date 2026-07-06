using System.Numerics;
using Content.Server.Interaction;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Interaction;
using Content.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Physics;

namespace Content.Server._SW.Targeting;

public sealed class SWAutoTargetingGunSystem : EntitySystem
{
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedBorgSystem _borg = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly RotateToFaceSystem _rotate = default!;
    [Dependency] private readonly NPCUtilitySystem _utility = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<RechargeBasicEntityAmmoComponent> _rechargeQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private const float ShootSpeed = 20f;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _rechargeQuery = GetEntityQuery<RechargeBasicEntityAmmoComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWAutoTargetingGunComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var xform))
        {
            UpdateAutoGun(uid, component, xform, frameTime);
        }
    }

    public void SetForcedTarget(EntityUid uid, EntityUid? target, SWAutoTargetingGunComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        component.ForcedTarget = target;
        component.Target = target;
        component.TargetInLineOfSight = false;
        component.LosUpdateAccumulator = 0f;
        component.ShootAccumulator = 0f;
    }

    private void UpdateAutoGun(EntityUid uid, SWAutoTargetingGunComponent component, TransformComponent xform, float frameTime)
    {
        if (!TryGetTarget(uid, component, xform, frameTime, out var target, out var targetXform, out var targetBody, out var forced))
            return;

        var attempt = new SWAutoTargetingGunAttemptEvent(target, forced);
        RaiseLocalEvent(uid, attempt);
        if (attempt.Cancelled)
        {
            component.ShootAccumulator = 0f;
            return;
        }

        if (!_gun.TryGetGun(uid, out var gunUid, out var gun) &&
            (!TrySelectBorgRangedModule(uid) || !_gun.TryGetGun(uid, out gunUid, out gun)))
        {
            component.ShootAccumulator = 0f;
            return;
        }

        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gunUid, ref ammo);
        if (ammo.Count == 0)
        {
            if (!_rechargeQuery.HasComponent(gunUid))
                component.ShootAccumulator = 0f;

            return;
        }

        var worldPos = _transform.GetWorldPosition(xform);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var distance = (targetPos - worldPos).Length();

        component.LosUpdateAccumulator -= frameTime;
        if (component.LosUpdateAccumulator <= 0f)
        {
            component.LosUpdateAccumulator = component.LosUpdateDelay;
            var collisionGroup = component.UseOpaqueForLineOfSight
                ? CollisionGroup.Opaque
                : CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

            component.TargetInLineOfSight = _interaction.InRangeUnobstructed(uid, target, component.Range, collisionGroup);
        }

        if (!component.TargetInLineOfSight && component.RequireLineOfSight)
        {
            component.ShootAccumulator = 0f;
            return;
        }

        if (distance > component.Range)
        {
            component.ShootAccumulator = 0f;
            return;
        }

        component.ShootAccumulator += frameTime;
        if (component.ShootAccumulator < component.ShootDelay)
            return;

        var targetSpot = targetPos + targetBody.LinearVelocity * distance / ShootSpeed;
        var goalRotation = (targetSpot - worldPos).ToWorldAngle();
        if (!_rotate.TryRotateTo(uid, goalRotation, frameTime, double.MaxValue, double.MaxValue, xform))
            return;

        if (!_gun.CanShoot(gun) ||
            gun.NextFire > _timing.CurTime)
        {
            return;
        }

        _gun.AttemptShoot(uid, gunUid, gun, GetTargetCoordinates(xform, targetPos, targetSpot), target);
    }

    private bool TryGetTarget(
        EntityUid uid,
        SWAutoTargetingGunComponent component,
        TransformComponent xform,
        float frameTime,
        out EntityUid target,
        out TransformComponent targetXform,
        out PhysicsComponent targetBody,
        out bool forced)
    {
        forced = false;
        target = default;
        targetXform = default!;
        targetBody = default!;

        if (component.ForcedTarget is { } forcedTarget && IsValidTarget(uid, xform, forcedTarget, out targetXform, out targetBody))
        {
            target = forcedTarget;
            forced = true;
            return true;
        }

        if (component.ForcedTarget is { } invalidForced && !IsValidTarget(uid, xform, invalidForced, out _, out _))
            component.ForcedTarget = null;

        component.TargetUpdateAccumulator -= frameTime;
        if (component.TargetUpdateAccumulator <= 0f)
        {
            component.TargetUpdateAccumulator = component.TargetUpdateDelay;
            component.Target = PickTarget(uid, component, xform);
            component.TargetInLineOfSight = false;
            component.LosUpdateAccumulator = 0f;
        }

        if (component.Target is not { } candidate ||
            !IsValidTarget(uid, xform, candidate, out targetXform, out targetBody))
        {
            component.Target = null;
            component.ShootAccumulator = 0f;
            return false;
        }

        target = candidate;
        return true;
    }

    private EntityUid? PickTarget(EntityUid uid, SWAutoTargetingGunComponent component, TransformComponent xform)
    {
        var blackboard = component.Blackboard;
        blackboard.Clear();
        blackboard.SetValue(NPCBlackboard.Owner, uid);
        blackboard.SetValue(NPCBlackboard.OwnerCoordinates, xform.Coordinates);
        blackboard.SetValue("RangedRange", component.Range);
        blackboard.SetValue("VisionRadius", component.Range);

        if (component.Target is { } target)
            blackboard.SetValue("Target", target);

        var result = _utility.GetEntities(blackboard, component.UtilityQuery);
        var picked = result.GetHighest();
        return picked.IsValid() ? picked : null;
    }

    private bool IsValidTarget(
        EntityUid uid,
        TransformComponent xform,
        EntityUid target,
        out TransformComponent targetXform,
        out PhysicsComponent targetBody)
    {
        targetXform = default!;
        targetBody = default!;

        if (target == uid ||
            TerminatingOrDeleted(target) ||
            !_xformQuery.TryGetComponent(target, out var foundTargetXform) ||
            !_physicsQuery.TryGetComponent(target, out var foundTargetBody) ||
            foundTargetXform.MapID != xform.MapID)
        {
            return false;
        }

        targetXform = foundTargetXform;
        targetBody = foundTargetBody;
        return true;
    }

    private EntityCoordinates GetTargetCoordinates(TransformComponent xform, Vector2 targetPos, Vector2 targetSpot)
    {
        if (_mapManager.TryFindGridAt(xform.MapID, targetPos, out var gridUid, out var mapGrid))
            return new EntityCoordinates(gridUid, _map.WorldToLocal(gridUid, mapGrid, targetSpot));

        return new EntityCoordinates(xform.MapUid!.Value, targetSpot);
    }

    private bool TrySelectBorgRangedModule(EntityUid uid)
    {
        if (!TryComp<BorgChassisComponent>(uid, out var chassis) || !chassis.Active)
            return false;

        foreach (var moduleUid in chassis.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<BorgModuleComponent>(moduleUid, out var module) ||
                !TryComp<ItemBorgModuleComponent>(moduleUid, out var itemModule) ||
                !HasComp<SelectableBorgModuleComponent>(moduleUid) ||
                !BorgModuleProvidesGun(itemModule))
            {
                continue;
            }

            if (!module.Installed || module.InstalledEntity != uid)
                _borg.InstallModule((uid, chassis), (moduleUid, module));

            _borg.SelectModule((uid, chassis), (moduleUid, module));
            return true;
        }

        return false;
    }

    private bool BorgModuleProvidesGun(ItemBorgModuleComponent module)
    {
        foreach (var hand in module.Hands)
        {
            if (hand.Item is not { } itemProto ||
                !_prototype.TryIndex<EntityPrototype>(itemProto, out var prototype))
            {
                continue;
            }

            if (prototype.HasComponent<GunComponent>(EntityManager.ComponentFactory))
                return true;
        }

        return false;
    }
}
