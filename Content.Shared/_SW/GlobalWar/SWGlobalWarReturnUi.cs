using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarReturnAction : byte { Redirect, Grant, Revoke }

[Serializable, NetSerializable]
public enum SWGlobalWarReturnFailure : byte
{
    None, Denied, InvalidRequest, Changed, InTransit, NotOwner, InvalidDestination, NoSpace, NoRoute,
    AlreadyReturning, HeadDecisionRequired, GrantRevoked, GrantExpired, GrantUsed, AuthorityChanged,
    NoPart, NoDestination, NoGrant, NoWorker, InvalidDuration, WorkerChanged,
}

/// <summary>Exact physical part; quantity is in market quanta, never rounded display units.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnSelection(Guid Shipment, Guid Batch, SWGlobalWarFaction Owner,
    string Node, int Quantity, string OldDestination, Guid? PreviousOperation, Guid DestinationRevision = default);

[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnCommand(Guid Generation, Guid Request, long AssignmentRevision,
    SWGlobalWarReturnAction Action, SWGlobalWarReturnSelection Part, string Destination,
    Guid Grant = default, NetUserId? Employee = null, long EmployeeRevision = 0,
    int Minutes = 10, bool HeadDecision = false, NetEntity? DestinationStore = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnDestination(string Planet, SWGlobalWarReturnFailure Failure,
    NetEntity? Store = null, string Name = "", bool AcceptsSupply = false);

[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnCandidate(SWGlobalWarReturnSelection Part, string Commodity,
    string HoldReason, SWGlobalWarReturnFailure Failure, List<SWGlobalWarReturnDestination> Destinations);

[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnGrantView(Guid Id, SWGlobalWarReturnSelection Part, string Destination,
    string Employee, TimeSpan Expires, SWGlobalWarReturnFailure Failure, NetEntity? DestinationStore = null,
    SWGlobalWarReturnTarget? Target = null);

/// <summary>The named physical destination and purpose authorized at the time of the operation.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnTarget(NetEntity Store, string Name, bool AcceptsSupply);

/// <summary>Historical own operation counts remain distinct from the current location of foreign cargo.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnReceiptView(Guid Operation, SWGlobalWarReturnSelection Part,
    string Destination, string Commodity, TimeSpan AcceptedAt, int Received, int Captured, int Redirected,
    SWGlobalWarReturnTarget? Target = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarReturnResult(Guid Request, SWGlobalWarReturnAction Action, bool Success,
    SWGlobalWarReturnFailure Failure, Guid Grant = default, SWGlobalWarReturnReceiptView? Receipt = null);

[Serializable, NetSerializable]
public sealed class SWGlobalWarReturnRequest(SWGlobalWarReturnCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarReturnCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarReturnReply(SWGlobalWarReturnResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarReturnResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}
