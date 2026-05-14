using Content.Shared._SW.Vehicle;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.ShootInVehicle;
public sealed class SWShootByVehicleSystem : EntitySystem
{
    ISawmill _s = Logger.GetSawmill("vg");
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWShootByVehicleUserComponent, SWGetGunEvent>(OnGetGun);
        SubscribeLocalEvent<SWShootByVehicleUserComponent, SWGetFromCoordsShootEvent>(OnGetCoords);
    }
    
    private void OnGetGun(Entity<SWShootByVehicleUserComponent> ent, ref SWGetGunEvent args)
    {
       
        _s.Fatal("0");
        if (!TryComp<SWInVehicleComponent>(GetEntity(args.User), out var inVehicle))
        {
            return;
        }
        _s.Fatal("1");
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

    private NetEntity? GetGun(Entity<SWShootByVehicleUserComponent> ent, EntityUid user)
    {
        if (!TryComp<SWInVehicleComponent>(user, out var inVehicle))
        {
            return null;
        }

        var vehicleComp = Comp<SWVehicleComponent>(inVehicle.Vehicle);
        if (vehicleComp.VehicleParts.TryGetValue(ent.Comp.GunVehiclePartId, out var part))
        {
            return part.SpawnedPart;
        }

        return null;
    }
}
