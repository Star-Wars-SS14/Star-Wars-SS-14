using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Network;

namespace Content.Shared._SW.GlobalWar;

/// <summary>Rates use the campaign's integer quanta per minute, not rounded whole items.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowSettings(ProtoId<SWGlobalWarPlanetPrototype> Source,
    ProtoId<SWGlobalWarPlanetPrototype> Destination, ProtoId<SWGlobalWarCommodityPrototype> Commodity,
    int Rate, int Order = 0, bool Enabled = true)
{
    public const int QuantaPerUnit = 60_000;
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowCommand(Guid Generation, Guid Request, long AssignmentRevision,
    string? Flow, long ExpectedRevision, SWGlobalWarFlowSettings Settings,
    Guid WorkGrant = default, bool HeadDecision = false);

[Serializable, NetSerializable]
public enum SWGlobalWarEconomyFailure : byte
{
    None,
    Denied,
    InvalidRequest,
    Conflict,
    NoRoute,
    ExpiredGrant,
    OfferExceeded,
    /// <summary>The campaign is ending or is between steps, so it takes no economy decisions.</summary>
    CampaignInactive,
    /// <summary>The sender's appointment changed since the console was opened, or the sender cannot act.</summary>
    AssignmentChanged,
    /// <summary>The source is not ours, and it is not a neutral source shipping into one of our worlds.</summary>
    NotOwned,
    /// <summary>The selected flow no longer exists or belongs to the other side.</summary>
    FlowUnavailable,
    /// <summary>No logistics office and no work authorization of one's own.</summary>
    NoAuthority,
    /// <summary>The head acts while a logistics minister holds office and did not mark a head decision.</summary>
    HeadDecisionRequired,
    /// <summary>The work authorization was revoked.</summary>
    GrantRevoked,
    /// <summary>The issuer of the work authorization lost the office that issued it.</summary>
    AuthorityChanged,
    /// <summary>Nodes, commodity, rate or priority are outside the work authorization.</summary>
    GrantOutOfRange,
    /// <summary>Source and destination are the same world.</summary>
    SameNode,
    /// <summary>The rate is below zero.</summary>
    NegativeRate,
    /// <summary>One of the two worlds does not trade this commodity.</summary>
    UnknownCommodity,
    /// <summary>The chosen worker changed office or left.</summary>
    WorkerChanged,
    /// <summary>The authorization duration is not a positive number of campaign minutes.</summary>
    InvalidDuration,
    /// <summary>The minimum is below zero or above the maximum.</summary>
    InvalidRange,
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowView(string Id, long Revision, SWGlobalWarFlowSettings Settings,
    SWGlobalWarFlowPriority Priority, double PlannedRate, string WaitReason, bool CanEdit,
    double? NeutralOffer = null, decimal? DesiredRate = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowResult(Guid Request, bool Success, SWGlobalWarEconomyFailure Failure,
    SWGlobalWarFlowView? Flow = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowGrantCommand(Guid Generation, Guid Request, long AssignmentRevision,
    NetUserId Employee, long EmployeeRevision, string? Flow, string Source, string Destination, string Commodity,
    int MinimumRate, int MaximumRate, int MinimumOrder, int MaximumOrder, TimeSpan Duration, bool HeadDecision = false);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowGrantView(Guid Id, string? Flow, string Source, string Destination,
    string Commodity, int MinimumRate, int MaximumRate, int MinimumOrder, int MaximumOrder,
    TimeSpan Expires, string Employee, bool Revoked);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowWorkerView(NetUserId User, long Revision, string Name);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFlowRevokeCommand(Guid Generation, Guid Request, long AssignmentRevision,
    Guid Grant, bool HeadDecision);

[Serializable, NetSerializable]
public sealed record SWGlobalWarNeutralOfferView(string Source, string Commodity, decimal Offer, decimal OwnOrdered);

[Serializable, NetSerializable]
public sealed class SWGlobalWarFlowRequest(SWGlobalWarFlowCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFlowCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFlowReply(SWGlobalWarFlowResult result, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFlowResult Result = result;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFlowGrantRequest(SWGlobalWarFlowGrantCommand command, Guid subscription)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFlowGrantCommand Command = command;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFlowRevokeRequest(SWGlobalWarFlowRevokeCommand command, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFlowRevokeCommand Command = command;
    public readonly Guid Subscription = subscription;
}
