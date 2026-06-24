using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public enum SWStatisticsConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public enum SWSectorSelectConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWStatEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string ShortName = string.Empty;
    public Color Color;
    public float Value;
    public float Min;
    public float Max;
}

[Serializable, NetSerializable]
public sealed class SWGlobalStatEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string ShortName = string.Empty;
    public Color Color;
    public float Policy;
    public float Actual;
    public float Min;
    public float Max;
    public List<SWStatEntry> Subs = new();
}

[Serializable, NetSerializable]
public sealed class SWSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public bool Claimed;
    public List<SWStatEntry> Stats = new();
}

[Serializable, NetSerializable]
public sealed class SWStatisticsConsoleState : BoundUserInterfaceState
{
    public List<SWGlobalStatEntry> Globals;
    public List<SWSectorEntry> Sectors;

    public SWStatisticsConsoleState(List<SWGlobalStatEntry> globals, List<SWSectorEntry> sectors)
    {
        Globals = globals;
        Sectors = sectors;
    }
}

[Serializable, NetSerializable]
public sealed class SWSectorSelectConsoleState : BoundUserInterfaceState
{
    public List<SWSectorEntry> Sectors;
    public bool HasCard;
    public bool IsSenatorCard;
    public string? CurrentSector;
    public string? CurrentSectorName;

    public SWSectorSelectConsoleState(List<SWSectorEntry> sectors, bool hasCard, bool isSenatorCard, string? currentSector, string? currentSectorName)
    {
        Sectors = sectors;
        HasCard = hasCard;
        IsSenatorCard = isSenatorCard;
        CurrentSector = currentSector;
        CurrentSectorName = currentSectorName;
    }
}

[Serializable, NetSerializable]
public sealed class SWSelectSectorMessage : BoundUserInterfaceMessage
{
    public string SectorId;

    public SWSelectSectorMessage(string sectorId)
    {
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public enum SWSenateInfoConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWSectorOccupancy
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public string? Holder;
}

[Serializable, NetSerializable]
public sealed class SWSenateInfoConsoleState : BoundUserInterfaceState
{
    public List<SWSectorOccupancy> Sectors;
    public string? Chancellor;
    public string? Speaker;
    public List<SWScheduleRow> Schedule;
    public string GameTime;
    public List<SWBillEntry> ActiveLaws;

    public SWSenateInfoConsoleState(List<SWSectorOccupancy> sectors, string? chancellor, string? speaker, List<SWScheduleRow> schedule, string gameTime, List<SWBillEntry> activeLaws)
    {
        Sectors = sectors;
        Chancellor = chancellor;
        Speaker = speaker;
        Schedule = schedule;
        GameTime = gameTime;
        ActiveLaws = activeLaws;
    }
}

[Serializable, NetSerializable]
public enum SWSenateWorkConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWSenateRosterEntry
{
    public string Position = string.Empty;
    public string Holder = string.Empty;
    public Color Color;
}

[Serializable, NetSerializable]
public sealed class SWSenateWorkConsoleState : BoundUserInterfaceState
{
    public bool HasCard;
    public bool IsSenateCard;
    public SWSenatePosition Position;
    public string PositionLabel = string.Empty;
    public bool InOffice;
    public bool CanTake;
    public bool CanResign;
    public string? SectorName;
    public List<SWStatEntry> SectorStats = new();
    public List<int> SectorTrends = new();
    public List<SWGlobalStatEntry> Globals = new();
    public List<SWSenateRosterEntry> Roster = new();
    public List<SWBillEntry> Bills = new();
    public int DraftBudget;
    public string GameTime = string.Empty;
    public List<SWScheduleRow> Schedule = new();
    public List<SWBillOption> SchedulableBills = new();
    public List<int> FreeStartHours = new();
    public List<SWSessionEntry> PendingSessions = new();
    public List<SWBillPresetEntry> Presets = new();
    public bool CanDilemmas;
    public List<SWDilemmaEntry> MyDilemmas = new();
    public List<SWDilemmaEntry> AllDilemmas = new();
    public List<SWDilemmaEntry> MyBudgetDilemmas = new();
    public bool IsChancellorView;
    public bool BudgetLocked;
    public int BudgetTotal;
    public int BudgetReserve;
    public int MyBudgetAvailable;
    public List<SWBudgetSectorEntry> BudgetSectors = new();
    public bool CanPrograms;
    public List<SWProgramEntry> Programs = new();
    public bool CanAmbitions;
    public List<SWAmbitionEntry> Ambitions = new();
    public List<SWCrisisEntry> Crises = new();
    public bool CanParties;
    public int MyPartyId = -1;
    public List<SWPartyEntry> Parties = new();
}

[Serializable, NetSerializable]
public sealed class SWPartyMemberEntry
{
    public string SectorId = string.Empty;
    public string SectorName = string.Empty;
    public string Holder = string.Empty;
    public Color Color;
    public bool IsLeader;
}

[Serializable, NetSerializable]
public sealed class SWPartyPendingEntry
{
    public string SectorId = string.Empty;
    public string SectorName = string.Empty;
    public string Holder = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWPartyEntry
{
    public int Id;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public Color Color;
    public bool Formed;
    public string StatusLabel = string.Empty;
    public string GraceWarning = string.Empty;
    public string LeaderName = string.Empty;
    public int MemberCount;
    public List<SWPartyMemberEntry> Members = new();
    public List<SWPartyPendingEntry> Pending = new();
    public bool IsMine;
    public bool ViewerIsLeader;
    public bool CanRequestJoin;
    public bool JoinPending;
    public bool ViewerCanLeave;
    public bool ViewerCanDisband;
    public bool ViewerCanInitiateVote;
    public bool CanManageMembers;
    public bool VoteActive;
    public bool VoteIsEvict;
    public string VoteTargetName = string.Empty;
    public int VoteYes;
    public int VoteNo;
    public int VoteNeeded;
    public bool ViewerCanCastVote;
    public bool ViewerVoted;
}

[Serializable, NetSerializable]
public sealed class SWSenateCreatePartyMessage : BoundUserInterfaceMessage
{
    public string Name;
    public string Description;

    public SWSenateCreatePartyMessage(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateRequestJoinPartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;

    public SWSenateRequestJoinPartyMessage(int partyId)
    {
        PartyId = partyId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateCancelJoinPartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;

    public SWSenateCancelJoinPartyMessage(int partyId)
    {
        PartyId = partyId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateConfirmJoinPartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;
    public string SectorId;

    public SWSenateConfirmJoinPartyMessage(int partyId, string sectorId)
    {
        PartyId = partyId;
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateRejectJoinPartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;
    public string SectorId;

    public SWSenateRejectJoinPartyMessage(int partyId, string sectorId)
    {
        PartyId = partyId;
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateLeavePartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;

    public SWSenateLeavePartyMessage(int partyId)
    {
        PartyId = partyId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateDisbandPartyMessage : BoundUserInterfaceMessage
{
    public int PartyId;

    public SWSenateDisbandPartyMessage(int partyId)
    {
        PartyId = partyId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateInitiateLeaderVoteMessage : BoundUserInterfaceMessage
{
    public int PartyId;

    public SWSenateInitiateLeaderVoteMessage(int partyId)
    {
        PartyId = partyId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateInitiateEvictVoteMessage : BoundUserInterfaceMessage
{
    public int PartyId;
    public string SectorId;

    public SWSenateInitiateEvictVoteMessage(int partyId, string sectorId)
    {
        PartyId = partyId;
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateCastLeaderVoteMessage : BoundUserInterfaceMessage
{
    public int PartyId;
    public bool Support;

    public SWSenateCastLeaderVoteMessage(int partyId, bool support)
    {
        PartyId = partyId;
        Support = support;
    }
}

[Serializable, NetSerializable]
public sealed class SWCrisisSolutionEntry
{
    public string Label = string.Empty;
    public string Description = string.Empty;
    public int Cost;
    public bool Affordable = true;
    public float Relief;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWCrisisEntry
{
    public int Id;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string TypeLabel = string.Empty;
    public string AffectedLabel = string.Empty;
    public float Severity;
    public string SeverityLabel = string.Empty;
    public List<SWBillEffectEntry> Impact = new();
    public string EscalationThreat = string.Empty;
    public string ExitProgress = string.Empty;
    public bool CanResolve;
    public bool CanAid;
    public int AidCost;
    public bool AidAffordable;
    public bool CanPropose;
    public List<SWCrisisSolutionEntry> Solutions = new();
    public List<SWCrisisLawEntry> Laws = new();
    public List<string> Log = new();
}

[Serializable, NetSerializable]
public sealed class SWCrisisLawEntry
{
    public int Index;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public float Relief;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateResolveCrisisMessage : BoundUserInterfaceMessage
{
    public int CrisisId;
    public int SolutionIndex;

    public SWSenateResolveCrisisMessage(int crisisId, int solutionIndex)
    {
        CrisisId = crisisId;
        SolutionIndex = solutionIndex;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateAidCrisisMessage : BoundUserInterfaceMessage
{
    public int CrisisId;

    public SWSenateAidCrisisMessage(int crisisId)
    {
        CrisisId = crisisId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateProposeEmergencyLawMessage : BoundUserInterfaceMessage
{
    public int CrisisId;
    public int LawIndex;

    public SWSenateProposeEmergencyLawMessage(int crisisId, int lawIndex)
    {
        CrisisId = crisisId;
        LawIndex = lawIndex;
    }
}

[Serializable, NetSerializable]
public sealed class SWAmbitionEntry
{
    public string Name = string.Empty;
    public string Description = string.Empty;
    public bool Met;
    public string Progress = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWProgramEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Category = string.Empty;
    public int Cost;
    public bool Affordable;
    public bool Applied;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateApplyProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWSenateApplyProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWBudgetSectorEntry
{
    public string SectorId = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class SWSenateSetBudgetMessage : BoundUserInterfaceMessage
{
    public string SectorId;
    public int Amount;

    public SWSenateSetBudgetMessage(string sectorId, int amount)
    {
        SectorId = sectorId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateLockBudgetMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWDilemmaOptionEntry
{
    public string Label = string.Empty;
    public string Description = string.Empty;
    public int Cost;
    public bool Affordable = true;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWDilemmaEntry
{
    public int Id;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public bool IsRepublic;
    public bool IsBudget;
    public string OwnerLabel = string.Empty;
    public string Status = string.Empty;
    public string TimeLabel = string.Empty;
    public int ChosenOption = -1;
    public bool CanResolve;
    public string FailText = string.Empty;
    public List<SWDilemmaOptionEntry> Options = new();
    public List<SWBillEffectEntry> FailEffects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateResolveDilemmaMessage : BoundUserInterfaceMessage
{
    public int DilemmaId;
    public int OptionIndex;

    public SWSenateResolveDilemmaMessage(int dilemmaId, int optionIndex)
    {
        DilemmaId = dilemmaId;
        OptionIndex = optionIndex;
    }
}

[Serializable, NetSerializable]
public sealed class SWBillPresetEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateSubmitPresetMessage : BoundUserInterfaceMessage
{
    public string PresetId;

    public SWSenateSubmitPresetMessage(string presetId)
    {
        PresetId = presetId;
    }
}

[Serializable, NetSerializable]
public sealed class SWFloorQueueEntry
{
    public NetEntity Target;
    public string Name = string.Empty;
    public bool IsChancellor;
}

[Serializable, NetSerializable]
public sealed class SWSenateGrantFloorMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;

    public SWSenateGrantFloorMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateRevokeFloorMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateRemoveFromQueueMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;

    public SWSenateRemoveFromQueueMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateGrantChancellorMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWBillEffectEntry
{
    public string StatName = string.Empty;
    public Color Color;
    public float Delta;
}

[Serializable, NetSerializable]
public sealed class SWBillEntry
{
    public int Id;
    public string Name = string.Empty;
    public string Author = string.Empty;
    public string Description = string.Empty;
    public string Stage = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateSubmitBillMessage : BoundUserInterfaceMessage
{
    public string Name;
    public string Description;
    public Dictionary<string, float> Effects;

    public SWSenateSubmitBillMessage(string name, string description, Dictionary<string, float> effects)
    {
        Name = name;
        Description = description;
        Effects = effects;
    }
}

[Serializable, NetSerializable]
public enum SWVoteChoice : byte
{
    For,
    Against,
    Abstain,
}

[Serializable, NetSerializable]
public enum SWSenateVoteConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWSectorVoteEntry
{
    public string Name = string.Empty;
    public Color Color;
    public string VoteLabel = string.Empty;
    public string PartyTag = string.Empty;
    public Color PartyColor;
}

[Serializable, NetSerializable]
public sealed class SWSenateVoteConsoleState : BoundUserInterfaceState
{
    public List<SWBillEntry> Bills = new();
    public bool HasCurrent;
    public int CurrentId;
    public string CurrentName = string.Empty;
    public string CurrentAuthor = string.Empty;
    public string CurrentDescription = string.Empty;
    public List<SWBillEffectEntry> CurrentEffects = new();
    public int VotesFor;
    public int VotesAgainst;
    public int VotesAbstain;
    public List<SWSectorVoteEntry> SectorVotes = new();
    public bool CanVote;
    public bool HasVoted;
    public SWVoteChoice MyVote;
    public string GameTime = string.Empty;
    public bool AwaitingTiebreak;
    public bool CanEndVote;
    public bool CanTiebreak;
    public string SessionInfo = string.Empty;
    public bool CanControlFloor;
    public string? FloorHolderName;
    public List<SWFloorQueueEntry> FloorQueue = new();
}

[Serializable, NetSerializable]
public sealed class SWSenateCastVoteMessage : BoundUserInterfaceMessage
{
    public SWVoteChoice Choice;

    public SWSenateCastVoteMessage(SWVoteChoice choice)
    {
        Choice = choice;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateEndVoteMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateTiebreakMessage : BoundUserInterfaceMessage
{
    public SWVoteChoice Choice;

    public SWSenateTiebreakMessage(SWVoteChoice choice)
    {
        Choice = choice;
    }
}

[Serializable, NetSerializable]
public sealed class SWScheduleRow
{
    public int Hour;
    public string TimeLabel = string.Empty;
    public string Text = string.Empty;
    public Color Color;
    public bool IsNow;
}

[Serializable, NetSerializable]
public sealed class SWBillOption
{
    public int Id;
    public string Name = string.Empty;
    public bool Emergency;
}

[Serializable, NetSerializable]
public sealed class SWSessionEntry
{
    public int Id;
    public string BillName = string.Empty;
    public string TimeRange = string.Empty;
    public string Status = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWSenateScheduleSessionMessage : BoundUserInterfaceMessage
{
    public int BillId;
    public int StartHour;
    public int Duration;

    public SWSenateScheduleSessionMessage(int billId, int startHour, int duration)
    {
        BillId = billId;
        StartHour = startHour;
        Duration = duration;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateConfirmSessionMessage : BoundUserInterfaceMessage
{
    public int SessionId;

    public SWSenateConfirmSessionMessage(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateRejectSessionMessage : BoundUserInterfaceMessage
{
    public int SessionId;

    public SWSenateRejectSessionMessage(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateRemoveSessionMessage : BoundUserInterfaceMessage
{
    public int SessionId;

    public SWSenateRemoveSessionMessage(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateEmergencySessionMessage : BoundUserInterfaceMessage
{
    public int BillId;

    public SWSenateEmergencySessionMessage(int billId)
    {
        BillId = billId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateAnnounceMessage : BoundUserInterfaceMessage
{
    public string Text;

    public SWSenateAnnounceMessage(string text)
    {
        Text = text;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateTakeOfficeMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateResignMessage : BoundUserInterfaceMessage
{
}
