using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;
public sealed class SWSharedVehicleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<SWVehicleComponent> ent, ref MapInitEvent args)
    {
        foreach (var (part, info) in ent.Comp.VehicleParts)
        {
            SpawnWithPlacementInfo(part, info, ent);
        }
    }

    private EntityUid SpawnWithPlacementInfo(ProtoId<EntityPrototype> proto, SWVehiclePlacementInfo info, EntityUid parent)
    {
        var coords = new EntityCoordinates(parent, info.BuckleCoords);
        return Spawn(proto, coords);
    }
}
