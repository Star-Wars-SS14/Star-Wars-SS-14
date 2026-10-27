using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public sealed class SWEconomyEventOptionView
{
    public int Cost;
    public string Materials = string.Empty, Status = string.Empty;
    public double WorkHours;
    public bool Available;
}

[Serializable, NetSerializable]
public sealed class SWEconomyEventView
{
    public int Id;
    public bool Active;
    public string CrisisId = string.Empty, Sector = string.Empty, SectorName = string.Empty, Kind = string.Empty, World = string.Empty, WorldName = string.Empty, Edge = string.Empty;
    public string AName = string.Empty, BName = string.Empty, Title = string.Empty, Detail = string.Empty;
    public string Status = string.Empty, Effect = string.Empty, ProjectId = string.Empty, Materials = string.Empty, Work = string.Empty;
    public double RemainingHours;
    public SWEconomyEventOptionView Standard = new(), Fast = new();
}

[Serializable, NetSerializable]
public sealed class SWEconomyRespondEventMessage(int eventId, bool accelerated) : BoundUserInterfaceMessage
{
    public int EventId = eventId;
    public bool Accelerated = accelerated;
}
