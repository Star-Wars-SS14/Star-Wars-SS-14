using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>A detached, actually departed medical cargo leg from the economy worker.</summary>
[Serializable, NetSerializable]
public sealed class SWHealthSupplyFlowView
{
    public string Id = "";
    public string ProjectId = "";
    public string Kind = "";
    public bool Payload;
    public string StrainId = "";
    public string TargetWorldId = "";
    public string EdgeId = "";
    public string From = "";
    public string To = "";
    public string Good = "";
    public string GoodName = "";
    public double Quantity;
    public double DepartAtHours;
    public double ArriveAtHours;
    /// <summary>Earliest arrival at the final recipient, excluding future dispatch queues.</summary>
    public double? DeliveryAtHours;
    public bool FinalLeg;
    public bool RouteBlocked;
}
