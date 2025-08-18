using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

[NetSerializable, Serializable]
public enum SWTurretConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWTurretConsoleBounderUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<string, bool> JobsAllowed = new Dictionary<string, bool>();
    public Dictionary<string, string> AvailableFactions = new Dictionary<string, string>();
    public SWTurretConsoleBounderUserInterfaceState(Dictionary<string, bool> dict, Dictionary<string, string> availableFactions)
    {
        JobsAllowed = dict;
        AvailableFactions = availableFactions;
    }
}

[Serializable, NetSerializable]
public sealed class SWTurretJobToggleMessage : BoundUserInterfaceMessage
{
    public string Job;

    public SWTurretJobToggleMessage(string job)
    {
        Job = job;
    }
}

[Serializable, NetSerializable]
public sealed class SWDisallowFaction : BoundUserInterfaceMessage
{
    public string Faction;
    public SWDisallowFaction(string faction)
    {
        Faction = faction;
    }
}

[Serializable, NetSerializable]
public sealed class SWAllowFaction : BoundUserInterfaceMessage
{
    public string Faction;
    public SWAllowFaction(string faction)
    {
        Faction = faction;
    }
}

