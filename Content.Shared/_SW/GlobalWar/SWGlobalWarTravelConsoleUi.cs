using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar;

[Serializable, NetSerializable]
public enum SWGlobalWarTravelConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarTravelConsoleState : BoundUserInterfaceState
{
    public readonly string CurrentPlanet;
    public readonly string Status;
    public readonly ProtoId<SWGlobalWarPlanetPrototype>? CurrentPlanetId;
    public readonly List<SWGlobalWarTravelDestination> Destinations;

    public SWGlobalWarTravelConsoleState(string currentPlanet, string status, List<SWGlobalWarTravelDestination> destinations,
        ProtoId<SWGlobalWarPlanetPrototype>? currentPlanetId = null)
    {
        CurrentPlanet = currentPlanet;
        Status = status;
        CurrentPlanetId = currentPlanetId;
        Destinations = destinations;
    }
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarTravelDestination(ProtoId<SWGlobalWarPlanetPrototype> Id, string Name, bool Available, string Reason,
    List<SWGlobalWarMarketGoodView>? Market = null,
    SWGlobalWarMarketAvailability MarketAvailability = SWGlobalWarMarketAvailability.Ready,
    SWGlobalWarFaction? Owner = null);

[Serializable, NetSerializable]
public sealed class SWGlobalWarTravelSnapshot(SWGlobalWarTravelConsoleState state, NetEntity viewer, long sequence,
    Guid subscription) : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarTravelConsoleState State = state;
    public readonly NetEntity Viewer = viewer;
    public readonly long Sequence = sequence;
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarTravelOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarTravelRequest : BoundUserInterfaceMessage
{
    public readonly ProtoId<SWGlobalWarPlanetPrototype> Destination;

    public SWGlobalWarTravelRequest(ProtoId<SWGlobalWarPlanetPrototype> destination)
    {
        Destination = destination;
    }
}
