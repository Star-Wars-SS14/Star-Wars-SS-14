using System.Linq;
using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Owns planetary markets and their virtual freight simulation for the active GlobalWarGamerule.</summary>
public sealed class SWGlobalWarMarketSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private EntityUid? _rule;
    private readonly Dictionary<ProtoId<SWGlobalWarPlanetPrototype>, EntityUid> _marketEntities = new();

    [ViewVariables]
    public SWGlobalWarMarketSimulation? Simulation { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
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
    }

    private SWGlobalWarMarketSimulation CreateSimulation(SWGlobalWarGameruleComponent globalWarGamerule)
    {
        if (!double.IsFinite(globalWarGamerule.AutoTradeCapacity) || globalWarGamerule.AutoTradeCapacity < 0
            || globalWarGamerule.AutoTradeTravelTime <= TimeSpan.Zero)
            throw new ArgumentException("Automatic trade needs a finite non-negative capacity and a positive travel time.");

        var commodities = _prototypes.EnumeratePrototypes<SWGlobalWarCommodityPrototype>()
            .Select(prototype => new SWGlobalWarCommodityDefinition(prototype.ID, prototype.CargoVolume)).ToArray();
        var active = globalWarGamerule.Planets.ToHashSet();
        var markets = new List<SWGlobalWarMarketDefinition>();
        var connections = new List<SWGlobalWarTradeConnection>();
        foreach (var id in globalWarGamerule.Planets)
        {
            var planet = _prototypes.Index(id);
            markets.Add(new SWGlobalWarMarketDefinition(id.Id, planet.Market.Select(pair =>
            {
                _prototypes.Index(pair.Key);
                return new SWGlobalWarMarketGoodDefinition(pair.Key.Id, pair.Value.InitialStock, pair.Value.ConsumptionPerMinute);
            }).ToArray()));
            foreach (var target in planet.Connections.Where(target => target != id && active.Contains(target)))
            {
                connections.Add(new SWGlobalWarTradeConnection(id.Id, target.Id,
                    globalWarGamerule.AutoTradeTravelTime, globalWarGamerule.AutoTradeCapacity));
            }
        }
        return new SWGlobalWarMarketSimulation(commodities, markets, connections);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_rule == null)
            return;
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) || _rule != globalWarGamerule.Owner)
        {
            Reset();
            return;
        }

        Simulation?.Advance(TimeSpan.FromSeconds(frameTime));
    }

    public void StopMarkets(EntityUid globalWarGamerule)
    {
        if (_rule == globalWarGamerule)
            Reset();
    }

    private void Reset()
    {
        Simulation = null;
        _rule = null;
        _marketEntities.Clear();
    }

    /// <summary>Returns detached market numbers for UI publication; callers cannot mutate the simulation through them.</summary>
    /// <param name="planet">Stable planet prototype ID in the active rule.</param>
    public List<SWGlobalWarMarketGoodView> GetMarketView(ProtoId<SWGlobalWarPlanetPrototype> planet)
    {
        if (!TryGetMarket(planet, out var market))
            return new List<SWGlobalWarMarketGoodView>();
        return market.State.Goods.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SWGlobalWarMarketGoodView(pair.Key, pair.Value.Stock,
                pair.Value.ConsumptionPerMinute, pair.Value.Incoming)).ToList();
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
