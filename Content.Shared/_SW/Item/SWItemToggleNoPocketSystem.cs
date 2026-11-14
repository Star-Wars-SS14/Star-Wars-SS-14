using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Shared._SW.Item;

/// <summary>
/// Prevents marked items from being activated in pocket slots.
/// </summary>
public sealed class SWItemToggleNoPocketSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWItemToggleNoPocketComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
    }

    private void OnActivateAttempt(Entity<SWItemToggleNoPocketComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (_inventory.InSlotWithFlags(ent.Owner, SlotFlags.POCKET))
            args.Cancelled = true;
    }
}
