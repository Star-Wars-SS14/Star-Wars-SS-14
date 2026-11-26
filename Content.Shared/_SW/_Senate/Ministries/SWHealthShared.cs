using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public enum SWHealthStage : byte { Samples, Analysis, Candidate, Trials, Production, Delivery }
[Serializable, NetSerializable]
public enum SWHealthAction : byte
{
    CollectSample, FocusAnalysis, SelectCandidate, StartTrials, ExtendTrials,
    OrderBatch, RequestQuarantine, LiftQuarantine, BuildClinic, SendAid, SetFunding, StartPrevention, StartPlannedCare,
}

[Serializable, NetSerializable]
public sealed class SWHealthActionMessage : BoundUserInterfaceMessage
{
    public SWHealthAction Action;
    public string WorldId;
    public string TargetWorldId;
    public int Value;
    public bool Option;
    public SWHealthActionMessage(SWHealthAction action, string worldId = "", string targetWorldId = "", int value = 0, bool option = false)
    { Action = action; WorldId = worldId; TargetWorldId = targetWorldId; Value = value; Option = option; }
}

[Serializable, NetSerializable]
public sealed class SWHealthQuarantineDecisionMessage : BoundUserInterfaceMessage
{
    public int Id;
    public bool Approve;
    public bool Lift;
    public SWHealthQuarantineDecisionMessage(int id, bool approve, bool lift = false)
    { Id = id; Approve = approve; Lift = lift; }
}

[Serializable, NetSerializable]
public sealed class SWHealthView
{
    public bool CanManage;
    public bool MinisterActive;
    public bool DayActive;
    public bool PathogenActive;
    public string PathogenName = "";
    public int Strain;
    public SWHealthResearchView Research = new();
    public List<SWHealthWorldView> Worlds = new();
    public List<SWHealthOrderView> Orders = new();
    public List<SWHealthQuarantineView> Quarantines = new();
    public List<string> Log = new();
    public int Budget;
    public int AidCost;
    public int BatchCost;
    public double ActiveHours;
    public SWHealthCareView Care = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthResearchView
{
    public SWHealthStage Stage;
    public float Progress;
    public int Funding;
    public int FundingPerHour;
    public int Focus;
    public int Candidate; // 0 targeted, 1 resilient
    public float TargetedEfficacy = 100f;
    public float ResilientEfficacy = 85f;
    public float TargetedMutationRetention = 62.5f;
    public float ResilientMutationRetention = 87f;
    public double TargetedTrialHours = 1.5;
    public double ResilientTrialHours = 2.5;
    public int TargetedBatchCost = 1200;
    public int ResilientBatchCost = 1600;
    public int Samples;
    public bool Extended;
    public bool ExtensionComplete;
    public bool Ready;
    public bool Stale;
    public bool TrialsRunning;
    public int TrialCost;
    public string TrialStatus = "";
    public List<SWHealthMaterialView> TrialMaterials = new();
    public int Strain;
    public string SourceWorldId = "";
    public string LaboratoryWorldId = "";
    public string Status = "";
    public double HoursLeft;
    public float Spread;
    public float Severity;
    public float Variability;
    public List<float> TrialTreated = new();
    public List<float> TrialControl = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthWorldView
{
    public string Id = "";
    public string Name = "";
    public string SectorId = "";
    public string SectorName = "";
    public string SystemId = "";
    public string SystemName = "";
    public float Infection;
    public float Immunity;
    public float PendingImmunity;
    public float ImmunityApplicationPerHour;
    public float ImmunitySuppressionPerHour;
    public string VaccinationStatus = "";
    public float BatchCoverage;
    public float Trend;
    public float CareCoverage;
    public float ClinicCoverage;
    public float ReserveHours;
    public double FoodHours;
    public double MedicineHours;
    public bool CanProduce;
    public bool HasProductionSite;
    public string ProductionStatus = "";
    public double ProductionRate;
    public double ProductionPowerAvailable;
    public double ProductionPowerRequired;
    public double ProductionLaborAvailable;
    public double ProductionLaborRequired;
    /// <summary>At least one world currently exceeds the public outbreak threshold.</summary>
    public bool HasOutbreakTarget;
    /// <summary>Open-link travel only; null with a target means no available route, zero means this world is an outbreak.</summary>
    public double? ClosestOutbreakTravelHours;
    public string ClosestOutbreakWorldId = "";
    public bool CanResearch;
    public bool Quarantined;
    public List<SWProgramEntry> Programs = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthQuarantineView
{
    public int Id;
    public string Label = "";
    public List<string> WorldIds = new();
    public bool Pending;
    public bool Humanitarian;
    public double HoursLeft;
    public string Consequences = "";
    public List<SWMapCrisisRouteEntry> Routes = new();
}

[Serializable, NetSerializable]
public sealed class SWHealthOrderView
{
    public string Id = "";
    public string Name = "";
    public string ProducerWorldId = "";
    public string TargetWorldId = "";
    public string Status = "";
    public float Progress;
    public int Strain;
    public bool Completed;
    public List<SWHealthMaterialView> Materials = new();
    public SWHealthMaterialView? PayloadDelivery;
}

[Serializable, NetSerializable]
public sealed class SWHealthMaterialView
{
    public string GoodId = "";
    public double RepublicStock;
    public bool StockKnown = true;
    public double? DeliveryHours;
    public double? MinimumTravelHours;
    public double? NextDeliveryHours;
    public double? NextMinimumTravelHours;
    public double NextDeliveryQuantity;
    public string DeliveryStatus = "waiting_materials";
    public bool Priority;
    public string Name = "";
    public double Required;
    public double Delivered;
    public double Incoming;
    public double QuantityTolerance;
}



