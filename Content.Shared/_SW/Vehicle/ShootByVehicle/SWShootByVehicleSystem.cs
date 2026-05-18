using Content.Shared._SW.Vehicle;
using Content.Shared._SW.Vehicle.ShootByVehicle;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.ShootInVehicle;
public sealed class SWShootByVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly SWVehicleSystem _vehicleSystem = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWShootByVehicleUserComponent, SWGetGunEvent>(OnGetGun);
        SubscribeLocalEvent<SWShootByVehicleUserComponent, SWGetFromCoordsShootEvent>(OnGetCoords);
        SubscribeLocalEvent<SWShootByVehicleGunComponent, AttemptShootEvent>(OnShoot);
    }

    private void OnGetGun(Entity<SWShootByVehicleUserComponent> ent, ref SWGetGunEvent args)
    {
        var gun = GetGun(ent, GetEntity(args.User));
        if (gun != null)
        {
            args.Handled = true;
            args.Gun = gun.Value;
        }
    }

    private void OnGetCoords(Entity<SWShootByVehicleUserComponent> ent, ref SWGetFromCoordsShootEvent args)
    {
        var gun = GetGun(ent, args.User);
        if (gun == null)
        {
            return;
        }
        args.Handled = true;
        args.Coords = Transform(GetEntity(gun.Value)).Coordinates;
    }

    private void OnShoot(Entity<SWShootByVehicleGunComponent> ent, ref AttemptShootEvent args)
    {
        if (args.ToCoordinates is not EntityCoordinates targetCoords)
            return;

        Vector2 gunWorldPos = _sharedTransformSystem.GetWorldPosition(ent.Owner);

        Vector2 targetWorldPos = _sharedTransformSystem.ToWorldPosition(targetCoords);

        Vector2 directionToTarget = (targetWorldPos - gunWorldPos).Normalized();

        Angle gunRotation = _sharedTransformSystem.GetWorldRotation(ent.Owner);
        Vector2 forward = gunRotation.RotateVec(ent.Comp.ForwardVector).Normalized();

        float cross = forward.X * directionToTarget.Y - forward.Y * directionToTarget.X;
        float dot = Vector2.Dot(forward, directionToTarget);
        float angleRad = MathF.Atan2(cross, dot);

        Angle angle = new Angle(angleRad);
        if (Math.Abs(angle.Degrees) > ent.Comp.AngleInDegrees / 2.0)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("sw-vehicle-gun-unavailable-range");
        }
    }
    private NetEntity? GetGun(Entity<SWShootByVehicleUserComponent> ent, EntityUid user)
    {
        if (!TryComp<SWInVehicleComponent>(user, out var inVehicle))
        {
            return null;
        }

        return _vehicleSystem.GetPart(inVehicle.Vehicle, ent.Comp.GunVehiclePartId);
    }
}
