using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Copies prototype data on the game thread. No prototype objects cross into the worker.</summary>
public static class SWRepublicEconomyFactory
{
    public const string ScenarioId = "SenateRepublicEconomy";

    public static EconomyDefinition Create(IPrototypeManager prototypes)
    {
        var errors = SWPlanetEconomyValidator.Validate(prototypes)
            .Concat(SWTransitLinkValidator.Validate(prototypes)).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("Invalid Republic economy geography: " + string.Join("; ", errors));
        var scenario = prototypes.Index<SWRepublicEconomyScenarioPrototype>(ScenarioId);
        var map = prototypes.Index(scenario.Map);
        var worlds = map.Worlds.Select(w => prototypes.Index<SWPlanetEconomyPrototype>(w.Id)).ToDictionary(w => w.ID);
        var goods = prototypes.EnumeratePrototypes<SWCommodityPrototype>().OrderBy(g => g.ID).ToArray();
        var links = prototypes.EnumeratePrototypes<SWTransitLinkPrototype>()
            .Where(e => worlds.ContainsKey(e.From) && worlds.ContainsKey(e.To)).OrderBy(e => e.ID).ToArray();
        var reactors = scenario.Reactors.ToDictionary(r => r.World.Id, r => r.CapacityPower);
        // Every possible initial home port has the same finite four-kit naval reserve.
        // Grouping follows map order, as Defense's first Republic world in each sector does.
        // These are dedicated naval stock, not an export surplus or a per-step stock grant.
        var depotWorlds = map.Worlds.Where(w => w.Sector != null && worlds[w.Id].ActiveEconomy
                && map.PoliticalAreas.LastOrDefault(a => a.Contains(w.Position))?.Faction == "republic")
            .GroupBy(w => w.Sector?.Id).Select(g => g.First().Id).ToHashSet();
        var stocks = scenario.Inventory.Select(s =>
        {
            return new EconomyInventory(
            s.World.Id, s.Good.Id,
            // A single pre-round completed hull lot makes the first funded formation
            // accessible in a 42-hour round. It is real finite inventory, debited through
            // project delivery; later formations require the unchanged shipyard recipe.
            s.World.Id == "kuat" && s.Good.Id == "military_ships"
                ? Math.Max(s.Initial, SWRepublicNavyLogistics.HullsPerFormation) : s.Initial,
            s.Target, s.Capacity);
        }).ToArray();
        var production = scenario.Production.Select(p =>
        {
            var world = worlds[p.World.Id];
            var recipe = prototypes.Index(p.Recipe);
            if (!world.ActiveEconomy || !world.Facilities.Contains(recipe.Facility)
                || recipe.Requirements.Any(r => !world.Conditions.Contains(r)))
                throw new InvalidOperationException($"Ineligible economy production: {p.World}/{p.Recipe}");
            if (recipe.ResourceCosts.Count != 0)
                throw new InvalidOperationException($"Finite resource extraction needs a reserve ledger: {p.Recipe}");
            return new EconomyProduction(p.World.Id, p.Recipe.Id, p.Rate, recipe.Hours, recipe.Power, recipe.Labor,
                recipe.Inputs.Select(a => new EconomyAmount(a.Key.Id, a.Value)).ToArray(),
                recipe.Outputs.Select(a => new EconomyAmount(a.Key.Id, a.Value)).ToArray());
        }).ToArray();
        var shipping = scenario.Shipping.Select(s =>
        {
            var edge = prototypes.Index(s.Link);
            RequireRoute(edge, s.Source.Id, s.Target.Id);
            var good = prototypes.Index(s.Good);
            if (good.CargoSlots <= 0)
                throw new InvalidOperationException($"Commissioned asset cannot be freight: {s.Good}");
            return new EconomyShipping(s.Link.Id, s.Source.Id, s.Target.Id, s.Good.Id, s.Rate,
                edge.TravelHours, edge.FuelPerSlot * good.CargoSlots, good.CargoSlots);
        }).Concat(scenario.EmptyReturns.Select(s =>
        {
            var edge = prototypes.Index(s.Link);
            RequireRoute(edge, s.Source.Id, s.Target.Id);
            return new EconomyShipping(s.Link.Id, s.Source.Id, s.Target.Id, null, s.SlotsPerHour,
                edge.TravelHours, edge.FuelPerSlot, 1);
        })).ToArray();
        foreach (var demand in scenario.Demands)
        {
            if (!worlds[demand.World.Id].ActiveEconomy)
                throw new InvalidOperationException($"External economy demand: {demand.World}");
            _ = prototypes.Index(demand.Good);
        }
        foreach (var stock in scenario.Inventory)
        {
            _ = worlds[stock.World.Id];
            _ = prototypes.Index(stock.Good);
        }
        foreach (var reactor in scenario.Reactors)
        {
            if (!worlds[reactor.World.Id].ActiveEconomy || !double.IsFinite(reactor.CapacityPower) || reactor.CapacityPower < 0)
                throw new InvalidOperationException($"Invalid reactor: {reactor.World}");
        }
        return new EconomyDefinition(
            goods.Select(g => new EconomyGood(g.ID, g.Storage == SWCommodityStorage.Cold,
                g.ID == "military_ships" ? SWRepublicNavyLogistics.HullCargoSlots : g.CargoSlots)).ToArray(),
            worlds.Values.OrderBy(w => w.ID).Select(w => new EconomyWorld(w.ID, w.BaseloadPower,
                w.PopulationUnits * scenario.DomesticPowerPerPopulation, w.LaborCapacity,
                reactors.GetValueOrDefault(w.ID), Active: w.ActiveEconomy)).ToArray(),
            stocks, production,
            links.Select(e => new EconomyEdge(e.ID, e.From, e.To, e.Accessible ? e.CapacitySlotsPerHour : 0,
                e.TravelHours, e.FuelPerSlot)).ToArray(),
            shipping,
            // The old Kuat hull order consumed a hull and advertised a defense bonus without
            // creating a fleet. Only Navy project receivers may now commission those hulls.
            // Garrison supplies, ground equipment and all civilian orders keep their receivers.
            scenario.Demands.Where(d => d.Group != "orders" || d.Good.Id != "military_ships")
                // The legacy military service included naval provisioning without a fleet
                // receiver. Keep half for planetary garrisons; actual Navy kits now claim
                // their share through physical deliveries rather than charging it twice.
                .Select(d => new EconomyDemand(d.World.Id, d.Good.Id, d.Group,
                    d.Group == "military" && d.Good.Id == "military_supplies" ? d.Rate * .5 : d.Rate,
                    d.OrderTotal)).ToArray(),
            scenario.Headroom, scenario.ColdDecayPerHour, scenario.ReactorPowerPerFuel,
            scenario.RecoveryHours, scenario.OrderDeadlineHours, EnableAssets: true)
        {
            NavyDepots = depotWorlds.Select(w => new EconomyNavyDepot(w, new[]
            {
                new EconomyAmount("military_supplies", SWRepublicNavyLogistics.OpeningDepotReserve("military_supplies")),
                new EconomyAmount("ship_fuel", SWRepublicNavyLogistics.OpeningDepotReserve("ship_fuel")),
            })).ToArray(),
        };
    }

    private static void RequireRoute(SWTransitLinkPrototype edge, string from, string to)
    {
        if (!edge.Accessible || !(edge.From == from && edge.To == to || edge.From == to && edge.To == from))
            throw new InvalidOperationException($"Inaccessible or mismatched freight route: {edge.ID}, {from} to {to}");
    }
}
