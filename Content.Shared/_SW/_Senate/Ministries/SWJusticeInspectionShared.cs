using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable] public enum SWJusticeInstitutionKind : byte { Medical, Industry, Port, Utilities, Procurement, Administration }
[Serializable, NetSerializable] public enum SWJusticeConclusion : byte { NoViolation, Negligence, IntentionalViolation }
[Serializable, NetSerializable] public enum SWJusticeRemedy : byte { Close, CorrectRecords, Restitution, RestrictOperation }
[Serializable, NetSerializable] public enum SWJusticeInspectionStage : byte { Inspecting, Enforcement, Closed }

[Serializable, NetSerializable] public sealed class SWJusticeInspectionState
{
    public double NowHours;
    public double RealSecondsPerHour = 240;
    public bool ClockRunning, MinisterActive;
    public int Budget, CaseCap, CheckCap, BusyChecks;
    public int Workload;
    public string WorkloadStatus = string.Empty;
    public List<SWJusticeWorldView> Worlds = new();
    public List<SWJusticeInstitutionView> Institutions = new();
    public List<SWJusticeCaseView> ActiveCases = new();
    public List<SWJusticeCaseView> Archive = new();
    public List<string> Log = new();
    public List<SWJusticeLogEntry> Journal = new();
}
[Serializable, NetSerializable] public sealed class SWJusticeLogEntry
{
    public int Sequence, CaseId, Day, Hour, Minute;
    // Elapsed active ministerial working hours, not the wall clock or a calendar date.
    public double ActiveHours;
    public string CaseTitle = string.Empty, InstitutionName = string.Empty, WorldName = string.Empty, Message = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeWorldView
{
    public string Id = string.Empty, Name = string.Empty, SectorId = string.Empty, SectorName = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeInstitutionView
{
    public string Id = string.Empty, Name = string.Empty, WorldId = string.Empty, WorldName = string.Empty, SectorId = string.Empty, SectorName = string.Empty, KindName = string.Empty;
    public SWJusticeInstitutionKind Kind;
    public bool Available;
    public string Reason = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeEvidenceView
{
    public string CheckId = string.Empty, Title = string.Empty, Text = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeCheckView
{
    public string Id = string.Empty, Title = string.Empty, Reason = string.Empty;
    public int Cost;
    public double Hours, EtaHours;
    public bool Available, Pending, Completed;
}
[Serializable, NetSerializable] public sealed class SWJusticeCaseView
{
    public int Id;
    public string Title = string.Empty, Briefing = string.Empty, InstitutionId = string.Empty, InstitutionName = string.Empty, WorldId = string.Empty, WorldName = string.Empty, SectorId = string.Empty, SectorName = string.Empty;
    public SWJusticeInspectionStage Stage;
    public SWJusticeConclusion? SelectedConclusion;
    public SWJusticeRemedy? SelectedRemedy;
    public List<SWJusticeEvidenceView> Evidence = new();
    public List<string> CitedEvidenceIds = new();
    public List<SWJusticeCheckView> Checks = new();
    public List<SWJusticeRemedy> AvailableRemedies = new();
    public List<SWJusticeRemedyOptionView> RemedyOptions = new();
    public bool CanConclude, CanFollowUp;
    public double RemedyEtaHours;
    // Filled only after closure.
    public SWJusticeConclusion? ActualConclusion;
    public string Explanation = string.Empty, Result = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeRemedyOptionView
{
    public SWJusticeRemedy Remedy;
    public int Cost;
    public double Hours;
    public bool Available;
    public string Reason = string.Empty;
}
[Serializable, NetSerializable] public sealed class SWJusticeOpenInspectionMessage(string institutionId) : BoundUserInterfaceMessage { public string InstitutionId = institutionId; }
[Serializable, NetSerializable] public sealed class SWJusticeRunCheckMessage(int caseId, string checkId) : BoundUserInterfaceMessage { public int CaseId = caseId; public string CheckId = checkId; }
[Serializable, NetSerializable] public sealed class SWJusticeConcludeMessage(int caseId, SWJusticeConclusion conclusion, List<string> evidenceIds) : BoundUserInterfaceMessage { public int CaseId = caseId; public SWJusticeConclusion Conclusion = conclusion; public List<string> EvidenceIds = evidenceIds; }
[Serializable, NetSerializable] public sealed class SWJusticeRemedyMessage(int caseId, SWJusticeRemedy remedy) : BoundUserInterfaceMessage { public int CaseId = caseId; public SWJusticeRemedy Remedy = remedy; }
[Serializable, NetSerializable] public sealed class SWJusticeFollowUpMessage(int caseId) : BoundUserInterfaceMessage { public int CaseId = caseId; }
