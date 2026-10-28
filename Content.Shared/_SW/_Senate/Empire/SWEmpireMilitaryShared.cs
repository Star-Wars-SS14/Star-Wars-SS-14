using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Shared._SW._Senate.Empire;

[Serializable, NetSerializable]
public enum SWEmpireMilitaryAction : byte
{
    Move, Guard, Patrol, Recon, Escort, BorderConflict, Raid, Blockade, Return, Repair, Train, Recruit
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryRequest
{
    public SWEmpireMilitaryAction Action;
    public int FleetId;
    public string Target = string.Empty, RequestId = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryQuote
{
    public bool Allowed;
    public string Reason = string.Empty;
    public int Cost;
    public double? EtaHours;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryFleetView
{
    public int Id;
    public string Name = string.Empty, WorldId = string.Empty, NextWorldId = string.Empty, Target = string.Empty;
    public string Status = string.Empty, Phase = string.Empty;
    public SWEmpireMilitaryAction Order;
    public double Strength, Damage, Supply, Training, EtaHours, LegStartedAt, LegEndsAt;
    public string CargoGood = string.Empty, CargoGoodName = string.Empty, CargoStatus = string.Empty;
    public double CargoAmount, CargoCapacity, CargoUsedSlots;
    public double? CargoReadyAt;
    public List<string> Route = new();
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryOptionView
{
    public int FleetId, Cost;
    public SWEmpireMilitaryAction Action;
    public string Target = string.Empty, Reason = string.Empty, Risk = string.Empty;
    public bool Available;
    public double? EtaHours;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryObservationView
{
    public string SectorId = string.Empty, Summary = string.Empty;
    public double SeenHours;
    public bool Current;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryBorderView
{
    public string Id = string.Empty, Name = string.Empty, OwnWorldId = string.Empty, ForeignSectorId = string.Empty;
    public string Status = string.Empty, Warning = string.Empty;
    public double? ReadyAt;
}

[Serializable, NetSerializable]
public sealed class SWEmpireMilitaryState
{
    public double NowHours;
    public double SupplyHour;
    public bool ClockRunning;
    public List<SWEmpireMilitaryFleetView> Fleets = new();
    public List<SWEmpireMilitaryOptionView> Options = new();
    public List<SWEmpireMilitaryObservationView> Observations = new();
    public List<SWEmpireMilitaryBorderView> Borders = new();
    public List<SWDefenseSupplyFlowView> SupplyFlows = new();
}
