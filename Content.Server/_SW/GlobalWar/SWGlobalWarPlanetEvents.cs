namespace Content.Server._SW.GlobalWar;

/// <summary>Raised on a planet controller after both its surface and orbit have finished loading.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarPlanetLoadedEvent;

/// <summary>Raised on a planet controller when its GlobalWarGamerule stops managing the planet.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarPlanetStoppedEvent;
