using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarGrowthAction : byte { Set, Grant, Revoke }

[Serializable, NetSerializable]
public enum SWGlobalWarGrowthFailure : byte
{
    None, Denied, InvalidRequest, Conflict, HeadDecisionRequired, GrantRevoked, GrantExpired,
    OutsideRange, AuthorityChanged, SurfaceChanged, CampaignInactive, AssignmentChanged, NoGrant,
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarGrowthCommand(Guid Generation, Guid Request, long AssignmentRevision,
    SWGlobalWarGrowthAction Action, string Planet, long ExpectedRevision = -1, int Percent = 10,
    Guid Grant = default, NetUserId? Employee = null, long EmployeeRevision = 0,
    int Minimum = 0, int Maximum = 25, int Minutes = 10, bool HeadDecision = false);

[Serializable, NetSerializable]
public sealed record SWGlobalWarGrowthView(string Planet, long Revision, int Percent);

[Serializable, NetSerializable]
public sealed record SWGlobalWarGrowthGrantView(Guid Id, string Planet, int Minimum, int Maximum,
    string Employee, TimeSpan Expires, bool Revoked, SWGlobalWarGrowthFailure Failure);

[Serializable, NetSerializable]
public sealed record SWGlobalWarGrowthResult(Guid Request, SWGlobalWarGrowthAction Action, bool Success,
    SWGlobalWarGrowthFailure Failure, SWGlobalWarGrowthView? View = null);

[Serializable, NetSerializable]
public sealed class SWGlobalWarGrowthRequest(SWGlobalWarGrowthCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarGrowthCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarGrowthReply(SWGlobalWarGrowthResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarGrowthResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}
