using System.Linq;
using Content.Shared._SW.GlobalWar;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Market;

public sealed class SWGlobalWarMarketConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarMarketConsoleComponent, BoundUIOpenedEvent>(OnOpened);
    }

    private void OnOpened(Entity<SWGlobalWarMarketConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarMarketConsoleUiKey.Key))
            _ui.SetUiState(ent.Owner, SWGlobalWarMarketConsoleUiKey.Key, GetState(ent.Owner));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWGlobalWarMarketConsoleComponent, UserInterfaceComponent>();
        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (_ui.IsUiOpen((uid, ui), SWGlobalWarMarketConsoleUiKey.Key))
                _ui.SetUiState(uid, SWGlobalWarMarketConsoleUiKey.Key, GetState(uid));
        }
    }

    /// <summary>Publishes inventories, current shipment legs and shared link occupancy from the active simulation.</summary>
    /// <param name="console">Console location is used only to mark its planet on the map.</param>
    public SWGlobalWarMarketConsoleState GetState(EntityUid console)
    {
        var planets = new List<SWGlobalWarPlanetMarketView>();
        var links = new List<SWGlobalWarMarketConnectionView>();
        var shipments = new List<SWGlobalWarShipmentView>();
        if (!_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) || _markets.Simulation is not { } simulation)
            return new SWGlobalWarMarketConsoleState(false, TimeSpan.Zero, planets, links, shipments, null);

        foreach (var id in rule.Comp.Planets)
            planets.Add(new SWGlobalWarPlanetMarketView(id, _prototypes.Index(id).Name, _markets.GetMarketView(id)));

        var occupied = new Dictionary<(string A, string B), double>();
        foreach (var shipment in simulation.Shipments)
        {
            shipments.Add(new SWGlobalWarShipmentView(shipment.Commodity, shipment.Quantity,
                shipment.Route.Select(id => new ProtoId<SWGlobalWarPlanetPrototype>(id)).ToList(),
                shipment.Leg, shipment.DepartedAt, shipment.ArrivesAt));
            if (shipment.ArrivesAt == null)
                continue;

            var lane = Lane(shipment.Route[shipment.Leg], shipment.Route[shipment.Leg + 1]);
            var volume = shipment.Quantity * _prototypes.Index<SWGlobalWarCommodityPrototype>(shipment.Commodity).CargoVolume;
            occupied[lane] = occupied.GetValueOrDefault(lane) + volume;
        }

        foreach (var link in simulation.Connections.Values)
            links.Add(new SWGlobalWarMarketConnectionView(link.From, link.To, link.TravelTime, link.Capacity,
                occupied.GetValueOrDefault(Lane(link.From, link.To))));

        ProtoId<SWGlobalWarPlanetPrototype>? current = null;
        if (Transform(console).MapUid is { } map && TryComp<SWGlobalWarPlanetMapComponent>(map, out var mapLink)
            && TryComp<SWGlobalWarPlanetComponent>(mapLink.Planet, out var planet) && planet.GlobalWarGamerule == rule.Owner)
            current = planet.Prototype;
        return new SWGlobalWarMarketConsoleState(true, simulation.Time, planets, links, shipments, current);
    }

    private static (string A, string B) Lane(string from, string to) =>
        string.CompareOrdinal(from, to) <= 0 ? (from, to) : (to, from);
}
