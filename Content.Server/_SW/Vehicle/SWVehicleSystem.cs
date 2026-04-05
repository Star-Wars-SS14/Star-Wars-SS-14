using Content.Shared._SW.Vehicle;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Movement.Events;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        SubscribeLocalEvent<SWInVehicleComponent, UpdateCanMoveEvent>(OnMoveInVehicle);
    }

    private void OnMapInit(Entity<SWVehicleComponent> ent, ref MapInitEvent args)
    {
        foreach (var (part, info) in ent.Comp.VehicleParts)
        {
            ent.Comp.VehicleParts[part].SpawnedPart = SpawnWithPlacementInfo(part, info, ent);
        }
        UpdateInterface(ent);
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

    private void OnMoveInVehicle(Entity<SWInVehicleComponent> ent, ref UpdateCanMoveEvent args)
    {
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

        var ev = new SWUserEnterVehicleEvent(ent);
        RaiseLocalEvent(user, ev);
        _actionBlocker.UpdateCanMove(user);
    }
    private EntityUid SpawnWithPlacementInfo(ProtoId<EntityPrototype> proto, SWVehiclePartInfo info, EntityUid parent)
    {
        var coords = new EntityCoordinates(parent, info.BuckleCoords);
        return Spawn(proto, coords);
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

        if (!TryComp<SWVehicleComponent>(ent.Comp.Vehicle, out var comp))
        {
            return;
        }

        var roleInfo = comp.RolesInfo[ent.Comp.OccupiedRole];
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
        UpdateInterface(new Entity<SWVehicleComponent>(ent.Comp.Vehicle, comp));
    }
}
