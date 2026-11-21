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
using Content.Shared.Whitelist;
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
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
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
        SubscribeLocalEvent<SWModularSaberComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<SWModularSaberComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, ItemSlotEjectAttemptEvent>(OnEjectAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<SWModularSaberComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
        SubscribeLocalEvent<SWModularSaberComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnInit(EntityUid uid, SWModularSaberComponent comp, ComponentInit args)
    {
        if (!HasComp<ItemSlotsComponent>(uid))
            return;

        _slots.TryGetSlot(uid, "saber-crystal", out comp.Crystal);
        _slots.TryGetSlot(uid, "saber-emitter", out comp.Emitter);
        _slots.TryGetSlot(uid, "saber-focus", out comp.Focus);
    }

    private void OnRemove(EntityUid uid, SWModularSaberComponent comp, ComponentRemove args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        ReleaseContents(uid, comp);
        foreach (var slot in new[] { comp.Crystal, comp.Emitter, comp.Focus })
        {
            if (TerminatingOrDeleted(uid))
                return;

            // A dying child must remain in its transform hierarchy, but no longer occupy a slot
            // that is about to be shut down (which would otherwise try to delete it again).
            if (slot?.Item is { } item && !Deleted(item) && Terminating(item) && slot.ContainerSlot != null)
                _containers.Remove(item, slot.ContainerSlot, reparent: false, force: true);

            if (TerminatingOrDeleted(uid))
                return;

            // ShutdownContainer deletes any remaining item; never destroy a live, unejected part.
            if (slot is { Item: null })
                _slots.RemoveItemSlot(uid, slot);
        }
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
        if (slot?.ContainerSlot == null || slot.HasItem || !FitsSlot(args.Used, slot) ||
            !_containers.CanInsert(args.Used, slot.ContainerSlot))
            return;

        args.Handled = true;
        if (!_hands.TryDrop(args.User, args.Used))
            return;

        // Insertion can fail after drop if another event changes the container. Preserve the item.
        if (!CanModify(ent.Owner, args.User) || slot.HasItem || !FitsSlot(args.Used, slot) ||
            !_containers.CanInsert(args.Used, slot.ContainerSlot) ||
            !_containers.Insert(args.Used, slot.ContainerSlot))
            _hands.PickupOrDrop(args.User, args.Used, dropNear: true);
    }

    private bool FitsSlot(EntityUid part, ItemSlot slot) =>
        !_whitelist.IsWhitelistFail(slot.Whitelist, part) && !_whitelist.IsWhitelistPass(slot.Blacklist, part);

    private void OnVerbs(Entity<SWModularSaberComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanModify(ent.Owner, args.User))
            return;

        if (ent.Comp.Crystal == null || ent.Comp.Emitter == null || ent.Comp.Focus == null)
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

    private void ReleaseContents(EntityUid uid, SWModularSaberComponent comp)
    {
        foreach (var slot in new[] { comp.Crystal, comp.Emitter, comp.Focus })
        {
            // Container callbacks can delete the hilt or map while releasing an earlier part.
            if (TerminatingOrDeleted(uid))
                return;

            var map = Transform(uid).MapUid;
            if (map != null && TerminatingOrDeleted(map))
                return;
            if (slot?.Item is not { } item || TerminatingOrDeleted(item) || slot.ContainerSlot == null)
                continue;

            if (map != null)
            {
                // Use map coordinates, never a parent container which may itself be terminating.
                var destination = _transform.ToCoordinates(_transform.GetMapCoordinates(uid));
                _containers.Remove(item, slot.ContainerSlot, force: true, destination: destination);
            }
            else if (_containers.Remove(item, slot.ContainerSlot, reparent: false, force: true) &&
                     !TerminatingOrDeleted(item) && Transform(item).ParentUid == uid)
            {
                // No map exists: preserve the part in nullspace without leaving it parented to the hilt.
                _transform.DetachEntity(item);
            }
        }
    }

    private void OnActivateAttempt(Entity<SWModularSaberComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (ent.Comp.Crystal?.Item is not { } crystal || !TryComp<SWSaberCrystalComponent>(crystal, out var color) ||
            ent.Comp.Emitter?.Item is not { } emitter || !TryComp<SWSaberEmitterComponent>(emitter, out var emitterComp) ||
            ent.Comp.Focus?.Item is not { } focus || !TryComp<SWSaberFocusComponent>(focus, out var focusComp))
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

        melee.AttackRate = args.Activated && ent.Comp.Focus?.Item is { } focus &&
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





