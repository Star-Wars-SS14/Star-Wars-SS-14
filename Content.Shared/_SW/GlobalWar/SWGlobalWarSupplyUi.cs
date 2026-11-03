using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyKind : byte
{
    T2, T1, Armor, Magazine, Ration, Medicine, AdvancedElectronics,
}

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyFailure : byte
{
    None, Unavailable, LineBusy, Materials, OutputSpace, ReceiveSpace, Funds, Quota, Revision, LocalOnly, Rate, Transfer,
    ExistingSupply, Denied, ReturnPending, WorkBusy, QuantityChanged, InvalidQuantity, TransportRoute,
}

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyStage : byte
{
    Reserved, Producing, Ready, InTransit, Complete, Cancelled,
}

[Serializable, NetSerializable]
public enum SWGlobalWarSupplyAction : byte
{
    ConfigureLine, Purchase, CancelPurchase, ResumeIntent, CancelProduction, PreviewPurchase, ApproveArsenal,
}

/// <summary>Exact addressed intent. A finance reservation never supplies the caller's subject authority.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyCommand(Guid Generation, Guid Request, long AssignmentRevision,
    SWGlobalWarSupplyAction Action, SWGlobalWarSupplyKind Kind, long ExpectedRevision = 0,
    Guid Target = default, NetEntity? Source = null, NetEntity? Recipient = null, int Quantity = 0,
    int LocalPercent = 25, bool Bacta = false, bool HeadDecision = false, Guid Quote = default);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyResult(Guid Request, bool Success, SWGlobalWarSupplyFailure Failure,
    Guid Target = default, long Revision = 0, bool ProductionContinues = false, SWGlobalWarSupplyQuote? Quote = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyInputView(string Prototype, bool Physical, int Quantity);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyQuote(int Quantity, int Ready, int Missing, int Output,
    int Market, int Local, int FreeMarket, int FreeLocal, TimeSpan Duration,
    List<SWGlobalWarSupplyInputView> Inputs, long Cost, long LedgerRevision, long LineRevision,
    SWGlobalWarSupplyFailure Failure, bool ConsumerRegistered, List<SWGlobalWarSupplyStoreView>? AllowedArsenals = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyStoreView(NetEntity Entity, string Planet, string Name, bool Arsenal);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyLineView(SWGlobalWarSupplyKind Kind, int Participants, int Target,
    int Accepted, long Outstanding, int Window, int WindowParticipants, int Budget, int Remaining,
    long Revision, int LocalPercent, bool Bacta, decimal Rate, Guid Intent, bool Suspended,
    SWGlobalWarSupplyFailure Failure, bool ConsumerRegistered);

/// <summary>Only a current eligible office receives the program controls; finance fields are independently filtered.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyView(List<SWGlobalWarSupplyLineView> Lines, bool CanPurchase,
    bool CanProduce, bool WarHeadDecisionRequired, bool IndustryHeadDecisionRequired,
    long? WarAvailable, long? LedgerRevision, List<SWGlobalWarSupplyOrderView> Orders,
    int TotalOrders, int Page, SWGlobalWarSupplyKind Kind, List<SWGlobalWarSupplyStoreView> Stores);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyBatchView(Guid Id, int Quantity, int MarketQuantity, int LocalQuantity,
    TimeSpan Duration, SWGlobalWarSupplyStage Stage, Guid? Purchase, bool ReturnPending,
    int AssignedToOrder, int? FreeMarket, int? FreeLocal, bool FreeIsPlanned);

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyOrderView(Guid Id, SWGlobalWarSupplyKind Kind, bool Bacta,
    int Window, int Quantity, int Accepted, int Outstanding, int Cancelled, SWGlobalWarSupplyStage Stage,
    string Source, string Recipient, bool ReturnPending, long? ReservedPayment, SWGlobalWarSupplyBatchView? Batch,
    int InitialReady, int InitialProduction, long ArsenalRevision = 0, List<string>? AllowedArsenals = null,
    int Number = 0, string TransportHold = "", List<Guid>? Parts = null);

[Serializable, NetSerializable]
public sealed class SWGlobalWarSupplyRequest(SWGlobalWarSupplyCommand command, Guid subscription)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarSupplyCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarSupplyReply(SWGlobalWarSupplyResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarSupplyResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}
