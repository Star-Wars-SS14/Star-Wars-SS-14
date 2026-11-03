using Content.Shared.DoAfter;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public sealed record SWGlobalWarHubCommand(Guid Generation, Guid Request, long AssignmentRevision,
    int ExpectedCapacity, Guid MoneyOrder = default, Guid WorkGrant = default, bool HeadDecision = false);

[Serializable, NetSerializable]
public sealed record SWGlobalWarHubGrantCommand(Guid Generation, Guid Request, long AssignmentRevision,
    NetUserId Employee, long EmployeeRevision, Guid MoneyOrder, int MaximumCapacity, TimeSpan Duration,
    bool HeadDecision = false);

[Serializable, NetSerializable]
public enum SWGlobalWarHubStatus : byte
{
    Working,
    Completed,
    Cancelled,
}

[Serializable, NetSerializable]
public enum SWGlobalWarHubFundingState : byte
{
    Reserved,
    Spent,
    DirectReleased,
    OrderAvailable,
    ParentClosed,
    ParentUnavailable,
    CleanupPending,
}

[Serializable, NetSerializable]
public enum SWGlobalWarHubFailure : byte
{
    None,
    Denied,
    InvalidRequest,
    Unavailable,
    OutOfRange,
    Busy,
    CapacityChanged,
    MaximumCapacity,
    Funding,
    InsufficientDuration,
    AuthorityChanged,
    OrbitChanged,
    Interrupted,
    CancelledByUser,
    CampaignEnded,
}

/// <summary>Only the authorized executor or their current logistics authority receives financial work details.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarHubResult(Guid Request, SWGlobalWarHubStatus Status, SWGlobalWarHubFailure Failure,
    int PreviousCapacity, int Capacity, long Price, TimeSpan Started, TimeSpan Finishes, bool FundingReleased,
    Guid MoneyOrder = default, bool Direct = false, string Purpose = "", TimeSpan? MoneyExpires = null,
    SWGlobalWarHubFundingState FundingState = SWGlobalWarHubFundingState.Reserved);

/// <summary>The built-in physical progress bar is independent of the work window.</summary>
[Serializable, NetSerializable]
public sealed partial class SWGlobalWarHubDoAfterEvent : SimpleDoAfterEvent;
