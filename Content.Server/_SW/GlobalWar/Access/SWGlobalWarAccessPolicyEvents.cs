using Content.Shared._SW.GlobalWar;

namespace Content.Server._SW.GlobalWar.Access;

/// <summary>Authoritative political evaluation after immutable identity, location and route validation.</summary>
[ByRefEvent]
public record struct SWGlobalWarEvaluateAccessEvent(
    EntityUid Actor, EntityUid Mind, SWGlobalWarFaction Faction,
    EntityUid SourcePlanet, SWGlobalWarPlanetLocation SourceLocation,
    EntityUid DestinationPlanet, SWGlobalWarPlanetLocation DestinationLocation,
    SWGlobalWarTransitionKind Kind, Guid Generation, bool Allowed);

/// <summary>Political rules notify pending operations after a server-side permission change.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarPolicyChangedEvent(Guid Generation, long Revision);

/// <summary>Transport channels share political permissions; spawning is deliberately a separate decision.</summary>
public enum SWGlobalWarTransitionKind : byte
{
    Landing,
    Ftl,
    Portal,
}

/// <summary>Versioned decision for immediate use; every delayed completion must evaluate again.</summary>
public readonly record struct SWGlobalWarAccessDecision(
    bool Allowed, string Reason, long PolicyRevision, long SourceOwnershipRevision, long DestinationOwnershipRevision);
