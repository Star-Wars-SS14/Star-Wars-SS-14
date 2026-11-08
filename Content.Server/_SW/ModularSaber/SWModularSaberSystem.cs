using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Content.Shared.Destructible;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Melee;
using Content.Shared.Tag;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Toggleable;
using Robust.Shared.Containers;

namespace Content.Server._SW.ModularSaber;

public sealed class SWModularSaberSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWModularSaberComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWModularSaberComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SWModularSaberComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<SWModularSaberComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<SWModularSaberComponent, BreakageEventArgs>(OnBreakage);
        SubscribeLocalEvent<SWModularSaberComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<SWModularSaberComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<SWModularSaberComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, ItemSlotEjectAttemptEvent>(OnEjectAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
        SubscribeLocalEvent<SWModularSaberComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnInit(EntityUid uid, SWModularSaberComponent comp, ComponentInit args)
    {
        _slots.AddItemSlot(uid, "saber-crystal", comp.Crystal);
        _slots.AddItemSlot(uid, "saber-emitter", comp.Emitter);
        _slots.AddItemSlot(uid, "saber-focus", comp.Focus);
    }

    private void OnRemove(EntityUid uid, SWModularSaberComponent comp, ComponentRemove args)
    {
        ReleaseContents(uid, comp);
        _slots.RemoveItemSlot(uid, comp.Crystal);
        _slots.RemoveItemSlot(uid, comp.Emitter);
        _slots.RemoveItemSlot(uid, comp.Focus);
    }

    // Generic item-slot entry points must never bypass the server-side transaction.
    private void OnInsertAttempt(Entity<SWModularSaberComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.SlotEntity == ent.Owner && IsSaberSlot(args.Slot))
            args.Cancelled = true;
    }

    private void OnEjectAttempt(Entity<SWModularSaberComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.SlotEntity == ent.Owner && IsSaberSlot(args.Slot))
            args.Cancelled = true;
    }

    private static bool IsSaberSlot(ItemSlot slot) =>
        slot.ID is "saber-crystal" or "saber-emitter" or "saber-focus";

    private bool CanModify(EntityUid saber, EntityUid user)
    {
        return !_toggle.IsActivated(saber) && _tags.HasTag(user, "ForceUser") &&
               _blocker.CanInteract(user, saber) && _interaction.IsAccessible(user, saber) &&
               _interaction.InRangeUnobstructed(user, saber);
    }

    private void OnInteractUsing(Entity<SWModularSaberComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !CanModify(ent.Owner, args.User) || _hands.GetActiveItem(args.User) != args.Used)
            return;

        ItemSlot? slot = HasComp<SWSaberCrystalComponent>(args.Used) ? ent.Comp.Crystal :
                         HasComp<SWSaberEmitterComponent>(args.Used) ? ent.Comp.Emitter :
                         HasComp<SWSaberFocusComponent>(args.Used) ? ent.Comp.Focus : null;
        if (slot?.ContainerSlot == null || slot.HasItem || !_containers.CanInsert(args.Used, slot.ContainerSlot))
            return;

        args.Handled = true;
        if (!_hands.TryDrop(args.User, args.Used))
            return;

        // Insertion can fail after drop if another event changes the container. Preserve the item.
        if (!CanModify(ent.Owner, args.User) || slot.HasItem ||
            !_containers.CanInsert(args.Used, slot.ContainerSlot) ||
            !_containers.Insert(args.Used, slot.ContainerSlot))
            _hands.PickupOrDrop(args.User, args.Used, dropNear: true);
    }

    private void OnVerbs(Entity<SWModularSaberComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanModify(ent.Owner, args.User))
            return;

        AddEjectVerb(ent, ent.Comp.Crystal, args, "sw-modular-saber-slot-crystal");
        AddEjectVerb(ent, ent.Comp.Emitter, args, "sw-modular-saber-slot-emitter");
        AddEjectVerb(ent, ent.Comp.Focus, args, "sw-modular-saber-slot-focus");
    }

    private void AddEjectVerb(Entity<SWModularSaberComponent> ent, ItemSlot slot,
        GetVerbsEvent<AlternativeVerb> args, string label)
    {
        if (slot.Item == null)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(label),
            Category = VerbCategory.Eject,
            Act = () => Eject(ent.Owner, user, slot),
        });
    }

    private void Eject(EntityUid saber, EntityUid user, ItemSlot slot)
    {
        if (!CanModify(saber, user) || slot.Item is not { } item || slot.ContainerSlot == null ||
            !_blocker.CanPickup(user, item) || !_containers.Remove(item, slot.ContainerSlot))
            return;
        _hands.PickupOrDrop(user, item, dropNear: true);
    }

    private void OnDestruction(EntityUid uid, SWModularSaberComponent comp, DestructionEventArgs args)
    {
        ReleaseContents(uid, comp);
    }

    private void OnBreakage(EntityUid uid, SWModularSaberComponent comp, BreakageEventArgs args)
    {
        ReleaseContents(uid, comp);
    }

    private void OnTerminating(Entity<SWModularSaberComponent> ent, ref EntityTerminatingEvent args)
    {
        ReleaseContents(ent.Owner, ent.Comp);
    }

    private void ReleaseContents(EntityUid uid, SWModularSaberComponent comp)
    {
        var xform = Transform(uid);
        if (xform.MapUid is not { } map || Terminating(map))
            return;

        // Use map coordinates, never a parent container which may itself be terminating.
        var destination = _transform.ToCoordinates(_transform.GetMapCoordinates(uid));
        foreach (var slot in new[] { comp.Crystal, comp.Emitter, comp.Focus })
        {
            if (slot.Item is { } item && slot.ContainerSlot != null)
                _containers.Remove(item, slot.ContainerSlot, force: true, destination: destination);
        }
    }

    private void OnActivateAttempt(Entity<SWModularSaberComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (ent.Comp.Crystal.Item is not { } crystal || !TryComp<SWSaberCrystalComponent>(crystal, out var color) ||
            ent.Comp.Emitter.Item is not { } emitter || !TryComp<SWSaberEmitterComponent>(emitter, out var emitterComp) ||
            ent.Comp.Focus.Item is not { } focus || !TryComp<SWSaberFocusComponent>(focus, out var focusComp))
        {
            args.Cancelled = true;
            args.Popup = Loc.GetString("sw-modular-saber-incomplete");
            return;
        }

        if (!TryComp<ItemToggleMeleeWeaponComponent>(ent, out var toggleMelee))
        {
            args.Cancelled = true;
            return;
        }

        var damage = emitterComp.Damage * focusComp.DamageMultiplier;
        if (emitterComp.Damage.DamageDict.TryGetValue("Structural", out var structural))
            damage.DamageDict["Structural"] = structural;
        toggleMelee.ActivatedDamage = damage;
        Dirty(ent, toggleMelee);
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.SetData(ent, ToggleableVisuals.Color, color.Color, appearance);
    }

    private void OnToggled(Entity<SWModularSaberComponent> ent, ref ItemToggledEvent args)
    {
        if (!TryComp<MeleeWeaponComponent>(ent, out var melee))
            return;

        melee.AttackRate = args.Activated && ent.Comp.Focus.Item is { } focus &&
                           TryComp<SWSaberFocusComponent>(focus, out var focusComp)
            ? focusComp.AttackRateMultiplier
            : 1f;
        DirtyField(ent, melee, nameof(MeleeWeaponComponent.AttackRate));
    }

    private void OnContainerRemoved(Entity<SWModularSaberComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID is "saber-crystal" or "saber-emitter" or "saber-focus" &&
            _toggle.IsActivated(ent.Owner))
            _toggle.TryDeactivate(ent.Owner, predicted: false);
    }
}





