using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private double ProjectExpectedIncoming(Project project, string good) => _projectCargo
        .Where(c => c.Project == project && c.Good == good && !c.Payload).Sum(CargoExpectedArrival);

    private double ProjectPathSurvival(string good, ProjectLeg[] path, double? firstHours = null) =>
        Survival(good, path.Length == 0 ? 0 : (firstHours ?? path[0].Hours) + path.Skip(1).Sum(l => l.Hours));

    private double SourceProjectAmount(string good, double missing, ProjectLeg[] path, double available, double? firstHours = null)
    {
        if (!_goods[good].Cold) return Math.Min(missing, available);
        var survival = ProjectPathSurvival(good, path, firstHours);
        if (survival <= 0 || !double.IsFinite(survival)) return 0;
        // Extra cold-chain input is real source stock, not manufactured compensation.
        // If an available load cannot survive the model's cargo cutoff, do not launch it.
        var amount = Math.Min(available, missing / survival);
        return amount * survival > Epsilon ? amount : 0;
    }

    private void StoreProjectSurplus(string world, string good, double quantity)
    {
        if (quantity <= 0) return;
        var key = new Key(world, good);
        var stored = 0d;
        if (_stores.TryGetValue(key, out var store))
        {
            stored = Math.Min(quantity, Math.Max(0, StorageCapacity(key) - Stock(key) - Get(Reservations(), key)));
            store.Quantity += stored;
        }
        // A changed route can deliver more than forecast. Never commission the kit twice:
        // keep real excess in the receiving warehouse, or explicitly discard what cannot fit.
        _ledgers[good].Losses += quantity - stored;
    }
}
