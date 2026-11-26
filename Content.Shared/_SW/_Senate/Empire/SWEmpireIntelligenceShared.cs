using Robust.Shared.Serialization;
namespace Content.Shared._SW._Senate.Empire;

[Serializable, NetSerializable] public enum SWEmpireIntelligenceAction : byte { Recon, EstablishNetwork, ProductionSabotage, TransitSabotage, Recall, Rest, Hire }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceRequest { public string RequestId = "", WorldId = "", TargetId = ""; public int GroupId; public SWEmpireIntelligenceAction Action; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceQuote { public bool Allowed; public string Reason = "", Result = "", Risk = ""; public int Cost; public double EtaHours, WorkHours; public float RiskPercent; public List<string> RouteWorldIds = new(); }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceGroupView { public int Id; public string Name = "", WorldId = "", WorldName = "", DestinationId = "", DestinationName = "", Status = ""; public float Readiness; public double EtaHours, LegStartedAt, LegEndsAt; public string NextWorldId = "", NextWorldName = ""; public List<string> Route = new(), RouteWorldIds = new(); }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceWorldView { public string Id = "", Name = "", Sector = "", Faction = ""; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceTargetView { public string Id = "", Name = ""; public SWEmpireIntelligenceAction Action; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceReportView { public string WorldId = "", WorldName = "", Summary = ""; public double Hour; public List<SWEmpireIntelligenceTargetView> Targets = new(); }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceNetworkView { public string WorldId = "", WorldName = ""; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceEffectView { public string WorldId = "", TargetName = "", Status = ""; public double RemainingHours; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceState { public int Revision; public double NowHours; public bool ClockRunning; public float RealSecondsPerHour; public List<SWEmpireIntelligenceGroupView> Groups = new(); public List<SWEmpireIntelligenceWorldView> Worlds = new(); public List<SWEmpireIntelligenceNetworkView> Networks = new(); public List<SWEmpireIntelligenceReportView> Reports = new(); public List<SWEmpireIntelligenceEffectView> Effects = new(); }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceQuoteMessage(SWEmpireIntelligenceRequest request) : BoundUserInterfaceMessage { public SWEmpireIntelligenceRequest Request = request; }
[Serializable, NetSerializable] public sealed class SWEmpireIntelligenceCommandMessage(SWEmpireIntelligenceRequest request) : BoundUserInterfaceMessage { public SWEmpireIntelligenceRequest Request = request; }
