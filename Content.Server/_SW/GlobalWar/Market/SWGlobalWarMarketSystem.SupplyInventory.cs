using System.Linq;
using Content.Server.Destructible;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Power.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SharedContainerSystem _supplyContainers = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _supplySolutions = default!;
    [Dependency] private readonly MetaDataSystem _supplyMetadata = default!;
    private const string SupplyContainer = "sw-program-supply";
    private readonly Dictionary<EntityUid, SWGlobalWarSupplyStore> _supplyStores = new();
    private readonly Dictionary<EntityUid, SWGlobalWarSupplyItem> _supplyItems = new();

    internal bool TryGetSupplyItem(EntityUid entity, out SWGlobalWarSupplyItem item)
    {
        return _supplyItems.TryGetValue(entity, out item!);
    }

    /// <summary>Registers an actual stationary store in the current surface market, without creating any goods.</summary>
    internal bool TryRegisterSupplyStore(Guid generation, EntityUid store, bool arsenal = false)
    {
        if (!CanMutateCampaign(generation) || TerminatingOrDeleted(store) ||
            !_hubPlanets.TryGetPhysicalLocation(store, out var planet, out var location) ||
            location != SWGlobalWarPlanetLocation.Surface)
            return false;
        var world = Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id;
        if (!Simulation!.Markets.ContainsKey(world))
            return false;
        if (_supplyStores.TryGetValue(store, out var old))
            return old.Generation == generation && old.Planet == world && old.Coordinates == Transform(store).Coordinates &&
                old.Arsenal == arsenal;
        _supplyContainers.EnsureContainer<Container>(store, SupplyContainer);
        var originalName = Name(store);
        var label = Loc.GetString(arsenal ? "sw-global-war-supply-arsenal-point" :
            "sw-global-war-supply-source-point", ("number", _supplyStores.Count + 1), ("name", originalName));
        _supplyStores.Add(store, new(generation, world, Transform(store).Coordinates, arsenal, label, originalName));
        _supplyMetadata.SetEntityName(store, label);
        return true;
    }

    private bool TryGetSupplyStore(Guid generation, EntityUid store, SWGlobalWarFaction owner,
        out SWGlobalWarSupplyStore binding, out BaseContainer container)
    {
        binding = default!;
        container = default!;
        return !TerminatingOrDeleted(store) && _supplyStores.TryGetValue(store, out binding!) &&
            binding.Generation == generation && Simulation?.Generation == generation &&
            Simulation.Markets[binding.Planet].Owner == owner && Transform(store).Coordinates == binding.Coordinates &&
            _hubPlanets.TryGetPhysicalLocation(store, out var planet, out var location) &&
            location == SWGlobalWarPlanetLocation.Surface && Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id == binding.Planet &&
            _supplyContainers.TryGetContainer(store, SupplyContainer, out container!);
    }

    /// <summary>Reads actual registered items, not a supplied suitability flag or a second integer inventory.</summary>
    internal bool IsSuitableSupplyItem(EntityUid item, SWGlobalWarSupplyKind kind, SWGlobalWarFaction faction,
        bool bacta = false)
    {
        if (TerminatingOrDeleted(item) || TryComp<DestructibleComponent>(item, out var destructible) && destructible.IsBroken)
            return false;
        var prototype = MetaData(item).EntityPrototype?.ID;
        if (kind == SWGlobalWarSupplyKind.Armor)
            return prototype is "SWRepublicSuit" or "SWImperialSuit";
        if (prototype != SupplyPrototype(kind, faction, bacta).Id)
            return false;
        if (kind == SWGlobalWarSupplyKind.Medicine)
        {
            var required = bacta ? 10 : 15;
            return _supplySolutions.TryGetSolution(item, "pen", out _, out var solution) &&
                solution.Volume == required &&
                solution.GetTotalPrototypeQuantity(bacta ? "SWBacta" : "SWKolto") == required;
        }
        if (kind == SWGlobalWarSupplyKind.Magazine)
            return TryComp<BatteryComponent>(item, out var battery) && float.IsFinite(battery.CurrentCharge) &&
                   float.IsFinite(battery.MaxCharge) && battery.MaxCharge > 0 &&
                   battery.CurrentCharge >= 0 && battery.CurrentCharge <= battery.MaxCharge;
        if (kind == SWGlobalWarSupplyKind.Ration)
            return IsCompleteSupplyRation(item);
        return true;
    }

    private bool IsCompleteSupplyRation(EntityUid box)
    {
        if (!_supplyContainers.TryGetContainer(box, "storagebase", out var contents) || contents.ContainedEntities.Count != 5)
            return false;
        var expected = new HashSet<string>
        {
            "DrinkMREFlask", "SWDustBread", "SWGorntSausage", "SWCannedGorntMeat", "SWProteinBar",
        };
        foreach (var item in contents.ContainedEntities)
        {
            if (TerminatingOrDeleted(item) || MetaData(item).EntityPrototype is not { } prototype ||
                !expected.Remove(prototype.ID))
                return false;
            // Compare actual consumable contents with the inherited prototype's full initial solution.
            // Sealed wrappers that replace themselves on use retain their exact original prototype here.
            if (!prototype.TryGetComponent<SolutionContainerManagerComponent>(out var initial, EntityManager.ComponentFactory) ||
                initial.Solutions == null)
                continue;
            foreach (var (name, original) in initial.Solutions)
            {
                if (!_supplySolutions.TryGetSolution(item, name, out _, out var actual) ||
                    actual.Volume != original.Volume || actual.Contents.Count != original.Contents.Count ||
                    original.Contents.Any(r => actual.GetReagentQuantity(r.Reagent) != r.Quantity))
                    return false;
            }
        }
        return expected.Count == 0;
    }

    internal static EntProtoId SupplyPrototype(SWGlobalWarSupplyKind kind, SWGlobalWarFaction faction, bool bacta = false)
    {
        return kind switch
        {
            SWGlobalWarSupplyKind.T2 => faction == SWGlobalWarFaction.Republic ? "SWH206Renegade" : "SWE1BlasterRifle",
            SWGlobalWarSupplyKind.T1 => "SWScoutBlasterSmall",
            SWGlobalWarSupplyKind.Armor => faction == SWGlobalWarFaction.Republic ? "SWRepublicSuit" : "SWImperialSuit",
            SWGlobalWarSupplyKind.Magazine => faction == SWGlobalWarFaction.Republic ? "SWH206Magazine" : "SWE1Magazine",
            SWGlobalWarSupplyKind.Ration => "SWBoxMRE",
            SWGlobalWarSupplyKind.Medicine => bacta ? "SWBactaMedipen" : "SWKoltoMedipen",
            SWGlobalWarSupplyKind.AdvancedElectronics => "SWAdvancedElectronics",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private int CountAcceptedSupplyStock(SWGlobalWarFaction faction, SWGlobalWarSupplyKind kind)
    {
        if (_campaignGeneration is not { } generation)
            return 0;
        var items = new HashSet<EntityUid>();
        foreach (var store in _supplyStores.Keys)
        {
            if (!TryGetSupplyStore(generation, store, faction, out var binding, out var container) || !binding.Arsenal)
                continue;
            foreach (var item in container.ContainedEntities)
            {
                if (_supplyItems.TryGetValue(item, out var record) && record.Generation == generation &&
                    record.Owner == faction && record.Kind == kind && record.Accepted &&
                    IsSuitableSupplyItem(item, kind, faction, record.Bacta))
                    items.Add(item);
            }
        }
        return items.Count;
    }

    private void CaptureSupplyStores(IReadOnlyList<SWGlobalWarMarketOwnershipChange> changes)
    {
        if (Simulation?.Generation is not { } generation)
            return;
        foreach (var purchase in _supplyPurchases.Values)
        {
            // Goods held for pickup still belong to their logical source node even if its device was destroyed.
            // A carrier has already left that escrow and is deliberately not included in this territorial transfer.
            foreach (var change in changes.Where(c => _supplyStores[purchase.Request.Source].Planet == c.Planet))
                if (_supplyContainers.TryGetContainer(purchase.Escrow, SupplyContainer, out var held))
                    foreach (var entity in held.ContainedEntities)
                        if (_supplyItems.TryGetValue(entity, out var item) && item.Owner != change.Owner)
                            _supplyItems[entity] = item with { Owner = change.Owner, Origin = SWGlobalWarSupplyOrigin.Trophy };
            if (!IsInterplanetSupplyPurchase(purchase) && changes.Any(c => c.Owner != purchase.Request.Faction &&
                    (_supplyStores[purchase.Request.Source].Planet == c.Planet ||
                     _supplyStores[purchase.Request.Recipient].Planet == c.Planet)))
                CancelSupplyPurchase(purchase);
        }
        foreach (var production in _supplyProduction.Values)
            if (changes.Any(c => c.Owner != production.Request.Faction &&
                    _supplyStores[production.Request.Store].Planet == c.Planet))
                CancelSupplyProduction(production);
        foreach (var change in changes)
        foreach (var (store, binding) in _supplyStores.Where(p => p.Value.Planet == change.Planet))
        {
            if (!TryGetSupplyStore(generation, store, change.Owner, out _, out var container))
                continue;
            foreach (var item in container.ContainedEntities)
                if (_supplyItems.TryGetValue(item, out var record) && record.Generation == generation && record.Owner != change.Owner)
                    _supplyItems[item] = record with
                    {
                        Owner = change.Owner, Order = null, Origin = SWGlobalWarSupplyOrigin.Trophy,
                    };
        }
    }

    private void ResetSupplyInventory()
    {
        ResetSupplyAutomatic();
        ResetSupplyPurchases();
        ResetSupplyProduction();
        _supplyItems.Clear();
        _startingArsenals.Clear();
        foreach (var (entity, store) in _supplyStores)
            if (!TerminatingOrDeleted(entity) && Name(entity) == store.Name)
                _supplyMetadata.SetEntityName(entity, store.OriginalName);
        _supplyStores.Clear();
    }
}

public sealed record SWGlobalWarSupplyStore(Guid Generation, string Planet,
    Robust.Shared.Map.EntityCoordinates Coordinates, bool Arsenal, string Name, string OriginalName);

public enum SWGlobalWarSupplyOrigin : byte { Industrial, Starting, Trophy }

/// <summary>Provenance attached to one real entity; payment history survives issue, return and ownership changes.</summary>
public sealed record SWGlobalWarSupplyItem(Guid Generation, SWGlobalWarSupplyKind Kind, bool Bacta,
    SWGlobalWarFaction OriginOwner, SWGlobalWarFaction Owner, SWGlobalWarSupplyOrigin Origin, Guid Source, Guid Unit,
    bool Accepted = false, Guid? Payment = null, Guid? Order = null, bool Market = false);
