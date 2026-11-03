using Content.Shared._SW.GlobalWar;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Trade stages all revalidate diplomacy; D owns the separate trade graph and physical capacity.</summary>
public enum SWGlobalWarTradeStage : byte
{
    Dispatch,
    NextLeg,
    Receipt,
}

/// <summary>Whether Current still physically holds the batch or is only the departure node of an in-flight leg.</summary>
public enum SWGlobalWarTradePosition : byte
{
    AtNode,
    InTransit,
}

/// <summary>
/// Server evidence resolved by D from its committed batch-transfer receipt, never accepted from a client.
/// Split preserves the evidenced quantity; mixed inventories must evaluate each provenance tranche separately.
/// C validates the territorial capture receipt, while D remains the sole owner of batch identity and quantities.
/// </summary>
public sealed record SWGlobalWarTradeCaptureEvidence(Guid Generation, Guid CaptureOperation, Guid TransferOperation,
    ProtoId<SWGlobalWarPlanetPrototype> Node, Guid Batch, long Quantity, SWGlobalWarFaction PreviousOwner,
    SWGlobalWarFaction NewOwner);

/// <summary>Immutable provenance and route context supplied by the authoritative market, never by a client.</summary>
public readonly record struct SWGlobalWarTradeRequest(Guid Generation,
    ProtoId<SWGlobalWarCommodityPrototype> Commodity, SWGlobalWarFaction Origin, SWGlobalWarFaction CargoOwner,
    ProtoId<SWGlobalWarPlanetPrototype> Sender, ProtoId<SWGlobalWarPlanetPrototype> Recipient,
    ProtoId<SWGlobalWarPlanetPrototype> Current, ProtoId<SWGlobalWarPlanetPrototype> Next, SWGlobalWarTradeStage Stage,
    SWGlobalWarTradePosition Position = SWGlobalWarTradePosition.AtNode, Guid Batch = default, long Quantity = 1,
    SWGlobalWarTradeCaptureEvidence? Capture = null);

/// <summary>Immediate-use policy result. A later leg or receipt must evaluate again.</summary>
public readonly record struct SWGlobalWarTradeDecision(bool Allowed, string Reason, long PolicyRevision,
    long SenderRevision, long RecipientRevision, long CurrentRevision, long NextRevision);
