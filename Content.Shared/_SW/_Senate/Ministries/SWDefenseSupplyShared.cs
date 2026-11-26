using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWDefenseSupplyKind : byte { Supply, Repair, Construction, PlanetaryConstruction, PlanetarySupply }

/// <summary>
/// One actual Navy project shipment on its current leg. Times use the accompanying
/// SupplyHour economic clock, not Senate wall time. A waiting relay is not a traveling flow.
/// </summary>
[Serializable, NetSerializable]
public sealed record SWDefenseSupplyFlowView(
    string Id,
    string ProjectId,
    SWDefenseSupplyKind Kind,
    int? RecipientFleetId,
    string TargetWorldId,
    string EdgeId,
    string From,
    string To,
    string Good,
    string GoodName,
    double Quantity,
    double DepartAtHours,
    double ArriveAtHours);
