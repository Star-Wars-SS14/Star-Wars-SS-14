namespace Content.Server._SW.GlobalWar.Spawning;

/// <summary>Filters the same station set for initial assignment and late-join vacancy publication.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarFilterJobStationsEvent(List<EntityUid> Stations);
