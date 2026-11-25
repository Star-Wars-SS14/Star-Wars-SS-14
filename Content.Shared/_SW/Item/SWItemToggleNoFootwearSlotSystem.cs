using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Containers;

namespace Content.Shared._SW.Item;

/// <summary>
/// Prevents activation and active insertion in direct item slots on footwear.
/// </summary>
public sealed class SWItemToggleNoFootwearSlotSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWItemToggleNoFootwearSlotComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<SWItemToggleNoFootwearSlotComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
    }

    private void OnActivateAttempt(Entity<SWItemToggleNoFootwearSlotComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (_containers.TryGetContainingContainer(ent.Owner, out var container) && IsFootwearItemSlot(container))
        {
            args.Cancelled = true;
        }
    }

    private void OnInsertAttempt(
        EntityUid uid,
        SWItemToggleNoFootwearSlotComponent component,
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
