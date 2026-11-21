using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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

    public float GetArcMultiplier(EntityUid user, SWSaberFormPrototype form)
    {
        if (form.DashArcMultiplier != 1f && TryComp<SWDashAbilityComponent>(user, out var dash) &&
            dash.IsDashing && dash.DashEndTime > _timing.CurTime)
        {
            return form.VisualArcMultiplier * form.DashArcMultiplier;
        }

        return form.VisualArcMultiplier;
    }

    public bool HasRiposte(EntityUid user, EntityUid weapon)
    {
        return TryComp<SWSaberFormComponent>(user, out var forms) &&
            forms.RiposteWeapon == weapon && forms.RiposteUntil > _timing.CurTime;
    }

    public void OnMeleeBlock(EntityUid user, EntityUid weapon, EntityUid attacker, bool fromLightsaber)
    {
        if (!TryGetActiveForm(user, weapon, out var form) ||
            !TryComp<SWSaberFormComponent>(user, out var forms))
        {
            return;
        }

        var armed = form.RiposteWindow > 0 && (fromLightsaber || form.RiposteFromAnyMelee);
        if (armed)
        {
            forms.RiposteWeapon = weapon;
            forms.RiposteUntil = _timing.CurTime + TimeSpan.FromSeconds(form.RiposteWindow);
            Dirty(user, forms);
        }

        var playClash = armed && fromLightsaber && form.RiposteAttackRateMultiplier != 1f;
        if (!fromLightsaber || form.ParryVisual == SWSaberDefenseVisual.None && !playClash)
            return;

        var defender = _transform.GetMapCoordinates(user);
        var delta = Vector2.Zero;
        if (TryComp<TransformComponent>(attacker, out var attackerTransform) && attackerTransform.MapID == defender.MapId)
            delta = _transform.GetWorldPosition(attackerTransform) - defender.Position;

        var distance = delta.Length();
        var axis = distance > 0.001f ? delta / distance : Vector2.UnitX;
        var contact = new MapCoordinates(defender.Position + axis * MathF.Min(0.55f, distance / 2f), defender.MapId);
        RaiseDefenseEffect(user, contact, form.ParryVisual, axis, axis, playClash);
    }

    private void OnMeleeAttack(Entity<SWSaberFormComponent> ent, ref MeleeAttackEvent args)
    {
        if (ent.Comp.RiposteWeapon != args.Weapon)
            return;

        if (ent.Comp.RiposteUntil > _timing.CurTime && TryGetActiveForm(ent.Owner, args.Weapon, out var form))
        {
            if (args.IsDisarm && form.RiposteDamageMultiplier != 1f)
                return;

            if (form.RiposteAttackRateMultiplier > 0 && form.RiposteAttackRateMultiplier != 1f &&
                TryComp<MeleeWeaponComponent>(args.Weapon, out var melee))
            {
                var recovery = melee.NextAttack - _timing.CurTime;
                if (recovery > TimeSpan.Zero)
                {
                    melee.NextAttack = _timing.CurTime + recovery / form.RiposteAttackRateMultiplier;
                    DirtyField(args.Weapon, melee, nameof(MeleeWeaponComponent.NextAttack));
                }
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
        if (!TryComp<SWWeaponBlockComponent>(ent.Owner, out var block) ||
            !block.IsBlocking ||
            block.BlockEndTime <= _timing.CurTime ||
            block.CurrentUser is not { } user ||
            !TryGetActiveForm(user, ent.Owner, out var form) ||
            form.ReflectionVisual == SWSaberDefenseVisual.None)
        {
            return;
        }

        var defender = _transform.GetMapCoordinates(user);
        var outgoing = args.Outgoing.LengthSquared() > 0.0001f ? args.Outgoing.Normalized() : Vector2.UnitX;
        var source = args.Incoming.LengthSquared() > 0.0001f ? -args.Incoming.Normalized() : outgoing;
        // Hitscan reflection currently starts from the target. For a moving projectile use its collision position.
        var contact = args.Contact is { } impact && impact.MapId == defender.MapId
            ? impact
            : new MapCoordinates(defender.Position + source * 0.34f, defender.MapId);
        RaiseDefenseEffect(user, contact, form.ReflectionVisual, outgoing, source);
    }

    private void RaiseDefenseEffect(EntityUid user, MapCoordinates contact, SWSaberDefenseVisual visual,
        Vector2 direction, Vector2 source, bool playClash = false)
    {
        var userTransform = Transform(user);
        if (contact.MapId == MapId.Nullspace || (userTransform.GridUid ?? userTransform.MapUid) is not { } anchor)
            return;

        var coordinates = _transform.ToCoordinates(anchor, contact);
        var rotation = _transform.GetWorldRotation(anchor);
        var ev = new SWSaberFormDefenseEvent(coordinates, visual, direction.ToAngle() - rotation,
            source.ToAngle() - rotation, playClash);
        RaiseLocalEvent(user, ref ev);
    }
}

[ByRefEvent]
public readonly record struct SWSaberFormWeaponLostEvent(EntityUid Weapon);
