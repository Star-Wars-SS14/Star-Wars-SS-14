using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

/// <summary>
/// Deterministic aggregate economy, owned by one sequential worker. All time is active game
/// hours; calling Advance(false) freezes stock, cargo, batches and service counters exactly.
/// Fractional batches and fixed freight schedules intentionally do not create ECS entities.
/// </summary>
public sealed partial class RepublicEconomyModel
{
    public const double StepHours = .25;
    private const double Epsilon = 1e-9;
    private static readonly string[] Foods = { "fresh_food", "rations", "synth_food" };
    private readonly EconomyDefinition _definition;
    private readonly Dictionary<string, EconomyGood> _goods;
    private readonly Dictionary<string, EconomyWorld> _worlds;
    private readonly Dictionary<string, EconomyEdge> _edges;
    private readonly Dictionary<Key, Store> _stores = new();
    private readonly Dictionary<Key, EconomyDemand[]> _demands;
    private readonly Dictionary<Key, Service> _services = new();
    private readonly Dictionary<Key, Order> _orders = new();
    private readonly HashSet<string> _terminalOrderGoods;
    private readonly HashSet<string> _protectedShippingGoods;
    private readonly Dictionary<Key, double> _pipeline = new();
    private readonly Dictionary<Key, double> _replenishment = new();
    private readonly Dictionary<string, Ledger> _ledgers;
    private readonly Dictionary<string, double> _edgeUsed = new();
    private readonly Dictionary<string, double> _edgeEmpty = new();
    private readonly Dictionary<Key, Activity> _activity = new();
    private readonly Dictionary<Key, double> _productionStarted = new();
    private readonly Dictionary<Key, double> _productionCompleted = new();
    private readonly Dictionary<FlowKey, double> _departures = new();
    private readonly List<Batch> _batches = new();
    private readonly List<Cargo> _cargo = new();
    private double _hour;

    private readonly record struct Key(string World, string Good);
    private readonly record struct FlowKey(string Edge, string From, string To, string Good, double TravelHours);
    private readonly record struct Resource(string Kind, string World, string Good = "");
    private sealed class Store(EconomyInventory definition)
    {
        public readonly EconomyInventory Definition = definition;
        public double Quantity = definition.Initial;
    }
    private sealed class Batch(EconomyProduction recipe, double cycles)
    {
        public readonly EconomyProduction Recipe = recipe;
        public readonly double Cycles = cycles;
        public double Remaining = recipe.Hours;
    }
    private sealed class Cargo(FlowKey flow, double quantity)
    {
        public readonly FlowKey Flow = flow;
        public readonly Key Target = new(flow.To, flow.Good);
        public double Quantity = quantity;
        public double Remaining = flow.TravelHours;
    }
    private sealed class Activity
    {
        public double Produced, Consumed, Required, Imported, Exported;
    }
    private sealed class Service
    {
        public double Required, Served, LastRequired, LastServed;
        public double? FirstShortage;
    }
    private sealed class Order
    {
        public double Total, Delivered;
        public double? Completed;
    }
    private sealed class Ledger
    {
        public double Initial, Manufactured, Transformed, Consumed, Losses;
    }
    private sealed record Proposal(double Amount, Dictionary<Resource, double> Needs);

    public RepublicEconomyModel(EconomyDefinition definition)
    {
        _definition = definition with
        {
            Goods = definition.Goods.ToArray(),
            Worlds = definition.Worlds.OrderBy(w => w.Id, StringComparer.Ordinal).ToArray(),
            Inventory = definition.Inventory.OrderBy(i => i.World, StringComparer.Ordinal).ThenBy(i => i.Good, StringComparer.Ordinal).ToArray(),
            Production = definition.Production.OrderBy(p => p.World, StringComparer.Ordinal).ThenBy(p => p.Recipe, StringComparer.Ordinal)
                .Select(p => p with { Inputs = p.Inputs.ToArray(), Outputs = p.Outputs.ToArray() }).ToArray(),
            Edges = definition.Edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray(),
            Shipping = definition.Shipping.OrderBy(f => f.Edge, StringComparer.Ordinal).ThenBy(f => f.Source, StringComparer.Ordinal)
                .ThenBy(f => f.Good, StringComparer.Ordinal).ToArray(),
            Demands = definition.Demands.ToArray(),
            NavyDepots = definition.NavyDepots.Select(d => d with { Materials = d.Materials.ToArray() }).ToArray(),
        };
        _goods = _definition.Goods.ToDictionary(g => g.Id);
        _worlds = _definition.Worlds.ToDictionary(w => w.Id);
        _edges = _definition.Edges.ToDictionary(e => e.Id);
        _ledgers = _goods.Keys.ToDictionary(g => g, _ => new Ledger());
        foreach (var inventory in _definition.Inventory)
        {
            _stores.Add(new Key(inventory.World, inventory.Good), new Store(inventory));
            if (_ledgers.TryGetValue(inventory.Good, out var ledger))
                ledger.Initial += inventory.Initial;
        }
        _demands = _definition.Demands.GroupBy(d => new Key(d.World, d.Group)).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var demand in _definition.Demands.Where(d => d.Group == "orders"))
        {
            var key = new Key(demand.World, demand.Good);
            if (!_orders.TryGetValue(key, out var order))
                _orders.Add(key, order = new Order());
            order.Total += demand.OrderTotal;
        }
        _terminalOrderGoods = _orders.Keys.Select(k => k.Good)
            // Naval hull manufacturing follows funded finite projects after migration away
            // from automatic asset orders. Do not keep building unwanted hulls into storage.
            .Concat(_goods.ContainsKey("military_ships") ? new[] { "military_ships" } : Array.Empty<string>())
            .Except(_definition.Production.SelectMany(p => p.Inputs).Select(a => a.Good)).ToHashSet();
        var essentialGroups = new HashSet<string>
        {
            "water", "nutrition", "care_drugs", "care_supplies", "closed_habitats",
            "maintenance", "household", "military", "other",
        };
        _protectedShippingGoods = _definition.Demands.Where(d => essentialGroups.Contains(d.Group))
            .Select(d => d.Good).Concat(Foods).Concat(new[] { "ship_fuel", "reactor_fuel" }).ToHashSet();
        // Protect the inputs of essential production as well as the finished supplies.
        bool expanded;
        do
        {
            expanded = false;
            foreach (var line in _definition.Production.Where(p => p.Outputs.Any(o => _protectedShippingGoods.Contains(o.Good))))
            foreach (var input in line.Inputs)
                expanded |= _protectedShippingGoods.Add(input.Good);
        } while (expanded);
        Validate();
        InitializeNavyDepots();
        foreach (var p in _definition.Production)
        foreach (var output in p.Outputs)
        {
            Add(_pipeline, new Key(p.World, output.Good), p.Rate * output.Quantity * p.Hours);
            Add(_replenishment, new Key(p.World, output.Good), p.Rate * output.Quantity);
        }
        foreach (var f in _definition.Shipping)
        {
            if (f.Good == null)
                continue;
            var survival = Survival(f.Good, f.TravelHours);
            Add(_pipeline, new Key(f.Target, f.Good), f.Rate * f.TravelHours * survival);
            Add(_replenishment, new Key(f.Target, f.Good), f.Rate * survival);
        }
        InitializeAssets();
    }

    private void Validate()
    {
        static void Nonnegative(double n, string field)
        {
            if (!double.IsFinite(n) || n < 0)
                throw new ArgumentException($"Invalid {field}: {n}");
        }
        void StoreRequired(string world, string good)
        {
            if (!_worlds.ContainsKey(world) || !_goods.ContainsKey(good) || !_stores.ContainsKey(new Key(world, good)))
                throw new ArgumentException($"Unknown or unstored commodity {world}/{good}");
        }
        Nonnegative(_definition.Headroom, "headroom");
        Nonnegative(_definition.ColdDecayPerHour, "decay");
        Nonnegative(_definition.ReactorPowerPerFuel, "reactor conversion");
        Nonnegative(_definition.RecoveryHours, "recovery");
        Nonnegative(_definition.OrderDeadlineHours, "deadline");
        if (_definition.Headroom < 1 || _definition.ColdDecayPerHour >= 1 || _definition.ReactorPowerPerFuel <= 0 || _definition.RecoveryHours <= 0)
            throw new ArgumentException("Invalid economy settings");
        foreach (var w in _definition.Worlds)
        {
            Nonnegative(w.BaseloadPower, "baseload"); Nonnegative(w.DomesticPower, "domestic power");
            Nonnegative(w.LaborCapacity, "labor"); Nonnegative(w.ReactorCapacityPower, "reactor capacity");
            if (w.ReactorCapacityPower > 0)
                StoreRequired(w.Id, "reactor_fuel");
        }
        foreach (var inventory in _definition.Inventory)
        {
            StoreRequired(inventory.World, inventory.Good);
            Nonnegative(inventory.Initial, "initial stock"); Nonnegative(inventory.Target, "stock target"); Nonnegative(inventory.Capacity, "storage capacity");
            if (Math.Max(inventory.Initial, inventory.Target) > inventory.Capacity + Epsilon)
                throw new ArgumentException("Initial stock or target exceeds storage capacity");
        }
        foreach (var p in _definition.Production)
        {
            Nonnegative(p.Rate, "production rate"); Nonnegative(p.Hours, "recipe duration"); Nonnegative(p.Power, "recipe power"); Nonnegative(p.Labor, "recipe labor");
            if (!_worlds.ContainsKey(p.World) || p.Hours <= 0 || p.Outputs.Length == 0)
                throw new ArgumentException("Invalid production site");
            foreach (var amount in p.Inputs.Concat(p.Outputs))
            {
                StoreRequired(p.World, amount.Good); Nonnegative(amount.Quantity, "recipe quantity");
                if (amount.Quantity <= 0)
                    throw new ArgumentException("Recipe quantities must be positive");
            }
            if (p.Inputs.Select(a => a.Good).Distinct().Count() != p.Inputs.Length || p.Outputs.Select(a => a.Good).Distinct().Count() != p.Outputs.Length)
                throw new ArgumentException("Duplicate recipe ingredient");
        }
        if (_definition.Production.Select(p => new Key(p.World, p.Recipe)).Distinct().Count() != _definition.Production.Length)
            throw new ArgumentException("Duplicate production line");
        foreach (var e in _definition.Edges)
        {
            Nonnegative(e.CapacitySlotsPerHour, "edge capacity");
            Nonnegative(e.TravelHours, "edge travel duration"); Nonnegative(e.FuelPerSlot, "edge fuel");
            if (e.TravelHours <= 0)
                throw new ArgumentException("Invalid edge travel duration");
            if (!_worlds.ContainsKey(e.A) || !_worlds.ContainsKey(e.B) || e.A == e.B)
                throw new ArgumentException("Invalid graph edge");
        }
        foreach (var f in _definition.Shipping)
        {
            Nonnegative(f.Rate, "shipping rate"); Nonnegative(f.TravelHours, "travel duration");
            Nonnegative(f.FuelPerLot, "shipping fuel"); Nonnegative(f.CargoSlots, "cargo slots");
            if (!_edges.TryGetValue(f.Edge, out var edge) || f.Source == f.Target ||
                !((edge.A == f.Source && edge.B == f.Target) || (edge.B == f.Source && edge.A == f.Target)) ||
                f.TravelHours <= 0 || f.CargoSlots <= 0)
                throw new ArgumentException("Invalid freight route");
            if (f.Good != null)
            {
                StoreRequired(f.Source, f.Good); StoreRequired(f.Target, f.Good);
            }
            else if (Math.Abs(f.CargoSlots - 1) > Epsilon)
                throw new ArgumentException("Empty returns are measured in cargo slots");
            if (f.FuelPerLot > 0)
                StoreRequired(f.Source, "ship_fuel");
        }
        foreach (var d in _definition.Demands)
        {
            StoreRequired(d.World, d.Good); Nonnegative(d.Rate, "demand"); Nonnegative(d.OrderTotal, "order quota");
        }
    }

    private static double Get<T>(Dictionary<T, double> values, T key) where T : notnull => values.GetValueOrDefault(key);
    private static void Add<T>(Dictionary<T, double> values, T key, double amount) where T : notnull => values[key] = Get(values, key) + amount;
    private double Stock(Key key) => _stores.TryGetValue(key, out var store) ? store.Quantity : 0;
    private Activity ActivityFor(Key key)
    {
        if (!_activity.TryGetValue(key, out var value))
            _activity.Add(key, value = new Activity());
        return value;
    }
    private double Survival(string good, double hours) => _goods[good].Cold ? Math.Pow(1 - _definition.ColdDecayPerHour, hours) : 1;
    private double Withdraw(Key key, double amount)
    {
        if (!_stores.TryGetValue(key, out var store))
            return 0;
        var taken = Math.Min(Math.Max(0, amount), Math.Max(0, store.Quantity));
        store.Quantity -= taken;
        _ledgers[key.Good].Consumed += taken;
        ActivityFor(key).Consumed += taken;
        return taken;
    }

    private Dictionary<Key, double> Reservations()
    {
        var result = new Dictionary<Key, double>();
        foreach (var batch in _batches)
        foreach (var output in batch.Recipe.Outputs)
            Add(result, new Key(batch.Recipe.World, output.Good), output.Quantity * batch.Cycles);
        foreach (var cargo in _cargo)
            Add(result, cargo.Target, cargo.Quantity);
        return result;
    }

    private double Factor(Key key, Dictionary<Key, double> reserved)
    {
        var error = _stores[key].Definition.Target + Get(_pipeline, key) - Stock(key) - Get(reserved, key);
        return Math.Clamp(1 + error / Math.Max(Epsilon, Get(_replenishment, key) * Math.Max(2, _definition.RecoveryHours)), 0, _definition.Headroom);
    }

    private double PowerAvailable(EconomyWorld world) => world.BaseloadPower + Math.Min(ReactorCapacity(world),
        Stock(new Key(world.Id, "reactor_fuel")) * _definition.ReactorPowerPerFuel / StepHours);

    public EconomySnapshot Advance(bool active = true, EconomyDemandModifier[]? modifiers = null)
    {
        if (!active)
            return Snapshot();
        var demandModifiers = new Dictionary<Key, double>();
        foreach (var modifier in modifiers ?? Array.Empty<EconomyDemandModifier>())
        {
            if (!_worlds.ContainsKey(modifier.World) || !double.IsFinite(modifier.Multiplier) || modifier.Multiplier < 0)
                throw new ArgumentException("Invalid demand modifier");
            demandModifiers.Add(new Key(modifier.World, modifier.Group), modifier.Multiplier);
        }
        _activity.Clear();
        _productionStarted.Clear();
        _productionCompleted.Clear();
        _departures.Clear();
        _diet.Clear();
        AdvanceProjectWork();
        AdvanceFleet();
        foreach (var (key, store) in _stores)
        {
            var after = store.Quantity * Survival(key.Good, StepHours);
            _ledgers[key.Good].Losses += store.Quantity - after;
            store.Quantity = after;
        }
        foreach (var cargo in _cargo)
        {
            var after = cargo.Quantity * Survival(cargo.Target.Good, Math.Min(StepHours, cargo.Remaining));
            _ledgers[cargo.Target.Good].Losses += cargo.Quantity - after;
            cargo.Quantity = after;
            cargo.Remaining -= StepHours;
            if (cargo.Remaining <= Epsilon)
            {
                _stores[cargo.Target].Quantity += cargo.Quantity;
                ActivityFor(cargo.Target).Imported += cargo.Quantity;
            }
        }
        _cargo.RemoveAll(c => c.Remaining <= Epsilon);
        AdvanceProjectCargo();
        var usedPower = new Dictionary<string, double>();
        var usedLabor = new Dictionary<string, double>();
        foreach (var batch in _batches)
        {
            Add(usedPower, batch.Recipe.World, batch.Cycles * batch.Recipe.Power);
            Add(usedLabor, batch.Recipe.World, batch.Cycles * batch.Recipe.Labor);
        }
        var progress = new Dictionary<string, double>();
        foreach (var world in _definition.Worlds)
        {
            var available = PowerAvailable(world);
            var domestic = Math.Min(DomesticPower(world), available);
            var powerRequired = Get(usedPower, world.Id);
            var laborRequired = Get(usedLabor, world.Id);
            var powerFraction = powerRequired > Epsilon ? Math.Max(0, available - domestic) / powerRequired : 1;
            var laborFraction = laborRequired > Epsilon ? LaborAvailable(world) / laborRequired : 1;
            var fraction = Math.Min(1, Math.Min(powerFraction, laborFraction));
            progress.Add(world.Id, fraction);
            if (ReactorCapacity(world) > 0)
                ActivityFor(new Key(world.Id, "reactor_fuel")).Required += Math.Min(ReactorCapacity(world),
                    Math.Max(0, DomesticPower(world) + powerRequired - world.BaseloadPower)) * StepHours / _definition.ReactorPowerPerFuel;
            Withdraw(new Key(world.Id, "reactor_fuel"), Math.Max(0, domestic + Get(usedPower, world.Id) * fraction - world.BaseloadPower) * StepHours / _definition.ReactorPowerPerFuel);
        }
        foreach (var batch in _batches)
        {
            batch.Remaining -= StepHours * progress[batch.Recipe.World];
            if (batch.Remaining > Epsilon)
                continue;
            Add(_productionCompleted, new Key(batch.Recipe.World, batch.Recipe.Recipe), batch.Cycles);
            foreach (var input in batch.Recipe.Inputs)
                _ledgers[input.Good].Transformed += input.Quantity * batch.Cycles;
            foreach (var output in batch.Recipe.Outputs)
            {
                var amount = output.Quantity * batch.Cycles;
                _stores[new Key(batch.Recipe.World, output.Good)].Quantity += amount;
                _ledgers[output.Good].Manufactured += amount;
                ActivityFor(new Key(batch.Recipe.World, output.Good)).Produced += amount;
            }
        }
        _batches.RemoveAll(b => b.Remaining <= Epsilon);
        Consume(demandModifiers);
        RefreshAssets();
        RefreshProjectRoutes();
        _projectSupplyReserves.Clear();
        _edgeUsed.Clear();
        _edgeEmpty.Clear();
        Dispatch(demandModifiers);
        StartProduction();
        _hour += StepHours;
        return Snapshot();
    }

    private static int Priority(string group) => group switch
    {
        "water" or "closed_habitats" => 0, "nutrition" => 1, "care_drugs" or "care_supplies" => 2,
        "maintenance" => 3, "household" => 4, "military" => 5, "orders" => 7, _ => 6,
    };

    private void Record(Key key, double required, double served)
    {
        if (!_services.TryGetValue(key, out var service))
            _services.Add(key, service = new Service());
        service.Required += required;
        service.Served += served;
        service.LastRequired = required;
        service.LastServed = served;
        if (served + 1e-7 < required)
            service.FirstShortage ??= _hour;
    }

    private void Consume(Dictionary<Key, double> modifiers)
    {
        var handled = new HashSet<Key>();
        double Multiplier(Key key) => modifiers.GetValueOrDefault(key, 1);
        foreach (var demand in _definition.Demands)
        {
            if (demand.Group != "orders")
                ActivityFor(new Key(demand.World, demand.Good)).Required += demand.Rate * StepHours * Multiplier(new Key(demand.World, demand.Group));
        }
        foreach (var (key, rows) in _demands.OrderBy(d => Priority(d.Key.Good)).ThenBy(d => d.Key.World, StringComparer.Ordinal).ThenBy(d => d.Key.Good, StringComparer.Ordinal))
        {
            if (!handled.Add(key))
                continue;
            var required = rows.Sum(d => d.Rate) * Multiplier(key) * StepHours;
            if (key.Good == "orders")
            {
                var totals = _orders.Where(o => o.Key.World == key.World).ToArray();
                foreach (var (orderKey, order) in totals)
                {
                    order.Delivered += Withdraw(orderKey, Math.Min(order.Total - order.Delivered,
                        Math.Max(0, Stock(orderKey) - ProjectCommissioningHold(orderKey))));
                    if (order.Delivered + Epsilon >= order.Total)
                        order.Completed ??= _hour + StepHours;
                }
                if (!_services.TryGetValue(key, out var service))
                    _services.Add(key, service = new Service());
                service.Required = totals.Sum(o => o.Value.Total);
                service.Served = totals.Sum(o => o.Value.Delivered);
                service.LastRequired = service.LastServed = 0;
                if (_hour + StepHours >= _definition.OrderDeadlineHours && service.Served + Epsilon < service.Required)
                    service.FirstShortage ??= _definition.OrderDeadlineHours;
            }
            else if (key.Good == "nutrition")
            {
                var served = rows.Sum(d => Eat(d.World, d.Good, d.Rate * Multiplier(key) * StepHours));
                foreach (var food in Foods)
                    served += Eat(key.World, food, required - served);
                Record(key, required, served);
            }
            else if (key.Good is "regenerative_care" or "implant_care" or "maintenance")
            {
                // A treatment needs every ingredient, not just the most plentiful one.
                var goods = rows.GroupBy(d => d.Good).ToDictionary(g => new Key(key.World, g.Key),
                    g => g.Sum(d => d.Rate) * Multiplier(key) * StepHours);
                double Available(Key good)
                {
                    if (key.Good == "maintenance")
                        return Stock(good);
                    // Elective treatment must not consume the next basic-care basket.
                    var reserve = 0d;
                    foreach (var group in new[] { "care_drugs", "care_supplies" })
                    {
                        var basic = new Key(key.World, group);
                        if (_demands.TryGetValue(basic, out var basicRows))
                            reserve += basicRows.Where(d => d.Good == good.Good).Sum(d => d.Rate) * Multiplier(basic) * StepHours;
                    }
                    return Math.Max(0, Stock(good) - reserve);
                }
                var fraction = Math.Clamp(goods.Where(g => g.Value > Epsilon)
                    .Select(g => Available(g.Key) / g.Value).Append(1d).Min(), 0, 1);
                foreach (var (good, amount) in goods)
                    Withdraw(good, amount * fraction);
                Record(key, required, required * fraction);
            }
            else if (key.Good is "care_drugs" or "care_supplies")
            {
                var bundle = new[] { new Key(key.World, "care_drugs"), new Key(key.World, "care_supplies") }.Where(_demands.ContainsKey).ToArray();
                var goods = new Dictionary<Key, double>();
                foreach (var group in bundle)
                foreach (var d in _demands[group])
                    Add(goods, new Key(d.World, d.Good), d.Rate * Multiplier(group) * StepHours);
                var fraction = goods.Where(g => g.Value > Epsilon).Select(g => Stock(g.Key) / g.Value).Append(1d).Min();
                fraction = Math.Clamp(fraction, 0, 1);
                foreach (var (good, amount) in goods)
                    Withdraw(good, amount * fraction);
                foreach (var group in bundle)
                {
                    var amount = _demands[group].Sum(d => d.Rate) * Multiplier(group) * StepHours;
                    Record(group, amount, amount * fraction);
                    handled.Add(group);
                }
            }
            else
            {
                var served = rows.Sum(d => d.Good == "luxury_food"
                    ? Eat(d.World, d.Good, d.Rate * Multiplier(key) * StepHours)
                    : Withdraw(new Key(d.World, d.Good), d.Rate * Multiplier(key) * StepHours));
                Record(key, required, served);
            }
        }
    }

    // Start with individually feasible proposals, then proportionally share scarce resources.
    // Only decreasing grants means later constraints cannot violate previously visited ones.
    private static double[] Allocate(List<Proposal> proposals, Dictionary<Resource, double> limits)
    {
        var grants = new double[proposals.Count];
        var users = new Dictionary<Resource, List<(int Index, double Cost)>>();
        for (var i = 0; i < proposals.Count; i++)
        {
            grants[i] = Math.Max(0, proposals[i].Amount);
            foreach (var (resource, cost) in proposals[i].Needs)
            {
                if (cost <= Epsilon)
                    continue;
                grants[i] = Math.Min(grants[i], Math.Max(0, limits[resource]) / cost);
                if (!users.TryGetValue(resource, out var list))
                    users.Add(resource, list = new List<(int, double)>());
                list.Add((i, cost));
            }
        }
        foreach (var (resource, list) in users)
        {
            var required = list.Sum(u => grants[u.Index] * u.Cost);
            var available = Math.Max(0, limits[resource]);
            if (required <= available + Epsilon)
                continue;
            foreach (var (index, _) in list)
                grants[index] *= available / required;
        }
        return grants;
    }

    private void Dispatch(Dictionary<Key, double> modifiers)
    {
        // A present formation loads its dedicated opening stock before scheduled empty
        // returns can allocate the same kit to remote merchants. Ordinary freight priorities
        // and the public project admission queue are unaffected.
        GatherNavyDepotMaterials();
        var reserved = Reservations();
        var desired = _definition.Shipping.Select(f => f.Rate * TransportMultiplier(f.Source) * StepHours * (f.Good == null ? 1 : Factor(new Key(f.Target, f.Good), reserved))).ToArray();
        for (var i = 0; i < desired.Length; i++)
        {
            var f = _definition.Shipping[i];
            if (f.FuelPerLot > 0)
            {
                var requested = desired[i];
                if (f.Good != null)
                {
                    var target = new Key(f.Target, f.Good);
                    requested = Math.Min(requested, Math.Max(0, StorageCapacity(target) - Stock(target) - Get(reserved, target)));
                    if (_terminalOrderGoods.Contains(f.Good) && _orders.TryGetValue(target, out var order))
                        requested = Math.Min(requested, Math.Max(0, order.Total - order.Delivered - Stock(target) - Get(reserved, target)));
                }
                ActivityFor(new Key(f.Source, "ship_fuel")).Required += requested * f.FuelPerLot;
            }
        }
        var allocated = new double[desired.Length];
        var construction = _projects.Values.Any(p => p.Completed == null) || _projectReturns.Count > 0;
        for (var pass = 0; pass < 3; pass++)
        {
            if (pass == 1)
            {
                // During construction, recurring essential freight gets first use of this
                // step's capacity. Ordinary scheduling stays unchanged when no projects run.
                GatherProjectMaterials(modifiers);
                DispatchProjects(modifiers);
                reserved = Reservations();
            }
            var proposals = new List<Proposal>();
            var limits = new Dictionary<Resource, double>();
            for (var i = 0; i < desired.Length; i++)
            {
                var f = _definition.Shipping[i];
                var protectedSupply = !construction || f.Good == null || _protectedShippingGoods.Contains(f.Good);
                var amount = pass < 2
                    ? protectedSupply == (pass == 0) ? Math.Min(desired[i], f.Rate * StepHours) : 0
                    : Math.Max(0, desired[i] - allocated[i]);
                var needs = new Dictionary<Resource, double> { [new Resource("edge", f.Edge)] = f.CargoSlots };
                if (_definition.EnableAssets)
                    needs.Add(new Resource("fleet", f.Source), f.CargoSlots);
                if (f.Good != null)
                {
                    needs.Add(new Resource("stock", f.Source, f.Good), 1);
                    needs.Add(new Resource("capacity", f.Target, f.Good), 1);
                }
                if (f.FuelPerLot > Epsilon)
                    Add(needs, new Resource("stock", f.Source, "ship_fuel"), f.FuelPerLot);
                foreach (var resource in needs.Keys)
                {
                    var key = new Key(resource.World, resource.Good);
                    limits[resource] = resource.Kind switch
                    {
                        "edge" => EdgeCapacity(resource.World) * StepHours - Get(_edgeUsed, resource.World),
                        "stock" => Stock(key),
                        "fleet" => FleetCapacity(resource.World) - FleetOccupied(resource.World),
                        _ => StorageCapacity(key) - Stock(key) - Get(reserved, key),
                    };
                }
                if (f.Good != null && _terminalOrderGoods.Contains(f.Good) && _orders.TryGetValue(new Key(f.Target, f.Good), out var order))
                {
                    var key = new Key(f.Target, f.Good);
                    var resource = new Resource("order_destination", f.Target, f.Good);
                    needs.Add(resource, 1);
                    limits[resource] = order.Total - order.Delivered - Stock(key) - Get(reserved, key);
                }
                proposals.Add(new Proposal(amount, needs));
            }
            var grants = Allocate(proposals, limits);
            var emptyCrossings = new List<(EconomyShipping Route, double Slots)>();
            for (var i = 0; i < grants.Length; i++)
            {
                var amount = grants[i];
                if (amount <= Epsilon)
                    continue;
                var f = _definition.Shipping[i];
                if (f.Good != null)
                {
                    _stores[new Key(f.Source, f.Good)].Quantity -= amount;
                    var key = new Key(f.Target, f.Good);
                    var flow = new FlowKey(f.Edge, f.Source, f.Target, f.Good, f.TravelHours);
                    _cargo.Add(new Cargo(flow, amount));
                    Add(_departures, flow, amount);
                    ActivityFor(new Key(f.Source, f.Good)).Exported += amount;
                    Add(reserved, key, amount);
                }
                else
                {
                    Add(_edgeEmpty, f.Edge, amount * f.CargoSlots);
                    emptyCrossings.Add((f, amount * f.CargoSlots));
                }
                ReserveFleet(f, amount);
                Withdraw(new Key(f.Source, "ship_fuel"), amount * f.FuelPerLot);
                Add(_edgeUsed, f.Edge, amount * f.CargoSlots);
                allocated[i] += amount;
            }
            // Fulfil every already allocated regular shipment before claiming construction stock.
            foreach (var (route, slots) in emptyCrossings)
                Add(_edgeEmpty, route.Edge, -LoadScheduledReturn(route, slots, modifiers));
        }
    }

    private void StartProduction()
    {
        var reserved = Reservations();
        var occupied = new Dictionary<Key, double>();
        var power = new Dictionary<string, double>();
        var labor = new Dictionary<string, double>();
        foreach (var batch in _batches)
        {
            Add(occupied, new Key(batch.Recipe.World, batch.Recipe.Recipe), batch.Cycles);
            Add(power, batch.Recipe.World, batch.Cycles * batch.Recipe.Power);
            Add(labor, batch.Recipe.World, batch.Cycles * batch.Recipe.Labor);
        }
        var desired = _definition.Production.Select(p => ProductionRate(p) * ProductionMultiplier(p.World) * StepHours * p.Outputs.Min(o => Factor(new Key(p.World, o.Good), reserved))).ToArray();
        var remainingOrders = _terminalOrderGoods.ToDictionary(g => g, g =>
            Math.Max(0, _orders.Where(o => o.Key.Good == g).Sum(o => o.Value.Total - o.Value.Delivered) + ProjectOutstanding(g) -
            _stores.Where(s => s.Key.Good == g).Sum(s => s.Value.Quantity + Get(reserved, s.Key))));
        for (var i = 0; i < desired.Length; i++)
        {
            var p = _definition.Production[i];
            var requested = Math.Min(desired[i], Math.Max(0, ProductionRate(p) * ProductionMultiplier(p.World) * p.Hours * _definition.Headroom - Get(occupied, new Key(p.World, p.Recipe))));
            foreach (var output in p.Outputs)
            {
                var key = new Key(p.World, output.Good);
                requested = Math.Min(requested, Math.Max(0, StorageCapacity(key) - Stock(key) - Get(reserved, key)) / output.Quantity);
                if (remainingOrders.TryGetValue(output.Good, out var remaining))
                    requested = Math.Min(requested, remaining / output.Quantity);
            }
            foreach (var input in p.Inputs)
                ActivityFor(new Key(p.World, input.Good)).Required += requested * input.Quantity;
        }
        var allocated = new double[desired.Length];
        for (var pass = 0; pass < 2; pass++)
        {
            var orderLeft = _terminalOrderGoods.ToDictionary(g => g, g =>
                _orders.Where(o => o.Key.Good == g).Sum(o => o.Value.Total - o.Value.Delivered) + ProjectOutstanding(g) -
                _stores.Where(s => s.Key.Good == g).Sum(s => s.Value.Quantity + Get(reserved, s.Key)));
            var proposals = new List<Proposal>();
            var limits = new Dictionary<Resource, double>();
            for (var i = 0; i < desired.Length; i++)
            {
                var p = _definition.Production[i];
                var amount = pass == 0 ? Math.Min(desired[i], ProductionRate(p) * StepHours) : Math.Max(0, desired[i] - allocated[i]);
                amount = Math.Min(amount, Math.Max(0, ProductionRate(p) * ProductionMultiplier(p.World) * p.Hours * _definition.Headroom - Get(occupied, new Key(p.World, p.Recipe))));
                var needs = new Dictionary<Resource, double>();
                foreach (var input in p.Inputs)
                    needs.Add(new Resource("stock", p.World, input.Good), input.Quantity);
                foreach (var output in p.Outputs)
                {
                    needs.Add(new Resource("capacity", p.World, output.Good), output.Quantity);
                    if (orderLeft.ContainsKey(output.Good))
                        needs.Add(new Resource("order", "", output.Good), output.Quantity);
                }
                needs.Add(new Resource("power", p.World), p.Power);
                needs.Add(new Resource("labor", p.World), p.Labor);
                foreach (var resource in needs.Keys)
                {
                    var key = new Key(resource.World, resource.Good);
                    limits[resource] = resource.Kind switch
                    {
                        "stock" => Stock(key),
                        "capacity" => StorageCapacity(key) - Stock(key) - Get(reserved, key),
                        "power" => PowerAvailable(_worlds[p.World]) - DomesticPower(_worlds[p.World]) - Get(power, p.World),
                        "labor" => LaborAvailable(_worlds[p.World]) - Get(labor, p.World),
                        _ => orderLeft[resource.Good],
                    };
                }
                proposals.Add(new Proposal(amount, needs));
            }
            var grants = Allocate(proposals, limits);
            for (var i = 0; i < grants.Length; i++)
            {
                var cycles = grants[i];
                if (cycles <= Epsilon)
                    continue;
                var p = _definition.Production[i];
                foreach (var input in p.Inputs)
                {
                    _stores[new Key(p.World, input.Good)].Quantity -= input.Quantity * cycles;
                    ActivityFor(new Key(p.World, input.Good)).Consumed += input.Quantity * cycles;
                }
                foreach (var output in p.Outputs)
                    Add(reserved, new Key(p.World, output.Good), output.Quantity * cycles);
                _batches.Add(new Batch(p, cycles));
                Add(_productionStarted, new Key(p.World, p.Recipe), cycles);
                Add(occupied, new Key(p.World, p.Recipe), cycles);
                Add(power, p.World, cycles * p.Power);
                Add(labor, p.World, cycles * p.Labor);
                allocated[i] += cycles;
            }
        }
    }

    public EconomySnapshot Snapshot()
    {
        var reserved = Reservations();
        var stock = new Dictionary<string, double>();
        var wip = new Dictionary<string, double>();
        var transit = new Dictionary<string, double>();
        var incoming = new Dictionary<Key, double>();
        var flows = new Dictionary<FlowKey, double>();
        foreach (var (key, store) in _stores)
            Add(stock, key.Good, store.Quantity);
        foreach (var (key, quantity) in _navyDepotStock)
            Add(stock, key.Good, quantity);
        foreach (var batch in _batches)
        foreach (var input in batch.Recipe.Inputs)
            Add(wip, input.Good, input.Quantity * batch.Cycles);
        foreach (var cargo in _cargo)
        {
            Add(transit, cargo.Target.Good, cargo.Quantity);
            Add(incoming, cargo.Target, cargo.Quantity);
            Add(flows, cargo.Flow, cargo.Quantity);
        }
        foreach (var cargo in _projectCargo)
        {
            Add(transit, cargo.Good, cargo.Quantity);
            if (!cargo.Traveling)
                continue;
            Add(incoming, new Key(cargo.Leg.To, cargo.Good), cargo.Quantity);
            Add(flows, cargo.Flow, cargo.Quantity);
        }
        return new EconomySnapshot(_hour,
            Array.AsReadOnly(_stores.Select(s => new EconomyInventoryState(s.Key.World, s.Key.Good,
                s.Value.Quantity + Get(_navyDepotStock, s.Key), s.Value.Definition.Target,
                StorageCapacity(s.Key) + Get(_navyDepotCapacity, s.Key), Get(reserved, s.Key))).ToArray()),
            Array.AsReadOnly(_services.Select(s => new EconomyServiceState(s.Key.World, s.Key.Good, s.Value.Required, s.Value.Served,
                s.Value.LastRequired, s.Value.LastServed, s.Value.FirstShortage)).ToArray()),
            Array.AsReadOnly(_orders.Select(o => new EconomyOrderState(o.Key.World, o.Key.Good, o.Value.Total, o.Value.Delivered, o.Value.Completed)).ToArray()),
            Array.AsReadOnly(_definition.Edges.Select(e => new EconomyEdgeState(e.Id, Get(_edgeUsed, e.Id), EdgeCapacity(e.Id), Get(_edgeEmpty, e.Id))).ToArray()),
            Array.AsReadOnly(_ledgers.Select(l => new EconomyLedgerState(l.Key, l.Value.Initial, l.Value.Manufactured, l.Value.Transformed,
                l.Value.Consumed, l.Value.Losses, Get(stock, l.Key), Get(wip, l.Key), Get(transit, l.Key))).ToArray()),
            _batches.Count, _cargo.Count + _projectCargo.Count,
            Array.AsReadOnly(_stores.Keys.Union(_activity.Keys).Union(incoming.Keys).Select(k =>
            {
                _activity.TryGetValue(k, out var a);
                return new EconomyGoodActivity(k.World, k.Good, a?.Produced ?? 0, a?.Consumed ?? 0, a?.Required ?? 0,
                    a?.Imported ?? 0, a?.Exported ?? 0, Get(incoming, k));
            }).ToArray()),
            Array.AsReadOnly(flows.Keys.Union(_departures.Keys).Select(f => new EconomyRouteActivity(f.Edge, f.From, f.To, f.Good,
                Get(_departures, f), Get(flows, f), f.TravelHours)).ToArray()))
        {
            Assets = AssetSnapshot(),
            Capacities = CapacitySnapshot(),
            Diets = DietSnapshot(),
            Projects = ProjectSnapshot(),
            NavySupplyFlows = NavySupplySnapshot(),
            Production = Array.AsReadOnly(_definition.Production.Select(p =>
                new EconomyProductionState(p.World, p.Recipe,
                    Get(_productionCompleted, new Key(p.World, p.Recipe)),
                    Get(_productionStarted, new Key(p.World, p.Recipe)),
                    _batches.Where(b => b.Recipe.World == p.World && b.Recipe.Recipe == p.Recipe).Sum(b => b.Cycles),
                    p.Rate)
                {
                    EffectiveCyclesPerHour = ProductionRate(p),
                    CommittedExpansionCyclesPerHour = Get(_committedProduction, new Key(p.World, p.Recipe)),
                }).ToArray()),
        };
    }
}
