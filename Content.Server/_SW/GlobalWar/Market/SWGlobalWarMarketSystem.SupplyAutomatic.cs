using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    private readonly HashSet<(EntityUid Store, SWGlobalWarSupplyKind Kind)> _supplyConsumers = new();
    private readonly Dictionary<(SWGlobalWarFaction Faction, int Window, SWGlobalWarSupplyKind Kind), SWGlobalWarSupplyIntent>
        _supplyIntents = new();
    private bool _supplyAutomaticRunning;

    /// <summary>
    /// Registers the source served by an E production consumer. Registration only permits real input reservations;
    /// the consumer still has to begin the recipe and present its actual output. No entity is manufactured here.
    /// </summary>
    internal bool TryRegisterSupplyConsumer(Guid generation, EntityUid store, SWGlobalWarSupplyKind kind)
    {
        if (!CanMutateCampaign(generation) || !Enum.IsDefined(kind) || !_supplyStores.TryGetValue(store, out var binding) ||
            !TryGetSupplyStore(generation, store, Simulation!.Markets[binding.Planet].Owner, out binding, out _) || binding.Arsenal)
            return false;
        _supplyConsumers.Add((store, kind));
        return true;
    }

    internal IReadOnlyList<SWGlobalWarSupplyProduction> GetSupplyProductionWork(Guid generation, EntityUid store)
    {
        return _supplyProduction.Values.Where(p => p.Request.Generation == generation && p.Request.Store == store &&
            p.Stage is SWGlobalWarSupplyStage.Reserved or SWGlobalWarSupplyStage.Producing).ToArray();
    }

    internal IReadOnlyCollection<SWGlobalWarSupplyIntent> SupplyIntents => _supplyIntents.Values;

    private void RunAutomaticSupplyPrograms()
    {
        if (_supplyAutomaticRunning || _campaignGeneration is not { } generation ||
            !CanMutateCampaign(generation, _ownershipCatchUp) || Simulation is not { } model)
            return;
        _supplyAutomaticRunning = true;
        try
        {
            foreach (var (faction, program) in model.SupplyPrograms)
            foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
            {
                if (!CanMutateCampaign(generation, _ownershipCatchUp))
                    return;
                if (program.Window > 0 && program.Windows[program.Window].Budgets[kind] > 0)
                {
                    var key = (faction, program.Window, kind);
                    if (!_supplyIntents.TryGetValue(key, out var intent))
                    {
                        intent = new(Guid.NewGuid(), generation, faction, program.Window, kind,
                            program.Windows[program.Window].Budgets[kind]);
                        _supplyIntents.Add(key, intent);
                    }
                    if (!intent.Suspended)
                        FulfillSupplyIntent(intent);
                }
                if (!CanMutateCampaign(generation, _ownershipCatchUp))
                    return;
                PrepareBaseSupply(generation, faction, kind);
            }
        }
        finally
        {
            _supplyAutomaticRunning = false;
        }
    }

    private void FulfillSupplyIntent(SWGlobalWarSupplyIntent intent)
    {
        var program = Simulation!.SupplyPrograms[intent.Faction];
        var outstanding = program.Orders.Values.Where(o => o.Kind == intent.Kind).Sum(o => (long) o.Outstanding);
        var wanted = Math.Min(program.RemainingQuota(intent.Kind), Math.Max(0L,
            SWGlobalWarSupplyProgram.Target(intent.Kind, program.Participants) -
            CountAcceptedSupplyStock(intent.Faction, intent.Kind) - outstanding));
        if (wanted == 0)
        {
            intent.Failure = SWGlobalWarSupplyFailure.None;
            return;
        }
        // Changing a factory recipe does not authorize replacing the military program's kolto with dearer bacta.
        // Bacta procurement requires an explicit purchase request with its own composition and price.
        const bool bacta = false;
        var unitPrice = SWGlobalWarSupplyProgram.UnitPrice(intent.Kind, bacta);
        var affordable = _ledger.GetBalance(intent.Faction, SWGlobalWarAccount.War).Available / unitPrice;
        if (affordable == 0)
        {
            intent.Failure = SWGlobalWarSupplyFailure.Funds;
            return;
        }
        var quantity = (int) Math.Min(120L, Math.Min(wanted, affordable));
        intent.Failure = SWGlobalWarSupplyFailure.Unavailable;
        // The capital arsenal is the program's primary receiver. A world's own arsenal is only the last fallback,
        // otherwise a farm would keep its output at home whenever its map happened to register first.
        var recipients = _supplyStores.OrderBy(p => Simulation.Markets[p.Value.Planet].Kind switch
        {
            SWGlobalWarSectorKind.Capital => 0,
            SWGlobalWarSectorKind.Industry => 1,
            _ => 2,
        }).Select(p => p.Key).ToArray();
        foreach (var source in _supplyStores.Keys)
        {
            if (!TryGetSupplyStore(intent.Generation, source, intent.Faction, out var sourceBinding, out var inventory) ||
                sourceBinding.Arsenal || !_supplyConsumers.Contains((source, intent.Kind)))
                continue;
            foreach (var recipient in recipients)
            {
                if (!TryGetSupplyStore(intent.Generation, recipient, intent.Faction, out var recipientBinding, out _) ||
                    !recipientBinding.Arsenal ||
                    (recipientBinding.Planet != sourceBinding.Planet && !IsHomeSupplyArsenal(recipientBinding, intent.Faction)) ||
                    !_ledger.TryGetLedger(out var ledger))
                    continue;
                // Remote orders use the physical manifest consumer and its current policy checks before any reservation.
                var request = new SWGlobalWarSupplyPurchaseRequest(intent.Generation, Guid.NewGuid(), ledger.Comp.Revision,
                    intent.Faction, source, recipient, intent.Kind, quantity, bacta, Intent: intent.Id);
                var ready = inventory.ContainedEntities.Count(e => IsUnpaidSupplyItem(e, request));
                var deficit = SupplyProductionDeficit(intent.Generation, intent.Faction, intent.Kind);
                var recipe = SupplyRecipe(intent.Kind, bacta);
                var line = program.Lines[intent.Kind];
                // Only a positive total deficit permits another whole recipe. Local goods and unallocated WIP
                // already cover that target, even when they cannot be purchased from the market allocation.
                var batches = (deficit + recipe.Output - 1) / recipe.Output;
                var newMarket = batches == 0 ? 0L : (long) decimal.Floor(line.MarketRemainder +
                    batches * recipe.Output * (100m - line.LocalPercent) / 100m);
                var possible = (int) Math.Min(quantity, ready + newMarket);
                if (possible == 0)
                {
                    intent.Failure = SWGlobalWarSupplyFailure.ExistingSupply;
                    continue;
                }
                request = request with { Quantity = possible };
                if (ReserveSupplyPurchase(request, out var purchase, out var failure, automatic: true))
                {
                    intent.Orders.Add(purchase.Request.Operation);
                    intent.Failure = SWGlobalWarSupplyFailure.None;
                    return;
                }
                intent.Failure = failure;
                // An unavailable new recipe must not hold up the already existing usable market portion.
                if (ready > 0 && ready < request.Quantity && failure is SWGlobalWarSupplyFailure.Materials or
                    SWGlobalWarSupplyFailure.LineBusy or SWGlobalWarSupplyFailure.Rate or
                    SWGlobalWarSupplyFailure.OutputSpace or SWGlobalWarSupplyFailure.LocalOnly or
                    SWGlobalWarSupplyFailure.Revision)
                {
                    request = request with { Operation = Guid.NewGuid(), Quantity = ready };
                    if (ReserveSupplyPurchase(request, out purchase, out failure, automatic: true))
                    {
                        intent.Orders.Add(purchase.Request.Operation);
                        intent.Failure = SWGlobalWarSupplyFailure.None;
                        return;
                    }
                    intent.Failure = failure;
                }
            }
        }
    }

    private void PrepareBaseSupply(Guid generation, SWGlobalWarFaction faction, SWGlobalWarSupplyKind kind)
    {
        if (SupplyProductionDeficit(generation, faction, kind) == 0)
            return;
        var program = Simulation!.SupplyPrograms[faction];
        foreach (var (store, type) in _supplyConsumers)
        {
            if (type != kind || !TryGetSupplyStore(generation, store, faction, out _, out _))
                continue;
            if (ReserveSupplyProduction(new(generation, Guid.NewGuid(), store, faction, kind, 1,
                    kind == SWGlobalWarSupplyKind.Medicine && program.Lines[kind].Bacta), out _, out _))
                return;
        }
    }

    private long SupplyProductionDeficit(Guid generation, SWGlobalWarFaction faction, SWGlobalWarSupplyKind kind)
    {
        var program = Simulation!.SupplyPrograms[faction];
        var outstanding = program.Orders.Values.Where(o => o.Kind == kind).Sum(o => (long) o.Outstanding);
        var factoryStock = 0L;
        foreach (var store in _supplyStores.Keys)
        {
            if (!TryGetSupplyStore(generation, store, faction, out var binding, out var inventory) || binding.Arsenal)
                continue;
            factoryStock += inventory.ContainedEntities.Count(e => _supplyItems.TryGetValue(e, out var item) &&
                item.Generation == generation && item.Owner == faction && item.Kind == kind && item.Order == null &&
                IsSuitableSupplyItem(e, kind, faction, item.Bacta));
        }
        var freeWork = _supplyProduction.Values.Where(p => p.Request.Generation == generation &&
                p.Request.Faction == faction && p.Request.Kind == kind && !p.ReturnPending &&
                p.Stage is SWGlobalWarSupplyStage.Reserved or SWGlobalWarSupplyStage.Producing)
            .Sum(p => (long) p.Quantity - (p.Purchase is { } id ? _supplyPurchases[id].ProductionQuantity : 0));
        return Math.Max(0L, SWGlobalWarSupplyProgram.Target(kind, program.Participants) -
            CountAcceptedSupplyStock(faction, kind) - factoryStock - outstanding - freeWork);
    }

    private void SuspendSupplyIntent(SWGlobalWarSupplyPurchase purchase)
    {
        if (purchase.Request.Intent == Guid.Empty)
            return;
        foreach (var intent in _supplyIntents.Values)
            if (intent.Id == purchase.Request.Intent)
                intent.Suspended = true;
    }

    private void ResetSupplyAutomatic()
    {
        ResetSupplyAuthority();
        _supplyConsumers.Clear();
        _supplyIntents.Clear();
        _supplyAutomaticRunning = false;
    }
}

/// <summary>One unmet intention per fixed window and kind; attempts do not multiply its quantity or create stock.</summary>
public sealed class SWGlobalWarSupplyIntent(Guid id, Guid generation, SWGlobalWarFaction faction, int window,
    SWGlobalWarSupplyKind kind, int budget)
{
    public Guid Id { get; } = id;
    public Guid Generation { get; } = generation;
    public SWGlobalWarFaction Faction { get; } = faction;
    public int Window { get; } = window;
    public SWGlobalWarSupplyKind Kind { get; } = kind;
    public int Budget { get; } = budget;
    public List<Guid> Orders { get; } = new();
    public SWGlobalWarSupplyFailure Failure { get; internal set; }
    public bool Suspended { get; internal set; }
}
