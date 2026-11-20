using System.Linq;
using Content.Server._SW.ModularSaber;
using Content.Shared._SW.CantEquipOnSlot;
using Content.Shared._SW.Loadouts;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Station;
using Content.Shared.Roles;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Loadouts;

/// <summary>Assembles the saber kit in a single starting-gear operation, including NPC kits.</summary>
public sealed class SWSaberSpawnAssemblySystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InventoryComponent, PrepareStartingGearEvent>(OnPrepare);
        SubscribeLocalEvent<InventoryComponent, CompleteStartingSaberEvent>(OnComplete);
    }

    private void OnPrepare(Entity<InventoryComponent> entity, ref PrepareStartingGearEvent args)
    {
        // Only the designated first spawn of a force user may assemble a starting kit.
        if (!HasComp<SWSaberInitialAssemblyComponent>(entity))
            return;
        RemComp<SWSaberInitialAssemblyComponent>(entity);
        // Player gear records exactly which contributions came from kit groups. NPC kits are
        // dedicated starting-gear prototypes and therefore use the whole gear as their source.
        var kit = args.Gear is SWMergedGear merged ? merged.SaberKit : args.Gear;
        if (kit == null)
            return;
        string? hiltSlot = null;
        EntProtoId hilt = default;
        foreach (var (slot, prototype) in kit.Equipment)
        {
            if (!HasPrototypeComponent<SWModularSaberComponent>(prototype) ||
                !args.Gear.Equipment.TryGetValue(slot, out var equipped) || equipped != prototype)
                continue;
            hiltSlot = slot;
            hilt = prototype;
            break;
        }
        if (hiltSlot == null)
            return;

        // Do not create anything in nullspace or on a map that is being deleted.
        if (Transform(entity).MapUid is not { } map || TerminatingOrDeleted(map))
            return;

        var gear = new SWMergedGear
        {
            Equipment = new(args.Gear.Equipment),
            Inhand = new(args.Gear.Inhand),
            Storage = new(),
        };
        foreach (var (slot, items) in args.Gear.Storage)
            gear.Storage[slot] = new(items);

        gear.Equipment.Remove(hiltSlot);
        var emitter = Take<SWSaberEmitterComponent>(kit, gear);
        var crystal = Take<SWSaberCrystalComponent>(kit, gear);
        var focus = Take<SWSaberFocusComponent>(kit, gear);
        // Never synthesize free parts when the configured kit is incomplete.
        if (emitter == null || crystal == null || focus == null)
            return;

        // Build temporary entities on the map. On any failure the original snapshot remains intact.
        var coordinates = _transform.GetMapCoordinates(entity);
        EntityUid? saber = null;
        EntityUid? crystalEntity = null;
        EntityUid? emitterEntity = null;
        EntityUid? focusEntity = null;
        var assembled = false;
        try
        {
            saber = Spawn(hilt, coordinates);
            crystalEntity = Spawn(crystal.Value, coordinates);
            emitterEntity = Spawn(emitter.Value, coordinates);
            focusEntity = Spawn(focus.Value, coordinates);
            if (!TryComp<SWModularSaberComponent>(saber.Value, out var modular) ||
                modular.Crystal?.ContainerSlot is not { } crystalSlot ||
                modular.Emitter?.ContainerSlot is not { } emitterSlot ||
                modular.Focus?.ContainerSlot is not { } focusSlot ||
                !FitsSlot(crystalEntity.Value, modular.Crystal) ||
                !_containers.Insert(crystalEntity.Value, crystalSlot) ||
                !FitsSlot(emitterEntity.Value, modular.Emitter) ||
                !_containers.Insert(emitterEntity.Value, emitterSlot) ||
                !FitsSlot(focusEntity.Value, modular.Focus) ||
                !_containers.Insert(focusEntity.Value, focusSlot))
                return;

            assembled = true;
        }
        finally
        {
            if (!assembled)
            {
                // Delete parts first: deleting the hilt would otherwise release inserted parts.
                if (crystalEntity is { } crystalUid) Del(crystalUid);
                if (emitterEntity is { } emitterUid) Del(emitterUid);
                if (focusEntity is { } focusUid) Del(focusUid);
                if (saber is { } saberUid) Del(saberUid);
            }
        }

        // The snapshot is private to this equip call; it is committed only after assembly succeeds.
        args.Gear = gear;
        args.Saber = saber;
    }

    private void OnComplete(Entity<InventoryComponent> entity, ref CompleteStartingSaberEvent args)
    {
        var saber = args.Saber;
        if (_inventory.TryGetSlots(entity, out var slots))
        {
            foreach (var slot in slots
                         .Where(slot => (slot.SlotFlags & SlotFlags.POCKET) != 0)
                         .OrderBy(slot => slot.Name switch { "pocket1" => 0, "pocket2" => 1, _ => 2 })
                         .ThenBy(slot => slot.Name, StringComparer.Ordinal))
            {
                if (TryComp<SWDisableSlotComponent>(entity, out var disabled) &&
                    (disabled.ProhibitedSlots & slot.SlotFlags) != 0)
                    continue;
                if (_inventory.TryEquip(entity, saber, slot.Name, silent: true))
                    return;
            }
        }

        if (_hands.TryPickupAnyHand(entity, saber, animate: false))
            return;

        Log.Warning("Could not equip initial lightsaber; it remains at its current location.");
    }

    private bool FitsSlot(EntityUid part, ItemSlot slot) =>
        !_whitelist.IsWhitelistFail(slot.Whitelist, part) && !_whitelist.IsWhitelistPass(slot.Blacklist, part);

    private bool HasPrototypeComponent<T>(EntProtoId id) where T : Component, new()
    {
        return _prototypes.TryIndex(id, out var prototype) &&
               prototype.TryGetComponent<T>(out _, EntityManager.ComponentFactory);
    }

    private EntProtoId? Take<T>(IEquipmentLoadout kit, SWMergedGear gear) where T : Component, new()
    {
        foreach (var (slot, items) in kit.Storage)
        {
            if (!gear.Storage.TryGetValue(slot, out var available))
                continue;
            foreach (var item in items)
            {
                if (HasPrototypeComponent<T>(item) && available.Remove(item))
                    return item;
            }
        }
        return null;
    }
}

/// <summary>One-shot authorization to assemble the initial force-user kit.</summary>
[RegisterComponent]
public sealed partial class SWSaberInitialAssemblyComponent : Component { }
