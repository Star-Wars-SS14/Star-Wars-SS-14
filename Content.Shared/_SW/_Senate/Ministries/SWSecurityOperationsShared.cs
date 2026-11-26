using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWSecurityOrder : byte { Move, Investigate, Surveil, Finish, Withdraw, Sabotage, Recover, Replace, Resupply }
[Serializable, NetSerializable]
public enum SWSecurityTeamPhase : byte { Ready, Travel, Work, Recovery, Lost, Supply }
[Serializable, NetSerializable]
public enum SWSecurityCaseKind : byte { Domestic, Imperial }

[Serializable, NetSerializable]
public sealed class SWSecurityMapState
{
    public string MapId = "SenateRepublic";
    public double NowHours;
    public double RealSecondsPerHour = 240;
    public bool MinisterActive;
    public bool ClockRunning;
    public int Budget;
    public int DeploymentCost;
    public int RecoveryCost;
    public int SabotageCost;
    public int ReplacementCost;
    public int TeamsLost;
    public List<SWSecuritySabotageRequestView> SabotageRequests = new();
    public int CompletedCases;
    public int CellCap;
    public List<SWSecurityOrderOptionView> OrderOptions = new();
    public List<SWSecurityWorldView> Worlds = new();
    public List<SWSecurityLinkView> Links = new();
    public List<SWSecurityTeamView> Teams = new();
    public List<SWSecurityCellView> Cells = new();
    public List<SWSecurityCaseView> Cases = new();
}
[Serializable, NetSerializable]
public sealed class SWSecurityWorldView
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string SectorId = string.Empty;
    public string Faction = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWSecurityLinkView
{
    public string Id = string.Empty;
    public string From = string.Empty;
    public string To = string.Empty;
    public double TravelHours;
    public bool MainHyperlane;
    public bool Traversable;
}
[Serializable, NetSerializable]
public sealed class SWSecurityTeamView
{
    public int Id;
    public string Name = string.Empty;
    public string WorldId = string.Empty;
    public string? NextWorldId;
    public string TargetWorldId = string.Empty;
    public SWSecurityTeamPhase Phase;
    public SWSecurityOrder Order;
    public int CaseId;
    public double LegStartedAt;
    public double EtaHours;
    public double DestinationEtaHours;
    public string Status = string.Empty;
    public int Supply;
    public string SupplyProjectWorld = string.Empty;
    public string SupplyStatus = string.Empty;
    public List<SWSecuritySupplyMaterialView> SupplyMaterials = new();
}
[Serializable, NetSerializable]
public sealed class SWSecuritySupplyMaterialView
{
    public string Good = string.Empty;
    public string Name = string.Empty;
    public double Required, Delivered, Incoming, EtaHours;
}
[Serializable, NetSerializable]
public sealed class SWSecurityCellView
{
    public string WorldId = string.Empty;
    public bool Deployed;
    public bool Burned;
    public double ReadyAt;
    public bool CanDeploy;
    public string Reason = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWSecurityCaseView
{
    public int Id;
    public string Title = string.Empty;
    public SWSecurityCaseKind Kind;
    public string WorldId = string.Empty;
    public int Evidence;
    public int RequiredEvidence;
    public bool ReconComplete;
    public bool SabotageApprovalPending;
    public bool SabotageApproved;
    public bool DirectorCreated;
    public string Status = string.Empty;
    public string Risk = string.Empty;
    public string Objective = string.Empty;
    public string Briefing = string.Empty;
    public string Scene = string.Empty;
    public List<string> Clues = new();
}
[Serializable, NetSerializable]
public sealed class SWSecurityOrderOptionView
{
    public int TeamId;
    public SWSecurityOrder Order;
    public string WorldId = string.Empty;
    public int CaseId;
    public bool Available;
    public string Reason = string.Empty;
    public string Risk = string.Empty;
    public int Cost;
    public int BaseRiskPercent, SupplyPenaltyPercent, LaunchRiskPercent, SupplyCost;
    public double TravelHours;
    public double WorkHours;
}
[Serializable, NetSerializable]
public sealed class SWSecuritySabotageRequestView
{
    public string WorldName = string.Empty;
    public int CaseId;
    public int TeamId;
    public string Title = string.Empty;
    public string WorldId = string.Empty;
    public string Risk = string.Empty;
    public int Cost;
}
[Serializable, NetSerializable]
public sealed class SWSecurityTeamOrderMessage : BoundUserInterfaceMessage
{
    public int TeamId;
    public SWSecurityOrder Order;
    public string WorldId;
    public int CaseId;
    public SWSecurityTeamOrderMessage(int teamId, SWSecurityOrder order, string worldId, int caseId = 0)
    { TeamId = teamId; Order = order; WorldId = worldId; CaseId = caseId; }
}
[Serializable, NetSerializable]
public sealed class SWSecurityCellCommandMessage : BoundUserInterfaceMessage
{
    public string WorldId;
    public bool Deploy;
    public SWSecurityCellCommandMessage(string worldId, bool deploy) { WorldId = worldId; Deploy = deploy; }
}
