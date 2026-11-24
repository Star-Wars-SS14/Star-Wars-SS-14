using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SharedStackSystem _supplyStacks = default!;
    private EntityUid? _supplyStagingMap;
    private readonly Dictionary<Guid, SWGlobalWarSupplyProduction> _supplyProduction = new();
    private readonly Dictionary<(EntityUid Store, SWGlobalWarSupplyKind Kind), int> _supplyPlaces = new();

    /// <summary>
    /// Prepares the base program's real inputs. This server-only seam is used by an authorized physical line,
    /// never by a client assertion that something was manufactured. It creates no finished goods.
    /// </summary>
    internal bool TryReserveSupplyProduction(SWGlobalWarSupplyProductionRequest request,
        out SWGlobalWarSupplyProduction production)
    {
        return TryReserveSupplyProduction(request, out production, out _);
    }

    /// <summary>Same as the overload without a failure, and names why the batch could not be prepared.</summary>
    internal bool TryReserveSupplyProduction(SWGlobalWarSupplyProductionRequest request,
        out SWGlobalWarSupplyProduction production, out SWGlobalWarSupplyFailure failure)
    {
        production = default!;
        failure = SWGlobalWarSupplyFailure.Unavailable;
        return CanMutateCampaign(request.Generation) && ReserveSupplyProduction(request, out production, out failure);
    }

    private bool ReserveSupplyProduction(SWGlobalWarSupplyProductionRequest request,
        out SWGlobalWarSupplyProduction production, out SWGlobalWarSupplyFailure failure)
    {
        production = default!;
        failure = SWGlobalWarSupplyFailure.Unavailable;
        if (request.Operation == Guid.Empty || request.Batches is < 1 or > 120 || !Enum.IsDefined(request.Kind) ||
            request.Faction is not (SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire) ||
            !TryGetSupplyStore(request.Generation, request.Store, request.Faction, out var store, out var input))
            return false;
        if (_supplyProduction.TryGetValue(request.Operation, out production!))
        {
            var success = production.Request == request && !production.ReturnPending &&
                production.Stage != SWGlobalWarSupplyStage.Cancelled;
            if (success)
                failure = SWGlobalWarSupplyFailure.None;
            return success;
        }
        failure = SWGlobalWarSupplyFailure.LineBusy;
        if (_supplyProduction.Values.Any(p => p.Request.Faction == request.Faction && p.Request.Kind == request.Kind &&
            p.Stage is SWGlobalWarSupplyStage.Reserved or SWGlobalWarSupplyStage.Producing))
            return false;
        var line = Simulation!.SupplyPrograms[request.Faction].Lines[request.Kind];
        failure = SWGlobalWarSupplyFailure.Revision;
        if (request.Bacta != (request.Kind == SWGlobalWarSupplyKind.Medicine && line.Bacta))
            return false;
        var recipe = SupplyRecipe(request.Kind, request.Bacta);
        var quantity = checked(request.Batches * recipe.Output);
        var rate = Simulation.EffectiveSupplyRate(request.Faction, request.Kind);
        failure = SWGlobalWarSupplyFailure.Rate;
        if (rate <= 0)
            return false;
        failure = SWGlobalWarSupplyFailure.OutputSpace;
        if (!HasSupplyPlace(request.Store, request.Kind, quantity, input))
            return false;
        failure = SWGlobalWarSupplyFailure.Materials;
        if (recipe.Raw.Any(p => Simulation.Markets[store.Planet].Goods[p.Key].Available <
                SWGlobalWarMarketUnits.FromUnits(p.Value * request.Batches)))
            return false;
        var physical = new List<(EntityUid Entity, int Quantity, string Material)>();
        foreach (var (prototype, amount) in recipe.Physical)
        {
            var needed = amount * request.Batches;
            foreach (var item in input.ContainedEntities.Where(e => MatchesSupplyMaterial(e, prototype)))
            {
                if (TerminatingOrDeleted(item) || _supplyItems.ContainsKey(item) ||
                    TryComp<StackComponent>(item, out var stack) && stack.Unlimited)
                    continue;
                var take = Math.Min(needed, _supplyStacks.GetCount(item));
                if (take <= 0)
                    continue;
                physical.Add((item, take, prototype));
                needed -= take;
                if (needed == 0)
                    break;
            }
            if (needed != 0)
                return false;
        }
        var stocks = new List<Guid>();
        EntityUid? escrow = null;
        var committed = false;
        var wasSettling = _settling;
        _settling = true;
        try
        {
            foreach (var (commodity, amount) in recipe.Raw)
            {
                var operation = Guid.NewGuid();
                if (!Simulation.TryReserveStock(new(request.Generation, operation, store.Planet, commodity,
                        SWGlobalWarMarketUnits.FromUnits(amount * request.Batches), request.Faction,
                        SWGlobalWarMarketSimulation.BaseSupplyPurpose), out _))
                    return false;
                stocks.Add(operation);
            }
            escrow = CreateSupplyEscrow();
            var held = _supplyContainers.EnsureContainer<Container>(escrow.Value, SupplyContainer);
            failure = SWGlobalWarSupplyFailure.Transfer;
            foreach (var (item, _, _) in physical)
                if (!_supplyContainers.Insert(item, held))
                    return false;
            production = new(request, quantity, escrow.Value, stocks, physical,
                TimeSpan.FromTicks((long) decimal.Ceiling(quantity * 60m * TimeSpan.TicksPerSecond / rate)),
                line.LocalPercent, line.MarketRemainder);
            _supplyProduction.Add(request.Operation, production);
            _supplyPlaces[(request.Store, request.Kind)] = _supplyPlaces.GetValueOrDefault((request.Store, request.Kind)) + quantity;
            committed = true;
            failure = SWGlobalWarSupplyFailure.None;
            return true;
        }
        finally
        {
            if (!committed)
            {
                foreach (var operation in stocks)
                    Simulation.TryCancelStock(request.Generation, operation);
                foreach (var (item, _, _) in physical)
                    if (!TerminatingOrDeleted(item) && !input.Contains(item))
                        _supplyContainers.Insert(item, input, force: true);
                if (escrow is { } failed)
                    Del(failed);
            }
            _settling = wasSettling;
        }
    }

    /// <summary>Consumes the prevalidated inputs once; WIP remains a funded obligation if procurement owns it.</summary>
    internal bool TryBeginSupplyProduction(Guid generation, Guid operation)
    {
        if (!CanMutateCampaign(generation) || !_supplyProduction.TryGetValue(operation, out var production) ||
            production.Request.Generation != generation || production.ReturnPending ||
            production.Stage == SWGlobalWarSupplyStage.Cancelled ||
            !TryGetSupplyStore(generation, production.Request.Store, production.Request.Faction, out _, out var source))
            return false;
        if (production.Stage != SWGlobalWarSupplyStage.Reserved)
            return true;
        if (!_supplyContainers.TryGetContainer(production.Escrow, SupplyContainer, out var held) ||
            production.Physical.Any(p => TerminatingOrDeleted(p.Entity) || !held.Contains(p.Entity) ||
                !MatchesSupplyMaterial(p.Entity, p.Material) || _supplyStacks.GetCount(p.Entity) < p.Quantity) ||
            production.Stocks.Any(id => !Simulation!.HasPendingStockReservation(generation, id)))
            return false;
        _settling = true;
        try
        {
            // Set the continuing WIP before any synchronous stack or container event can inspect the order.
            production.Stage = SWGlobalWarSupplyStage.Producing;
            production.ReadyAt = Simulation!.Time + production.Duration;
            if (production.Purchase is { } purchaseId && _supplyPurchases.TryGetValue(purchaseId, out var purchase))
                RefreshSupplyPurchaseStage(purchase);
            foreach (var reservation in production.Stocks)
                if (!Simulation.TryCommitStock(generation, reservation, out _))
                    throw new InvalidOperationException("Prepared supply material reservation could not commit.");
            foreach (var (item, quantity, _) in production.Physical)
            {
                if (TryComp<StackComponent>(item, out var stack))
                {
                    if (!_supplyStacks.TryUse((item, stack), quantity))
                        throw new InvalidOperationException("Prepared physical supply input could not commit.");
                    if (!TerminatingOrDeleted(item) && stack.Count > 0)
                        _supplyContainers.Insert(item, source, force: true);
                }
                else
                    Del(item);
            }
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    /// <summary>
    /// Accepts the actual, inaccessible results of an E consumer after the input and time boundaries.
    /// A retry returns the same registered entities; a committed material receipt never permits another output.
    /// </summary>
    internal bool TryCompleteSupplyProduction(Guid generation, Guid operation, IReadOnlyList<EntityUid> output)
    {
        if (!CanMutateCampaign(generation) || !_supplyProduction.TryGetValue(operation, out var production) ||
            production.Request.Generation != generation ||
            !TryGetSupplyStore(generation, production.Request.Store, production.Request.Faction, out _, out var destination))
            return false;
        if (production.Stage == SWGlobalWarSupplyStage.Complete)
            return production.Output.SequenceEqual(output);
        if (production.Stage != SWGlobalWarSupplyStage.Producing || Simulation!.Time < production.ReadyAt ||
            output.Count != production.Quantity || output.Distinct().Count() != output.Count ||
            output.Any(e => _supplyItems.ContainsKey(e) || TerminatingOrDeleted(e) || Transform(e).MapUid != _supplyStagingMap ||
                !IsSuitableSupplyItem(e, production.Request.Kind, production.Request.Faction, production.Request.Bacta) ||
                !IsEmptyFactoryWeapon(e, production.Request.Kind)))
            return false;
        _settling = true;
        var moved = new List<EntityUid>();
        try
        {
            var staging = _supplyContainers.EnsureContainer<Container>(production.Escrow, SupplyContainer);
            var purchase = production.Purchase is { } purchaseId ? _supplyPurchases.GetValueOrDefault(purchaseId) : null;
            var assigned = purchase == null ? 0 : purchase.ProductionQuantity;
            BaseContainer? orderStorage = null;
            if (assigned > 0 && !_supplyContainers.TryGetContainer(purchase!.Escrow, SupplyContainer, out orderStorage))
                return false;
            for (var i = 0; i < output.Count; i++)
            {
                var item = output[i];
                if (!_supplyContainers.Insert(item, i < assigned ? orderStorage! : destination))
                {
                    foreach (var prepared in moved)
                        _supplyContainers.Insert(prepared, staging, force: true);
                    return false;
                }
                moved.Add(item);
            }
            for (var i = 0; i < output.Count; i++)
            {
                var item = output[i];
                _supplyItems.Add(item, new(generation, production.Request.Kind, production.Request.Bacta,
                    production.Request.Faction, production.Request.Faction, SWGlobalWarSupplyOrigin.Industrial,
                    operation, Guid.NewGuid(), Order: i < assigned ? purchase!.Request.Operation : null,
                    Market: i < production.MarketQuantity));
                if (i < assigned)
                    purchase!.Goods.Add(item);
            }
            if (purchase != null)
            {
                purchase.ProductionQuantity = 0;
                RefreshSupplyPurchaseStage(purchase);
            }
            production.Output = output.ToArray();
            Simulation!.SupplyPrograms[production.Request.Faction].Lines[production.Request.Kind].MarketRemainder =
                production.MarketRemainderAfter;
            production.Stage = SWGlobalWarSupplyStage.Complete;
            _supplyPlaces[(production.Request.Store, production.Request.Kind)] -= production.Quantity;
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    private bool IsEmptyFactoryWeapon(EntityUid item, SWGlobalWarSupplyKind kind)
    {
        if (kind == SWGlobalWarSupplyKind.Magazine)
            return Comp<BatteryComponent>(item).CurrentCharge == 0;
        return kind is not (SWGlobalWarSupplyKind.T1 or SWGlobalWarSupplyKind.T2) ||
               !_supplyContainers.TryGetContainer(item, "gun_magazine", out var magazine) || magazine.ContainedEntities.Count == 0;
    }

    private bool HasSupplyPlace(EntityUid store, SWGlobalWarSupplyKind kind, int quantity, BaseContainer container)
    {
        var physical = container.ContainedEntities.Count(e => _supplyItems.TryGetValue(e, out var item) && item.Kind == kind);
        return (long) physical + _supplyPlaces.GetValueOrDefault((store, kind)) + quantity <= 600;
    }

    /// <summary>Closes only unfinished production; consumed inputs and existing outputs are never recreated or deleted.</summary>
    internal bool TryCancelSupplyProduction(Guid generation, Guid operation, EntityUid? returnStore = null)
    {
        if (!CanMutateCampaign(generation) || !_supplyProduction.TryGetValue(operation, out var production) ||
            production.Request.Generation != generation || production.Stage == SWGlobalWarSupplyStage.Complete ||
            production.Purchase is { } purchaseId && _supplyPurchases[purchaseId].Obligation.Outstanding > 0)
            return false;
        _settling = true;
        try
        {
            return CancelSupplyProduction(production, returnStore);
        }
        finally
        {
            _settling = false;
        }
    }

    private bool CancelSupplyProduction(SWGlobalWarSupplyProduction production, EntityUid? returnStore = null)
    {
        if (production.Stage is SWGlobalWarSupplyStage.Cancelled or SWGlobalWarSupplyStage.Complete)
            return true;
        if (production.Stage == SWGlobalWarSupplyStage.Reserved)
        {
            // A missing store does not destroy the inputs held outside it. Keep the reservation and reject Begin
            // until the same physical return point is restored; cancellation must not strand a whole excess stack.
            production.ReturnPending = true;
            var remaining = production.Physical.Where(p => !TerminatingOrDeleted(p.Entity) &&
                _supplyContainers.TryGetContainingContainer(p.Entity, out var held) && held.Owner == production.Escrow).ToArray();
            if (remaining.Length > 0)
            {
                if (!_supplyStores.TryGetValue(production.Request.Store, out var original) ||
                    !TryGetSupplyStore(production.Request.Generation, returnStore ?? production.Request.Store,
                        Simulation!.Markets[original.Planet].Owner, out var restored, out var source) ||
                    restored.Planet != original.Planet || restored.Coordinates != original.Coordinates)
                    return false;
                foreach (var (item, _, _) in remaining)
                    if (!_supplyContainers.Insert(item, source))
                        return false;
            }
            foreach (var reservation in production.Stocks)
                Simulation?.TryCancelStock(production.Request.Generation, reservation);
        }
        production.ReturnPending = false;
        production.Stage = SWGlobalWarSupplyStage.Cancelled;
        _supplyPlaces[(production.Request.Store, production.Request.Kind)] -= production.Quantity;
        return true;
    }

    private void ResetSupplyProduction()
    {
        var wasSettling = _settling;
        _settling = true;
        try
        {
            foreach (var production in _supplyProduction.Values)
                CancelSupplyProduction(production);
            _supplyProduction.Clear();
            _supplyPlaces.Clear();
            if (_supplyStagingMap is { } map && !TerminatingOrDeleted(map))
                QueueDel(map);
            _supplyStagingMap = null;
        }
        finally
        {
            _settling = wasSettling;
        }
    }

    private EntityUid CreateSupplyEscrow()
    {
        if (_supplyStagingMap is not { } map || TerminatingOrDeleted(map))
            _supplyStagingMap = _hubMaps.CreateMap();
        return Spawn(null, new EntityCoordinates(_supplyStagingMap.Value, Vector2.Zero));
    }

    private bool MatchesSupplyMaterial(EntityUid item, string prototype)
    {
        if (TerminatingOrDeleted(item))
            return false;
        var expected = _prototypes.Index<EntityPrototype>(prototype);
        if (expected.TryGetComponent<StackComponent>(out var material, EntityManager.ComponentFactory))
            return TryComp<StackComponent>(item, out var actual) && !actual.Unlimited &&
                   actual.StackTypeId == material.StackTypeId;
        return MetaData(item).EntityPrototype?.ID == prototype;
    }

    internal EntityCoordinates SupplyProductionOutputCoordinates(Guid operation)
    {
        return new(_supplyProduction[operation].Escrow, Vector2.Zero);
    }

    private static SWGlobalWarSupplyRecipe SupplyRecipe(SWGlobalWarSupplyKind kind, bool bacta)
    {
        return kind switch
        {
            SWGlobalWarSupplyKind.T2 => new(1, new() { ["SWGlobalWarE"] = 2 }, new() { ["SWBlasterFrame"] = 1, ["SWBlasterBarrel"] = 1 }),
            SWGlobalWarSupplyKind.T1 => new(1, new() { ["SWGlobalWarS"] = 2, ["SWGlobalWarE"] = 1 }, new()),
            SWGlobalWarSupplyKind.Armor => new(1, new(), new() { ["SWReinforcedSteel"] = 5 }),
            SWGlobalWarSupplyKind.Magazine => new(1, new() { ["SWGlobalWarS"] = 1 }, new() { ["SWAdvancedCables1"] = 1 }),
            SWGlobalWarSupplyKind.Ration => new(2, new() { ["SWGlobalWarF"] = 2 }, new()),
            SWGlobalWarSupplyKind.Medicine => new(5, new() { ["SWGlobalWarF"] = 10, [bacta ? "SWGlobalWarE" : "SWGlobalWarS"] = 1 }, new()),
            SWGlobalWarSupplyKind.AdvancedElectronics => new(1, new() { ["SWGlobalWarE"] = 1 }, new() { ["SWAdvancedCables1"] = 2, ["SWSheetLantanidAlloy1"] = 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

public sealed record SWGlobalWarSupplyProductionRequest(Guid Generation, Guid Operation, EntityUid Store,
    SWGlobalWarFaction Faction, SWGlobalWarSupplyKind Kind, int Batches, bool Bacta = false);

public sealed class SWGlobalWarSupplyProduction(SWGlobalWarSupplyProductionRequest request, int quantity, EntityUid escrow,
    IReadOnlyList<Guid> stocks, IReadOnlyList<(EntityUid Entity, int Quantity, string Material)> physical, TimeSpan duration,
    int localPercent, decimal marketRemainder)
{
    public SWGlobalWarSupplyProductionRequest Request { get; } = request;
    public int Quantity { get; } = quantity;
    public EntityUid Escrow { get; } = escrow;
    public IReadOnlyList<Guid> Stocks { get; } = stocks;
    public IReadOnlyList<(EntityUid Entity, int Quantity, string Material)> Physical { get; } = physical;
    public TimeSpan Duration { get; } = duration;
    public int LocalPercent { get; } = localPercent;
    public decimal MarketRemainderBefore { get; } = marketRemainder;
    public int MarketQuantity { get; } = (int) decimal.Floor(marketRemainder + quantity * (100m - localPercent) / 100m);
    public decimal MarketRemainderAfter { get; } =
        (marketRemainder + quantity * (100m - localPercent) / 100m) % 1m;
    public SWGlobalWarSupplyStage Stage { get; internal set; }
    public bool ReturnPending { get; internal set; }
    public Guid? Purchase { get; internal set; }
    public TimeSpan ReadyAt { get; internal set; }
    public IReadOnlyList<EntityUid> Output { get; internal set; } = [];
}

public sealed record SWGlobalWarSupplyRecipe(int Output, Dictionary<string, int> Raw, Dictionary<string, int> Physical);
