using System;
using System.Collections.Generic;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private readonly Dictionary<Key, double> _navyDepotStock = new();
    private readonly Dictionary<Key, double> _navyDepotCapacity = new();

    private void InitializeNavyDepots()
    {
        foreach (var depot in _definition.NavyDepots)
        foreach (var material in depot.Materials)
        {
            var key = new Key(depot.World, material.Good);
            if (!_worlds.TryGetValue(depot.World, out var world) || !world.Active || !_stores.ContainsKey(key)
                || material.Good is not ("military_supplies" or "ship_fuel")
                || !double.IsFinite(material.Quantity) || material.Quantity <= 0 || _navyDepotStock.ContainsKey(key))
                throw new ArgumentException("Invalid opening Navy depot");
            _navyDepotStock.Add(key, material.Quantity);
            _navyDepotCapacity.Add(key, material.Quantity);
            _ledgers[material.Good].Initial += material.Quantity;
        }
    }

    // Opening naval reserves are real finite capital inventory, protected from scheduled
    // merchant exports. They are never replenished automatically or consumed by other kits.
    private double WithdrawNavyDepot(Key key, double requested)
    {
        var amount = Math.Min(requested, Get(_navyDepotStock, key));
        if (amount <= Epsilon) return 0;
        Add(_navyDepotStock, key, -amount);
        _ledgers[key.Good].Consumed += amount;
        ActivityFor(key).Consumed += amount;
        return amount;
    }

    private void GatherNavyDepotMaterials()
    {
        foreach (var project in ProjectsCollectingMaterials())
        {
            if (!project.Request.NavySupply) continue;
            foreach (var material in project.Request.Materials)
            {
                var missing = Math.Max(0, material.Quantity - project.Delivered[material.Good] - ProjectIncoming(project, material.Good));
                project.Delivered[material.Good] += WithdrawNavyDepot(new Key(project.Request.World, material.Good), missing);
            }
        }
    }
}
