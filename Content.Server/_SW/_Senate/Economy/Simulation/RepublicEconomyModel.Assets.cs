using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private sealed record AssetRule(string Effect, double PerUnit, double Cap, params string[] Services);

    // Commissioning is a transfer from the goods ledger into capital, not recurring consumption.
    // Existing infrastructure maintenance includes the finite round portfolio. Its supplied share
    // controls availability here; charging the same parts, treatment or military kits again would
    // double-count that service. Physical structures remain standing during a supply interruption.
    private static readonly Dictionary<string, AssetRule> AssetRules = new()
    {
        ["machinery"] = new("production", .03, .20, "maintenance", "other"),
        ["work_droids"] = new("labor", .04, .20, "maintenance", "other"),
        ["building_modules"] = new("storage", .05, .50),
        ["life_support"] = new("domestic_power", .04, .20, "maintenance", "other", "closed_habitats"),
        ["com_equipment"] = new("transport", .03, .15, "maintenance", "other"),
        ["ground_vehicles"] = new("transport", .04, .20, "maintenance", "other"),
        ["freighters"] = new("fleet", 16, 64, "maintenance"),
        ["reactor_modules"] = new("reactor", 8, 32, "maintenance"),
        ["medical_equipment"] = new("healthcare", 1, 2, "maintenance", "care_drugs", "care_supplies"),
        ["lab_equipment"] = new("education", 1, 2, "maintenance", "other"),
        ["education_kits"] = new("education", 1, 4, "maintenance", "other"),
        ["entertainment"] = new("society", 1, 3, "maintenance", "other"),
        ["shield_generators"] = new("defense", .5, 2, "maintenance", "military"),
        ["blasters"] = new("defense", .25, 2, "maintenance", "military"),
        ["armor"] = new("defense", .25, 2, "maintenance", "military"),
    };

    private sealed class FleetHold(string world, double slots, double hours)
    {
        public readonly string World = world;
        public readonly double Slots = slots;
        public double Remaining = hours;
    }

    private readonly List<FleetHold> _fleetHolds = new();
    private readonly Dictionary<string, double> _baseFleet = new();
    private readonly Dictionary<string, double> _fleetOccupied = new();
    private readonly Dictionary<Key, EconomyAssetState> _assets = new();
    private readonly Dictionary<Key, double> _assetEffects = new();
    private readonly Dictionary<string, (double Consumed, double QualityTotal)> _diet = new();

    private void InitializeAssets()
    {
        foreach (var world in _definition.Worlds)
        {
            if (world.FleetCapacity is { } configured && (!double.IsFinite(configured) || configured < 0))
                throw new ArgumentException("Invalid initial fleet capacity");
            // A vehicle is busy for the whole leg, including an empty repositioning trip.
            // Round up to a simulation step: arrivals cannot release capacity between steps.
            _baseFleet[world.Id] = world.FleetCapacity ?? _definition.Shipping
                .Where(f => f.Source == world.Id)
                .Sum(f => f.Rate * f.CargoSlots * Math.Ceiling(f.TravelHours / StepHours) * StepHours);
        }
        RefreshAssets();
    }

    private void RefreshAssets()
    {
        _assets.Clear();
        _assetEffects.Clear();
        if (!_definition.EnableAssets)
            return;
        foreach (var (key, order) in _orders)
        {
            if (!AssetRules.TryGetValue(key.Good, out var rule))
                continue;
            var condition = 1d;
            foreach (var group in rule.Services)
            {
                // A habitat-only service is irrelevant on an open, habitable world.
                if (group == "closed_habitats" && !_demands.ContainsKey(new Key(key.World, group)))
                    continue;
                var serviceKey = new Key(key.World, group);
                // Equipment without any configured maintenance supply is not self-sustaining.
                var supplied = _services.TryGetValue(serviceKey, out var service) && service.LastRequired > Epsilon
                    ? Math.Clamp(service.LastServed / service.LastRequired, 0, 1) : 0;
                condition = Math.Min(condition, supplied);
            }
            var operational = order.Delivered * condition;
            var bonus = Math.Min(rule.Cap, operational * rule.PerUnit);
            _assets.Add(key, new EconomyAssetState(key.World, key.Good, order.Delivered, operational, order.Total, rule.Effect, bonus));
            Add(_assetEffects, new Key(key.World, rule.Effect), bonus);
        }
        // Communication and ground fleets share one logistics ceiling. Publish each item's
        // actual share of that ceiling, so the UI never advertises an unapplied extra bonus.
        foreach (var world in _definition.Worlds)
        {
            var total = Effect(world.Id, "transport");
            if (total <= .25)
                continue;
            foreach (var key in _assets.Where(a => a.Key.World == world.Id && a.Value.Effect == "transport").Select(a => a.Key).ToArray())
                _assets[key] = _assets[key] with { EffectiveBonus = _assets[key].EffectiveBonus * .25 / total };
            _assetEffects[new Key(world.Id, "transport")] = .25;
        }
    }

    private double Effect(string world, string effect) => Get(_assetEffects, new Key(world, effect));
    private double ProductionMultiplier(string world) => 1 + Math.Min(.20, Effect(world, "production"));
    private double TransportMultiplier(string world) => 1 + Math.Min(.25, Effect(world, "transport"));
    private double LaborAvailable(EconomyWorld world) => world.LaborCapacity * (1 + Math.Min(.20, Effect(world.Id, "labor")));
    private double DomesticPower(EconomyWorld world) => world.DomesticPower * (1 - Math.Min(.20, Effect(world.Id, "domestic_power")));
    private double ReactorCapacity(EconomyWorld world) => world.ReactorCapacityPower + Effect(world.Id, "reactor");
    private double StorageMultiplier(string world) => 1 + Math.Min(.50, Effect(world, "storage"));
    private double StorageCapacity(Key key) => _stores[key].Definition.Capacity * StorageMultiplier(key.World);
    private double FleetCapacity(string world) => (_baseFleet.GetValueOrDefault(world) + Effect(world, "fleet")) * TransportMultiplier(world);
    private double FleetOccupied(string world) => _fleetOccupied.GetValueOrDefault(world) + _projectFleetOccupied.GetValueOrDefault(world);

    private void AdvanceFleet()
    {
        _fleetOccupied.Clear();
        foreach (var hold in _fleetHolds)
        {
            hold.Remaining -= StepHours;
            if (hold.Remaining > Epsilon)
                Add(_fleetOccupied, hold.World, hold.Slots);
        }
        _fleetHolds.RemoveAll(h => h.Remaining <= Epsilon);
    }

    private void ReserveFleet(EconomyShipping route, double amount)
    {
        if (!_definition.EnableAssets)
            return;
        var slots = amount * route.CargoSlots;
        _fleetHolds.Add(new FleetHold(route.Source, slots, route.TravelHours));
        Add(_fleetOccupied, route.Source, slots);
    }

    private double Eat(string world, string good, double requested)
    {
        var amount = Withdraw(new Key(world, good), requested);
        var score = good switch { "fresh_food" or "luxury_food" => 1d, "rations" => .5, "synth_food" => .25, _ => 0 };
        var previous = _diet.GetValueOrDefault(world);
        _diet[world] = (previous.Consumed + amount, previous.QualityTotal + amount * score);
        return amount;
    }

    private IReadOnlyList<EconomyAssetState> AssetSnapshot() => Array.AsReadOnly(_assets.Values.ToArray());
    private IReadOnlyList<EconomyDietState> DietSnapshot() => Array.AsReadOnly(_diet.Select(d => new EconomyDietState(
        d.Key, d.Value.Consumed, d.Value.Consumed > Epsilon ? d.Value.QualityTotal / d.Value.Consumed : 0)).ToArray());
    private IReadOnlyList<EconomyCapacityState> CapacitySnapshot() => Array.AsReadOnly(_definition.Worlds.Select(w =>
        new EconomyCapacityState(w.Id, ProductionMultiplier(w.Id), LaborAvailable(w), StorageMultiplier(w.Id),
            DomesticPower(w), ReactorCapacity(w), FleetCapacity(w.Id), FleetOccupied(w.Id))).ToArray());
}
