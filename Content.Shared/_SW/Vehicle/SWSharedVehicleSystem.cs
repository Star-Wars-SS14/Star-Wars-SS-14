using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Movement.Events;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
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
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;

    public const string ActionExitVehicle = "SWVehicleGetOffAction";
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWVehicleComponent, SWOccupyRoleMessage>(OnOccupy);
        SubscribeLocalEvent<SWInVehicleComponent, ComponentInit>(OnInitInVehicle);
        SubscribeLocalEvent<SWInVehicleComponent, UpdateCanMoveEvent>(OnMoveInVehicle);
        SubscribeLocalEvent<SWInVehicleComponent, SWExitVehicleRequested>(OnExit);
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
        var state = new SWVehicleMenuBoundUserInterfaceState(ent.Comp.RolesInfo);
        _uiSystem.SetUiState(ent.Owner, SWVehicleMenuUiKey.Key, state);
    }

    private void OnOccupy(Entity<SWVehicleComponent> ent, ref SWOccupyRoleMessage args)
    {
        OccupyRole(ent, args.Actor, args.Role);
        UpdateInterface(ent);
    }

    private void OnInitInVehicle(Entity<SWInVehicleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.ActionEntity = _actions.AddAction(ent.Owner, ActionExitVehicle);
    }

    private void OnMoveInVehicle(Entity<SWInVehicleComponent> ent, ref UpdateCanMoveEvent args)
    {
        ISawmill _s = Logger.GetSawmill("asdfghj");
        _s.Fatal("cancelled");
        args.Cancel();
    }

    private void OccupyRole(Entity<SWVehicleComponent> ent, EntityUid user, string role)
    {
        if (HasComp<SWInVehicleComponent>(user))
        {
            return;
        }

        ent.Comp.RolesInfo[role].Occupied = true;
        var proto = _prototype.Index(ent.Comp.RolesInfo[role].Role);
        EntityManager.AddComponents(user, proto.ComponentsAddToUser);

        var comp = AddComp<SWInVehicleComponent>(user);
        comp.Vehicle = ent;
        comp.OccupiedRole = role;

        var coords = new EntityCoordinates(ent, ent.Comp.RolesInfo[role].BuckleCoords);
        _transformSystem.SetCoordinates(user, coords);
        _actionBlocker.UpdateCanMove(user);
    }

    private void OnExit(Entity<SWInVehicleComponent> ent, ref SWExitVehicleRequested args)
    {
        var roleInfo = ent.Comp.Vehicle.Comp.RolesInfo[ent.Comp.OccupiedRole];
        roleInfo.Occupied = false;
        RemComp<SWInVehicleComponent>(ent);

        if (ent.Comp.ActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        }

        var proto = _prototype.Index(roleInfo.Role);
        EntityManager.RemoveComponents(ent, proto.ComponentsAddToUser);

        args.Handled = true;
        _actionBlocker.UpdateCanMove(ent);
        UpdateInterface(ent.Comp.Vehicle);
    }
}


public sealed partial class SWExitVehicleRequested : InstantActionEvent
{

}
