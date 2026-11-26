using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Ministries;

[Serializable, NetSerializable]
public sealed class SWDefensePlanetaryState
{
    public bool CanManage;
    public int Budget;
    public List<SWDefensePlanetaryWorldView> Worlds = new();
    public List<SWDefenseSupportRequestView> Requests = new();
}

[Serializable, NetSerializable]
public sealed class SWDefensePlanetaryWorldView
{
    public string WorldId = string.Empty;
    public string Name = string.Empty;
    public string SectorId = string.Empty;
    public string SectorName = string.Empty;
    public string Governor = string.Empty;
    public string ProtectedSite = string.Empty;
    // Fractions of operational capacity, 0..1, including any delivered emergency reserve.
    public float GarrisonReadiness;
    public float OrbitalReadiness;
    public int ActiveThreats;
    public List<SWProgramEntry> Programs = new();
    public bool CanResupply;
    public int ResupplyCost;
    public float ReserveHours;
    public string ResupplyStatus = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWDefenseSupportRequestView
{
    public string Id = string.Empty;
    public string WorldId = string.Empty;
    public string Governor = string.Empty;
    public string Title = string.Empty;
    public string Reason = string.Empty;
    public string ProgramId = string.Empty;
    public int Priority;
    public string Status = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWDefenseWorldProgramMessage(string worldId, string programId) : BoundUserInterfaceMessage
{
    public string WorldId = worldId;
    public string ProgramId = programId;
}

[Serializable, NetSerializable]
public sealed class SWDefenseWorldResupplyMessage(string worldId) : BoundUserInterfaceMessage
{
    public string WorldId = worldId;
}
