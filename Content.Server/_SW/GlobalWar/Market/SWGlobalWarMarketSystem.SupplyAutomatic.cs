using System.Linq;
using Content.Shared._SW.GlobalWar;

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
            foreach (var faction in model.SupplyPrograms.Keys)
            foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
            {
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
