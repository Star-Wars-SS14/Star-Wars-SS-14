using Content.Server.Armor;
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Cargo;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Roles;
using Robust.Shared.Containers;

namespace Content.Server._SW.Loadouts;

/// <summary>
/// What a job's loadout means at spawn, beyond the items themselves.
/// </summary>
public sealed class SWLoadoutSpawnSystem : EntitySystem
{
    [Dependency] private readonly IDependencyCollection _dependencies = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<InventoryComponent, StartingGearEquippedEvent>(OnGearEquipped);

        // Last, because these add to the price and would put back what this takes away.
        SubscribeLocalEvent<SWNoResaleComponent, PriceCalculationEvent>(OnPrice,
            after: [typeof(ArmorSystem), typeof(BatterySystem), typeof(PredictedBatterySystem), typeof(GunSystem)]);
    }

    /// <summary>
    /// Preferences are only checked when they load or are saved, so ownership, play time or the rules can change after
    /// that. What is no longer allowed is dropped here and the group falls back to its default. This edits the cached
    /// profile only: the database stays as it is until the player saves again.
    /// </summary>
    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (ev.Profile.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(ev.JobId), out var loadout))
            loadout.EnsureValid(ev.Profile, ev.Player, _dependencies);
    }

    /// <summary>
    /// A body gets its gear again at each spawn, so the gear must not be a way to earn money by dying.
    /// </summary>
    private void OnGearEquipped(Entity<InventoryComponent> ent, ref StartingGearEquippedEvent args)
    {
        var slots = _inventory.GetSlotEnumerator((ent.Owner, ent.Comp));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item)
                Mark(item);
        }

        if (!TryComp<HandsComponent>(ent, out var hands))
            return;

        foreach (var held in _hands.EnumerateHeld((ent.Owner, hands)))
        {
            Mark(held);
        }
    }

    private void Mark(EntityUid item)
    {
        EnsureComp<SWNoResaleComponent>(item);

        if (!TryComp<ContainerManagerComponent>(item, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                Mark(contained);
            }
        }
    }

    /// <summary>
    /// Worth nothing itself, but a bag still counts what a player put in it later.
    /// </summary>
    private void OnPrice(Entity<SWNoResaleComponent> ent, ref PriceCalculationEvent args)
    {
        args.Handled = true;
        args.Price = 0;

        if (!TryComp<ContainerManagerComponent>(ent, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                args.Price += _pricing.GetPrice(contained);
            }
        }
    }
}
