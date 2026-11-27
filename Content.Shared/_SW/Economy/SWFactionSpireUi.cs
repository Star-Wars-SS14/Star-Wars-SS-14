using Robust.Shared.Serialization;

namespace Content.Shared._SW.Economy;

[Serializable, NetSerializable]
public enum SWFactionSpireUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWFactionSpireBoundUserInterfaceState : BoundUserInterfaceState
{
    public string OwnerFaction = string.Empty;
    public string OwnerName = string.Empty;
    public string CapturingName = string.Empty;
    public bool IsCapturing;
    public float CaptureProgress;
    public float PayoutProgress;
    public float CaptureRemainingSeconds;
    public float PayoutRemainingSeconds;
    public bool HasCard;
    public string CardName = string.Empty;
    public string CardJob = string.Empty;
    public string CardIcon = string.Empty;
}

[Serializable, NetSerializable]
public sealed class SWFactionSpireCaptureMessage : BoundUserInterfaceMessage
{
}

/// <summary>
/// Request to eject the inserted ID card. Allowed at any time, including during
/// a capture (that is the abort mechanism).
/// </summary>
[Serializable, NetSerializable]
public sealed class SWFactionSpireEjectMessage : BoundUserInterfaceMessage
{
}