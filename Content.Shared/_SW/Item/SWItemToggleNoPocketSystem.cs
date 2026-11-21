using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Containers;

namespace Content.Shared._SW.Item;

/// <summary>
/// Restricts activation in pockets and direct item slots on footwear.
/// </summary>
public sealed class SWItemToggleNoPocketSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWItemToggleNoPocketComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<SWItemToggleNoPocketComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
    }

    private void OnActivateAttempt(Entity<SWItemToggleNoPocketComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (_inventory.InSlotWithFlags(ent.Owner, SlotFlags.POCKET)
            || (_containers.TryGetContainingContainer(ent.Owner, out var container) && IsFootwearItemSlot(container)))
        {
            args.Cancelled = true;
        }
    }

    private void OnInsertAttempt(
        EntityUid uid,
        SWItemToggleNoPocketComponent component,
        ContainerGettingInsertedAttemptEvent args)
    {
        if (TryComp(uid, out ItemToggleComponent? toggle)
            && _itemToggle.IsActivated((uid, toggle))
            && IsFootwearItemSlot(args.Container))
        {
            args.Cancel();
        }
    }

    private bool IsFootwearItemSlot(BaseContainer container)
    {
        return TryComp(container.Owner, out ClothingComponent? clothing)
            && (clothing.Slots & SlotFlags.FEET) != 0
            && _itemSlots.TryGetSlot(container.Owner, container.ID, out _);
    }
}
