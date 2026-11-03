using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyWorkAction : byte
{
    Claim, Collect, Accept, Release, Revoke,
}

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyWorkStatus : byte
{
    Assigned, Collected, Completed, Released, Revoked,
}

/// <summary>A delivery claim grants authority over exact physical items, never over the purchasing budget.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyWorkCommand(Guid Generation, Guid Request, long AssignmentRevision,
    SWGlobalWarSupplyWorkAction Action, Guid Target, int Quantity = 1, NetEntity? Carrier = null,
    bool HeadDecision = false);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyWorkResult(Guid Request, bool Success, SWGlobalWarSupplyFailure Failure,
    Guid Work = default);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyDeliveryView(Guid Purchase, SWGlobalWarSupplyKind Kind, bool Bacta,
    int Available, string Source, string Recipient, NetEntity? Carrier, int Number, string SourceName, string RecipientName);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyWorkView(Guid Id, SWGlobalWarSupplyKind Kind, bool Bacta, int Quantity,
    string Source, string Recipient, SWGlobalWarSupplyWorkStatus Status, bool Own, string Worker,
    Guid Purchase, int Number, string SourceName, string RecipientName, bool ReturnPending);

/// <summary>Contains published tasks and the viewer's work; no balances, prices, or unrelated inventory.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyCarrierView(NetEntity Entity, string Name);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyWorkBoard(List<SWGlobalWarSupplyDeliveryView> Deliveries,
    List<SWGlobalWarSupplyWorkView> Work, bool CanRevoke, bool HeadDecisionRequired, int Total, int Page,
    List<SWGlobalWarSupplyCarrierView> Carriers);

[Serializable, NetSerializable]
public sealed class SWGlobalWarSupplyWorkRequest(SWGlobalWarSupplyWorkCommand command, Guid subscription)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarSupplyWorkCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarSupplyWorkReply(SWGlobalWarSupplyWorkResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarSupplyWorkResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}
