using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Empire;

[Serializable, NetSerializable] public enum SWEmpireDiplomacyTerm : byte { Transit, Supply, Basing, LocalCeasefire }
[Serializable, NetSerializable] public enum SWEmpireDiplomacyAction : byte { Propose, ShipCargo }
[Serializable, NetSerializable] public enum SWEmpireDiplomacyParty : byte { Republic, Neutral }
[Serializable, NetSerializable] public enum SWEmpireDiplomacyScope : byte { World, Sector, Link }
[Serializable, NetSerializable] public enum SWEmpireDiplomacyStatus : byte { AwaitingApproval, Negotiating, Active, Completed, Expired, Broken, Rejected }

[Serializable, NetSerializable]
public sealed class SWEmpireDiplomacyRequest
{
    public string RequestId = string.Empty, TargetId = string.Empty, Good = string.Empty, SourceWorld = string.Empty, DestinationWorld = string.Empty;
    public SWEmpireDiplomacyTerm Term;
    public SWEmpireDiplomacyAction Action = SWEmpireDiplomacyAction.Propose;
    public SWEmpireDiplomacyParty Party;
    public SWEmpireDiplomacyScope Scope;
    public int Hours = 12;
    public double Amount;
    public List<string> RouteWorlds = new(), RouteLinks = new();
}
[Serializable, NetSerializable]
public sealed class SWEmpireDiplomacyQuote
{
    public bool Allowed, NeedsTransit, NegotiationUnsupported;
    public SWEmpireDiplomacyParty RequiredParty;
    public List<string> MissingTransitLinks = new();
    public string Reason = string.Empty;
    public int Cost;
    public SWEmpireDiplomacyRequest Request = new();
}
[Serializable, NetSerializable]
public sealed class SWEmpireDiplomacyAgreementView
{
    public int Id, Cost;
    public SWEmpireDiplomacyRequest Terms = new();
    public SWEmpireDiplomacyStatus Status;
    public double CreatedAt, StartsAt, EndsAt;
    public string Reason = string.Empty;
}
[Serializable, NetSerializable]
public sealed class SWEmpireDiplomacyState
{
    public double NowHours;
    public bool ClockRunning;
    public List<SWEmpireDiplomacyAgreementView> Agreements = new();
    public List<SWEmpireDiplomacyCargoView> Cargo = new();
}

[Serializable, NetSerializable] public enum SWEmpireDiplomacyDecision : byte { Approve, Reject, Break }
[Serializable, NetSerializable] public sealed class SWEmpireDiplomacyQuoteMessage(SWEmpireDiplomacyRequest request) : BoundUserInterfaceMessage { public readonly SWEmpireDiplomacyRequest Request = request; }
[Serializable, NetSerializable] public sealed class SWEmpireDiplomacyCommandMessage(SWEmpireDiplomacyRequest request) : BoundUserInterfaceMessage { public readonly SWEmpireDiplomacyRequest Request = request; }
[Serializable, NetSerializable] public sealed class SWEmpireDiplomacyBreakMessage(int id) : BoundUserInterfaceMessage { public readonly int Id = id; }
[Serializable, NetSerializable] public sealed class SWEmpireDiplomacyDecisionMessage(int id, SWEmpireDiplomacyDecision decision) : BoundUserInterfaceMessage { public readonly int Id = id; public readonly SWEmpireDiplomacyDecision Decision = decision; }

[Serializable, NetSerializable] public enum SWEmpireDiplomacyCargoPhase : byte { AwaitingApproval, DebitPending, InTransit, Paused, DepositPending, Returning, Completed, Rejected }
[Serializable, NetSerializable] public sealed class SWEmpireDiplomacyCargoView
{
    public int AgreementId;
    public string Id = string.Empty, Good = string.Empty, SourceWorld = string.Empty, DestinationWorld = string.Empty, CurrentWorld = string.Empty, NextWorld = string.Empty, Reason = string.Empty;
    public double Amount, Delivered, EtaHours, LegStartedAt, LegEndsAt;
    public SWEmpireDiplomacyCargoPhase Phase;
    public List<string> Route = new();
}

