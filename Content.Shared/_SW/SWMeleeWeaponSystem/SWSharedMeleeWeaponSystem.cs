using Content.Shared.Hands;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
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
        //SubscribeLocalEvent<SWForceUserBlockComponent, TargetMeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<SWWeaponBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<SWForceUserBlockComponent, HitByLightningEvent>(OnLightningHit);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
        {
            return;
        }

        EntityUid? target = null;
        //From all hit entities we choose one forceuser, if there are any
        foreach (EntityUid entity in args.HitEntities)
        {
            if (HasComp<SWForceUserBlockComponent>(entity))
            {
                target = entity;
                break;
            }
        }

        if (target == null || !TryComp<SWForceUserBlockComponent>(target, out var userBlockComp))
        {
            return;
        }

        //We disable block only if forceuser is being attacked by forceuser with an energetic sword
        if (userBlockComp.IsBlocking)
        {
            args.Handled = true;
            PopupSystem.PopupPredicted("Удар отражен!", ent.Owner, null, PopupType.Medium);
            if (HasComp<SWWeaponBlockComponent>(args.Weapon)
                && HasComp<SWForceUserBlockComponent>(args.User))
            {
                DisableBlock(target.Value);
            }
        }
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
        userComp.NextReflectionAvailable = _gameTiming.CurTime + TimeSpan.FromSeconds(userComp.DelayBetweenBlocks);


        userComp.IsBlocking = false;
    }

    private void OnTryBlock(SWTryReflectEvent ev)
    {
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

        if (CanBlock(comp.CurrentUser.Value, forceComp))
        {
            EnableBlock(GetEntity(ev.Weapon), comp.CurrentUser.Value);
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

    private void EnableBlock(EntityUid weapon, EntityUid forceUser)
    {
        var reflectComp = Comp<ReflectComponent>(weapon);
        var forceComp = Comp<SWForceUserBlockComponent>(forceUser);
        PopupSystem.PopupPredicted("Вы ставите блок!", weapon, null, PopupType.Medium);
        reflectComp.ReflectProb = 1f;
        forceComp.IsBlocking = true;
        forceComp.BlockEndTime = _gameTiming.CurTime + TimeSpan.FromSeconds(forceComp.BlockDuration);
    }
    private void OnLightningHit(Entity<SWForceUserBlockComponent> ent, ref HitByLightningEvent args)
    {
        if (CanBlock(ent.Owner, ent.Comp))
        {
            DebugTools.Assert(ent.Comp.CurrentForceWeapon != null);
            EnableBlock(ent.Comp.CurrentForceWeapon.Value, ent.Owner);
        }
        if (ent.Comp.IsBlocking)
        {
            args.Cancel();
            PopupSystem.PopupPredicted("Удар отражен!", ent.Owner, null, PopupType.Medium);
        }
    }

    public bool CanBlock(EntityUid forceUser, SWForceUserBlockComponent? forceComp = null)
    {
        if (forceComp == null)
        {
            forceComp = Comp<SWForceUserBlockComponent>(forceUser);
        }

        if (forceComp.CurrentForceWeapon == null)
        {
            return false;
        }

        if (_gameTiming.CurTime >= forceComp.NextReflectionAvailable && !forceComp.IsBlocking)
        {
            return true;
        }

        return false;
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
