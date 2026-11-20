using System.Diagnostics.CodeAnalysis;
using Content.Shared._SW.ForceAbilities;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._SW.SaberForms;

public sealed class SharedSWSaberFormSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWSaberFormComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<SWSaberFormComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWSaberFormComponent, StunnedEvent>(OnStunned);
        SubscribeLocalEvent<SWForceUserBlockComponent, ComponentShutdown>(OnBlockEligibilityLost);
        SubscribeLocalEvent<SWSaberFormWeaponComponent, HandDeselectedEvent>(OnWeaponDeselected);
        SubscribeLocalEvent<SWSaberFormWeaponComponent, GotUnequippedHandEvent>(OnWeaponUnequipped);
        SubscribeLocalEvent<SWSaberFormWeaponComponent, ItemToggledEvent>(OnWeaponToggled);
        SubscribeLocalEvent<SWSaberFormWeaponComponent, ReflectionSucceededEvent>(OnReflectionSucceeded);
    }

    public bool TryGetActiveForm(EntityUid user, EntityUid weapon, [NotNullWhen(true)] out SWSaberFormPrototype? form)
    {
        form = null;
        return TryComp<SWSaberFormComponent>(user, out var forms) &&
            HasComp<SWForceUserBlockComponent>(user) &&
            _mobState.IsAlive(user) &&
            _blocker.CanInteract(user, null) &&
            TryComp<SWSaberFormWeaponComponent>(weapon, out var weaponForms) &&
            weaponForms.Enabled &&
            _hands.TryGetActiveItem(user, out var held) &&
            held == weapon &&
            _toggle.IsActivated(weapon) &&
            _prototypes.TryIndex(forms.ActiveForm, out form);
    }

    public void OnSaberParry(EntityUid user, EntityUid weapon, EntityUid attacker)
    {
        if (!TryGetActiveForm(user, weapon, out var form) ||
            form.RiposteWindow <= 0 ||
            !TryComp<SWSaberFormComponent>(user, out var forms))
        {
            return;
        }

        forms.RiposteWeapon = weapon;
        forms.RiposteUntil = _timing.CurTime + TimeSpan.FromSeconds(form.RiposteWindow);
        Dirty(user, forms);
        RaiseDefenseEffect(user, attacker, playClash: true);
    }

    private void OnMeleeAttack(Entity<SWSaberFormComponent> ent, ref MeleeAttackEvent args)
    {
        if (ent.Comp.RiposteWeapon != args.Weapon)
            return;

        if (ent.Comp.RiposteUntil > _timing.CurTime &&
            TryGetActiveForm(ent.Owner, args.Weapon, out var form) &&
            form.RiposteAttackRateMultiplier > 0 &&
            TryComp<MeleeWeaponComponent>(args.Weapon, out var melee))
        {
            var recovery = melee.NextAttack - _timing.CurTime;
            if (recovery > TimeSpan.Zero)
            {
                melee.NextAttack = _timing.CurTime + recovery / form.RiposteAttackRateMultiplier;
                DirtyField(args.Weapon, melee, nameof(MeleeWeaponComponent.NextAttack));
            }
        }

        ClearRiposte(ent);
    }

    public void ClearRiposte(Entity<SWSaberFormComponent> ent)
    {
        if (ent.Comp.RiposteWeapon == null)
            return;

        ent.Comp.RiposteWeapon = null;
        ent.Comp.RiposteUntil = TimeSpan.Zero;
        Dirty(ent);
    }

    private void OnMobStateChanged(Entity<SWSaberFormComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ClearRiposte(ent);
    }

    private void OnStunned(Entity<SWSaberFormComponent> ent, ref StunnedEvent args)
    {
        ClearRiposte(ent);
    }

    private void OnBlockEligibilityLost(Entity<SWForceUserBlockComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SWSaberFormComponent>(ent.Owner, out var forms))
            ClearRiposte((ent.Owner, forms));
    }

    private void OnWeaponDeselected(Entity<SWSaberFormWeaponComponent> ent, ref HandDeselectedEvent args)
    {
        OnWeaponLost(args.User, ent.Owner);
    }

    private void OnWeaponUnequipped(Entity<SWSaberFormWeaponComponent> ent, ref GotUnequippedHandEvent args)
    {
        OnWeaponLost(args.User, ent.Owner);
    }

    private void OnWeaponToggled(Entity<SWSaberFormWeaponComponent> ent, ref ItemToggledEvent args)
    {
        if (!args.Activated)
            OnWeaponLost(Transform(ent).ParentUid, ent.Owner);
    }

    private void OnWeaponLost(EntityUid user, EntityUid weapon)
    {
        if (!TryComp<SWSaberFormComponent>(user, out var forms))
            return;

        if (forms.RiposteWeapon == weapon)
            ClearRiposte((user, forms));

        var ev = new SWSaberFormWeaponLostEvent(weapon);
        RaiseLocalEvent(user, ref ev);
    }

    private void OnReflectionSucceeded(Entity<SWSaberFormWeaponComponent> ent, ref ReflectionSucceededEvent args)
    {
        if (TryComp<SWWeaponBlockComponent>(ent.Owner, out var block) &&
            block.IsBlocking &&
            block.BlockEndTime > _timing.CurTime &&
            TryGetActiveForm(args.User, ent.Owner, out var form) &&
            form.GuardReflectionSparks)
        {
            RaiseDefenseEffect(args.User, args.Source);
        }
    }

    private void RaiseDefenseEffect(EntityUid user, EntityUid source, bool playClash = false)
    {
        var userPos = _transform.GetMapCoordinates(user);
        if (userPos.MapId == MapId.Nullspace)
            return;

        var contact = userPos.Position;
        if (TryComp<TransformComponent>(source, out var xform) && xform.MapID == userPos.MapId)
        {
            var delta = _transform.GetWorldPosition(xform) - contact;
            if (delta.LengthSquared() > 0)
                contact += delta.Normalized() * MathF.Min(0.55f, delta.Length() / 2);
        }

        var coordinates = _transform.ToCoordinates(new MapCoordinates(contact, userPos.MapId));
        var ev = new SWSaberFormDefenseEvent(coordinates, playClash);
        RaiseLocalEvent(user, ref ev);
    }
}

[ByRefEvent]
public readonly record struct SWSaberFormWeaponLostEvent(EntityUid Weapon);
