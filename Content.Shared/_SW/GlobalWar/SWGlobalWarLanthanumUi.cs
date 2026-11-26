using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarLanthanumPhase : byte
{
    Disabled, Waiting, Starting, Releasing, NoRecipient, NoDeposit, Exhausted, WaitingFunds, WaitingSpace,
}

[Serializable, NetSerializable]
public enum SWGlobalWarLanthanumWaitingReason : byte
{
    None, EnclaveAvailable, StockSufficient, NoEnclave,
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarLanthanumDepositView(string Planet, int Remaining, int Released,
    SWGlobalWarLanthanumPhase Phase);

[Serializable, NetSerializable]
public sealed record SWGlobalWarLanthanumView(long Revision, bool Automatic, SWGlobalWarLanthanumPhase Phase,
    TimeSpan? ReadyAt, decimal Stock, decimal Demand, decimal CivilElectronics, decimal EmergencyT2,
    List<SWGlobalWarLanthanumDepositView> Deposits, SWGlobalWarLanthanumWaitingReason WaitingReason, decimal Incoming);

[Serializable, NetSerializable]
public sealed record SWGlobalWarLanthanumCommand(Guid Generation, Guid Request, long AssignmentRevision,
    long ExpectedRevision, bool Automatic);

/// <summary>Reason is a localization key naming why a refused decision failed; empty when unknown or accepted.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarLanthanumResult(Guid Request, bool Success, bool Conflict, long Revision,
    string Reason = "");

[Serializable, NetSerializable]
public sealed class SWGlobalWarLanthanumRequest(SWGlobalWarLanthanumCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarLanthanumCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarLanthanumReply(SWGlobalWarLanthanumResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarLanthanumResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}
