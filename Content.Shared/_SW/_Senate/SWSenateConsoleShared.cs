using System.Collections.Generic;
using Content.Shared._SW._Senate.Ministries;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate;

[Serializable, NetSerializable]
public enum SWStatisticsConsoleUiKey
{
    Key
}

// Emergency powers the Senate can vest in the Chancellor, by escalating tier.
[Serializable, NetSerializable]
public enum SWEmergencyPower : byte
{
    Veto,            // tier I — reject a passed law instead of signing it
    Command,         // tier I — invoke ministers' big buttons without them
    EmergencyBudget, // tier II — requisition / redirect ministry budgets
    MartialLaw,      // tier II — combined defence + security + courts crackdown
    Mobilization,    // tier II — war economy / conscription
    Censorship,      // tier II — suppress dissent
    Purge,           // tier III — political prosecutions
    Decree,          // tier III — enact law bypassing the Senate vote
    MinisterControl, // tier III — appoint / dismiss ministers
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
    public List<SWCrisisEntry> Crises = new();
    public List<SWBillEntry> ActiveLaws = new();
    public string GameTime = string.Empty;
    public int DefconLevel;
    public string DefconName = string.Empty;
    public Color DefconColor;
    public int BorderPressure;
    public string BorderStatus = string.Empty;
    public List<SWBillEffectEntry> DefconEffects = new();
    public string PostureName = string.Empty;
    public Color PostureColor;
    public int Threat;
    public string ThreatStatus = string.Empty;
    public int Unrest;
    public string UnrestStatus = string.Empty;
    public List<SWBillEffectEntry> PostureEffects = new();
    public bool PathogenActive;
    public string PandemicStatus = string.Empty;
    public Color PandemicColor;
    public string PathogenName = string.Empty;
    public int PathogenStrain;
    public int InfectionAvg;
    public int Immunity;
    public int Research;
    public string EconPhaseName = string.Empty;
    public Color EconPhaseColor;
    public float Growth;
    public int Overheat;
    public string OverheatStatus = string.Empty;
    public int KeyRate;
    public int Inflation;
    public string InflationStatus = string.Empty;
    public int Debt;
    public string CreditRating = string.Empty;
    public Color RatingColor;
    public int Backlog;
    public string BacklogStatus = string.Empty;
    public string JusticeStanceName = string.Empty;
    public Color JusticeStanceColor;
    public int CaseCount;
    public int CorruptionIndex;
    public string CorruptionStatus = string.Empty;
    public bool PowersActive;
    public string EmergencyLevelName = string.Empty;
    public int Authority;
    public int Legitimacy;

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
public enum SWMinistrySelectConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWMinistryEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public Color Color;
    public int KindGroup;
    public string KindLabel = string.Empty;
    public string? ParentName;
    public bool Claimed;
    public string? Holder;
    public bool Locked;
}

[Serializable, NetSerializable]
public sealed class SWMinistrySelectConsoleState : BoundUserInterfaceState
{
    public List<SWMinistryEntry> Ministries;
    public bool HasCard;
    public bool IsMinisterCard;
    public string? Current;
    public string? CurrentName;

    public SWMinistrySelectConsoleState(List<SWMinistryEntry> ministries, bool hasCard, bool isMinisterCard, string? current, string? currentName)
    {
        Ministries = ministries;
        HasCard = hasCard;
        IsMinisterCard = isMinisterCard;
        Current = current;
        CurrentName = currentName;
    }
}

[Serializable, NetSerializable]
public sealed class SWSelectMinistryMessage : BoundUserInterfaceMessage
{
    public string MinistryId;

    public SWSelectMinistryMessage(string ministryId)
    {
        MinistryId = ministryId;
    }
}

[Serializable, NetSerializable]
public enum SWMinistryWorkConsoleUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SWMinistryWorkConsoleState : BoundUserInterfaceState
{
    public bool HasCard;
    public bool IsMinisterCard;
    public bool HasMinistry;
    public bool CanResign;
    public string MinistryName = string.Empty;
    public string MinistryTitle = string.Empty;
    public string MinistryDescription = string.Empty;
    public Color MinistryColor;
    public string KindLabel = string.Empty;
    public string? DomainStatId;
    public List<string> ExtraDomainStatIds = new();
    public List<SWGlobalStatEntry> Globals = new();
    public string GameTime = string.Empty;
    public int MinistryBudget;
    public int MinistryAdmin;
    public int AuditCost;
    public List<SWMinistryRequestEntry> Requests = new();

    // КОМАНДОВАНИЕ (Defense ministry only).
    public bool IsDefense;
    public SWDefenseStance Stance;
    public int DefconLevel;
    public string StanceName = string.Empty;
    public Color StanceColor;
    public List<SWBillEffectEntry> StanceEffects = new();
    public bool WarPending;
    public string PendingStanceName = string.Empty;
    public int BorderPressure;
    public string BorderStatus = string.Empty;
    public int WarWeariness;
    public string SelectedProgram = string.Empty;
    public int ProgramProgress;
    public List<SWDefenseProgramEntry> Programs = new();
    public List<SWFleetGroupEntry> Fleet = new();
    public List<SWFleetSectorEntry> FleetSectors = new();
    public int FleetUpkeep;
    public List<SWDefenseOperationEntry> Operations = new();
    public List<SWActiveOpEntry> ActiveOps = new();
    public int ActiveOpSlots;
    public int ActiveOpUsed;
    public List<string> DefenseLog = new();

    // КОНТРОЛЬ (Internal Security ministry only).
    public bool IsSecurity;
    public SWSecurityPosture Posture;
    public string PostureName = string.Empty;
    public Color PostureColor;
    public List<SWBillEffectEntry> PostureEffects = new();
    public bool EmergencyPending;
    public string PendingPostureName = string.Empty;
    public int Threat;
    public string ThreatStatus = string.Empty;
    public int Unrest;
    public string UnrestStatus = string.Empty;
    public string SelectedSecProgram = string.Empty;
    public int SecProgramProgress;
    public List<SWDefenseProgramEntry> SecPrograms = new();
    public List<SWCellEntry> Cells = new();
    public List<SWCellSectorEntry> CellSectors = new();
    public int CellUpkeep;
    public List<SWDefenseOperationEntry> SecOperations = new();
    public List<SWActiveOpEntry> SecActiveOps = new();
    public int SecOpSlots;
    public int SecOpUsed;
    public List<string> SecurityLog = new();
    public List<SWPlotEntry> Plots = new();

    // ЭПИДЕМИЯ (Health ministry only).
    public bool IsHealth;
    public bool PathogenActive;
    public string PathogenName = string.Empty;
    public int PathogenStrain;
    public int Virulence;
    public string PandemicStatus = string.Empty;
    public Color PandemicColor;
    public int InfectionAvg;
    public int Immunity;
    public int Research;
    public bool VaccineReady;
    public bool VaccineStale;
    public int ResearchFunding;
    public string ResearchFundingLabel = string.Empty;
    public bool HealthEmergencyActive;
    public int HealthEmergencyHoursLeft;
    public bool HealthEmergencyPending;
    public string SelectedHealthProgram = string.Empty;
    public int HealthProgramProgress;
    public List<SWDefenseProgramEntry> HealthPrograms = new();
    public List<SWInfectionSectorEntry> InfectionSectors = new();
    public List<string> HealthLog = new();
    public int HospitalMaxLevel;

    // ЭКОНОМИКА (Economy ministry only).
    public bool IsEconomy;
    public SWEconomyPhase EconPhase;
    public string EconPhaseName = string.Empty;
    public Color EconPhaseColor;
    public float Growth;
    public int Overheat;
    public string OverheatStatus = string.Empty;
    public int EconPolicy;
    public string EconPolicyLabel = string.Empty;
    public bool StimulusPending;
    public string SelectedEconProgram = string.Empty;
    public int EconProgramProgress;
    public List<SWDefenseProgramEntry> EconPrograms = new();
    public List<SWMegaprojectEntry> Megaprojects = new();
    public List<SWActiveMegaprojectEntry> ActiveProjects = new();
    public int ProjectSlots;
    public int ProjectUsed;
    public List<SWTradeRouteEntry> TradeRoutes = new();
    public int TradeRouteCap;
    public List<SWEconSectorEntry> EconSectors = new();
    public List<string> EconomyLog = new();

    // ФИНАНСЫ (Finance ministry only).
    public bool IsFinance;
    public int Treasury;
    public int KeyRate;
    public List<int> RateOptions = new();
    public int Inflation;
    public string InflationStatus = string.Empty;
    public int PricePercent;
    public int Debt;
    public string DebtStatus = string.Empty;
    public string CreditRating = string.Empty;
    public Color RatingColor;
    public int TaxLevel;
    public string TaxLevelLabel = string.Empty;
    public bool CanIssueBonds;
    public int BondProceeds;
    public bool CanRepayDebt;
    public bool EmissionPending;
    public string SelectedFinanceProgram = string.Empty;
    public int FinanceProgramProgress;
    public List<SWDefenseProgramEntry> FinancePrograms = new();
    public List<string> FinanceLog = new();

    // БЮДЖЕТЫ (Finance only) — daily budget project.
    public string BudgetPhaseLabel = string.Empty;
    public Color BudgetPhaseColor;
    public int BudgetDayTotal;
    public int BudgetSectorPool;
    public int BudgetMinistryPool;
    public int BudgetSectorPct;
    public int BudgetRejects;
    public string BudgetRejectReason = string.Empty;
    public bool BudgetCanEdit;
    public bool BudgetCanSubmit;
    public List<SWBudgetShareEntry> BudgetShares = new();

    // ЮСТИЦИЯ (Justice ministry only).
    public bool IsJustice;
    public SWJusticeStance JusticeStance;
    public string JusticeStanceName = string.Empty;
    public Color JusticeStanceColor;
    public int Backlog;
    public string BacklogStatus = string.Empty;
    public int CourtCapacity;
    public int CourtCapacityMax;
    public int OvertimeCost;
    public bool CanBuyOvertime;
    public bool TribunalActive;
    public int TribunalHoursLeft;
    public bool TribunalPending;
    public string SelectedJusticeProgram = string.Empty;
    public int JusticeProgramProgress;
    public List<SWDefenseProgramEntry> JusticePrograms = new();
    public List<SWCaseEntry> Cases = new();
    public int CaseCount;
    public List<string> JusticeLog = new();
    public List<SWClosedCaseEntry> JusticeArchive = new();
    public int JusticeResolved;
    public int JusticeConvictJust;
    public int JusticeConvictWrong;
    public int JusticeAcquitJust;
    public int JusticeAcquitWrong;
    public int JusticeDismissed;
    public int JusticeOverdue;
    public int JusticeAccuracy;

    // КОРРУПЦИЯ (Justice tab).
    public int CorruptionIndex;
    public string CorruptionStatus = string.Empty;
    public int ScreenCost;
    public int ProsecuteCost;
    public List<SWCorruptTargetEntry> CorruptSectors = new();
    public List<SWCorruptTargetEntry> CorruptPeople = new();
    public List<string> CorruptionLog = new();

    // Personal corruption (the viewer — any office holder).
    public bool HasPersonal;
    public int MyCorruption;
    public int MyAccount;
}

[Serializable, NetSerializable]
public sealed class SWCorruptTargetEntry
{
    public string Key = string.Empty;
    public bool Sector;
    public string Name = string.Empty;
    public string Role = string.Empty;
    public Color Color;
    public bool Revealed;
    public int Corruption;
    public int Account;
    public bool CanScreen;
    public bool CanProsecute;
}

[Serializable, NetSerializable]
public sealed class SWCorruptionScreenMessage : BoundUserInterfaceMessage
{
    public string Key;
    public bool Sector;

    public SWCorruptionScreenMessage(string key, bool sector)
    {
        Key = key;
        Sector = sector;
    }
}

[Serializable, NetSerializable]
public sealed class SWCorruptionProsecuteMessage : BoundUserInterfaceMessage
{
    public string Key;
    public bool Sector;

    public SWCorruptionProsecuteMessage(string key, bool sector)
    {
        Key = key;
        Sector = sector;
    }
}

[Serializable, NetSerializable]
public sealed class SWClosedCaseEntry
{
    public int Id;
    public string TypeLabel = string.Empty;
    public Color TypeColor;
    public string ResultLabel = string.Empty;
    public Color ResultColor;
}

[Serializable, NetSerializable]
public sealed class SWCaseEntry
{
    public int Id;
    public SWCaseStage Stage;
    public string TypeLabel = string.Empty;
    public Color TypeColor;
    public string StageLabel = string.Empty;
    public int Evidence;
    public string EvidenceLabel = string.Empty;
    public string SeverityLabel = string.Empty;
    public string AgeLabel = string.Empty;
    public string SectorName = string.Empty;
    public bool CanInvestigate;
    public bool CanSendToCourt;
    public bool CanHold;
    public bool AtVerdict;
}

[Serializable, NetSerializable]
public sealed class SWJusticeInvestigateMessage : BoundUserInterfaceMessage
{
    public int CaseId;

    public SWJusticeInvestigateMessage(int caseId)
    {
        CaseId = caseId;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeSendToCourtMessage : BoundUserInterfaceMessage
{
    public int CaseId;

    public SWJusticeSendToCourtMessage(int caseId)
    {
        CaseId = caseId;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeHoldHearingMessage : BoundUserInterfaceMessage
{
    public int CaseId;

    public SWJusticeHoldHearingMessage(int caseId)
    {
        CaseId = caseId;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeVerdictMessage : BoundUserInterfaceMessage
{
    public int CaseId;
    public SWVerdict Verdict;

    public SWJusticeVerdictMessage(int caseId, SWVerdict verdict)
    {
        CaseId = caseId;
        Verdict = verdict;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeSetStanceMessage : BoundUserInterfaceMessage
{
    public SWJusticeStance Stance;

    public SWJusticeSetStanceMessage(SWJusticeStance stance)
    {
        Stance = stance;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeSelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWJusticeSelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWJusticeOvertimeMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWJusticeDeclareTribunalMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWJusticeApproveTribunalMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWJusticeRejectTribunalMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWBudgetShareEntry
{
    public string MinistryId = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Weight;
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class SWBudgetAdjustRatioMessage : BoundUserInterfaceMessage
{
    public int DeltaPct;

    public SWBudgetAdjustRatioMessage(int deltaPct)
    {
        DeltaPct = deltaPct;
    }
}

[Serializable, NetSerializable]
public sealed class SWBudgetSetWeightMessage : BoundUserInterfaceMessage
{
    public string MinistryId;
    public int Delta;

    public SWBudgetSetWeightMessage(string ministryId, int delta)
    {
        MinistryId = ministryId;
        Delta = delta;
    }
}

[Serializable, NetSerializable]
public sealed class SWBudgetResetWeightsMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWBudgetSubmitMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWBudgetApproveMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWBudgetRejectMessage : BoundUserInterfaceMessage
{
    public string Reason;

    public SWBudgetRejectMessage(string reason)
    {
        Reason = reason;
    }
}

[Serializable, NetSerializable]
public sealed class SWMegaprojectEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Phases;
    public int CostPerPhase;
    public int PhaseHours;
    public SWOpTarget Target;
    public bool Available;
    public string StatusLabel = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWActiveMegaprojectEntry
{
    public int Index;
    public string Name = string.Empty;
    public string Detail = string.Empty;
    public int Phase;
    public int TotalPhases;
    public int PhaseLeft;
    public bool Stalled;
}

[Serializable, NetSerializable]
public sealed class SWTradeRouteEntry
{
    public int Id;
    public string AName = string.Empty;
    public string BName = string.Empty;
    public bool Active;
    public string StatusLabel = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWEconSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Economy;
    public int Industry;
}

[Serializable, NetSerializable]
public sealed class SWEconomySetPolicyMessage : BoundUserInterfaceMessage
{
    public int Level;

    public SWEconomySetPolicyMessage(int level)
    {
        Level = level;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyLaunchProjectMessage : BoundUserInterfaceMessage
{
    public string ProjectId;
    public string Target;

    public SWEconomyLaunchProjectMessage(string projectId, string target)
    {
        ProjectId = projectId;
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyCancelProjectMessage : BoundUserInterfaceMessage
{
    public int Index;

    public SWEconomyCancelProjectMessage(int index)
    {
        Index = index;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyAddRouteMessage : BoundUserInterfaceMessage
{
    public string AId;
    public string BId;

    public SWEconomyAddRouteMessage(string aId, string bId)
    {
        AId = aId;
        BId = bId;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyRemoveRouteMessage : BoundUserInterfaceMessage
{
    public int RouteId;

    public SWEconomyRemoveRouteMessage(int routeId)
    {
        RouteId = routeId;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyDeclareStimulusMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWEconomySelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWEconomySelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWEconomyApproveStimulusMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWEconomyRejectStimulusMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWFinanceSetRateMessage : BoundUserInterfaceMessage
{
    public int Rate;

    public SWFinanceSetRateMessage(int rate)
    {
        Rate = rate;
    }
}

[Serializable, NetSerializable]
public sealed class SWFinanceSetTaxMessage : BoundUserInterfaceMessage
{
    public int Level;

    public SWFinanceSetTaxMessage(int level)
    {
        Level = level;
    }
}

[Serializable, NetSerializable]
public sealed class SWFinanceIssueBondsMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWFinanceRepayDebtMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWFinanceEmissionMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWFinanceSelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWFinanceSelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWFinanceApproveEmissionMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWFinanceRejectEmissionMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWInfectionSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Infection;
    public string StatusLabel = string.Empty;
    public Color StatusColor;
    public bool Quarantined;
    public int HospitalLevel;
    public bool Building;
    public int BuildLeft;
    public bool CanBuild;
    public int BuildCost;
    public bool CanAid;
    public int AidCost;
}

[Serializable, NetSerializable]
public sealed class SWHealthSetFundingMessage : BoundUserInterfaceMessage
{
    public int Level;

    public SWHealthSetFundingMessage(int level)
    {
        Level = level;
    }
}

[Serializable, NetSerializable]
public sealed class SWHealthDeployVaccineMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWHealthQuarantineMessage : BoundUserInterfaceMessage
{
    public string SectorId;
    public bool On;

    public SWHealthQuarantineMessage(string sectorId, bool on)
    {
        SectorId = sectorId;
        On = on;
    }
}

[Serializable, NetSerializable]
public sealed class SWHealthBuildHospitalMessage : BoundUserInterfaceMessage
{
    public string SectorId;

    public SWHealthBuildHospitalMessage(string sectorId)
    {
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public sealed class SWHealthEmergencyAidMessage : BoundUserInterfaceMessage
{
    public string SectorId;

    public SWHealthEmergencyAidMessage(string sectorId)
    {
        SectorId = sectorId;
    }
}

[Serializable, NetSerializable]
public sealed class SWHealthDeclareEmergencyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWHealthSelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWHealthSelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWHealthApproveEmergencyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWHealthRejectEmergencyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWCellEntry
{
    public int Id;
    public SWCellState State;
    public string LocationLabel = string.Empty;
    public string? SectorId;
    public bool Burned;
    public int RebuildLeft;
}

[Serializable, NetSerializable]
public sealed class SWCellSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Order;
    public int Cells;
    public bool HasPlot;
    public bool PlotRevealed;
    public string PlotLabel = string.Empty;
    public Color PlotColor;
}

[Serializable, NetSerializable]
public sealed class SWPlotEntry
{
    public int Id;
    public string SectorId = string.Empty;
    public string SectorName = string.Empty;
    public string TypeLabel = string.Empty;
    public Color TypeColor;
    public int Maturity;
    public bool Revealed;
    public int Cells;
    public int SuccessChance;
    public bool CanRaid;
    public string ThreatLabel = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWSecuritySetPostureMessage : BoundUserInterfaceMessage
{
    public SWSecurityPosture Posture;

    public SWSecuritySetPostureMessage(SWSecurityPosture posture)
    {
        Posture = posture;
    }
}

[Serializable, NetSerializable]
public sealed class SWSecuritySelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWSecuritySelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSecurityDeployCellMessage : BoundUserInterfaceMessage
{
    public int CellId;
    public string Target;

    public SWSecurityDeployCellMessage(int cellId, string target)
    {
        CellId = cellId;
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWSecurityLaunchOpMessage : BoundUserInterfaceMessage
{
    public string OpId;
    public string Target;

    public SWSecurityLaunchOpMessage(string opId, string target)
    {
        OpId = opId;
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWSecurityRaidPlotMessage : BoundUserInterfaceMessage
{
    public int PlotId;

    public SWSecurityRaidPlotMessage(int plotId)
    {
        PlotId = plotId;
    }
}

[Serializable, NetSerializable]
public sealed class SWSecurityApproveEmergencyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSecurityRejectEmergencyMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWDefenseOperationEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int Cost;
    public int AdminCost;
    public int Duration;
    public SWOpTarget Target;
    public bool Available;
    public string StatusLabel = string.Empty;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWActiveOpEntry
{
    public string Name = string.Empty;
    public string Detail = string.Empty;
    public int HoursLeft;
}

[Serializable, NetSerializable]
public sealed class SWDefenseLaunchOpMessage : BoundUserInterfaceMessage
{
    public string OpId;
    public string Target;

    public SWDefenseLaunchOpMessage(string opId, string target)
    {
        OpId = opId;
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWFleetGroupEntry
{
    public int Id;
    public SWFleetState State;
    public string LocationLabel = string.Empty;
    public string? SectorId;
    public bool Damaged;
    public int RepairLeft;
}

[Serializable, NetSerializable]
public sealed class SWFleetSectorEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public Color Color;
    public int Defense;
    public bool Shipyard;
}

[Serializable, NetSerializable]
public sealed class SWFleetDeployMessage : BoundUserInterfaceMessage
{
    public int GroupId;
    public string Target;

    public SWFleetDeployMessage(int groupId, string target)
    {
        GroupId = groupId;
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class SWDefenseProgramEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public bool Selected;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWMinistryResignMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWDefenseSetStanceMessage : BoundUserInterfaceMessage
{
    public SWDefenseStance Stance;

    public SWDefenseSetStanceMessage(SWDefenseStance stance)
    {
        Stance = stance;
    }
}

[Serializable, NetSerializable]
public sealed class SWDefenseSelectProgramMessage : BoundUserInterfaceMessage
{
    public string ProgramId;

    public SWDefenseSelectProgramMessage(string programId)
    {
        ProgramId = programId;
    }
}

[Serializable, NetSerializable]
public sealed class SWMinistryRequestOptionEntry
{
    public string Label = string.Empty;
    public string Description = string.Empty;
    public int Cost;
    public bool Affordable = true;
    public List<SWBillEffectEntry> Effects = new();
}

[Serializable, NetSerializable]
public sealed class SWMinistryRequestEntry
{
    public int Id;
    public string ProblemName = string.Empty;
    public string ProblemDescription = string.Empty;
    public string OwnerLabel = string.Empty;
    public string DeadlineLabel = string.Empty;
    public bool Audited;
    public bool AuditInProgress;
    public string AuditLabel = string.Empty;
    public string IssueDescription = string.Empty;
    public bool CanAudit;
    public List<SWMinistryRequestOptionEntry> Options = new();
    public List<SWBillEffectEntry> SideEffects = new();
    public string FailText = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWMinistryAuditRequestMessage : BoundUserInterfaceMessage
{
    public int RequestId;

    public SWMinistryAuditRequestMessage(int requestId)
    {
        RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class SWMinistryResolveRequestMessage : BoundUserInterfaceMessage
{
    public int RequestId;
    public int OptionIndex;

    public SWMinistryResolveRequestMessage(int requestId, int optionIndex)
    {
        RequestId = requestId;
        OptionIndex = optionIndex;
    }
}

[Serializable, NetSerializable]
public sealed class SWMinistryRefuseRequestMessage : BoundUserInterfaceMessage
{
    public int RequestId;
    public string Reason;

    public SWMinistryRefuseRequestMessage(int requestId, string reason)
    {
        RequestId = requestId;
        Reason = reason;
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
    public bool CanScheduleSessions;
    public bool CanConfirmSessions;
    public bool OfficesVacant;
    public bool WarProposed;
    public bool CanApproveWar;
    public string PendingStanceName = string.Empty;
    public bool EmergencyProposed;
    public bool CanApproveEmergency;
    public string PendingPostureName = string.Empty;
    public bool HealthEmergencyProposed;
    public bool CanApproveHealthEmergency;
    public bool StimulusProposed;
    public bool CanApproveStimulus;
    public bool EmissionProposed;
    public bool CanApproveEmission;
    public bool TribunalProposed;
    public bool CanApproveTribunal;
    public List<SWBillPresetEntry> Presets = new();
    public bool CanDilemmas;
    public List<string> MinistryNotices = new();
    public List<SWDilemmaEntry> MyDilemmas = new();
    public List<SWDilemmaEntry> AllDilemmas = new();
    public List<SWDilemmaEntry> MyBudgetDilemmas = new();
    public bool IsChancellorView;
    public bool BudgetLocked;
    public int BudgetTotal;
    public int BudgetReserve;
    public int MyBudgetAvailable;
    public List<SWBudgetSectorEntry> BudgetSectors = new();
    public bool BudgetProjectPending;
    public int BudgetSectorPool;
    public int BudgetMinistryPool;
    public int BudgetSectorPct;
    public int BudgetRejects;
    public bool CanRejectBudget;
    public bool CanApproveBudget;
    public List<SWBudgetShareEntry> BudgetMinistrySplit = new();
    public bool CanPrograms;
    public List<SWProgramEntry> Programs = new();
    public bool CanAmbitions;
    public List<SWAmbitionEntry> Ambitions = new();
    public List<SWCrisisEntry> Crises = new();
    public bool CanParties;
    public int MyPartyId = -1;
    public List<SWPartyEntry> Parties = new();
    public List<SWPartyLawEntry> PartyLaws = new();
    public bool HasPersonal;
    public int MyCorruption;
    public int MyAccount;
    public List<SWBillEntry> LawsAwaitingSignature = new();
    public bool CanSignLaws;

    // ПОЛНОМОЧИЯ (Chancellor / acting officer view).
    public bool ShowPowers;
    public bool PowersActive;
    public int EmergencyLevel;
    public string EmergencyLevelName = string.Empty;
    public int Authority;
    public int Legitimacy;
    public int EmergencyDaysLeft;
    public bool CanRelinquish;
    public bool HasVeto;
    public bool HasCommand;
    public List<int> AvailablePowers = new();
    public bool CanProposeMotions;
    public List<SWMotionEntry> Motions = new();
}

[Serializable, NetSerializable]
public enum SWMotionType : byte
{
    GrantLevel,
    GrantPower,
    NoConfidence,
}

[Serializable, NetSerializable]
public sealed class SWMotionEntry
{
    public int Id;
    public string Label = string.Empty;
    public int Yes;
    public int No;
    public int Needed;
    public int Electorate;
    public bool Voted;
    public bool Support;
    public bool CanVote;
}

[Serializable, NetSerializable]
public sealed class SWProposeMotionMessage : BoundUserInterfaceMessage
{
    public SWMotionType Type;
    public int Level;
    public SWEmergencyPower Power;

    public SWProposeMotionMessage(SWMotionType type, int level, SWEmergencyPower power)
    {
        Type = type;
        Level = level;
        Power = power;
    }
}

[Serializable, NetSerializable]
public sealed class SWVoteMotionMessage : BoundUserInterfaceMessage
{
    public int MotionId;
    public bool Support;

    public SWVoteMotionMessage(int motionId, bool support)
    {
        MotionId = motionId;
        Support = support;
    }
}

[Serializable, NetSerializable]
public sealed class SWEmergencyRelinquishMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWEmergencyCommandMessage : BoundUserInterfaceMessage
{
    public int Action;

    public SWEmergencyCommandMessage(int action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public sealed class SWSenateVetoBillMessage : BoundUserInterfaceMessage
{
    public int BillId;

    public SWSenateVetoBillMessage(int billId)
    {
        BillId = billId;
    }
}

[Serializable, NetSerializable]
public sealed class SWPartyLawEntry
{
    public string PartyName = string.Empty;
    public Color PartyColor;
    public string BillName = string.Empty;
    public string ChoiceLabel = string.Empty;
    public Color ChoiceColor;
    public string ResultLabel = string.Empty;
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
    public int Cohesion;
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
    public string MinistryName = string.Empty;
    public bool MinistryAvailable;
    public bool Corrupt;
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
public sealed class SWSenateSignBillMessage : BoundUserInterfaceMessage
{
    public int BillId;

    public SWSenateSignBillMessage(int billId)
    {
        BillId = billId;
    }
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
    public bool CanPartyWhip;
    public string PartyWhipLabel = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWSenatePartyWhipMessage : BoundUserInterfaceMessage
{
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
    public bool Free;
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
public sealed class SWDefenseApproveWarMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWDefenseRejectWarMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateTakeOfficeMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class SWSenateResignMessage : BoundUserInterfaceMessage
{
}
