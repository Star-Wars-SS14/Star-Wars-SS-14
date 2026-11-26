using System;
using System.Collections.Generic;
using Content.Shared._SW._Senate.Economy;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Empire;

[Serializable, NetSerializable]
public enum SWEmpireCouncilSphere : byte { None, ProductionLogistics, MilitaryOffense, Intelligence, ExpansionDiplomacy }
[Serializable, NetSerializable]
public enum SWEmpireIndustryAction : byte { UpgradeLine, CreateLine, ExpandRoute, Ship, Concentrate, AcceptMaterial }
[Serializable, NetSerializable]
public enum SWEmpireCouncilConsoleUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class SWEmpireIndustryRequest
{
    public SWEmpireIndustryAction Action;
    public string RequestId = string.Empty, World = string.Empty, Recipe = string.Empty, Link = string.Empty;
    public string Source = string.Empty, Target = string.Empty, Good = string.Empty;
    public double Amount, DeadlineHours;
    public bool Urgent;
}

[Serializable, NetSerializable]
public sealed class SWEmpireIndustryProjectView
{
    public string Id = string.Empty, World = string.Empty, Good = string.Empty, Status = string.Empty, Reason = string.Empty;
    public string Source = string.Empty, Target = string.Empty, Recipe = string.Empty, Link = string.Empty;
    public SWEmpireIndustryAction Action;
    public SWEmpireCouncilSphere SourceSphere;
    public double Amount, Delivered, Sent, InTransit, DeadlineHours, WorkedHours, WorkHours, AddedRate;
    public double? EtaHours;
    public int Cost;
    public bool Urgent, AwaitingSource, ExpiredUnaccepted, RejectedUnaccepted;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMaterialRequest
{
    public string RequestId = string.Empty, World = string.Empty, Good = string.Empty;
    public double Amount, DeadlineHours;
    public bool Urgent;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMaterialView
{
    public string Good = string.Empty;
    public double Quantity;
}

[Serializable, NetSerializable]
public sealed class SWEmpireIndustryQuote
{
    public int Cost;
    public List<SWEmpireMaterialView> Materials = new();
    public double WorkHours, AddedRate, Power, Labor;
    public double? EtaHours;
    public string Reason = string.Empty;
    public bool Allowed;
}

[Serializable, NetSerializable]
public sealed class SWEmpireCouncilConsoleState : BoundUserInterfaceState
{
    public SWEmpireCouncilSphere Sphere;
    public long Treasury;
    public long Allocated, TotalFunds;
    public int DailyAllocation, NextAllocationDay;
    public Dictionary<SWEmpireCouncilSphere, int> Budgets = new();
    public Dictionary<SWEmpireCouncilSphere, long> Spent = new(), Received = new();
    public List<SWEmpireIndustryProjectView> Projects = new();
    public SWEconomyView Economy = new() { ViewerFaction = "empire" };
    public string Result = string.Empty;
    public string ResultRequestId = string.Empty;
    public bool ActionResultIsCommand;
    public int QuoteCost;
    public bool QuoteAllowed;
    public SWEmpireIndustryQuote Quote = new();
    public SWEmpireMilitaryState Military = new();
    public SWEmpireMilitaryQuote MilitaryQuote = new();
    public SWEmpireIntelligenceState Intelligence = new();
    public SWEmpireIntelligenceQuote IntelligenceQuote = new();
    public SWEmpireDiplomacyState Diplomacy = new();
    public SWEmpireDiplomacyQuote DiplomacyQuote = new();
    public double IncomePerHour;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryQuoteMessage(SWEmpireMilitaryRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireMilitaryRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryCommandMessage(SWEmpireMilitaryRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireMilitaryRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireIndustryQuoteMessage(SWEmpireIndustryRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireIndustryRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMaterialQuoteMessage(SWEmpireMaterialRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireMaterialRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMaterialCommandMessage(SWEmpireMaterialRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireMaterialRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMaterialRejectMessage(string requestId) : BoundUserInterfaceMessage
{
    public string RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed class SWEmpireCouncilUpdateMessage(SWEmpireCouncilConsoleState state) : BoundUserInterfaceMessage
{
    public SWEmpireCouncilConsoleState State = state;
}

[Serializable, NetSerializable]
public sealed class SWEmpireIndustryCommandMessage(SWEmpireIndustryRequest request) : BoundUserInterfaceMessage
{
    public SWEmpireIndustryRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class SWEmpireBudgetTransferMessage(SWEmpireCouncilSphere target, int amount, string requestId) : BoundUserInterfaceMessage
{
    public SWEmpireCouncilSphere Target = target;
    public int Amount = amount;
    public string RequestId = requestId;
}
