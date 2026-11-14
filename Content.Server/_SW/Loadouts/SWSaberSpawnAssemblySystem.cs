using Content.Server._SW.ModularSaber;
using Content.Shared._SW.CantEquipOnSlot;
using Content.Shared._SW.Loadouts;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Station;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.Loadouts;

/// <summary>Assembles the saber kit in a single starting-gear operation, including NPC kits.</summary>
public sealed class SWSaberSpawnAssemblySystem : EntitySystem
{
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
        if (!args.Gear.Equipment.TryGetValue("pocket1", out var hilt) ||
            (hilt != "SWModularSaber" && hilt != "SWModularSaberDouble"))
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

        gear.Equipment.Remove("pocket1");
        gear.Storage.TryGetValue("belt", out var belt);
        var emitter = Take(belt, "SWSaberEmitterStandard", "SWSaberEmitterReinforced") ?? "SWSaberEmitterStandard";
        var crystal = Take(belt, "SWKyberCrystalBlue", "SWKyberCrystalGreen", "SWKyberCrystalYellow",
            "SWKyberCrystalPurple", "SWKyberCrystalRed") ??
            (gear.Equipment.TryGetValue("belt", out var beltId) && beltId == "SWSithBelt"
                ? "SWKyberCrystalRed" : "SWKyberCrystalBlue");
        var focus = Take(belt, "SWSaberFocusStandard") ?? "SWSaberFocusStandard";

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
            crystalEntity = Spawn(crystal, coordinates);
            emitterEntity = Spawn(emitter, coordinates);
            focusEntity = Spawn(focus, coordinates);
            if (!TryComp<SWModularSaberComponent>(saber.Value, out var modular) ||
                modular.Crystal.ContainerSlot is not { } crystalSlot ||
                modular.Emitter.ContainerSlot is not { } emitterSlot ||
                modular.Focus.ContainerSlot is not { } focusSlot ||
                !_containers.Insert(crystalEntity.Value, crystalSlot) ||
                !_containers.Insert(emitterEntity.Value, emitterSlot) ||
                !_containers.Insert(focusEntity.Value, focusSlot))
                return;

            assembled = true;
        }
        catch (Exception error)
        {
            Log.Error($"Failed to assemble initial lightsaber: {error}");
            return;
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
        try
        {
            if (_inventory.TryGetSlots(entity, out var slots))
            {
                foreach (var name in new[] { "pocket1", "pocket2" })
                {
                    foreach (var slot in slots)
                    {
                        if (slot.Name != name ||
                            (TryComp<SWDisableSlotComponent>(entity, out var disabled) &&
                             (disabled.ProhibitedSlots & slot.SlotFlags) != 0))
                            continue;
                        if (_inventory.TryEquip(entity, saber, name, silent: true))
                            return;
                    }
                }
            }

            if (_hands.TryPickupAnyHand(entity, saber, animate: false))
                return;
        }
        catch (Exception error)
        {
            Log.Error($"Could not equip initial lightsaber: {error}");
        }

        Log.Error("Could not equip initial lightsaber; it remains at the spawn location.");
    }

    private static EntProtoId? Take(List<EntProtoId>? items, params EntProtoId[] candidates)
    {
        if (items == null)
            return null;
        foreach (var candidate in candidates)
        {
            if (!items.Remove(candidate))
                continue;
            return candidate;
        }
        return null;
    }
}

/// <summary>One-shot authorization to assemble the initial force-user kit.</summary>
[RegisterComponent]
public sealed partial class SWSaberInitialAssemblyComponent : Component { }
