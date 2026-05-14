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
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWShootByVehicleUserComponent, SWGetGunEvent>(OnGetGun);
    }

    private void OnGetGun(Entity<SWShootByVehicleUserComponent> ent, ref SWGetGunEvent args)
    {
        ISawmill _s = Logger.GetSawmill("vg");
        _s.Fatal("0");
        if (!TryComp<SWInVehicleComponent>(GetEntity(args.User), out var inVehicle))
        {
            return;
        }
        _s.Fatal("1");
        var vehicleComp = Comp<SWVehicleComponent>(inVehicle.Vehicle);
        if (vehicleComp.VehicleParts.TryGetValue(ent.Comp.GunVehiclePartId, out var part))
        {
            _s.Fatal($"euid: {GetEntity(part.SpawnedPart)} net: {part.SpawnedPart}");
            args.Gun = part.SpawnedPart;
            args.Handled = true;
        }
    }
}
