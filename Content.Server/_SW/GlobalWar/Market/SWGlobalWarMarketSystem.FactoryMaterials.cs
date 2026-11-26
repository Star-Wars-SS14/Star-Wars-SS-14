using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server._SW.Lathe.SWLathe;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.Lathe;
using Content.Shared.Materials;
using Content.Shared.Item;
using Content.Shared.Power.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Components;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly EntityLookupSystem _factoryLookup = default!;
    private readonly Dictionary<Guid, SWGlobalWarFactoryMaterialization> _factoryMaterializations = new();
    private readonly Dictionary<Guid, SWGlobalWarFactoryRecipeWork> _factoryRecipeWork = new();

    /// <summary>The original lathe must never consume or spawn independently on a campaign location.</summary>
    public bool IsCampaignProductionLocation(EntityUid entity)
    {
        return TryComp(entity, out TransformComponent? transform) &&
               HasComp<SWGlobalWarPlanetMapComponent>(transform.MapUid);
    }

    private SWGlobalWarSupplyKind[] FactoryKinds(EntityUid entity)
    {
        return MetaData(entity).EntityPrototype?.ID switch
        {
            "SWBlasterLathe" => [SWGlobalWarSupplyKind.T1, SWGlobalWarSupplyKind.T2, SWGlobalWarSupplyKind.Armor],
            "SWElectronicsLathe" => [SWGlobalWarSupplyKind.Magazine, SWGlobalWarSupplyKind.AdvancedElectronics],
            "SWSmeltLathe" or "SWDroidFabricator" or "SWVehicleFabricator" => [],
            _ => Enum.GetValues<SWGlobalWarSupplyKind>(),
        };
    }

    private void CollectFactoryInputs(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarFaction owner)
    {
        if (!this.IsPowered(ent, EntityManager) || !TryComp<FixturesComponent>(ent, out var fixtures) ||
            !TryGetSupplyStore(ent.Comp.Generation, ent, owner, out _, out var inventory))
            return;
        var materials = FactoryKinds(ent).SelectMany(k => SupplyRecipe(k, false).Physical.Keys)
            .Concat(FactoryRecipes(ent).SelectMany(r => r.Materials.Keys)).Distinct().ToArray();
        var prefix = TryComp<SWLatheComponent>(ent, out var lathe) ? lathe.FixtureInPrefix : "in";
        foreach (var (name, fixture) in fixtures.Fixtures)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            foreach (var contact in fixture.Contacts.Values.ToArray())
            {
                var item = contact.OtherEnt(ent);
                if (inventory.ContainedEntities.Count >= 600 || TerminatingOrDeleted(item) ||
                    _supplyContainers.TryGetContainingContainer(item, out _) || Transform(item).Anchored ||
                    !materials.Any(p => MatchesSupplyMaterial(item, p)) ||
                    _supplyItems.TryGetValue(item, out var registered) &&
                    (registered.Generation != ent.Comp.Generation || registered.Owner != owner ||
                     registered.Market || registered.Order != null))
                    continue;
                _supplyContainers.Insert(item, inventory);
            }
        }
    }

    private bool TakeFactoryOutput(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor, EntityUid item)
    {
        if (HasComp<ItemComponent>(item))
            return _supplyHands.TryPickupAnyHand(actor.Body, item);
        if (!ent.Comp.Factory || !TryGetSupplyStore(actor.Generation, ent, actor.Faction, out _, out var inventory) ||
            Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridData))
            return false;
        // Large chassis and vehicles leave onto a real clear platform, never into a hand or inside a wall.
        var destination = _supplyTransforms.ToMapCoordinates(new EntityCoordinates(ent, new Vector2(0, 4)));
        var rotation = _supplyTransforms.GetWorldRotation(ent);
        var bounds = _factoryLookup.GetAABBNoContainer(item, destination.Position, rotation);
        var tiles = _hubMaps.GetTilesIntersecting(grid, gridData, bounds, ignoreEmpty: false).ToArray();
        if (tiles.Length == 0 || tiles.Any(t => t.Tile.IsEmpty))
            return false;
        foreach (var obstacle in _factoryLookup.GetEntitiesIntersecting(destination.MapId, bounds,
                     LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (obstacle == grid || obstacle == item || !TryComp<PhysicsComponent>(obstacle, out var physics) ||
                !physics.CanCollide)
                continue;
            if (_factoryLookup.GetAABBNoContainer(obstacle, _supplyTransforms.GetWorldPosition(obstacle),
                    _supplyTransforms.GetWorldRotation(obstacle)).Intersects(bounds))
                return false;
        }
        return _supplyContainers.Remove(item, inventory,
            destination: _supplyTransforms.ToCoordinates(grid, destination),
            localRotation: rotation - _supplyTransforms.GetWorldRotation(grid));
    }

    private List<SWGlobalWarFactoryChoice> FactoryMaterials(Entity<SWGlobalWarFactoryComponent> ent)
    {
        if (!ent.Comp.Factory)
            return new();
        var ids = new[] { "SWGlobalWarFe", "SWGlobalWarC", "SWGlobalWarAu", "SWGlobalWarLn", "SWGlobalWarS", "SWGlobalWarE", "SWGlobalWarF" };
        return ids.Select(id => new SWGlobalWarFactoryChoice(id,
            Loc.GetString(_prototypes.Index<SWGlobalWarCommodityPrototype>(id).Name))).ToList();
    }

    private bool MaterializeFactoryStock(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        Guid request, string commodity, ref string rejected)
    {
        if (!FactoryOperator(actor))
        {
            rejected = FactoryRejectedOperator;
            return false;
        }
        if (!this.IsPowered(ent, EntityManager))
        {
            rejected = "sw-global-war-factory-rejected-power";
            return false;
        }
        if (!FactoryMaterials(ent).Any(c => c.Id == commodity) ||
            !TryGetSupplyStore(actor.Generation, ent, actor.Faction, out var binding, out var inventory))
            return false;
        if (_factoryMaterializations.TryGetValue(request, out var previous))
            return previous.Generation == actor.Generation && previous.Store == ent.Owner &&
                   previous.Commodity == commodity && previous.Assignment == actor.Id;
        if (inventory.ContainedEntities.Count >= 600)
        {
            rejected = FactoryRejectedSpace;
            return false;
        }
        if (!TryReserveStock(new(actor.Generation, request, binding.Planet, commodity,
                SWGlobalWarMarketUnits.FromUnits(1), actor.Faction, "physical-factory-material"), out _))
        {
            rejected = "sw-global-war-factory-rejected-stock";
            return false;
        }
        // The receipt remains after the physical entity is consumed, split or destroyed. A retry never spawns again.
        var escrow = CreateSupplyEscrow();
        var item = Spawn(_prototypes.Index<SWGlobalWarCommodityPrototype>(commodity).Entity,
            new EntityCoordinates(escrow, Vector2.Zero));
        if (!_supplyContainers.CanInsert(item, inventory) ||
            !Simulation!.TryCommitStock(actor.Generation, request, out var receipt))
        {
            Del(item);
            Del(escrow);
            Simulation!.TryCancelStock(actor.Generation, request);
            return false;
        }
        _factoryMaterializations.Add(request, new(actor.Generation, actor.Id, ent, commodity, item, receipt));
        if (!_supplyContainers.Insert(item, inventory))
            throw new InvalidOperationException("Prepared material output could not enter its physical inventory.");
        Del(escrow);
        return true;
    }

    private List<SWGlobalWarFactoryRecipe> FactoryRecipes(Entity<SWGlobalWarFactoryComponent> ent)
    {
        var result = new List<SWGlobalWarFactoryRecipe>();
        if (!TryComp<SWLatheComponent>(ent, out var lathe))
            return result;
        foreach (var id in lathe.Recipes)
        {
            // These two legacy recipes bypass the campaign's normative weapon/AE accounting.
            if (id.Id is "SWAdvancedElectronicsSLR" or "SWT1WeaponSLR")
                continue;
            var recipe = _prototypes.Index(id);
            result.Add(new(recipe.ID, recipe.Materials, recipe.Result,
                TimeSpan.FromSeconds(Math.Max(1, recipe.ProduceTime))));
        }
        if (FactoryKinds(ent).Contains(SWGlobalWarSupplyKind.T2) &&
            _supplyStores.TryGetValue(ent, out var binding) && Simulation?.Markets[binding.Planet].Owner is { } owner &&
            owner is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire)
        {
            result.Add(new("SWGlobalWarUpgradeRifle", new()
                {
                    [SupplyPrototype(SWGlobalWarSupplyKind.T2, owner).Id] = 1,
                    ["SWAdvancedElectronics"] = 2,
                }, new() { [owner == SWGlobalWarFaction.Republic ? "SWGlobalWarImprovedRepublicRifle" :
                    "SWGlobalWarImprovedEmpireRifle"] = 1 }, TimeSpan.FromSeconds(90)));
        }
        // The chip's side is the fabricator owner's; the buy consoles that sold it are not part of a campaign.
        if (MetaData(ent).EntityPrototype?.ID == "SWDroidFabricator" && _supplyStores.TryGetValue(ent, out var site) &&
            Simulation?.Markets[site.Planet].Owner is { } chipOwner &&
            chipOwner is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire)
        {
            result.Add(new("SWGlobalWarDroidFactionChip", new()
                {
                    ["SWElectronics1"] = 1,
                    ["SWAdvancedCables1"] = 1,
                }, new() { [chipOwner == SWGlobalWarFaction.Republic ? "SWDroidFactionChipRepublic" :
                    "SWDroidFactionChipEmpire"] = 1 }, TimeSpan.FromSeconds(10)));
        }
        // An agent appointed during the round gets no kit, so the tool of the post has to be replaceable.
        if (MetaData(ent).EntityPrototype?.ID == "SWElectronicsLathe")
        {
            result.Add(new("SWGlobalWarReconDatapad", new() { ["SWElectronics1"] = 2 },
                new() { ["SWGlobalWarReconDatapad"] = 1 }, TimeSpan.FromSeconds(20)));
        }
        const string board = "SWGlobalWarCaptureCircuitboard";
        if (MetaData(ent).EntityPrototype?.ID == "SWElectronicsLathe" &&
            _prototypes.TryIndex<LatheRecipePrototype>(board, out var repair) && repair.Result is { } output)
        {
            // Translate actual material units using the physical single-sheet prototypes, not campaign S/E units.
            var inputs = new Dictionary<string, int>();
            var sheets = new Dictionary<string, string>
            {
                ["Steel"] = "SheetSteel1", ["Glass"] = "SheetGlass1", ["Gold"] = "IngotGold1",
            };
            foreach (var (material, amount) in repair.Materials)
            {
                if (!sheets.TryGetValue(material.Id, out var sheet) ||
                    !_prototypes.Index<EntityPrototype>(sheet).TryGetComponent<PhysicalCompositionComponent>(
                        out var composition, EntityManager.ComponentFactory) ||
                    !composition.MaterialComposition.TryGetValue(material.Id, out var perUnit) ||
                    perUnit <= 0 || amount % perUnit != 0)
                    return result;
                inputs.Add(sheet, amount / perUnit);
            }
            result.Add(new(board, inputs, new() { [output.Id] = 1 }, repair.CompleteTime));
        }
        return result;
    }

    private bool StartFactoryRecipe(Entity<SWGlobalWarFactoryComponent> ent, SWGlobalWarAssignment actor,
        Guid operation, string recipeId, ref string rejected)
    {
        if (ent.Comp.Factory && !FactoryOperator(actor))
        {
            rejected = FactoryRejectedOperator;
            return false;
        }
        if (!ent.Comp.Factory || !TryGetSupplyStore(actor.Generation, ent, actor.Faction, out _, out var inventory))
            return false;
        if (_factoryRecipeWork.TryGetValue(operation, out var previous))
            return previous.Generation == actor.Generation && previous.Store == ent.Owner &&
                   previous.Assignment == actor.Id && previous.Recipe.Id == recipeId;
        var recipe = FactoryRecipes(ent).FirstOrDefault(r => r.Id == recipeId);
        if (recipe == null)
            return false;
        if (_factoryRecipeWork.Values.Any(w => w.Store == ent.Owner && !w.Complete) ||
            GetSupplyProductionWork(actor.Generation, ent).Count != 0)
        {
            rejected = FactoryRejectedBusy;
            return false;
        }
        if (inventory.ContainedEntities.Count + recipe.Results.Values.Sum() > 600)
        {
            rejected = FactoryRejectedSpace;
            return false;
        }
        var inputs = new List<(EntityUid Entity, int Quantity)>();
        foreach (var (prototype, quantity) in recipe.Materials)
        {
            var needed = quantity;
            foreach (var item in inventory.ContainedEntities)
            {
                if (!MatchesSupplyMaterial(item, prototype) ||
                    recipe.Id == "SWGlobalWarUpgradeRifle" &&
                    _supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine) &&
                    magazine.ContainedEntities.Count > 0 ||
                    _supplyItems.TryGetValue(item, out var registered) &&
                    (registered.Generation != actor.Generation || registered.Owner != actor.Faction ||
                     registered.Market || registered.Order != null))
                    continue;
                var count = Math.Min(needed, _supplyStacks.GetCount(item));
                if (count <= 0)
                    continue;
                inputs.Add((item, count));
                needed -= count;
                if (needed == 0)
                    break;
            }
            if (needed != 0)
            {
                rejected = "sw-global-war-factory-rejected-materials";
                return false;
            }
        }
        var escrow = CreateSupplyEscrow();
        var held = _supplyContainers.EnsureContainer<Container>(escrow, SupplyContainer);
        foreach (var (item, _) in inputs)
        {
            if (_supplyContainers.Insert(item, held))
                continue;
            foreach (var moved in held.ContainedEntities.ToArray())
                _supplyContainers.Insert(moved, inventory, force: true);
            Del(escrow);
            return false;
        }
        var work = new SWGlobalWarFactoryRecipeWork(actor.Generation, actor.Id, actor.Faction, ent, recipe, escrow, inputs);
        _factoryRecipeWork.Add(operation, work);
        ent.Comp.Enabled = true;
        return true;
    }

    private void AdvanceFactoryRecipe(SWGlobalWarFactoryRecipeWork work, TimeSpan elapsed)
    {
        if (work.Complete || !TryGetSupplyStore(work.Generation, work.Store, work.Owner, out _, out var inventory))
            return;
        if (!work.Started)
        {
            work.Started = true;
            foreach (var (item, quantity) in work.Inputs)
            {
                if (TryComp<StackComponent>(item, out var stack))
                {
                    if (!_supplyStacks.TryUse((item, stack), quantity))
                        throw new InvalidOperationException("Prepared additional recipe input could not commit.");
                    if (!TerminatingOrDeleted(item) && stack.Count > 0)
                        _supplyContainers.Insert(item, inventory, force: true);
                }
                else
                    Del(item);
            }
            return;
        }
        work.ProductiveTime += elapsed;
        if (work.ProductiveTime < work.Recipe.Duration)
            return;
        if (work.Output == null)
        {
            work.Output = new();
            foreach (var (prototype, quantity) in work.Recipe.Results)
            for (var i = 0; i < quantity; i++)
            {
                var item = Spawn(prototype, new EntityCoordinates(work.Escrow, Vector2.Zero));
                work.Output.Add(item);
                if (TryComp<BatteryComponent>(item, out var battery))
                    _factoryBatteries.SetCharge((item, battery), 0);
                if (_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine))
                    foreach (var loaded in magazine.ContainedEntities.ToArray())
                        Del(loaded);
            }
        }
        if (work.Output.Any(item => !inventory.Contains(item) && !_supplyContainers.CanInsert(item, inventory)))
            return;
        foreach (var item in work.Output)
            if (!inventory.Contains(item) && !_supplyContainers.Insert(item, inventory))
                return;
        work.Complete = true;
    }
}

internal sealed record SWGlobalWarFactoryMaterialization(Guid Generation, Guid Assignment, EntityUid Store,
    string Commodity, EntityUid Item, SWGlobalWarStockReservation Receipt);

internal sealed record SWGlobalWarFactoryRecipe(string Id, Dictionary<string, int> Materials,
    Dictionary<string, int> Results, TimeSpan Duration);

internal sealed class SWGlobalWarFactoryRecipeWork(Guid generation, Guid assignment, SWGlobalWarFaction owner,
    EntityUid store, SWGlobalWarFactoryRecipe recipe, EntityUid escrow, List<(EntityUid Entity, int Quantity)> inputs)
{
    public readonly Guid Generation = generation;
    public readonly Guid Assignment = assignment;
    public readonly SWGlobalWarFaction Owner = owner;
    public readonly EntityUid Store = store;
    public readonly SWGlobalWarFactoryRecipe Recipe = recipe;
    public readonly EntityUid Escrow = escrow;
    public readonly List<(EntityUid Entity, int Quantity)> Inputs = inputs;
    public bool Started;
    public bool Complete;
    public TimeSpan ProductiveTime;
    public List<EntityUid>? Output;
}
