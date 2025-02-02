using Content.Shared._SW.Force.Components;
using Content.Shared.Hands;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Linguini.Bundle.Errors;
using Microsoft.VisualBasic;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

public sealed class SWMeleeWeaponSystem : EntitySystem
{
    ISawmill _s = default!;
    [Dependency] IGameTiming _gameTiming = default!;
    [Dependency] SharedPopupSystem _popupSystem = default!;
    [Dependency] TagSystem _tagSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<SWTryReflectEvent>(OnMakeAttack);
        SubscribeLocalEvent<SWForceUserBlockComponent, DidEquipHandEvent>(OnEquipHand);
        SubscribeLocalEvent<SWForceUserBlockComponent, DidUnequipHandEvent>(OnUnequipHand);
        SubscribeLocalEvent<SWForceUserBlockComponent, TargetMeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<SWWeaponBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
        _s = Logger.GetSawmill("swmeleeee");
    }

    private void OnAttemptMelee(Entity<SWWeaponBlockComponent> ent, ref AttemptMeleeEvent args)
    {
        args.Cancelled = ent.Comp.IsBlocking;        
    }

    private void OnMeleeHit(Entity<SWForceUserBlockComponent> ent, ref TargetMeleeHitEvent args)
    {
        _s.Debug($"{ent.Comp.CurrentForceWeapon}");
        if (ent.Comp.CurrentForceWeapon != null && TryComp<SWWeaponBlockComponent>(ent.Comp.CurrentForceWeapon, out var weaponComp))
        {
            if (weaponComp.IsBlocking) {
                args.Handled = true;
                _popupSystem.PopupPredicted("Удар отражен!", ent.Owner, null, PopupType.Medium);
            }
        }
    }

    private void OnUnequipHand(Entity<SWForceUserBlockComponent> ent, ref DidUnequipHandEvent args)
    {
        if (!TryComp<SWForceUserBlockComponent>(ent.Owner, out var comp))
        {
            return;
        }
        if (comp.CurrentForceWeapon == args.Unequipped)
        {
            comp.CurrentForceWeapon = null;
        }
    }

    private void OnEquipHand(Entity<SWForceUserBlockComponent> ent, ref DidEquipHandEvent args)
    {
        if (_tagSystem.HasTag(args.Equipped, "ForceUserWeapon") && HasComp<SWWeaponBlockComponent>(args.Equipped))
        {
            ent.Comp.CurrentForceWeapon = args.Equipped;
        }
    }

    private void OnMakeAttack(SWTryReflectEvent ev)
    {
        if (!TryComp<ReflectComponent>(GetEntity(ev.Weapon), out var reflectComp))
        {
            return;
        }

        if (!TryComp<SWWeaponBlockComponent>(GetEntity(ev.Weapon), out var forceComp))
        {
            return;
        }

        _s.Debug("got");
        if (_gameTiming.CurTime >= forceComp.NextReflectionAvailable)
        {
            _s.Debug("start");
            _popupSystem.PopupPredicted("Вы ставите блок!", GetEntity(ev.Weapon), null, PopupType.Medium);
            reflectComp.Enabled = true;
            forceComp.IsBlocking = true;
            forceComp.ReflectionEndTime = _gameTiming.CurTime + TimeSpan.FromSeconds(forceComp.ReflectionDuration);
            forceComp.NextReflectionAvailable = _gameTiming.CurTime + TimeSpan.FromSeconds(forceComp.ReflectionDuration) + TimeSpan.FromSeconds(forceComp.DelayBetweenReflections);
        } else
        {
            //_popupSystem.PopupPredicted("Вы не можете ставить блок так часто!", GetEntity(ev.Weapon), null, PopupType.Medium);
        }
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWWeaponBlockComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (TryComp<ReflectComponent>(ent, out var reflComp))
            {
                if (comp.ReflectionEndTime <= _gameTiming.CurTime && reflComp.Enabled)
                {
                    _popupSystem.PopupPredicted("Вы убираете блок!", ent, null, PopupType.Medium);
                    _s.Debug("stopped");
                    reflComp.Enabled = false;
                    comp.IsBlocking = false;
                }
            }
        }
    }
}

[Serializable, NetSerializable]
public sealed class SWTryReflectEvent : EntityEventArgs
{
    public readonly NetEntity Weapon;
    public SWTryReflectEvent(NetEntity weapon)
    {
        Weapon = weapon;
    }
}
