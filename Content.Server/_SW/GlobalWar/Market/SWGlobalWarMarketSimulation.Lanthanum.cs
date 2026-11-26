using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSimulation
{
    internal const string BaseSupplyPurpose = "base-supply-program";
    private const string Lanthanum = "SWGlobalWarLn";
    private readonly Dictionary<string, SWGlobalWarLanthanumDeposit> _lanthanumDeposits = new();
    private readonly Dictionary<SWGlobalWarFaction, SWGlobalWarLanthanumDoctrine> _lanthanumDoctrines = new()
    {
        [SWGlobalWarFaction.Republic] = new(), [SWGlobalWarFaction.Empire] = new(),
    };
    internal Func<SWGlobalWarLanthanumRelease, Action, bool>? SettleLanthanum;
    internal Func<string, SWGlobalWarFaction, bool>? CanFundLanthanum;
    internal Action? ReconcileLanthanumFunding;
    public IReadOnlyDictionary<string, SWGlobalWarLanthanumDeposit> LanthanumDeposits => _lanthanumDeposits;
    public IReadOnlyDictionary<SWGlobalWarFaction, SWGlobalWarLanthanumDoctrine> LanthanumDoctrines => _lanthanumDoctrines;

    internal bool SetLanthanumDoctrine(SWGlobalWarFaction faction, long revision, bool automatic)
    {
        if (_advancing || Generation == null || IsActive?.Invoke() == false ||
            !_lanthanumDoctrines.TryGetValue(faction, out var doctrine) || doctrine.Revision != revision)
            return false;
        doctrine.Automatic = automatic;
        doctrine.Revision++;
        RefreshLanthanumDoctrine(faction, doctrine);
        return true;
    }

    private SWGlobalWarMarketState[] HomeIndustry(SWGlobalWarFaction faction)
    {
        return _markets.Values.Where(m => m.OriginalOwner == faction && m.Owner == faction &&
            m.Kind == SWGlobalWarSectorKind.Industry && m.Goods.ContainsKey(Lanthanum)).ToArray();
    }

    /// <summary>Normative demand includes each recipe chain once, even while execution lacks inputs or capacity.</summary>
    public static decimal LanthanumDemand(decimal civilElectronics, SWGlobalWarBaseSupplyNorm norm)
    {
        return 3 * civilElectronics + 12 * norm.T2 + 3 * norm.T1 + 5 * norm.AdvancedElectronics + 0.6m * norm.Bacta;
    }

    public static decimal EmergencyT2Rate(decimal civilElectronics)
    {
        return Math.Max(0, Math.Min(0.5m, (12 - 3 * civilElectronics) / 12));
    }

    private decimal AvailableProgramLanthanum(SWGlobalWarMarketState market, SWGlobalWarFaction faction)
    {
        var good = market.Goods[Lanthanum];
        long quantity = good.Batches.Where(b => b.Owner == faction).Sum(b => (long) b.Quantity - b.Reserved);
        foreach (var reservation in _stockReservations.Values.Where(r => r.Status == SWGlobalWarStockReservationStatus.Pending &&
                     r.Request.Owner == faction && r.Request.Planet == market.Planet && r.Request.Commodity == Lanthanum &&
                     r.Request.Purpose == BaseSupplyPurpose))
        foreach (var part in reservation.Parts)
            if (good.MutableBatches!.TryGetValue(part.Batch, out var batch) && batch.Owner == faction)
                quantity += part.Quantity;
        return quantity / (decimal) UnitsPerItem;
    }

    /// <summary>Searches all policy-valid economic paths without considering stock or temporarily occupied capacity.</summary>
    internal bool HasLanthanumPath(string source, string destination, SWGlobalWarFaction faction)
    {
        if (Generation is not { } generation || !_commodities.TryGetValue(Lanthanum, out var commodity) ||
            !_markets.ContainsKey(source) || !_markets.ContainsKey(destination))
            return false;
        var request = new SWGlobalWarTradeRequest(generation, Lanthanum, faction, faction, source, destination,
            source, destination, SWGlobalWarTradeStage.Receipt);
        var visited = new HashSet<string> { source };
        var queue = new Queue<string>();
        queue.Enqueue(source);
        while (queue.TryDequeue(out var current))
        {
            if (current == destination && (TradePolicy == null || TradePolicy(request with { Current = destination }).Allowed))
                return true;
            foreach (var edge in _outgoing[current])
            {
                if (visited.Contains(edge.To) || TransferQuantity(1, _laneCapacity[Lane(edge.From, edge.To)], commodity.CargoVolume) == 0 ||
                    TradePolicy != null && !TradePolicy(request with
                    {
                        Current = current, Next = edge.To,
                        Stage = current == source ? SWGlobalWarTradeStage.Dispatch : SWGlobalWarTradeStage.NextLeg,
                    }).Allowed)
                    continue;
                visited.Add(edge.To);
                queue.Enqueue(edge.To);
            }
        }
        return false;
    }

    private void RefreshLanthanumDoctrine(SWGlobalWarFaction faction, SWGlobalWarLanthanumDoctrine doctrine)
    {
        doctrine.WaitingReason = SWGlobalWarLanthanumWaitingReason.None;
        var industries = HomeIndustry(faction);
        doctrine.CivilElectronics = _markets.Values.Where(m => m.OriginalOwner == faction && m.Owner == faction &&
                m.Goods.ContainsKey("SWGlobalWarE"))
            .Sum(m => MandatoryRate(m, "SWGlobalWarE"));
        doctrine.Demand = LanthanumDemand(doctrine.CivilElectronics, doctrine.Norm);
        doctrine.Stock = industries.Sum(m => AvailableProgramLanthanum(m, faction));
        var enclave = _markets.Values.FirstOrDefault(m => m.OriginalOwner == faction && m.Enclave &&
            m.Profile?.Recipes.Any(r => r.Output.Id == Lanthanum) == true);
        var accessible = enclave != null && enclave.Owner == faction &&
            industries.Any(m => HasLanthanumPath(enclave.Planet, m.Planet, faction));
        if (!doctrine.Automatic || industries.Length == 0 || accessible || enclave == null)
        {
            doctrine.WaitingReason = accessible ? SWGlobalWarLanthanumWaitingReason.EnclaveAvailable :
                enclave == null ? SWGlobalWarLanthanumWaitingReason.NoEnclave : SWGlobalWarLanthanumWaitingReason.None;
            doctrine.StartedAt = null;
            doctrine.Phase = !doctrine.Automatic ? SWGlobalWarLanthanumPhase.Disabled :
                industries.Length == 0 ? SWGlobalWarLanthanumPhase.NoRecipient : SWGlobalWarLanthanumPhase.Waiting;
            return;
        }
        if (doctrine.StartedAt == null && doctrine.Stock < 20 * doctrine.Demand)
            doctrine.StartedAt = Time;
        if (doctrine.StartedAt == null)
        {
            doctrine.Phase = SWGlobalWarLanthanumPhase.Waiting;
            doctrine.WaitingReason = SWGlobalWarLanthanumWaitingReason.StockSufficient;
        }
        else if (Time < doctrine.StartedAt + TimeSpan.FromSeconds(120))
            doctrine.Phase = SWGlobalWarLanthanumPhase.Starting;
        else
        {
            var owned = _lanthanumDeposits.Values.Where(d => _markets[d.Planet].Owner == faction).ToArray();
            doctrine.Phase = owned.Length == 0 ? SWGlobalWarLanthanumPhase.NoDeposit : owned.All(d => d.Remaining == 0)
                ? SWGlobalWarLanthanumPhase.Exhausted : SWGlobalWarLanthanumPhase.Releasing;
        }
    }

    internal void RefreshLanthanumOwnership()
    {
        if (_lanthanumDeposits.Count == 0)
            return;
        foreach (var (faction, doctrine) in _lanthanumDoctrines)
            RefreshLanthanumDoctrine(faction, doctrine);
        ReconcileLanthanumFunding?.Invoke();
    }

    internal decimal HomeLanthanumStock(SWGlobalWarFaction faction)
    {
        return HomeIndustry(faction).Sum(m => AvailableProgramLanthanum(m, faction));
    }

    internal decimal HomeLanthanumIncoming(SWGlobalWarFaction faction)
    {
        var recipients = HomeIndustry(faction).Select(m => m.Planet).ToHashSet();
        return _shipments.Where(s => s.Commodity == Lanthanum && s.Batch?.Owner == faction &&
            recipients.Contains(s.Destination)).Sum(s => (long) s.Quantity) / (decimal) UnitsPerItem;
    }

    private void StepLanthanumReserves()
    {
        if (Generation is not { } generation || _lanthanumDeposits.Count == 0)
            return;
        RefreshLanthanumOwnership();
        if (IsActive?.Invoke() == false)
            return;
        foreach (var deposit in _lanthanumDeposits.Values)
        {
            var market = _markets[deposit.Planet];
            if (!_lanthanumDoctrines.TryGetValue(market.Owner, out var doctrine) ||
                doctrine.Phase != SWGlobalWarLanthanumPhase.Releasing)
            {
                deposit.Progress = 0;
                deposit.Phase = doctrine?.Phase ?? SWGlobalWarLanthanumPhase.Disabled;
                continue;
            }
            if (deposit.Remaining == 0)
            {
                deposit.Phase = SWGlobalWarLanthanumPhase.Exhausted;
                continue;
            }
            if (deposit.Phase is not (SWGlobalWarLanthanumPhase.WaitingFunds or SWGlobalWarLanthanumPhase.WaitingSpace))
                deposit.Phase = SWGlobalWarLanthanumPhase.Releasing;
            EnsureLanthanumFlows(deposit, market.Owner);
            if (Time - StepDuration < doctrine.StartedAt + TimeSpan.FromSeconds(120))
                continue;
            var good = market.Goods[Lanthanum];
            if ((long) good.Capacity - good.Stock - good.Incoming - good.ReservedIncoming < UnitsPerItem)
            {
                deposit.Progress = 0;
                deposit.Phase = SWGlobalWarLanthanumPhase.WaitingSpace;
                continue;
            }
            if (CanFundLanthanum?.Invoke(deposit.Planet, market.Owner) != true)
            {
                deposit.Progress = 0;
                deposit.Phase = SWGlobalWarLanthanumPhase.WaitingFunds;
                continue;
            }
            // Progress belongs to the physical deposit. Blocked time never becomes prepaid production credit.
            deposit.Progress += 12;
            if (deposit.Progress < 60)
                continue;
            deposit.Progress -= 60;
            var plan = new SWGlobalWarLanthanumRelease(generation, Guid.NewGuid(), deposit.Planet, market.Owner, Time);
            var completed = false;
            void Complete()
            {
                if (completed)
                    throw new InvalidOperationException("Lanthanum release committed twice.");
                completed = true;
                deposit.Remaining--;
                deposit.Released++;
                AddBatch(good, new(Guid.NewGuid(), generation, market.Owner, market.Owner, UnitsPerItem));
                good.Produced += UnitsPerItem;
                deposit.Phase = deposit.Remaining == 0 ? SWGlobalWarLanthanumPhase.Exhausted : SWGlobalWarLanthanumPhase.Releasing;
            }
            var success = SettleLanthanum?.Invoke(plan, Complete) ?? false;
            if (success != completed)
                throw new InvalidOperationException("Lanthanum settlement did not match the goods commit.");
            if (!success)
                deposit.Phase = SWGlobalWarLanthanumPhase.WaitingFunds;
            if (IsActive?.Invoke() == false)
                return;
        }
    }

    private void EnsureLanthanumFlows(SWGlobalWarLanthanumDeposit deposit, SWGlobalWarFaction faction)
    {
        foreach (var recipient in HomeIndustry(faction))
        {
            var id = $"SWReserveLn:{deposit.Planet}:{recipient.Planet}:{faction}";
            if (_flows.ContainsKey(id))
                continue;
            var route = FindRoute(deposit.Planet, recipient.Planet, _commodities[Lanthanum].CargoVolume, false);
            if (route == null)
                continue;
            // Ordinary editable delivery contracts consume the one physical stock and retain all transport limits.
            _flows.Add(id, new(id, deposit.Planet, recipient.Planet, Lanthanum, SWGlobalWarFlowPriority.DomesticCivilian)
            {
                ContractFaction = faction, ExpectedRecipient = faction, ExplicitRate = 12, Route = route,
            });
            _flowLookupsStale = true;
        }
    }
}

/// <summary>Approved readiness rates, independent of execution stalls and emergency reductions.</summary>
public sealed record SWGlobalWarBaseSupplyNorm(decimal T2 = 0.5m, decimal T1 = 0.125m,
    decimal AdvancedElectronics = 0.1m, decimal Bacta = 0);

public sealed class SWGlobalWarLanthanumDoctrine
{
    public bool Automatic { get; internal set; } = true;
    public long Revision { get; internal set; }
    public SWGlobalWarBaseSupplyNorm Norm { get; internal set; } = new();
    public TimeSpan? StartedAt { get; internal set; }
    public SWGlobalWarLanthanumPhase Phase { get; internal set; } = SWGlobalWarLanthanumPhase.Waiting;
    public SWGlobalWarLanthanumWaitingReason WaitingReason { get; internal set; }
    public decimal CivilElectronics { get; internal set; }
    public decimal Demand { get; internal set; }
    public decimal Stock { get; internal set; }
}

public sealed class SWGlobalWarLanthanumDeposit(string planet)
{
    public string Planet { get; } = planet;
    public int Remaining { get; internal set; } = 1200;
    public int Released { get; internal set; }
    public SWGlobalWarLanthanumPhase Phase { get; internal set; } = SWGlobalWarLanthanumPhase.Waiting;
    internal int Progress;
}

public sealed record SWGlobalWarLanthanumRelease(Guid Generation, Guid Operation, string Planet,
    SWGlobalWarFaction Faction, TimeSpan At);
