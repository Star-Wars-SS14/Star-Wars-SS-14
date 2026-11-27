using Robust.Shared.Serialization;
using System.Collections.Generic;

namespace Content.Shared._SW.Economy;

[Serializable, NetSerializable]
public enum SWSpireMonitorUiKey : byte
{
    Key,
}

/// <summary>
/// One spire row. Carries everything the detail panel needs so row selection
/// stays client-local; this message only pings the server for the UI beep.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWSpireMonitorRowData
{
    public string SpireName = string.Empty;
    public string OwnerFaction = string.Empty;
    public string OwnerName = string.Empty;
    public string CapturingName = string.Empty;
    public bool IsCapturing;
    public float CaptureProgress;
    public float PayoutProgress;
    public float CaptureRemainingSeconds;
    public float PayoutRemainingSeconds;
}

[Serializable, NetSerializable]
public sealed class SWSpireMonitorBoundUserInterfaceState : BoundUserInterfaceState
{
    public List<SWSpireMonitorRowData> Rows = new();
}

/// <summary>
/// Server-side ping so row selection plays the standard console beep.
/// </summary>
[Serializable, NetSerializable]
public sealed class SWSpireMonitorSelectMessage : BoundUserInterfaceMessage
{
}