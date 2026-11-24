using System.Linq;
using Content.Shared._SW.CantEquipOnSlot;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Loadouts;

/// <summary>
/// Whether a species can wear something in a slot, without a body of it at hand. The lobby's doll is a human for every
/// species of ours, so the doll cannot say.
/// </summary>
public static class SWSpeciesSlots
{
    public static bool CanWear(
        ProtoId<SpeciesPrototype> species,
        string slot,
        IPrototypeManager protos,
        IComponentFactory factory)
    {
        if (!protos.TryIndex(species, out var speciesProto)
            || !protos.TryIndex(speciesProto.Prototype, out var body)
            || !body.TryGetComponent<InventoryComponent>(out var inventory, factory)
            || !protos.TryIndex<InventoryTemplatePrototype>(inventory.TemplateId, out var template))
        {
            return true;
        }

        var definition = template.Slots.FirstOrDefault(s => s.Name == slot);
        if (definition == null)
            return false;

        return !body.TryGetComponent<SWDisableSlotComponent>(out var disabled, factory)
            || (disabled.ProhibitedSlots & definition.SlotFlags) == 0;
    }
}
