using System;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._SW.ForceAbilities.SWMeleeWeaponSystem;

public abstract class SWSharedMeleeWeaponSystem : EntitySystem
{
    private const string HitBlockedPopup = "sw-weapon-block-hit-blocked";
    private const string LowerBlockPopup = "sw-weapon-block-lower";
    private const string AlreadyBlockingPopup = "sw-weapon-block-already-blocking";
    private const string BlockCooldownPopup = "sw-weapon-block-cooldown";
    private const string RaiseBlockPopup = "sw-weapon-block-raise";
    private static readonly ProtoId<TagPrototype> LightsaberTag = "SWEnergySwordTag";

    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] protected readonly SharedPopupSystem PopupSystem = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<SWTryReflectEvent>(OnTryBlock);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandSelectedEvent>(OnSelectInHand);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandDeselectedEvent>(OnDeselectInHand);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<SWWeaponBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<SWForceUserBlockComponent, HitByLightningEvent>(OnLightningHit);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var entity in args.HitEntities)
        {
            if (!TryGetCurrentBlockWeapon(entity, out var blockWeapon) ||
                !blockWeapon.Comp.IsBlocking ||
                !blockWeapon.Comp.BlocksMelee)
            {
                continue;
            }

            args.Handled = true;
            PopupSystem.PopupPredicted(Loc.GetString(HitBlockedPopup), ent.Owner, null, PopupType.Medium);
            DisableBlock(blockWeapon);
            break;
        }
    }

    private void OnDeselectInHand(Entity<SWWeaponBlockComponent> ent, ref HandDeselectedEvent args)
    {
        if (ent.Comp.CurrentUser != args.User)
            return;

        DisableBlock(ent);

        ent.Comp.CurrentUser = null;
    }

    private void OnSelectInHand(Entity<SWWeaponBlockComponent> ent, ref HandSelectedEvent args)
    {
        if (!CanUseBlockWeapon(args.User, ent.Comp))
            return;

        ent.Comp.CurrentUser = args.User;

    }

    private void OnAttemptMelee(Entity<SWWeaponBlockComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ent.Comp.IsBlocking)
            DisableBlock(ent);
    }

    private void DisableBlock(Entity<SWWeaponBlockComponent> ent)
    {
        if (!ent.Comp.IsBlocking)
            return;

        PopupSystem.PopupPredicted(Loc.GetString(LowerBlockPopup), ent.Comp.CurrentUser ?? ent.Owner, null, PopupType.Medium);
        RestoreProjectileReflection(ent);
        ent.Comp.NextReflectionAvailable = _gameTiming.CurTime + TimeSpan.FromSeconds(ent.Comp.DelayBetweenBlocks);
        ent.Comp.IsBlocking = false;
    }

    private void OnTryBlock(SWTryReflectEvent ev)
    {
        var weapon = GetEntity(ev.Weapon);
        if (!TryComp<SWWeaponBlockComponent>(weapon, out var comp))
            return;

        if (comp.CurrentUser == null || !CanUseBlockWeapon(comp.CurrentUser.Value, comp))
            return;

        if (!_toggle.IsActivated(weapon))
            return;

        var ent = (weapon, comp);
        if (CanBlock(ent))
        {
            EnableBlock(ent);
        }
        else if (comp.IsBlocking)
        {
            PopupSystem.PopupPredicted(Loc.GetString(AlreadyBlockingPopup), weapon, null, PopupType.Medium);
        }
        else if (_gameTiming.CurTime < comp.NextReflectionAvailable)
        {
            PopupSystem.PopupPredicted(Loc.GetString(BlockCooldownPopup), weapon, null, PopupType.Medium);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWWeaponBlockComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.BlockEndTime <= _gameTiming.CurTime && comp.IsBlocking)
                DisableBlock((ent, comp));
        }
    }

    private void EnableBlock(Entity<SWWeaponBlockComponent> weapon)
    {
        PopupSystem.PopupPredicted(Loc.GetString(RaiseBlockPopup), weapon.Comp.CurrentUser ?? weapon.Owner, null, PopupType.Medium);
        EnableProjectileReflection(weapon);
        weapon.Comp.IsBlocking = true;
        weapon.Comp.BlockEndTime = _gameTiming.CurTime + TimeSpan.FromSeconds(weapon.Comp.BlockDuration);
    }

    private void OnLightningHit(Entity<SWForceUserBlockComponent> ent, ref HitByLightningEvent args)
    {
        if (args.Cancelled)
            return;

        foreach (var held in _hands.EnumerateHeld(ent.Owner))
        {
            if (!_tag.HasTag(held, LightsaberTag) || !_toggle.IsActivated(held))
                continue;

            args.Cancel();
            PopupSystem.PopupPredicted(Loc.GetString(HitBlockedPopup), ent.Owner, null, PopupType.Medium);
            return;
        }
    }

    public bool BlockAvailable(Entity<SWWeaponBlockComponent> weapon)
    {
        if (weapon.Comp.CurrentUser == null || !CanUseBlockWeapon(weapon.Comp.CurrentUser.Value, weapon.Comp))
            return false;

        if (!weapon.Comp.BlocksMelee && !weapon.Comp.BlocksProjectiles)
            return false;

        if (TryComp<ItemToggleComponent>(weapon.Owner, out var toggleComp) && !toggleComp.Activated)
            return false;

        return true;
    }

    public bool CanBlock(Entity<SWWeaponBlockComponent> weapon)
    {
        if (!BlockAvailable(weapon))
            return false;

        return _gameTiming.CurTime >= weapon.Comp.NextReflectionAvailable && !weapon.Comp.IsBlocking;
    }

    private bool CanUseBlockWeapon(EntityUid user, SWWeaponBlockComponent comp)
    {
        return !comp.RequiresForceUser || HasComp<SWForceUserBlockComponent>(user);
    }

    private void EnableProjectileReflection(Entity<SWWeaponBlockComponent> weapon)
    {
        if (!weapon.Comp.BlocksProjectiles || !TryComp<ReflectComponent>(weapon.Owner, out var reflect))
            return;

        weapon.Comp.StoredReflectProb ??= reflect.ReflectProb;
        reflect.ReflectProb = weapon.Comp.ProjectileReflectProb;
        Dirty(weapon.Owner, reflect);
    }

    private void RestoreProjectileReflection(Entity<SWWeaponBlockComponent> weapon)
    {
        if (weapon.Comp.StoredReflectProb == null || !TryComp<ReflectComponent>(weapon.Owner, out var reflect))
            return;

        reflect.ReflectProb = weapon.Comp.StoredReflectProb.Value;
        weapon.Comp.StoredReflectProb = null;
        Dirty(weapon.Owner, reflect);
    }

    private bool TryGetCurrentBlockWeapon(EntityUid user, out Entity<SWWeaponBlockComponent> weapon)
    {
        var query = EntityQueryEnumerator<SWWeaponBlockComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.CurrentUser != user)
                continue;

            weapon = (uid, comp);
            return true;
        }

        weapon = default;
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
