using Robust.Shared.Network;

namespace Content.Shared._SW._Senate.Planner;

public enum SWGuestRequestKind : byte { Press, Delegation }

// Raised on the initiating actor. No policy listener means denial.
// Authorized describes the actor's current office, independently of target connectivity.
// TargetUserId is the current appointed holder's stable id, even while disconnected;
// null means the office is vacant or its identity cannot be proven. Never substitute a name.
[ByRefEvent]
public record struct SWGuestRequestAuthorizeEvent(SWGuestRequestKind Kind, string TargetKey)
{
    public bool Authorized;
    public NetUserId? TargetUserId;
    public string? TargetDisplayName;
}

// Raised on the target actor on every read and response, not just at creation.
[ByRefEvent]
public record struct SWGuestRequestRespondAuthorizeEvent(SWGuestRequestKind Kind, string TargetKey, NetUserId ExpectedTargetUserId)
{
    public bool Authorized;
}

// Global policy: Unknown is the default and never deletes a meeting. Invalid must be
// backed by authoritative appointment data, not connection, body, or day/night status.
public enum SWGuestRequestValidity : byte { Unknown, Valid, Invalid }

[ByRefEvent]
public record struct SWGuestRequestValidateEvent(SWGuestRequestKind Kind, string TargetKey,
    NetUserId OwnerUserId, NetUserId ExpectedTargetUserId)
{
    public SWGuestRequestValidity Validity;
}
