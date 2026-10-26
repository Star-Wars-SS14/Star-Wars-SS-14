using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;
using Content.Shared.Eui;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWDefenseFleetOrder : byte { Guard, Patrol, Border, Return, Repair, Train, Respond, Move }
[Serializable, NetSerializable]
public enum SWDefenseOperationPhase : byte { Standing, Preparation, Travel, Execution, Recovery, Lost }
[Serializable, NetSerializable]
public enum SWDefenseEventKind : byte { Piracy, Unrest, Incursion, Blockade, Raid }

/// <summary>Authoritative strategic state. Time and ETAs use active Senate hours.</summary>
[Serializable, NetSerializable]
public sealed class SWDefenseMapState
{
    public double NowHours;
    public string MapId = "SenateRepublic";
    public bool MinisterActive;
    public bool ClockRunning;
    public double RealSecondsPerHour = 240;
    public int RecruitmentCost = 5000;
    public int Budget;
    public int UpkeepPerHour;
    public double SupplyHour;
    public List<SWDefenseSupplyFlowView> SupplyFlows = new();
    public List<SWDefenseFleetView> Fleets = new();
    public List<SWDefenseWorldView> Worlds = new();
    public List<SWDefenseLinkView> Links = new();
    public List<SWDefenseBorderView> Borders = new();
    public List<SWDefenseEventView> Events = new();
    public List<SWDefenseShipyardView> Shipyards = new();
    public List<SWDefenseOrderOptionView> OrderOptions = new();
}

[Serializable, NetSerializable]
public sealed class SWDefenseFleetView
{
    public int Id;
    public string Name = string.Empty;
    public string HomeWorldId = string.Empty;
    public string CurrentWorldId = string.Empty;
    public string? NextWorldId;
    public string TargetWorldId = string.Empty;
    public SWDefenseFleetOrder Order;
    public SWDefenseOperationPhase Phase;
    public float Strength;
    public float Damage;
    public float Supply;
    public float Training;
    public double EtaHours;
    public double DestinationEtaHours;
    public double LegStartedAt;
    public string PatrolEdgeId = string.Empty;
    public int EventId;
    public string Status = string.Empty;
    public string Risk = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWDefenseWorldView
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string SectorId = string.Empty;
    public string Faction = string.Empty;
    public bool Shipyard;
}
[Serializable, NetSerializable]
public sealed class SWDefenseLinkView
{
    public string Id = string.Empty;
    public string From = string.Empty;
    public string To = string.Empty;
    public double TravelHours;
    public bool MainHyperlane;
    public string RouteName = string.Empty;
    public bool MilitaryOnly;
    public bool Traversable;
}
[Serializable, NetSerializable]
public sealed class SWDefenseBorderView
{
    public string Id = string.Empty;
    public string RepublicWorldId = string.Empty;
    public string ForeignWorldId = string.Empty;
    public string Faction = string.Empty;
    public float Pressure;
    public float Coverage;
}
[Serializable, NetSerializable]
public sealed class SWDefenseOrderOptionView
{
    public int FleetId;
    public SWDefenseFleetOrder Order;
    public string Target = string.Empty;
    // Target is the command argument; a border or patrol may name a link instead of a world.
    public string ResolvedWorldId = string.Empty;
    public int EventId;
    public double PreparationHours;
    public double TravelHours;
    public double ExecutionHours;
    public int Cost;
    public bool Available;
    public string Reason = string.Empty;
    public string Risk = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWDefenseEventView
{
    public int Id;
    public string Title = string.Empty;
    public string WorldId = string.Empty;
    public string EdgeId = string.Empty;
    public SWDefenseEventKind Kind;
    public float Severity;
    public float EnemyStrength;
    public double ExpiresAt;
    public bool Resolved;
    public bool DirectorCreated;
    public string Status = string.Empty;
    public string Risk = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWDefenseShipyardView
{
    public string WorldId = string.Empty;
    public string Name = string.Empty;
    public bool Busy;
    public string Status = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWDefenseFleetOrderMessage : BoundUserInterfaceMessage
{
    public int FleetId;
    public SWDefenseFleetOrder Order;
    public string Target;
    public int EventId;
    public SWDefenseFleetOrderMessage(int fleetId, SWDefenseFleetOrder order, string target, int eventId = 0)
    { FleetId = fleetId; Order = order; Target = target; EventId = eventId; }
}
[Serializable, NetSerializable]
public sealed class SWDefenseRecruitMessage : BoundUserInterfaceMessage
{
    public string WorldId;
    public SWDefenseRecruitMessage(string worldId) { WorldId = worldId; }
}
[Serializable, NetSerializable]
public sealed class SWDefenseDirectorEventMessage : EuiMessageBase
{
    public int EventId;
    public string Title;
    public string WorldId;
    public string EdgeId;
    public float Severity;
    public float EnemyStrength;
    public double DurationHours;
    public bool Resolve;
    public SWDefenseEventKind Kind;
    public SWDefenseDirectorEventMessage(int eventId, string title, string worldId, string edgeId,
        float severity, float enemyStrength, double durationHours, bool resolve = false, SWDefenseEventKind kind = SWDefenseEventKind.Piracy)
    { EventId = eventId; Title = title; WorldId = worldId; EdgeId = edgeId; Severity = severity;
        EnemyStrength = enemyStrength; DurationHours = durationHours; Resolve = resolve; Kind = kind; }
}
