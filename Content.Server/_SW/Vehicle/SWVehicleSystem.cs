using Content.Shared._SW.Vehicle;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Destructible;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._SW.Vehicle;
public sealed class SWVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWInVehicleComponent, SWExitVehicleRequested>(OnExit);
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWVehicleComponent, SWOccupyRoleMessage>(OnOccupy);
        SubscribeLocalEvent<SWInVehicleComponent, ComponentInit>(OnInitInVehicle);
        //SubscribeLocalEvent<SWInVehicleComponent, UpdateCanMoveEvent>(OnMoveInVehicle);
        SubscribeLocalEvent<SWVehicleComponent, DestructionEventArgs>(OnDestroy);
    }

    private void OnMapInit(Entity<SWVehicleComponent> ent, ref MapInitEvent args)
    {
        foreach (var (part, info) in ent.Comp.VehicleParts)
        {
            ent.Comp.VehicleParts[part].SpawnedPart = GetNetEntity(SpawnWithPlacementInfo(info, ent));
        }
        UpdateInterface(ent);
        Dirty(ent);
    }

    private void OnOccupy(Entity<SWVehicleComponent> ent, ref SWOccupyRoleMessage args)
    {
        OccupyRole(ent, args.Actor, args.Role);
        UpdateInterface(ent);
    }

    private void OnInitInVehicle(Entity<SWInVehicleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.ActionEntity = _actions.AddAction(ent.Owner, SWSharedVehicleSystem.ActionExitVehicle);
    }

    //private void OnMoveInVehicle(Entity<SWInVehicleComponent> ent, ref UpdateCanMoveEvent args)
    //{
    //    args.Cancel();
    //}

    private void OccupyRole(Entity<SWVehicleComponent> ent, EntityUid user, string role)
    {
        if (HasComp<SWInVehicleComponent>(user))
        {
            return;
        }

        var proto = _prototype.Index(ent.Comp.RolesInfo[role].Role);
        EntityManager.AddComponents(user, proto.ComponentsAddToUser);

        var comp = AddComp<SWInVehicleComponent>(user);
        comp.Vehicle = ent;
        comp.OccupiedRole = role;
        ent.Comp.RolesInfo[role].Occupant = GetNetEntity(user);
        var coords = new EntityCoordinates(ent, ent.Comp.RolesInfo[role].BuckleCoords);
        _transformSystem.SetCoordinates(user, coords);

        var ev = new SWUserEnterVehicleEvent(ent);
        RaiseLocalEvent(user, ev);
        _actionBlocker.UpdateCanMove(user);
    }
    private EntityUid SpawnWithPlacementInfo(SWVehiclePartInfo info, EntityUid parent)
    {
        var coords = new EntityCoordinates(parent, info.BuckleCoords);
        return Spawn(info.EntProto, coords);
    }

    private void UpdateInterface(Entity<SWVehicleComponent> ent)
    {
        var state = new SWVehicleMenuBoundUserInterfaceState(ent.Comp.RolesInfo);
        _uiSystem.SetUiState(ent.Owner, SWVehicleMenuUiKey.Key, state);
    }

    private void OnExit(Entity<SWInVehicleComponent> ent, ref SWExitVehicleRequested args)
    {
        if (args.Handled)
        {
            return;
        }

        if (!TryComp<SWVehicleComponent>(ent.Comp.Vehicle, out var vehicleComp))
        {
            return;
        }

        DeoccupyRole(new Entity<SWVehicleComponent>(ent.Comp.Vehicle, vehicleComp), ent.Comp.OccupiedRole);
        args.Handled = true;
    }

    private void DeoccupyRole(Entity<SWVehicleComponent> vehicle, string occupiedRole)
    {
        var roleInfo = vehicle.Comp.RolesInfo[occupiedRole];

        if (roleInfo.Occupant == null)
        {
            return;
        }
        var occupantEntity = GetEntity(roleInfo.Occupant.Value);
        Entity<SWInVehicleComponent> occupant = new Entity<SWInVehicleComponent>(occupantEntity, Comp<SWInVehicleComponent>(occupantEntity));

        if (occupant.Comp.ActionEntity != null)
        {
            _actions.RemoveAction(occupant.Owner, occupant.Comp.ActionEntity);
        }

        var proto = _prototype.Index(roleInfo.Role);

        //Raise event before deletion to properly shutdown all components
        var ev = new SWUserExitVehicleEvent(vehicle);
        RaiseLocalEvent(occupant, ev);

        RemComp<SWInVehicleComponent>(occupant);
        EntityManager.RemoveComponents(occupant, proto.ComponentsAddToUser);
        var grid = Transform(occupant).GridUid;

        _actionBlocker.UpdateCanMove(occupant);
        roleInfo.Occupant = null;
        UpdateInterface(vehicle);

        if (grid == null)
        {
            return;
        }
        _transformSystem.SetParent(occupant, grid.Value);
    }

    private void OnDestroy(Entity<SWVehicleComponent> ent, ref DestructionEventArgs args)
    {
        ent.Comp.RolesInfo.Keys.ToList().ForEach(roleName => DeoccupyRole(ent, roleName));
    }
}
