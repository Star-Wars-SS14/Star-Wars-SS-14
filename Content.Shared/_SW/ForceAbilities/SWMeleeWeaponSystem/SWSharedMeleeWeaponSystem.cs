using System;
using Content.Shared._SW.SaberForms;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Network;
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
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] protected readonly SharedPopupSystem PopupSystem = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedSWSaberFormSystem _forms = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<SWTryReflectEvent>(OnTryBlock);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandSelectedEvent>(OnSelectInHand);
        SubscribeLocalEvent<SWWeaponBlockComponent, HandDeselectedEvent>(OnDeselectInHand);
        SubscribeLocalEvent<SWWeaponBlockComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<SWWeaponBlockComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<SWWeaponBlockComponent, MeleeAttackPreCooldownEvent>(OnAttackPreCooldown);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<SWWeaponBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<SWForceUserBlockComponent, HitByLightningEvent>(OnLightningHit);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.Handled)
            return;

        foreach (var entity in args.HitEntities)
        {
            if (!TryGetCurrentBlockWeapon(entity, out var blockWeapon) ||
                !ShouldSimulateBlock(blockWeapon.Owner, entity) ||
                !IsBlocking(blockWeapon) ||
                !blockWeapon.Comp.BlocksMelee)
            {
                continue;
            }

            args.Handled = true;
            PopupSystem.PopupPredicted(Loc.GetString(HitBlockedPopup), ent.Owner, null, PopupType.Medium);
            DisableBlock(blockWeapon);
            if (_tag.HasTag(args.Weapon, LightsaberTag) && _toggle.IsActivated(args.Weapon))
                _forms.OnSaberParry(entity, blockWeapon.Owner, args.User);

            break;
        }
    }

    private void OnDeselectInHand(Entity<SWWeaponBlockComponent> ent, ref HandDeselectedEvent args)
    {
        if (ent.Comp.CurrentUser != args.User || !ShouldSimulateBlock(ent.Owner, args.User))
            return;

        DisableBlock(ent);

        ent.Comp.CurrentUser = null;
        Dirty(ent);
    }

    private void OnUnequipped(Entity<SWWeaponBlockComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (ent.Comp.CurrentUser != args.User || !ShouldSimulateBlock(ent.Owner, args.User))
            return;

        DisableBlock(ent);
        ent.Comp.CurrentUser = null;
        Dirty(ent);
    }

    private void OnToggled(Entity<SWWeaponBlockComponent> ent, ref ItemToggledEvent args)
    {
        if (!args.Activated && ShouldSimulateBlock(ent.Owner, ent.Comp.CurrentUser))
            DisableBlock(ent);
    }

    private void OnAttackPreCooldown(Entity<SWWeaponBlockComponent> ent, ref MeleeAttackPreCooldownEvent args)
    {
        if (ent.Comp.IsBlocking && ent.Comp.AttackDelayAfterGuard > TimeSpan.Zero &&
            ShouldSimulateBlock(ent.Owner, ent.Comp.CurrentUser))
            DisableBlock(ent);
    }

    private void OnSelectInHand(Entity<SWWeaponBlockComponent> ent, ref HandSelectedEvent args)
    {
        if (!ShouldSimulateBlock(ent.Owner, args.User) || !CanUseBlockWeapon(args.User, ent.Comp))
            return;

        ent.Comp.CurrentUser = args.User;
        Dirty(ent);
    }

    private void OnAttemptMelee(Entity<SWWeaponBlockComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ent.Comp.IsBlocking && ShouldSimulateBlock(ent.Owner, ent.Comp.CurrentUser))
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
        if (ent.Comp.AttackDelayAfterGuard > TimeSpan.Zero && TryComp<MeleeWeaponComponent>(ent.Owner, out var melee))
        {
            var nextAttack = _gameTiming.CurTime + ent.Comp.AttackDelayAfterGuard;
            if (nextAttack > melee.NextAttack)
            {
                melee.NextAttack = nextAttack;
                DirtyField(ent.Owner, melee, nameof(MeleeWeaponComponent.NextAttack));
            }
        }

        ent.Comp.AttackDelayAfterGuard = TimeSpan.Zero;
        Dirty(ent);
    }

    private void OnTryBlock(SWTryReflectEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user && TryGetEntity(ev.Weapon, out var weapon))
            TryRaiseBlock(user, weapon.Value);
    }

    public bool TryRaiseBlock(EntityUid user, EntityUid weapon)
    {
        if (!TryComp<SWWeaponBlockComponent>(weapon, out var comp) ||
            comp.CurrentUser != user ||
            !ShouldSimulateBlock(weapon, user) ||
            !_hands.TryGetActiveItem(user, out var held) || held != weapon ||
            !_combatMode.IsInCombatMode(user) ||
            !_blocker.CanAttack(user) ||
            !_toggle.IsActivated(weapon))
        {
            return false;
        }

        if (TryComp<SWSaberFormWeaponComponent>(weapon, out var forms) && forms.Enabled &&
            TryComp<MeleeWeaponComponent>(weapon, out var melee) && melee.NextAttack > _gameTiming.CurTime)
        {
            return false;
        }

        var ent = (weapon, comp);
        if (CanBlock(ent))
        {
            EnableBlock(ent, user);
            return true;
        }

        if (comp.IsBlocking)
            PopupSystem.PopupPredicted(Loc.GetString(AlreadyBlockingPopup), weapon, null, PopupType.Medium);
        else if (_gameTiming.CurTime < comp.NextReflectionAvailable)
            PopupSystem.PopupPredicted(Loc.GetString(BlockCooldownPopup), weapon, null, PopupType.Medium);

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWWeaponBlockComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.IsBlocking && comp.BlockEndTime <= _gameTiming.CurTime && ShouldSimulateBlock(ent, comp.CurrentUser))
                DisableBlock((ent, comp));
        }
    }

    public bool IsBlocking(Entity<SWWeaponBlockComponent> weapon)
    {
        return weapon.Comp.IsBlocking && weapon.Comp.BlockEndTime > _gameTiming.CurTime;
    }

    private void EnableBlock(Entity<SWWeaponBlockComponent> weapon, EntityUid user)
    {
        PopupSystem.PopupPredicted(Loc.GetString(RaiseBlockPopup), weapon.Comp.CurrentUser ?? weapon.Owner, null, PopupType.Medium);
        _forms.TryGetActiveForm(user, weapon.Owner, out var form);
        EnableProjectileReflection(weapon, form?.GuardReflectChanceMultiplier ?? 1f);
        weapon.Comp.IsBlocking = true;
        weapon.Comp.BlockEndTime = _gameTiming.CurTime +
            TimeSpan.FromSeconds(weapon.Comp.BlockDuration * (form?.GuardDurationMultiplier ?? 1f));
        weapon.Comp.AttackDelayAfterGuard = TimeSpan.FromSeconds(form?.GuardExitAttackDelay ?? 0);
        Dirty(weapon);
    }

    private void OnLightningHit(Entity<SWForceUserBlockComponent> ent, ref HitByLightningEvent args)
    {
        if (_net.IsClient || args.Cancelled)
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

    private bool ShouldSimulateBlock(EntityUid weapon, EntityUid? user)
    {
        // Legacy guard stays server-side. The form users need their guard
        // transitions predicted so an attack from Soresu does not show a false swing.
        return _net.IsServer ||
            HasComp<SWSaberFormComponent>(user) &&
            TryComp<SWSaberFormWeaponComponent>(weapon, out var forms) && forms.Enabled;
    }

    private bool CanUseBlockWeapon(EntityUid user, SWWeaponBlockComponent comp)
    {
        return !comp.RequiresForceUser || HasComp<SWForceUserBlockComponent>(user);
    }

    private void EnableProjectileReflection(Entity<SWWeaponBlockComponent> weapon, float chanceMultiplier)
    {
        if (!weapon.Comp.BlocksProjectiles || !TryComp<ReflectComponent>(weapon.Owner, out var reflect))
            return;

        weapon.Comp.StoredReflectProb ??= reflect.ReflectProb;
        reflect.ReflectProb = weapon.Comp.ProjectileReflectProb * chanceMultiplier;
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
        if (_hands.TryGetActiveItem(user, out var held) &&
            TryComp<SWWeaponBlockComponent>(held, out var comp) &&
            comp.CurrentUser == user)
        {
            weapon = (held.Value, comp);
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
