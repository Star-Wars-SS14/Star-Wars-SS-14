using Content.Shared._SW.Vehicle;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
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
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWInVehicleComponent, SWExitVehicleRequested>(OnExit);
        SubscribeLocalEvent<SWVehicleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWVehicleComponent, SWOccupyRoleMessage>(OnOccupy);
        SubscribeLocalEvent<SWVehicleExtractableComponent, GetVerbsEvent<AlternativeVerb>>(OnGetExtractVerbs);
        SubscribeLocalEvent<SWVehicleExtractableComponent, SWVehicleExtractDoAfterEvent>(OnExtractDoAfter);
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
        if (HasComp<SWInVehicleComponent>(user))
        {
            _popupSystem.PopupEntity(Loc.GetString("sw-vehicle-unable-enter-default"), user);
            return;
        }

        if (!ent.Comp.RolesInfo.TryGetValue(role, out var roleInfo))
            return;

        if (roleInfo.Occupant != null &&
            TryGetEntity(roleInfo.Occupant.Value, out var occupant) &&
            !TerminatingOrDeleted(occupant.Value))
        {
            _popupSystem.PopupEntity(Loc.GetString("sw-vehicle-unable-enter-default"), user);
            return;
        }

        var proto = _prototype.Index(roleInfo.Role);

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

        args.Handled = TryDeoccupy(ent, new Entity<SWVehicleComponent>(ent.Comp.Vehicle, vehicleComp));
    }

    public bool TryDeoccupy(Entity<SWInVehicleComponent> occupant)
    {
        if (!TryComp<SWVehicleComponent>(occupant.Comp.Vehicle, out var vehicleComp))
            return false;

        return TryDeoccupy(occupant, (occupant.Comp.Vehicle, vehicleComp));
    }

    public bool TryDeoccupy(Entity<SWInVehicleComponent> occupant, Entity<SWVehicleComponent> vehicle)
    {
        if (TerminatingOrDeleted(occupant.Owner) ||
            occupant.Comp.Vehicle != vehicle.Owner ||
            !vehicle.Comp.RolesInfo.TryGetValue(occupant.Comp.OccupiedRole, out var roleInfo))
        {
            return false;
        }

        return DeoccupyOccupant(occupant, vehicle, roleInfo);
    }

    public bool TryDeoccupyRole(Entity<SWVehicleComponent> vehicle, string occupiedRole)
    {
        var roleInfo = vehicle.Comp.RolesInfo[occupiedRole];

        if (roleInfo.Occupant == null)
        {
            return false;
        }

        if (!TryGetEntity(roleInfo.Occupant.Value, out var occupantEntity))
        {
            roleInfo.Occupant = null;
            return false;
        }

        var occupantUid = occupantEntity.Value;

        if (TerminatingOrDeleted(occupantUid))
        {
            roleInfo.Occupant = null;
            return false;
        }

        if (!TryComp<SWInVehicleComponent>(occupantUid, out var inVehicle))
        {
            roleInfo.Occupant = null;
            return false;
        }

        return DeoccupyOccupant((occupantUid, inVehicle), vehicle, roleInfo);
    }

    private bool DeoccupyOccupant(
        Entity<SWInVehicleComponent> occupant,
        Entity<SWVehicleComponent> vehicle,
        SWVehicleRoleInfo roleInfo)
    {
        if (TerminatingOrDeleted(occupant.Owner))
            return false;

        var occupantCoordinates = _transformSystem.ToMapCoordinates(Transform(occupant.Owner).Coordinates);

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

        var occupantNet = GetNetEntity(occupant.Owner);
        if (roleInfo.Occupant == null ||
            roleInfo.Occupant == occupantNet ||
            !TryGetEntity(roleInfo.Occupant.Value, out _))
        {
            roleInfo.Occupant = null;
        }

        UpdateInterface(vehicle);
        return true;
    }

    private void OnTerminating(Entity<SWVehicleComponent> ent, ref EntityTerminatingEvent args)
    {
        DeoccupyAllRoles(ent);
    }

    private void DeoccupyAllRoles(Entity<SWVehicleComponent> vehicle)
    {
        vehicle.Comp.RolesInfo.Keys.ToList().ForEach(roleName => TryDeoccupyRole(vehicle, roleName));
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

    private void OnGetExtractVerbs(Entity<SWVehicleExtractableComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !TryComp<SWVehicleComponent>(ent, out var vehicle))
        {
            return;
        }

        foreach (var (roleId, roleInfo) in vehicle.RolesInfo)
        {
            if (roleInfo.Occupant == null ||
                !TryGetEntity(roleInfo.Occupant.Value, out var occupant) ||
                args.User == occupant.Value ||
                TerminatingOrDeleted(occupant.Value))
            {
                continue;
            }

            var rolePrototype = _prototype.Index(roleInfo.Role);
            var roleName = Loc.GetString(rolePrototype.LocalizedName);
            var delay = GetExtractionDelay(occupant.Value, ent.Comp);
            var occupantNet = roleInfo.Occupant.Value;
            var user = args.User;

            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("sw-vehicle-extract-verb", ("role", roleName)),
                Category = VerbCategory.Eject,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/unbuckle.svg.192dpi.png")),
                Act = () => StartExtraction(ent, user, roleId, occupantNet, delay),
            });
        }
    }

    private void StartExtraction(
        Entity<SWVehicleExtractableComponent> vehicle,
        EntityUid user,
        string role,
        NetEntity occupant,
        float delay)
    {
        if (!TryComp<SWVehicleComponent>(vehicle, out var vehicleComp) ||
            !RoleHasOccupant(vehicleComp, role, occupant))
        {
            return;
        }

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            delay,
            new SWVehicleExtractDoAfterEvent(role, occupant),
            vehicle,
            target: vehicle)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnExtractDoAfter(Entity<SWVehicleExtractableComponent> ent, ref SWVehicleExtractDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled ||
            !TryComp<SWVehicleComponent>(ent, out var vehicle) ||
            !RoleHasOccupant(vehicle, args.Role, args.Occupant))
        {
            return;
        }

        if (TryDeoccupyRole((ent.Owner, vehicle), args.Role))
            _popupSystem.PopupEntity(Loc.GetString("sw-vehicle-extract-finished"), ent, args.User);

        args.Handled = true;
    }

    private bool RoleHasOccupant(SWVehicleComponent vehicle, string role, NetEntity occupant)
    {
        return vehicle.RolesInfo.TryGetValue(role, out var roleInfo) &&
               roleInfo.Occupant == occupant;
    }

    private float GetExtractionDelay(EntityUid occupant, SWVehicleExtractableComponent component)
    {
        if (!TryComp<MobStateComponent>(occupant, out var mobState))
            return component.AliveDelay;

        return mobState.CurrentState switch
        {
            MobState.Dead => component.DeadDelay,
            MobState.Critical => component.CriticalDelay,
            _ => component.AliveDelay,
        };
    }
}

[Serializable, NetSerializable]
public sealed partial class SWVehicleExtractDoAfterEvent : DoAfterEvent
{
    public string Role;
    public NetEntity Occupant;

    public SWVehicleExtractDoAfterEvent(string role, NetEntity occupant)
    {
        Role = role;
        Occupant = occupant;
    }

    public override DoAfterEvent Clone()
    {
        return new SWVehicleExtractDoAfterEvent(Role, Occupant);
    }
}
