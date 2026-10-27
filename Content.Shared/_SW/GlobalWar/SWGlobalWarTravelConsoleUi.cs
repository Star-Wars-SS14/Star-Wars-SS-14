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
    public readonly List<SWGlobalWarTravelDestination> Destinations;

    public SWGlobalWarTravelConsoleState(string currentPlanet, string status, List<SWGlobalWarTravelDestination> destinations)
    {
        CurrentPlanet = currentPlanet;
        Status = status;
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
