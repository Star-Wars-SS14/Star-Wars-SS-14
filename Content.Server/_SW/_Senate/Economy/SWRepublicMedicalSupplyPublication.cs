using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Economy;

public static class SWRepublicMedicalSupplyPublication
{
    public static IReadOnlyDictionary<string, double> WarehouseStock(EconomySnapshot? snapshot, IReadOnlySet<string> republicWorlds)
    {
        var totals = new Dictionary<string, double>();
        if (snapshot != null)
            foreach (var inventory in snapshot.Inventory.Where(i => republicWorlds.Contains(i.World)))
                totals[inventory.Good] = totals.GetValueOrDefault(inventory.Good) + inventory.Stock;
        return totals;
    }

    public static IReadOnlyList<SWHealthSupplyFlowView> Create(EconomySnapshot? snapshot, IReadOnlyDictionary<string, string>? names = null) =>
        Array.AsReadOnly(snapshot?.MedicalSupplyFlows.Select(f => new SWHealthSupplyFlowView
        {
            Id = f.Id, ProjectId = f.ProjectId, Kind = f.Medical.Kind.ToString(), StrainId = f.Medical.StrainId,
            TargetWorldId = f.TargetWorldId, EdgeId = f.EdgeId, From = f.From, To = f.To,
            Good = f.Good, Payload = f.Payload, GoodName = f.Payload ? "Готовая вакцина" : names?.GetValueOrDefault(f.Good, f.Good) ?? f.Good,
            Quantity = f.Quantity, DepartAtHours = f.DepartAtHours, ArriveAtHours = f.ArriveAtHours,
            DeliveryAtHours = f.DeliveryAtHours, FinalLeg = f.FinalLeg, RouteBlocked = f.RouteBlocked,
        }).ToArray() ?? Array.Empty<SWHealthSupplyFlowView>());
}
