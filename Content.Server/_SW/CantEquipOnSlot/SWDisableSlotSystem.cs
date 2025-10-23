using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.CantEquipOnSlot;
public sealed class SWDisableSlotSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDisableSlotComponent, IsEquippingAttemptEvent>(OnTryEquip);
    }

    private void OnTryEquip(Entity<SWDisableSlotComponent> ent, ref IsEquippingAttemptEvent args)
    {
        if ((ent.Comp.ProhibitedSlots & args.SlotFlags) != 0)
        {
            args.Cancel();
        }
    }
}
