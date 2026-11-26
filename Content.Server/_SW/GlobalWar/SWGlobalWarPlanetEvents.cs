using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar;

/// <summary>One physical location is fully initialized and available to its consumers.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarLocationReadyEvent(
    EntityUid Planet, SWGlobalWarPlanetLocation Location, EntityUid Map, Guid Generation);

/// <summary>Published once after the authoritative owner and revision have been updated.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarOwnershipChangedEvent(
    EntityUid Planet, SWGlobalWarPlanetLocation Location, SWGlobalWarFaction PreviousOwner, SWGlobalWarFaction Owner,
    long Revision, Guid OperationId);

/// <summary>Raised on a planet controller after both its surface and orbit have finished loading.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarPlanetLoadedEvent;

/// <summary>Raised on a planet controller when its GlobalWarGamerule stops managing the planet.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarPlanetStoppedEvent(EntityUid Planet, EntityUid Rule);
