using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.Loadouts;

/// <summary>
/// A job's gear with the player's loadout on top: a chosen item takes the place of the job's item in that slot.
/// Equipped in one pass, in the order of the inventory's slots, so a weapon in suit storage finds the suit already on,
/// and a backpack gets its contents whoever gave it. Job gear stays the default, so bots and other spawners need nothing.
/// </summary>
public sealed class SWMergedGear : IEquipmentLoadout
{
    public Dictionary<string, EntProtoId> Equipment { get; set; } = new();

    public List<EntProtoId> Inhand { get; set; } = new();

    public Dictionary<string, List<EntProtoId>> Storage { get; set; } = new();

    public static SWMergedGear Build(
        StartingGearPrototype? jobGear,
        RoleLoadout? loadout,
        RoleLoadoutPrototype? role,
        IPrototypeManager protoMan)
    {
        var merged = new SWMergedGear();
        merged.Add(jobGear);

        if (loadout == null || role == null)
            return merged;

        foreach (var group in role.Groups)
        {
            if (!loadout.SelectedLoadouts.TryGetValue(group, out var selected))
                continue;

            foreach (var item in selected)
            {
                if (!protoMan.TryIndex(item.Prototype, out var proto))
                    continue;

                if (protoMan.TryIndex(proto.StartingGear, out var gear))
                    merged.Add(gear);

                merged.Add(proto);
            }
        }

        return merged;
    }

    private void Add(IEquipmentLoadout? gear)
    {
        if (gear == null)
            return;

        foreach (var (slot, item) in gear.Equipment)
        {
            Equipment[slot] = item;
        }

        Inhand.AddRange(gear.Inhand);

        foreach (var (slot, items) in gear.Storage)
        {
            if (!Storage.TryGetValue(slot, out var stored))
                Storage[slot] = stored = new List<EntProtoId>();

            stored.AddRange(items);
        }
    }
}
