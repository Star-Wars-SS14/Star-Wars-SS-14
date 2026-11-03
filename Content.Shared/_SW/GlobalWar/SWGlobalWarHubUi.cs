using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarHubUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed record SWGlobalWarHubGrantView(Guid Id, Guid MoneyOrder, string Employee, int MaximumCapacity,
    TimeSpan Expires, TimeSpan? MoneyExpires, long Remaining, string Purpose, bool Revoked, SWGlobalWarHubFailure Failure);

[Serializable, NetSerializable]
public sealed record SWGlobalWarHubOrderView(Guid Id, NetUserId Employee, long EmployeeRevision,
    string Name, long Remaining, TimeSpan Expires, string Purpose);

[Serializable, NetSerializable]
public sealed record SWGlobalWarHubView(bool Ready, Guid Generation, Guid Assignment, long AssignmentRevision,
    TimeSpan Time, string Planet, SWGlobalWarFaction Orbit, SWGlobalWarFaction Surface,
    int Capacity, int NextCapacity, long Price, bool CanManage, bool Head, bool HeadDecisionRequired, SWGlobalWarHubFailure Failure,
    string Executor, SWGlobalWarHubResult? Active, List<SWGlobalWarHubResult> OwnResults,
    List<SWGlobalWarHubGrantView> Grants, List<SWGlobalWarHubOrderView> Orders);

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubSnapshot(SWGlobalWarHubView view, NetEntity viewer, Guid subscription, long sequence)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarHubView View = view;
    public readonly NetEntity Viewer = viewer;
    public readonly Guid Subscription = subscription;
    public readonly long Sequence = sequence;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubStartRequest(SWGlobalWarHubCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarHubCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubCancelRequest(Guid generation, long revision, Guid attempt, Guid subscription)
    : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly Guid Attempt = attempt;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubGrantRequest(SWGlobalWarHubGrantCommand command, Guid subscription)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarHubGrantCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubRevokeRequest(Guid generation, Guid request, long revision, Guid grant,
    bool headDecision, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly Guid Request = request;
    public readonly long Revision = revision;
    public readonly Guid Grant = grant;
    public readonly bool HeadDecision = headDecision;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarHubReply(Guid request, bool success, SWGlobalWarHubFailure failure,
    SWGlobalWarHubResult? result, NetEntity viewer, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Request = request;
    public readonly bool Success = success;
    public readonly SWGlobalWarHubFailure Failure = failure;
    public readonly SWGlobalWarHubResult? Result = result;
    public readonly NetEntity Viewer = viewer;
    public readonly Guid Subscription = subscription;
}
