using Content.Shared.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.CantEquipOnSlot;

[RegisterComponent]
public sealed partial class SWDisableSlotComponent : Component
{
    [DataField("slots")]
    public SlotFlags ProhibitedSlots = SlotFlags.HEAD;
}
