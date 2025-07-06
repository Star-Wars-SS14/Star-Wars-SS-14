using Content.Shared.Hands;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._SW;

public abstract class SWSharedMeleeWeaponSystem : EntitySystem
{
    [Dependency] IGameTiming _gameTiming = default!;
    [Dependency] protected SharedPopupSystem PopupSystem = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<SWTryReflectEvent>(OnTryBlock);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandSelectedEvent>(OnSelectInHand);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandDeselectedEvent>(OnDeselectInHand);
        SubscribeLocalEvent<SWForceUserBlockComponent, TargetMeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<SWWeaponBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
    }

    private void OnDeselectInHand(Entity<SWWeaponBlockComponent> ent, ref HandDeselectedEvent args)
    {
        if (!TryComp<SWForceUserBlockComponent>(args.User, out var comp))
        {
            return;
        }
        DisableBlock(args.User);
        comp.CurrentForceWeapon = null;
        ent.Comp.CurrentUser = null;
    }

    private void OnSelectInHand(Entity<SWWeaponBlockComponent> ent, ref HandSelectedEvent args)
    {
        if (TryComp<SWForceUserBlockComponent>(args.User, out var comp))
        {
            ent.Comp.CurrentUser = args.User;
            comp.CurrentForceWeapon = ent.Owner;
        }
    }

    private void OnAttemptMelee(Entity<SWWeaponBlockComponent> ent, ref AttemptMeleeEvent args)
    {
        if (!TryComp<SWForceUserBlockComponent>(ent.Comp.CurrentUser, out var userComp))
        {
            return;
        }
        if (userComp.IsBlocking)
        {
            DisableBlock(ent.Comp.CurrentUser.Value);
        }
    }

    private void DisableBlock(EntityUid user)
    {
        if (!TryComp<SWForceUserBlockComponent>(user, out var userComp)
            || !TryComp<ReflectComponent>(userComp.CurrentForceWeapon, out var reflComp))
        {
            return;
        }

        PopupSystem.PopupPredicted("Вы убираете блок!", user, null, PopupType.Medium);
        reflComp.ReflectProb = 0.0f;

        userComp.IsBlocking = false;
    }

    private void OnMeleeHit(Entity<SWForceUserBlockComponent> ent, ref TargetMeleeHitEvent args)
    {
        if (ent.Comp.CurrentForceWeapon != null)
        {
            if (ent.Comp.IsBlocking)
            {
                args.Handled = true;
                PopupSystem.PopupPredicted("Удар отражен!", ent.Owner, null, PopupType.Medium);
                DisableBlock(ent.Comp.CurrentForceWeapon.Value);
            }
        }
    }
    private void OnTryBlock(SWTryReflectEvent ev)
    {
        if (!TryComp<ReflectComponent>(GetEntity(ev.Weapon), out var reflectComp))
        {
            return;
        }

        if (!TryComp<SWWeaponBlockComponent>(GetEntity(ev.Weapon), out var comp))
        {
            return;
        }

        if (!TryComp<SWForceUserBlockComponent>(comp.CurrentUser, out var forceComp))
        {
            return;
        }

        if (!_toggle.IsActivated(GetEntity(ev.Weapon)))
        {
            return;
        }

        if (_gameTiming.CurTime >= forceComp.NextReflectionAvailable && !forceComp.IsBlocking)
        {
            PopupSystem.PopupPredicted("Вы ставите блок!", GetEntity(ev.Weapon), null, PopupType.Medium);
            reflectComp.ReflectProb = 1f;
            forceComp.IsBlocking = true;
            forceComp.BlockEndTime = _gameTiming.CurTime + TimeSpan.FromSeconds(forceComp.BlockDuration);
            forceComp.NextReflectionAvailable = _gameTiming.CurTime + TimeSpan.FromSeconds(forceComp.BlockDuration) + TimeSpan.FromSeconds(forceComp.DelayBetweenBlocks);
        }
        else if (forceComp.IsBlocking)
        {
            PopupSystem.PopupPredicted("Вы уже выставили блок!", GetEntity(ev.Weapon), null, PopupType.Medium);
        }
        else if (_gameTiming.CurTime < forceComp.NextReflectionAvailable)
        {
            PopupSystem.PopupPredicted("Вы не можете ставить блок так часто!", GetEntity(ev.Weapon), null, PopupType.Medium);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWForceUserBlockComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.BlockEndTime <= _gameTiming.CurTime && comp.IsBlocking)
            {
                DisableBlock(ent);
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
