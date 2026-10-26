using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Economy;

public sealed record SWNavySupplyState(double Hour, IReadOnlyList<SWDefenseSupplyFlowView> Flows);

/// <summary>Copies a single completed worker snapshot into immutable military-only telemetry.</summary>
public static class SWRepublicNavySupplyPublication
{
    public static SWNavySupplyState Create(EconomySnapshot? snapshot, IReadOnlyDictionary<string, string> names) =>
        new(snapshot?.Hour ?? 0, Array.AsReadOnly(snapshot?.NavySupplyFlows.Select(f => new SWDefenseSupplyFlowView(
            f.Id, f.ProjectId, (SWDefenseSupplyKind) f.Kind, f.RecipientFleetId, f.TargetWorldId,
            f.EdgeId, f.From, f.To, f.Good, names.GetValueOrDefault(f.Good, f.Good),
            f.Quantity, f.DepartAtHours, f.ArriveAtHours)).ToArray() ?? Array.Empty<SWDefenseSupplyFlowView>()));
}
