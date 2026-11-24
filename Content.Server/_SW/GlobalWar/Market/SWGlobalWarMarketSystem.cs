using System.Linq;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.GameTicking;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Owns planetary markets and their virtual freight simulation for the active GlobalWarGamerule.</summary>
public sealed partial class SWGlobalWarMarketSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCampaignStepSystem _campaignStep = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    private bool _settling;
    private bool _ownershipCatchUp;
    private readonly Dictionary<SWGlobalWarFaction, long> _taxRemainders = new();

    private EntityUid? _rule;
    private Guid? _campaignGeneration;
    private TimeSpan _lastCampaignElapsed;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, EntityUid> _marketEntities = new();

    [ViewVariables]
    public SWGlobalWarMarketSimulation? Simulation { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
        InitializeHubs();
        InitializePhysicalFactories();
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(_ =>
        {
            if (Simulation?.Generation != null)
                Simulation.CaptureStartingSupply();
        });
    }

    public void ValidateConfiguration(SWGlobalWarGameruleComponent globalWarGamerule)
    {
        CreateSimulation(globalWarGamerule);
    }

    /// <summary>Creates market inventories on the newly loaded planet controllers using prototype configuration.</summary>
    public void InitializeMarkets()
    {
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) || _rule == globalWarGamerule.Owner)
            return;

        var simulation = CreateSimulation(globalWarGamerule.Comp);
        _marketEntities.Clear();
        foreach (var (id, uid) in globalWarGamerule.Comp.PlanetEntities)
        {
            var market = EnsureComp<SWGlobalWarMarketComponent>(uid);
            market.GlobalWarGamerule = globalWarGamerule.Owner;
            market.State = simulation.Markets[id.Id];
            _marketEntities.Add(id, uid);
        }
        Simulation = simulation;
        _rule = globalWarGamerule.Owner;
        _campaignGeneration = HasComp<SWGlobalWarCampaignComponent>(_rule)
            ? globalWarGamerule.Comp.Generation : null;
        _lastCampaignElapsed = TimeSpan.Zero;
    }

    private SWGlobalWarMarketSimulation CreateSimulation(SWGlobalWarGameruleComponent globalWarGamerule)
    {
        var campaign = HasComp<SWGlobalWarCampaignComponent>(globalWarGamerule.Owner);
        if (!campaign && (!double.IsFinite(globalWarGamerule.AutoTradeCapacity) || globalWarGamerule.AutoTradeCapacity < 0
            || globalWarGamerule.AutoTradeTravelTime <= TimeSpan.Zero))
            throw new ArgumentException("Automatic trade needs a finite non-negative capacity and a positive travel time.");

        var commodities = _prototypes.EnumeratePrototypes<SWGlobalWarCommodityPrototype>()
            .Select(prototype => new SWGlobalWarCommodityDefinition(prototype.ID, prototype.CargoVolume, prototype.WholeItems)).ToArray();
        var active = globalWarGamerule.Planets.ToHashSet();
        var profiles = globalWarGamerule.Planets.Any(id => _prototypes.Index(id).EconomyProfile != null);
        var markets = new List<SWGlobalWarMarketDefinition>();
        var connections = new List<SWGlobalWarTradeConnection>();
        foreach (var id in globalWarGamerule.Planets)
        {
            var planet = _prototypes.Index(id);
            if (campaign && profiles)
            {
                if (planet.EconomyProfile is not { } profileId)
                    throw new ArgumentException($"Campaign economy profile is missing for {id}.");
                var profile = _prototypes.Index(profileId);
                if (profile.HubCapacity <= 0 || profile.HubCapacity > profile.MaximumHubCapacity)
                    throw new ArgumentException($"Invalid hub capacity for {id}.");
                var goods = new List<SWGlobalWarMarketGoodDefinition>();
                foreach (var commodity in commodities.Where(c =>
                             _prototypes.EnumeratePrototypes<SWGlobalWarEconomyProfilePrototype>().Any(p =>
                                 p.Mandatory.ContainsKey(c.Id) || p.OpeningStock.ContainsKey(c.Id) ||
                                 p.Recipes.Any(r => r.Output.Id == c.Id || r.Inputs.ContainsKey(c.Id)))))
                {
                    var rate = profile.Mandatory.GetValueOrDefault(commodity.Id);
                    var stock = profile.OpeningStock.GetValueOrDefault(commodity.Id, rate * 10);
                    goods.Add(new SWGlobalWarMarketGoodDefinition(commodity.Id,
                        SWGlobalWarMarketUnits.FromUnits(stock), SWGlobalWarMarketUnits.FromUnits(rate)));
                }
                foreach (var recipe in profile.Recipes)
                {
                    if (recipe.PerMinute <= 0 || recipe.Inputs.Values.Any(v => v <= 0 || decimal.Truncate(v) != v))
                        throw new ArgumentException($"Invalid virtual recipe in {profileId}.");
                    _prototypes.Index(recipe.Output);
                    foreach (var input in recipe.Inputs.Keys)
                        _prototypes.Index(input);
                }
                markets.Add(new SWGlobalWarMarketDefinition(id.Id, goods, planet.InitialOwner, profile,
                    planet.Kind, planet.Enclave, planet.LanthanumReserve));
                foreach (var (target, edge) in planet.TradeConnections.Where(p => active.Contains(p.Key)))
                    connections.Add(new SWGlobalWarTradeConnection(id.Id, target.Id, edge.TravelTime, edge.Capacity));
                continue;
            }
            markets.Add(new SWGlobalWarMarketDefinition(id.Id, planet.Market.Select(pair =>
            {
                _prototypes.Index(pair.Key);
                return new SWGlobalWarMarketGoodDefinition(pair.Key.Id,
                    campaign ? SWGlobalWarMarketUnits.FromUnits(pair.Value.InitialStock) : pair.Value.InitialStock,
                    campaign ? SWGlobalWarMarketUnits.FromUnits(pair.Value.ConsumptionPerMinute) : pair.Value.ConsumptionPerMinute);
            }).ToArray(), planet.InitialOwner));
            if (campaign)
            {
                foreach (var (target, edge) in planet.TradeConnections.Where(p => active.Contains(p.Key)))
                    connections.Add(new SWGlobalWarTradeConnection(id.Id, target.Id, edge.TravelTime, edge.Capacity));
                continue;
            }
            foreach (var target in planet.Connections.Where(target => target != id && active.Contains(target)))
            {
                connections.Add(new SWGlobalWarTradeConnection(id.Id, target.Id,
                    globalWarGamerule.AutoTradeTravelTime, globalWarGamerule.AutoTradeCapacity));
            }
        }
        var simulation = new SWGlobalWarMarketSimulation(commodities, markets, connections,
            campaign ? globalWarGamerule.Generation : null);
        if (campaign)
        {
            var generation = globalWarGamerule.Generation;
            simulation.ConfigureFlows(_prototypes.EnumeratePrototypes<SWGlobalWarEconomyFlowPrototype>()
                .Where(f => active.Contains(f.Source) && active.Contains(f.Destination)));
            simulation.TradePolicy = _politics.EvaluateTrade;
            simulation.SettleConsumption = SettleConsumption;
            simulation.SupplyParticipants = faction => _identity.CountSupplyParticipants(generation, faction);
            simulation.AcceptedSupplyStock = CountAcceptedSupplyStock;
            simulation.RunSupplyPrograms = RunSupplyProgramsAndTransport;
            simulation.ReceiveSupplyManifest = ReceivePhysicalSupplyManifest;
            simulation.CaptureSupplyManifest = CapturePhysicalSupplyManifest;
            simulation.CheckSupplyReturn = CheckPhysicalSupplyReturn;
            simulation.CommitSupplyReturn = CommitPhysicalSupplyReturn;
            simulation.SettleLanthanum = SettleLanthanum;
            simulation.CanFundLanthanum = CanFundLanthanum;
            simulation.ReconcileLanthanumFunding = () => ReconcileLanthanumOrders();
            simulation.IsActive = () => CanMutateCampaign(generation, _ownershipCatchUp);
            foreach (var commodity in _prototypes.EnumeratePrototypes<SWGlobalWarCommodityPrototype>())
            {
                if (commodity.ConsumptionTax < 0)
                    throw new ArgumentException($"Invalid consumption tax for {commodity.ID}.");
                simulation.TaxRates.Add(commodity.ID, commodity.ConsumptionTax);
            }
        }
        return simulation;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_rule == null || _settling || _hubMutating || _campaignStep.IsClosing)
            return;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) || _rule != globalWarGamerule.Owner)
        {
            Reset();
            return;
        }

        if (_campaignGeneration is { } generation)
        {
            // Campaign time is shared with diplomacy and freezes at Ending, even while the rule still exists.
            // A missing/stopped campaign must never fall back to the legacy frame-time path.
            if (!_campaign.TryGetCampaign(out var campaign) || campaign.Owner != _rule ||
                campaign.Comp.Generation != generation || campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            {
                UpdateHubWork();
                ReconcileLanthanumOrders(stop: true);
                return;
            }
            var elapsed = _campaign.Elapsed(campaign);
            var delta = elapsed - _lastCampaignElapsed;
            if (delta <= TimeSpan.Zero)
            {
                UpdateHubWork();
                return;
            }
            _lastCampaignElapsed = elapsed;
            Simulation?.Advance(delta);
            UpdateHubWork();
        }
        else
            Simulation?.Advance(TimeSpan.FromSeconds(frameTime));
    }

    public void StopMarkets(EntityUid globalWarGamerule)
    {
        if (_rule == globalWarGamerule)
            Reset();
    }

    private void Reset()
    {
        ResetPhysicalFactories();
        ResetSupplyInventory();
        ResetLanthanum();
        ResetHubWork();
        ResetFlowEditing();
        Simulation = null;
        _rule = null;
        _campaignGeneration = null;
        _lastCampaignElapsed = TimeSpan.Zero;
        _taxRemainders.Clear();
        _marketEntities.Clear();
    }

    /// <summary>Internal goods ownership commit; called only from the campaign's closed territory batch.</summary>
    internal void CommitOwnershipBatch(Guid generation, IReadOnlyList<SWGlobalWarOwnershipChangedEvent> changes)
    {
        if (!_campaignStep.IsClosing || Simulation?.Generation != generation || _campaignGeneration != generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation)
            return;
        var goods = changes.Where(e => e.Location == SWGlobalWarPlanetLocation.Surface)
            .Select(e => new SWGlobalWarMarketOwnershipChange(Comp<SWGlobalWarPlanetComponent>(e.Planet).Prototype.Id,
                e.Owner, e.OperationId)).ToArray();
        Simulation.CommitOwnershipBatch(generation, goods, _campaign.Elapsed(campaign));
        CaptureSupplyStores(goods);
        Simulation.RefreshLanthanumOwnership();
    }

    /// <summary>
    /// Closes all due economic seconds under the previous owners before the territory batch begins. The caller
    /// defers ledger notifications across the complete ownership commit; external D operations remain closed.
    /// </summary>
    internal void SynchronizeOwnershipBoundary(Guid generation)
    {
        if (!_campaignStep.IsClosing || _settling || Simulation?.Generation != generation ||
            !_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation ||
            campaign.Comp.Phase == SWGlobalWarCampaignPhase.Ending)
            return;
        var elapsed = _campaign.Elapsed(campaign);
        var delta = elapsed - _lastCampaignElapsed;
        if (delta <= TimeSpan.Zero)
            return;
        _ownershipCatchUp = true;
        try
        {
            _lastCampaignElapsed = elapsed;
            Simulation.Advance(delta);
        }
        finally
        {
            _ownershipCatchUp = false;
        }
    }

    private bool CanMutateCampaign(Guid generation, bool ownershipCatchUp = false)
    {
        return !_settling && !_hubMutating && (!_campaignStep.IsClosing || ownershipCatchUp) && _campaignGeneration == generation &&
               Simulation?.Generation == generation && _campaign.TryGetCampaign(out var campaign) &&
               campaign.Owner == _rule && campaign.Comp.Generation == generation &&
               campaign.Comp.Phase != SWGlobalWarCampaignPhase.Ending;
    }

    /// <summary>Server reservation boundary for D/E; callers still need their action-specific actor authorization.</summary>
    public bool TryReserveStock(SWGlobalWarStockRequest request, out SWGlobalWarStockReservation reservation)
    {
        reservation = default!;
        return CanMutateCampaign(request.Generation) && Simulation!.TryReserveStock(request, out reservation);
    }

    /// <summary>Server-only return transaction. Player requests additionally require current cargo authorization.</summary>
    internal bool TryReturnShipment(SWGlobalWarMarketReturnRequest request, out SWGlobalWarMarketReturnReceipt receipt)
    {
        receipt = default!;
        return CanMutateCampaign(request.Generation) && Simulation!.TryReturnShipment(request, out receipt);
    }

    private bool SettleConsumption(SWGlobalWarConsumptionPlan plan, Action<bool> completeGoods)
    {
        if (!CanMutateCampaign(plan.Generation, _ownershipCatchUp) || !_ledger.TryGetLedger(out var ledger))
        {
            completeGoods(false);
            return false;
        }
        // A fraction belongs to the faction that consumed the goods. Later captures cannot redirect it.
        const long denominator = 2 * SWGlobalWarMarketUnits.PerUnit;
        var remainders = new Dictionary<SWGlobalWarFaction, long>(_taxRemainders);
        var commands = new List<SWGlobalWarLedgerCommand>();
        foreach (var faction in new[] { SWGlobalWarFaction.Republic, SWGlobalWarFaction.Empire })
        {
            var numerator = remainders.GetValueOrDefault(faction);
            foreach (var line in plan.Lines.Where(l => l.TaxRecipient == faction))
                numerator = checked(numerator + (long) line.Quantity * line.TaxRate * line.TaxMultiplier);
            remainders[faction] = numerator % denominator;
            if (numerator / denominator > 0)
                commands.Add(new SWGlobalWarLedgerCommand(plan.Generation, Guid.NewGuid(), ledger.Comp.Revision,
                    SWGlobalWarLedgerAction.Credit, faction, SWGlobalWarAccount.Treasury, numerator / denominator,
                    Purpose: "mandatory-consumption-tax"));
        }
        _settling = true;
        try
        {
            using var notifications = _ledger.DeferNotifications();
            if (commands.Count > 0 && !_ledger.TryExecuteSystemBatch(plan.Operation, commands))
            {
                completeGoods(false);
                return false;
            }
            completeGoods(true);
            _taxRemainders.Clear();
            foreach (var (faction, remainder) in remainders)
                _taxRemainders.Add(faction, remainder);
            return true;
        }
        finally
        {
            _settling = false;
        }
    }

    /// <summary>Returns detached market numbers for UI publication; callers cannot mutate the simulation through them.</summary>
    /// <param name="planet">Stable planet prototype ID in the active rule.</param>
    public List<SWGlobalWarMarketGoodView> GetMarketView(ProtoId<SWGlobalWarPlanetPrototype> planet, EntityUid? viewer = null)
    {
        if (GetMarketAvailability(planet, viewer) != SWGlobalWarMarketAvailability.Ready ||
            !TryGetMarket(planet, out var market))
            return new List<SWGlobalWarMarketGoodView>();
        var scale = (double) Simulation!.UnitsPerItem;
        return market.State.Goods.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SWGlobalWarMarketGoodView(pair.Key, pair.Value.Stock / scale,
                pair.Value.ConsumptionPerMinute / scale, pair.Value.Incoming / scale, pair.Value.TargetStock / scale,
                pair.Value.Reserved / scale, (double) market.State.ProductionOrders.GetValueOrDefault(pair.Key),
                pair.Value.ProductionDemandPerMinute / scale, pair.Value.GrowthDemandPerMinute / scale,
                pair.Value.Capacity / scale, pair.Value.ReservedIncoming / scale)).ToList();
    }

    public bool TryAddStock(ProtoId<SWGlobalWarPlanetPrototype> planet, ProtoId<SWGlobalWarCommodityPrototype> commodity, int quantity) =>
        TryGetMarket(planet, out _) && Simulation!.TryAddStock(planet.Id, commodity.Id, quantity);

    public bool TryTakeStock(ProtoId<SWGlobalWarPlanetPrototype> planet, ProtoId<SWGlobalWarCommodityPrototype> commodity, int quantity) =>
        TryGetMarket(planet, out _) && Simulation!.TryTakeStock(planet.Id, commodity.Id, quantity);

    public bool TrySetConsumption(ProtoId<SWGlobalWarPlanetPrototype> planet, ProtoId<SWGlobalWarCommodityPrototype> commodity, int perMinute) =>
        TryGetMarket(planet, out _) && Simulation!.TrySetConsumption(planet.Id, commodity.Id, perMinute);

    private bool TryGetMarket(ProtoId<SWGlobalWarPlanetPrototype> planet, out SWGlobalWarMarketComponent market)
    {
        market = default!;
        if (Simulation == null || !_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) || _rule != globalWarGamerule.Owner
            || !_marketEntities.TryGetValue(planet, out var uid) || TerminatingOrDeleted(uid)
            || !TryComp<SWGlobalWarMarketComponent>(uid, out var component) || component.GlobalWarGamerule != _rule)
            return false;
        market = component;
        return true;
    }
}
