using Content.Shared._SW.Vehicle;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Destructible;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Shared._SW.Vehicle;
public sealed class SWVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWInVehicleComponent, SWExitVehicleRequested>(OnExit);
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWVehicleComponent, SWOccupyRoleMessage>(OnOccupy);
        //SubscribeLocalEvent<SWInVehicleComponent, UpdateCanMoveEvent>(OnMoveInVehicle);
        //SubscribeLocalEvent<SWVehicleComponent, DestructionEventArgs>(OnDestroy);
        SubscribeLocalEvent<SWVehicleComponent, EntityTerminatingEvent>(OnTerminating);
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
        TryOccupyRole(ent, args.Actor, args.Role);
        UpdateInterface(ent);
    }

    //private void OnMoveInVehicle(Entity<SWInVehicleComponent> ent, ref UpdateCanMoveEvent args)
    //{
    //    args.Cancel();
    //}
    public void TryOccupyRole(Entity<SWVehicleComponent> ent, EntityUid user, string role)
    {
        var proto = _prototype.Index(ent.Comp.RolesInfo[role].Role);

        EntityManager.AddComponents(user, proto.ComponentsAddToUser);
        var ev = new SWUserTryEnterVehicleEvent(ent);

        RaiseLocalEvent(user, ref ev);
        if (ev.Cancelled)
        {
            _popupSystem.PopupEntity(ev.Message, user);
            EntityManager.RemoveComponents(user, proto.ComponentsAddToUser);
            return;
        }
        OccupyRoleInner(ent, user, role);
    }


    private void OccupyRoleInner(Entity<SWVehicleComponent> ent, EntityUid user, string role)
    {
        if (HasComp<SWInVehicleComponent>(user))
        {
            return;
        }

        _actionBlocker.UpdateCanMove(user);
        var comp = AddComp<SWInVehicleComponent>(user);
        comp.Vehicle = ent;
        comp.OccupiedRole = role;
        _actions.AddAction(user, ref comp.ActionEntity, SWSharedVehicleSystem.ActionExitVehicle);
        Dirty(user, comp);
        ent.Comp.RolesInfo[role].Occupant = GetNetEntity(user);

        var evOnUser = new SWUserEnterVehicleEvent(ent);
        RaiseLocalEvent(user, evOnUser);

        var evOnVehicle = new SWVehicleIsEnteredEvent(user, comp);
        RaiseLocalEvent(ent, evOnVehicle);
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

        if (TerminatingOrDeleted(occupantEntity) ||
            !TryComp<SWInVehicleComponent>(occupantEntity, out var inVehicle))
        {
            roleInfo.Occupant = null;
            return;
        }

        var occupantCoordinates = _transformSystem.ToMapCoordinates(Transform(occupantEntity).Coordinates);
        Entity<SWInVehicleComponent> occupant = new Entity<SWInVehicleComponent>(occupantEntity, Comp<SWInVehicleComponent>(occupantEntity));

        if (occupant.Comp.ActionEntity != null)
        {
            _actions.RemoveAction(occupant.Owner, occupant.Comp.ActionEntity);
        }

        var proto = _prototype.Index(roleInfo.Role);

        //Raise event before deletion to properly shutdown all components
        var evOnUser = new SWUserExitVehicleEvent(vehicle);
        RaiseLocalEvent(occupant, evOnUser);
        var evOnVehicle = new SWVehicleIsExitedEvent(occupant);
        RaiseLocalEvent(vehicle, evOnVehicle);

        RemComp<SWInVehicleComponent>(occupant);
        EntityManager.RemoveComponents(occupant, proto.ComponentsAddToUser);
        DetachOccupantFromVehicle((occupant.Owner, Transform(occupant.Owner)), vehicle, occupantCoordinates);

        _actionBlocker.UpdateCanMove(occupant);
        roleInfo.Occupant = null;
        UpdateInterface(vehicle);
    }

    private void OnTerminating(Entity<SWVehicleComponent> ent, ref EntityTerminatingEvent args)
    {
        DeoccupyAllRoles(ent);
    }

    private void DeoccupyAllRoles(Entity<SWVehicleComponent> vehicle)
    {
        vehicle.Comp.RolesInfo.Keys.ToList().ForEach(roleName => DeoccupyRole(vehicle, roleName));
    }

    private void DetachOccupantFromVehicle(
        Entity<TransformComponent> occupant,
        Entity<SWVehicleComponent> vehicle,
        MapCoordinates fallbackCoordinates)
    {
        if (TerminatingOrDeleted(occupant.Owner))
            return;

        if (occupant.Comp.ParentUid == EntityUid.Invalid ||
            IsAttachedToVehicle(occupant.Comp.ParentUid, vehicle))
        {
            _transformSystem.SetMapCoordinates(occupant, fallbackCoordinates);
        }
    }

    private bool IsAttachedToVehicle(EntityUid entity, EntityUid vehicle)
    {
        var current = entity;
        while (current.IsValid())
        {
            if (current == vehicle)
                return true;

            if (!TryComp(current, out TransformComponent? xform))
                return false;

            current = xform.ParentUid;
        }

        return false;
    }

    public NetEntity? GetPart(EntityUid vehicle, string partId)
    {
        var vehicleComp = Comp<SWVehicleComponent>(vehicle);
        if (vehicleComp.VehicleParts.TryGetValue(partId, out var part))
        {
            return part.SpawnedPart;
        }

        return null;
    }
}
