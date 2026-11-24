using System;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Shared (predicted) firing-cone gate for mounted turrets.
/// Must live in Content.Shared so the client cancels the shot BEFORE playing
/// muzzle flash / sound / projectile, and PopupClient shows the reason.
/// </summary>
public sealed class SWTurretAimGateSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SWVehicleSystem _vehicleSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTurretAimGunComponent, AttemptShootEvent>(OnGunAttemptShoot);
    }

    private void OnGunAttemptShoot(Entity<SWTurretAimGunComponent> gun, ref AttemptShootEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.ToCoordinates is not EntityCoordinates targetCoords)
            return;

        var query = EntityQueryEnumerator<SWTurretAimComponent, TransformComponent>();
        while (query.MoveNext(out var turretUid, out var aim, out var turretXform))
        {
            var partNet = _vehicleSystem.GetPart(turretUid, aim.GunPartId);
            if (partNet == null || GetEntity(partNet.Value) != gun.Owner)
                continue;

            var turretMap = _transform.GetMapCoordinates(turretUid, xform: turretXform);
            var targetMap = _transform.ToMapCoordinates(targetCoords);
            if (targetMap.MapId != turretMap.MapId)
                return;

            // By construction of the aim system the barrel world angle equals
            // the turret world rotation, so no sprite-forward offset is needed.
            var desired = (targetMap.Position - turretMap.Position).ToWorldAngle().Theta;
            var barrel = _transform.GetWorldRotation(turretXform).Theta;

            var delta = desired - barrel;
            while (delta > Math.PI) delta -= 2 * Math.PI;
            while (delta < -Math.PI) delta += 2 * Math.PI;

            if (Math.Abs(delta) * (180.0 / Math.PI) > aim.FiringCone)
            {
                args.Cancelled = true;
                args.Message = Loc.GetString("sw-turret-aim-out-of-cone");
            }
            return;
        }
    }
}