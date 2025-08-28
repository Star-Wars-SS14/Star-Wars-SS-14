using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
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
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGunPreparationComponent, ComponentStartup>(OnCompStartup);
        SubscribeLocalEvent<SWGunPreparationComponent, SWRequestPreparationEvent>(OnRequest);
        SubscribeLocalEvent<SWGunPreparationComponent, SWPreparationFinishedDoAfterEvent>(OnFinish);
        SubscribeLocalEvent<SWGunPreparationComponent, GetItemActionsEvent>(OnGetActions);

        SubscribeLocalEvent<HandsComponent, MoveEvent>(OnMove);

        SubscribeLocalEvent<SWGunRequiresPreparationComponent, ShotAttemptedEvent>(OnShotAttempt);
    }

    private void OnMove(Entity<HandsComponent> ent, ref MoveEvent args)
    {
        var held = _handsSystem.EnumerateHeld(new Entity<HandsComponent?>(ent.Owner, ent.Comp));
        foreach (var item in held)
        {
            if (TryComp<SWGunPreparationComponent>(item, out var comp) && comp.Prepared){
                comp.Prepared = false;
                _popupSystem.PopupEntity(Loc.GetString("sw-preparation-on-move"), ent.Owner);
            }
        }
    }
    private void OnCompStartup(Entity<SWGunPreparationComponent> ent, ref ComponentStartup args)
    {
        _action.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.AddAction(ent, ref ent.Comp.SelfActionEntity, ent.Comp.Action);
    }
    private void OnRequest(Entity<SWGunPreparationComponent> ent, ref SWRequestPreparationEvent args)
    {
        if (args == null)
        {
            return;
        }
        var doafterArgs = new DoAfterArgs
            (EntityManager,
            args.Performer,
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
    private void OnFinish(Entity<SWGunPreparationComponent> ent, ref SWPreparationFinishedDoAfterEvent args)
    {
        ent.Comp.Prepared = true;
    }

    private void OnGetActions(Entity<SWGunPreparationComponent> ent, ref GetItemActionsEvent args)
    {
        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }
    private void OnShotAttempt(Entity<SWGunRequiresPreparationComponent> ent, ref ShotAttemptedEvent args)
    {
        if (!TryComp<SWGunPreparationComponent>(ent.Owner, out var comp))
        {
            return;
        }

        if (!comp.Prepared)
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
