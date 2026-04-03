using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;
public sealed class SWSharedVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWVehicleComponent, SWOccupyRoleMessage>(OnOccupy);
    }

    private void OnMapInit(Entity<SWVehicleComponent> ent, ref MapInitEvent args)
    {
        foreach (var (part, info) in ent.Comp.VehicleParts)
        {
            SpawnWithPlacementInfo(part, info, ent);
        }
        UpdateInterface(ent);
    }

    private EntityUid SpawnWithPlacementInfo(ProtoId<EntityPrototype> proto, SWVehiclePlacementInfo info, EntityUid parent)
    {
        var coords = new EntityCoordinates(parent, info.BuckleCoords);
        return Spawn(proto, coords);
    }

    private void UpdateInterface(Entity<SWVehicleComponent> ent)
    {
        var dict = new Dictionary<ProtoId<SWVehicleRolePrototype>, bool>();
        foreach (var (id, roleInfo) in ent.Comp.RolesInfo)
        {
            dict[id] = roleInfo.Occupied;
        }
        var state = new SWVehicleMenuBUIState(dict);
        _uiSystem.SetUiState(ent.Owner, SWVehicleMenuUiKey.Key, state);
    }

    private void OnOccupy(Entity<SWVehicleComponent> ent, ref SWOccupyRoleMessage args)
    {
        OccupyRole(ent, args.Actor, args.Role);
        UpdateInterface(ent);
    }

    private void OccupyRole(Entity<SWVehicleComponent> ent, EntityUid user, ProtoId<SWVehicleRolePrototype> role)
    {
        ent.Comp.RolesInfo[role].Occupied = true;
    }
}
