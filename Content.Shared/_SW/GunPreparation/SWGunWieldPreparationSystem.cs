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
using Robust.Shared.Containers;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.GunPreparation;

public sealed class SWGunWieldPreparationSystem : EntitySystem
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
        SubscribeLocalEvent<SWGunWieldPreparationComponent, HandSelectedEvent>(OnSelect);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, HandDeselectedEvent>(OnDeselect);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, EntGotInsertedIntoContainerMessage>(OnInsert);
        SubscribeLocalEvent<SWGunWieldPreparationComponent, SWPreparationFinishedDoAfterEvent>(OnFinish);

        SubscribeLocalEvent<SWPreparationWieldGunUserComponent, WieldAttemptEvent>(OnAttemptWield);
        SubscribeLocalEvent<SWPreparationWieldGunUserComponent, MoveEvent>(OnMove);
    }

    private void OnSelect(Entity<SWGunWieldPreparationComponent> ent, ref HandSelectedEvent args)
    {
        EnablePreparation(ent, args.User);
    }

    private void OnDeselect(Entity<SWGunWieldPreparationComponent> ent, ref HandDeselectedEvent args)
    {
        DisablePreparation(ent);
    }

    private void OnInsert(Entity<SWGunWieldPreparationComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        DisablePreparation(ent);
        ent.Comp.Prepared = false;
    }

    private void OnFinish(Entity<SWGunWieldPreparationComponent> ent, ref SWPreparationFinishedDoAfterEvent args)
    {
        ent.Comp.Prepared = true;
        _wieldableSystem.TryWield(ent.Owner, Comp<WieldableComponent>(ent), args.User);
    }

    private void DisablePreparation(Entity<SWGunWieldPreparationComponent> ent)
    {
        if (ent.Comp.CurrentUser == null)
        {
            return;
        }

        RemComp<SWPreparationWieldGunUserComponent>(ent.Comp.CurrentUser.Value);
    }

    private void EnablePreparation(Entity<SWGunWieldPreparationComponent> ent, EntityUid user)
    {
        var comp = EnsureComp<SWPreparationWieldGunUserComponent>(user);
        comp.Gun = ent;
        ent.Comp.CurrentUser = user;
    }

    private void OnAttemptWield(Entity<SWPreparationWieldGunUserComponent> ent, ref WieldAttemptEvent args)
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
    private void OnMove(Entity<SWPreparationWieldGunUserComponent> ent, ref MoveEvent args)
    {
        bool positionChanged = args.NewPosition != args.OldPosition;

        if (ent.Comp.Gun == null || !ent.Comp.Gun.Value.Comp.Prepared || !positionChanged)
        {
            return;
        }

        DisablePreparation(ent.Owner, ent.Comp.Gun.Value);
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
        _popupSystem.PopupPredicted(Loc.GetString("sw-preparation-break"), ent.Owner, null);
    }
}

public sealed partial class SWRequestPreparationEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class SWPreparationFinishedDoAfterEvent : SimpleDoAfterEvent
{
}
