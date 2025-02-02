using Content.Shared._SW.Force.Components;
using Content.Shared.Popups;
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
    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<SWTryReflectEvent>(OnMakeAttack);
        _s = Logger.GetSawmill("swmeleeee");
    }

    private void OnMakeAttack(SWTryReflectEvent ev)
    {
        if (!TryComp<ReflectComponent>(GetEntity(ev.Weapon), out var reflectComp))
        {
            return;
        }

        if (!TryComp<SWForceUserReflectionComponent>(GetEntity(ev.Weapon), out var forceComp))
        {
            return;
        }

        _s.Debug("got");
        if (_gameTiming.CurTime >= forceComp.NextReflectionAvailable)
        {
            _s.Debug("start");
            _popupSystem.PopupPredicted("Вы ставите блок!", GetEntity(ev.Weapon), null, PopupType.Medium);
            reflectComp.Enabled = true;
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
        var query = EntityQueryEnumerator<SWForceUserReflectionComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (TryComp<ReflectComponent>(ent, out var reflComp))
            {
                if (comp.ReflectionEndTime <= _gameTiming.CurTime && reflComp.Enabled)
                {
                    _popupSystem.PopupPredicted("Вы убираете блок!", ent, null, PopupType.Medium);
                    _s.Debug("stopped");
                    reflComp.Enabled = false;
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
