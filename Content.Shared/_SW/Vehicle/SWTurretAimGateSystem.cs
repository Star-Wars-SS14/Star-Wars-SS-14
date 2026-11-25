using System;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Shared firing-direction gate for mounted turrets: redirects the shot along the
/// continuous smoothed barrel angle. The 8-direction sprite is only a visual
/// approximation; the projectile must NOT be snapped to the sprite views, otherwise
/// the turret fires only along the 8 compass directions.
/// Lives in Content.Shared so client prediction and server agree.
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
        var query = EntityQueryEnumerator<SWTurretAimComponent, TransformComponent>();
        while (query.MoveNext(out var turretUid, out var aim, out var turretXform))
        {
            var partNet = _vehicleSystem.GetPart(turretUid, aim.GunPartId);
            if (partNet == null || GetEntity(partNet.Value) != gun.Owner)
                continue;

            var turretMap = _transform.GetMapCoordinates(turretUid, xform: turretXform);
            var barrel = _transform.GetWorldRotation(turretXform);

            // Continuous angle: no 45-degree snap. ShotAngleOffset is the empirical
            // compensation, applied once here at shot time (never per-tick in Update).
            var forward = new Angle(barrel.Theta + aim.ShotAngleOffset.Theta).ToVec();
            var target = turretMap.Position + forward * 10f;
            args.ToCoordinates = _transform.ToCoordinates(new MapCoordinates(target, turretMap.MapId));
            return;
        }
    }
}