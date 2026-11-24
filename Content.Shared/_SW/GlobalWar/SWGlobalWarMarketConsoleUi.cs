using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarMarketConsoleUiKey : byte
{
    Key,
}

/// <summary>A read-only snapshot of active planetary markets and their actual virtual freight.</summary>
[Serializable, NetSerializable]
public sealed class SWGlobalWarMarketConsoleState(bool ready, TimeSpan time,
    List<SWGlobalWarPlanetMarketView> planets, List<SWGlobalWarMarketConnectionView> connections,
    List<SWGlobalWarShipmentView> shipments, ProtoId<SWGlobalWarPlanetPrototype>? currentPlanet,
    Guid generation = default, Guid assignment = default, long assignmentRevision = 0, bool partial = false,
    SWGlobalWarFreightQuery? query = null, int totalShipments = 0,
    List<SWGlobalWarFreightTotal>? freightTotals = null,
    List<SWGlobalWarFlowView>? flows = null, List<SWGlobalWarFlowResult>? flowResults = null,
    bool canManageFlows = false, bool head = false, List<SWGlobalWarFlowGrantView>? flowGrants = null,
    List<SWGlobalWarFlowWorkerView>? flowWorkers = null,
    List<SWGlobalWarNeutralOfferView>? neutralOffers = null, List<SWGlobalWarGrowthView>? growth = null,
    List<SWGlobalWarGrowthGrantView>? growthGrants = null, List<SWGlobalWarGrowthResult>? growthResults = null,
    bool logisticsHeadDecisionRequired = false, List<SWGlobalWarReturnCandidate>? returnCandidates = null,
    List<SWGlobalWarReturnGrantView>? returnGrants = null, List<SWGlobalWarReturnResult>? returnResults = null,
    SWGlobalWarLanthanumView? lanthanum = null, List<SWGlobalWarLanthanumResult>? lanthanumResults = null,
    SWGlobalWarSupplyView? supply = null, List<SWGlobalWarSupplyResult>? supplyResults = null,
    SWGlobalWarSupplyWorkBoard? supplyWork = null,
    List<SWGlobalWarSupplyWorkResult>? supplyWorkResults = null) : BoundUserInterfaceState
{
    public const int ShipmentPageSize = 40;
    public readonly bool Ready = ready;
    public readonly TimeSpan Time = time;
    public readonly List<SWGlobalWarPlanetMarketView> Planets = planets;
    public readonly List<SWGlobalWarMarketConnectionView> Connections = connections;
    public readonly List<SWGlobalWarShipmentView> Shipments = shipments;
    public readonly ProtoId<SWGlobalWarPlanetPrototype>? CurrentPlanet = currentPlanet;
    public readonly Guid Generation = generation;
    public readonly Guid Assignment = assignment;
    public readonly long AssignmentRevision = assignmentRevision;
    public readonly bool Partial = partial;
    public readonly SWGlobalWarFreightQuery Query = query ?? new();
    public readonly int TotalShipments = totalShipments;
    public readonly List<SWGlobalWarFreightTotal> FreightTotals = freightTotals ?? [];
    public readonly List<SWGlobalWarFlowView> Flows = flows ?? [];
    public readonly List<SWGlobalWarFlowResult> FlowResults = flowResults ?? [];
    public readonly bool CanManageFlows = canManageFlows;
    public readonly bool Head = head;
    public readonly List<SWGlobalWarFlowGrantView> FlowGrants = flowGrants ?? [];
    public readonly List<SWGlobalWarFlowWorkerView> FlowWorkers = flowWorkers ?? [];
    public readonly List<SWGlobalWarNeutralOfferView> NeutralOffers = neutralOffers ?? [];
    public readonly List<SWGlobalWarGrowthView> Growth = growth ?? [];
    public readonly List<SWGlobalWarGrowthGrantView> GrowthGrants = growthGrants ?? [];
    public readonly List<SWGlobalWarGrowthResult> GrowthResults = growthResults ?? [];
    public readonly bool LogisticsHeadDecisionRequired = logisticsHeadDecisionRequired;
    public readonly List<SWGlobalWarReturnCandidate> ReturnCandidates = returnCandidates ?? [];
    public readonly List<SWGlobalWarReturnGrantView> ReturnGrants = returnGrants ?? [];
    public readonly List<SWGlobalWarReturnResult> ReturnResults = returnResults ?? [];
    public readonly SWGlobalWarLanthanumView? Lanthanum = lanthanum;
    public readonly List<SWGlobalWarLanthanumResult> LanthanumResults = lanthanumResults ?? [];
    public readonly SWGlobalWarSupplyView? Supply = supply;
    public readonly List<SWGlobalWarSupplyResult> SupplyResults = supplyResults ?? [];
    public readonly SWGlobalWarSupplyWorkBoard? SupplyWork = supplyWork;
    public readonly List<SWGlobalWarSupplyWorkResult> SupplyWorkResults = supplyWorkResults ?? [];
}

/// <summary>Filters are applied after authorization. A page contains exact cargo identities, never aggregate actions.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarFreightQuery(ProtoId<SWGlobalWarPlanetPrototype>? Planet = null,
    ProtoId<SWGlobalWarCommodityPrototype>? Commodity = null, ProtoId<SWGlobalWarPlanetPrototype>? From = null,
    ProtoId<SWGlobalWarPlanetPrototype>? To = null, int Page = 0, int SupplyPage = 0,
    SWGlobalWarSupplyKind SupplyKind = SWGlobalWarSupplyKind.T2, int SupplyWorkPage = 0);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFreightTotal(ProtoId<SWGlobalWarCommodityPrototype> Commodity,
    double Moving, double Waiting, int Count);

[Serializable, NetSerializable]
public sealed class SWGlobalWarMarketPageRequest(SWGlobalWarFreightQuery query, Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFreightQuery Query = query;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarMarketOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarMarketSnapshot(SWGlobalWarMarketConsoleState state, NetEntity viewer, long sequence,
    Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarMarketConsoleState State = state;
    public readonly NetEntity Viewer = viewer;
    public readonly long Sequence = sequence;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarPlanetMarketView(ProtoId<SWGlobalWarPlanetPrototype> Planet, string Name,
    List<SWGlobalWarMarketGoodView> Goods, SWGlobalWarMarketAvailability Availability = SWGlobalWarMarketAvailability.Ready,
    double? Population = null, int? SendCapacity = null, int? ReceiveCapacity = null, SWGlobalWarFaction? Owner = null);

/// <summary>Directed travel time with a capacity and occupied volume shared by both directions.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarMarketConnectionView(ProtoId<SWGlobalWarPlanetPrototype> From,
    ProtoId<SWGlobalWarPlanetPrototype> To, TimeSpan TravelTime, double Capacity, double InFlightVolume,
    bool OccupancyComplete = true);

[Serializable, NetSerializable]
public sealed record SWGlobalWarShipmentView(ProtoId<SWGlobalWarCommodityPrototype> Commodity, double Quantity,
    List<ProtoId<SWGlobalWarPlanetPrototype>> Route, int Leg, TimeSpan DepartedAt, TimeSpan? ArrivesAt,
    Guid Id = default, string HoldReason = "", bool Returning = false, SWGlobalWarFaction? Origin = null,
    SWGlobalWarFaction? Owner = null, bool Captured = false, Guid Batch = default, Guid DepartureReceipt = default,
    Guid? ReturnOperation = null, SWGlobalWarMarketCaptureView? Capture = null,
    SWGlobalWarSupplyTransportView? Supply = null)
{
    public ProtoId<SWGlobalWarPlanetPrototype> Source => Route[0];
    public ProtoId<SWGlobalWarPlanetPrototype> Destination => Route[^1];
    public ProtoId<SWGlobalWarPlanetPrototype> From => Route[Leg];
    public ProtoId<SWGlobalWarPlanetPrototype> To => Route[Math.Min(Leg + 1, Route.Count - 1)];
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarSupplyTransportView(Guid Purchase, int Number, int Accepted, int Outstanding,
    string Recipient, List<string> AllowedArsenals, bool AcceptsSupply = false,
    List<ProtoId<SWGlobalWarPlanetPrototype>>? PhysicalWorlds = null);

[Serializable, NetSerializable]
public sealed record SWGlobalWarMarketCaptureView(Guid Operation, Guid TransferOperation,
    ProtoId<SWGlobalWarPlanetPrototype> Node, Guid Batch, double Quantity,
    SWGlobalWarFaction PreviousOwner, SWGlobalWarFaction NewOwner);
