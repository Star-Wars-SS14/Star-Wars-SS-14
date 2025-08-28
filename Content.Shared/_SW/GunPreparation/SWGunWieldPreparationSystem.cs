using Content.Shared._SW.GunPreparation;
using Content.Shared.Actions;
using Content.Shared.Camera;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.GunPreparation;

public sealed class SWGunPreparationSystem : EntitySystem
{
    [Dependency] ActionContainerSystem _action = default!;
    [Dependency] SharedActionsSystem _actions = default!;
    [Dependency] SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] SharedHandsSystem _handsSystem = default!;
    [Dependency] SharedPopupSystem _popupSystem = default!;
    [Dependency] SharedWieldableSystem _wieldableSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGunWieldPreparationComponent, ComponentStartup>(OnCompStartup);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, SWRequestPreparationEvent>(OnRequest);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, SWPreparationFinishedDoAfterEvent>(OnFinish);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, HandSelectedEvent>(OnSelect);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, HandDeselectedEvent>(OnDeselect);
       

        SubscribeLocalEvent<HandsComponent, MoveEvent>(OnMove);

        SubscribeLocalEvent<SWGunWieldPreparationComponent, ShotAttemptedEvent>(OnShotAttempt);

        SubscribeLocalEvent<SWPreparationGunUserComponent, WieldAttemptEvent>(OnAttemptWield);
    }

    private void OnDeselect(Entity<SWGunWieldPreparationComponent> ent, ref HandDeselectedEvent args)
    {
        RemComp<SWPreparationGunUserComponent>(args.User);
    }

    private void OnSelect(Entity<SWGunWieldPreparationComponent> ent, ref HandSelectedEvent args)
    {
        var comp = EnsureComp<SWPreparationGunUserComponent>(args.User);
        comp.Gun = ent;
    }

    private void OnAttemptWield(Entity<SWPreparationGunUserComponent> ent, ref WieldAttemptEvent args)
    {
        if (ent.Comp.Gun == null)
        {
            return;
        }
        if (ent.Comp.Gun.Value.Comp.Prepared)
        {
            return;
        }
        args.Cancel();
        StartPreparation(ent.Owner, ent.Comp.Gun.Value);
    }

    private void OnCompStartup(Entity<SWGunWieldPreparationComponent> ent, ref ComponentStartup args)
    {
        _action.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.AddAction(ent, ref ent.Comp.SelfActionEntity, ent.Comp.Action);
    }
    private void OnRequest(Entity<SWGunWieldPreparationComponent> ent, ref SWRequestPreparationEvent args)
    {
        if (args == null)
        {
            return;
        }
        args.Handled = true;
    }

    private void StartPreparation(EntityUid user, Entity<SWGunWieldPreparationComponent> ent)
    {
        var doafterArgs = new DoAfterArgs
            (EntityManager,
            user,
            ent.Comp.PreparationTime,
            new SWPreparationFinishedDoAfterEvent(),
            ent)
        {
            BreakOnDamage = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            BreakOnMove = true
        };
        _doAfterSystem.TryStartDoAfter(doafterArgs);
    }

    private void DisablePreparation(EntityUid user, Entity<SWGunWieldPreparationComponent> ent)
    {
        ent.Comp.Prepared = false;
        _wieldableSystem.TryUnwield(ent.Owner, Comp<WieldableComponent>(ent), user);
        _popupSystem.PopupPredicted(Loc.GetString("sw-preparation-on-move"), ent.Owner, null);
    }
    private void OnFinish(Entity<SWGunWieldPreparationComponent> ent, ref SWPreparationFinishedDoAfterEvent args)
    {
        ent.Comp.Prepared = true;
        _wieldableSystem.TryWield(ent.Owner, Comp<WieldableComponent>(ent), args.User);
    }

    private void OnGetActions(Entity<SWGunWieldPreparationComponent> ent, ref GetItemActionsEvent args)
    {
        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnMove(Entity<HandsComponent> ent, ref MoveEvent args)
    {
        var held = _handsSystem.EnumerateHeld(new Entity<HandsComponent?>(ent.Owner, ent.Comp));
        foreach (var item in held)
        {
            if (TryComp<SWGunWieldPreparationComponent>(item, out var comp) && comp.Prepared)
            {
                DisablePreparation(ent.Owner, new Entity<SWGunWieldPreparationComponent>(item, comp));
            }
        }
    }
    private void OnShotAttempt(Entity<SWGunWieldPreparationComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!ent.Comp.Prepared)
        {
            args.Cancel();
        }
    }
}

public sealed partial class SWRequestPreparationEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class SWPreparationFinishedDoAfterEvent : SimpleDoAfterEvent
{
}
