using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Politics;

[Serializable, NetSerializable]
public enum SWGlobalWarWithdrawalStatus : byte
{
    Countdown, Returning, WaitingForPoint, WaitingForSeparation, Departed, Returned, Cancelled, MayRemain,
}

[Serializable, NetSerializable]
public enum SWGlobalWarWithdrawalReason : byte { Truce, Integration }

/// <summary>Only this attachment's withdrawal and confirmed property outcome; no other passenger information.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarWithdrawalView(Guid Id, SWGlobalWarWithdrawalReason Reason,
    SWGlobalWarWithdrawalStatus Status, TimeSpan Remaining, string Sector, string? Destination,
    bool Active, bool ShipReturned, bool WarResumed);
