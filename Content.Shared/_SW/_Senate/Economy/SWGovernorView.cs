using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Public state of the governor assigned to a sector for the current round.</summary>
[Serializable, NetSerializable]
public sealed class SWGovernorView
{
    public string Sector = string.Empty;
    public string Name = string.Empty;
    public string Status = string.Empty;
    public string Priority = string.Empty;
    public string Project = string.Empty;
    public string World = string.Empty;
    public string WorldName = string.Empty;
    public string ProjectStatus = string.Empty;
    public List<string> Traits = new();
    public List<string> History = new();
    public int Budget;
    public int Reserve;
    public int RequestedCredits;
    public float ProjectProgress;
    public double LastDecisionHour;
    public bool HasRequest;
}
