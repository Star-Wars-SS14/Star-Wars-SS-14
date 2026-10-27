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
public sealed record SWGlobalWarTravelDestination(ProtoId<SWGlobalWarPlanetPrototype> Id, string Name, bool Available, string Reason);

[Serializable, NetSerializable]
public sealed class SWGlobalWarTravelRequest : BoundUserInterfaceMessage
{
    public readonly ProtoId<SWGlobalWarPlanetPrototype> Destination;

    public SWGlobalWarTravelRequest(ProtoId<SWGlobalWarPlanetPrototype> destination)
    {
        Destination = destination;
    }
}
