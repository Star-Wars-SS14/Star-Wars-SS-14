using System.Collections.Generic;
using System.Linq;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// The one kind of paid item that may protect: a costume made from the very clothes a job is issued. It must then be
/// worth exactly as much as those clothes, in every job that can wear it.
/// </summary>
internal static class SWCostumeRule
{
    /// <summary>
    /// The issued clothes this item is a child of, if it is a costume at all.
    /// </summary>
    public static EntityPrototype? IssuedParent(EntityPrototype item, IPrototypeManager protos)
    {
        var issued = protos.EnumeratePrototypes<StartingGearPrototype>()
            .SelectMany(g => ((IEquipmentLoadout) g).Equipment.Values)
            .Select(id => id.Id)
            .ToHashSet();

        foreach (var parent in item.Parents ?? [])
        {
            if (issued.Contains(parent))
                return protos.Index<EntityPrototype>(parent);
        }

        return null;
    }

    public static (string? Armour, string? Speed, string? Storage) Stats(EntityPrototype proto, IComponentFactory factory)
    {
        string? armour = null;
        if (proto.TryGetComponent<ArmorComponent>(out var armorComp, factory))
        {
            armour = string.Join(",", armorComp.Modifiers.Coefficients.OrderBy(c => c.Key).Select(c => $"{c.Key}{c.Value}"))
                + "|"
                + string.Join(",", armorComp.Modifiers.FlatReduction.OrderBy(c => c.Key).Select(c => $"{c.Key}{c.Value}"));
        }

        string? speed = null;
        if (proto.TryGetComponent<ClothingSpeedModifierComponent>(out var speedComp, factory))
            speed = $"{speedComp.WalkModifier}/{speedComp.SprintModifier}";

        string? storage = null;
        if (proto.TryGetComponent<StorageComponent>(out var storageComp, factory))
            storage = $"{string.Join(",", storageComp.Grid)}/{storageComp.MaxItemSize}";

        return (armour, speed, storage);
    }

    /// <summary>
    /// Holds a costume to its parent: nothing added but looks, and the same armour, speed and storage.
    /// </summary>
    public static void AssertAddsNothing(EntityPrototype item, EntityPrototype parent, IComponentFactory factory)
    {
        var added = item.Components.Keys.Except(parent.Components.Keys);
        Assert.That(added, Is.Empty, $"{item.ID} has more than {parent.ID}.");
        Assert.That(Stats(item, factory), Is.EqualTo(Stats(parent, factory)), $"{item.ID} is not worth the same as {parent.ID}.");
    }

    /// <summary>
    /// Every job that can pick the costume must have clothes in that slot worth the same, or it would gain from it.
    /// </summary>
    public static void AssertSameForEveryJob(LoadoutPrototype loadout, string slot, EntityPrototype item, IPrototypeManager protos, IComponentFactory factory)
    {
        var groups = protos.EnumeratePrototypes<LoadoutGroupPrototype>()
            .Where(g => g.Loadouts.Contains(loadout.ID))
            .Select(g => g.ID)
            .ToHashSet();

        foreach (var job in protos.EnumeratePrototypes<JobPrototype>())
        {
            if (job.StartingGear is not { } gearId
                || !protos.TryIndex<RoleLoadoutPrototype>($"Job{job.ID}", out var role)
                || !role.Groups.Any(g => groups.Contains(g)))
            {
                continue;
            }

            var own = ((IEquipmentLoadout) protos.Index(gearId)).GetGear(slot);
            Assert.That(own, Is.Not.Empty, $"{job.ID} wears nothing in {slot}, so {item.ID} would give it armour.");
            Assert.That(Stats(item, factory), Is.EqualTo(Stats(protos.Index<EntityPrototype>(own), factory)), $"{item.ID} changes {job.ID}.");
        }
    }
}
