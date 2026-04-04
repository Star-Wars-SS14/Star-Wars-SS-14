using Content.Shared._SW.Vehicle;
using Content.Shared.Mind;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TransferToVehiclePart;
public sealed class SWTransferToVehiclePartSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _sharedMindSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTransferMindToVehiclePartComponent, SWUserEnterVehicleEvent>(OnStartup);
    }

    private void OnStartup(Entity<SWTransferMindToVehiclePartComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        ISawmill _s = Logger.GetSawmill("adfsg");
        _s.Fatal("0");
        var mind = _sharedMindSystem.GetMind(ent);
        if (mind == null)
        {
            return;
        }
        _s.Fatal("1");
        EntityUid part = args.Vehicle.Owner;
        _s.Fatal($"{part}");
        if (ent.Comp.PartId != null)
        {
            part = args.Vehicle.Comp.VehicleParts[ent.Comp.PartId.Value].SpawnedPart;
        }
        _s.Fatal($"{part}");
        _sharedMindSystem.TransferTo(mind.Value, part);
    }
}
