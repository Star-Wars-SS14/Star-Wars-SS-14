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
    List<SWGlobalWarShipmentView> shipments, ProtoId<SWGlobalWarPlanetPrototype>? currentPlanet) : BoundUserInterfaceState
{
    public readonly bool Ready = ready;
    public readonly TimeSpan Time = time;
    public readonly List<SWGlobalWarPlanetMarketView> Planets = planets;
    public readonly List<SWGlobalWarMarketConnectionView> Connections = connections;
    public readonly List<SWGlobalWarShipmentView> Shipments = shipments;
    public readonly ProtoId<SWGlobalWarPlanetPrototype>? CurrentPlanet = currentPlanet;
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarPlanetMarketView(ProtoId<SWGlobalWarPlanetPrototype> Planet, string Name,
    List<SWGlobalWarMarketGoodView> Goods);

/// <summary>Directed travel time with a capacity and occupied volume shared by both directions.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarMarketConnectionView(ProtoId<SWGlobalWarPlanetPrototype> From,
    ProtoId<SWGlobalWarPlanetPrototype> To, TimeSpan TravelTime, double Capacity, double InFlightVolume);

[Serializable, NetSerializable]
public sealed record SWGlobalWarShipmentView(ProtoId<SWGlobalWarCommodityPrototype> Commodity, int Quantity,
    List<ProtoId<SWGlobalWarPlanetPrototype>> Route, int Leg, TimeSpan DepartedAt, TimeSpan? ArrivesAt)
{
    public ProtoId<SWGlobalWarPlanetPrototype> Source => Route[0];
    public ProtoId<SWGlobalWarPlanetPrototype> Destination => Route[^1];
    public ProtoId<SWGlobalWarPlanetPrototype> From => Route[Leg];
    public ProtoId<SWGlobalWarPlanetPrototype> To => Route[Leg + 1];
}
