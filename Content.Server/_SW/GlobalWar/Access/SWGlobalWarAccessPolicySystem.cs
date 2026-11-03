using Content.Server._SW.GlobalWar.Identity;
using Content.Shared._SW.GlobalWar;
using Content.Shared.GameTicking;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Access;

/// <summary>Shared server contract for real transition channels; diplomacy supplies explicit exceptions.</summary>
public sealed class SWGlobalWarAccessPolicySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private Guid _generation;
    private long _revision;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>(OnOwnershipChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _generation = Guid.Empty;
            _revision = 0;
        });
    }

    /// <summary>Evaluates the actual actor, actual source map and a registered target without loading maps.</summary>
    public SWGlobalWarAccessDecision EvaluateTransition(EntityUid actor, EntityUid destination,
        SWGlobalWarPlanetLocation location, SWGlobalWarTransitionKind kind, Guid generation)
    {
        var denied = new SWGlobalWarAccessDecision(false, "sw-global-war-access-denied", _revision, 0, 0);
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation ||
            !Enum.IsDefined(location) || !Enum.IsDefined(kind) ||
            !_planets.TryGetPhysicalLocation(actor, out var source, out var sourceLocation) ||
            !TryComp<SWGlobalWarPlanetComponent>(destination, out var target) || target.GlobalWarGamerule != rule.Owner ||
            !_identities.TryGetIdentity(actor, out var mind, out var faction))
            return denied;

        EnsureGeneration(generation);
        return EvaluateValidated(actor, mind, faction, source, sourceLocation, destination, target, location, kind, generation);
    }

    /// <summary>Rechecks an actor aboard a real in-flight transport using its server-recorded departure context.</summary>
    public SWGlobalWarAccessDecision EvaluateInFlight(EntityUid actor, EntityUid transport)
    {
        var denied = new SWGlobalWarAccessDecision(false, "sw-global-war-access-denied", _revision, 0, 0);
        if (!_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<SWGlobalWarTravelComponent>(transport, out var flight) || !HasComp<FTLComponent>(transport) ||
            flight.GlobalWarGamerule != rule.Owner || flight.Generation != rule.Comp.Generation ||
            !TryComp<TransformComponent>(actor, out var transform) || transform.GridUid != transport ||
            !_identities.TryGetIdentity(actor, out var mind, out var faction) ||
            !TryComp<SWGlobalWarPlanetComponent>(flight.SourcePlanet, out var source) || source.GlobalWarGamerule != rule.Owner ||
            !TryComp<SWGlobalWarPlanetComponent>(flight.DestinationPlanet, out var target) || target.GlobalWarGamerule != rule.Owner)
            return denied;
        EnsureGeneration(flight.Generation);
        return EvaluateValidated(actor, mind, faction, flight.SourcePlanet, SWGlobalWarPlanetLocation.Orbit,
            flight.DestinationPlanet, target, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarTransitionKind.Ftl,
            flight.Generation, inFlight: true);
    }

    private SWGlobalWarAccessDecision EvaluateValidated(EntityUid actor, EntityUid mind, SWGlobalWarFaction faction,
        EntityUid source, SWGlobalWarPlanetLocation sourceLocation, EntityUid destination, SWGlobalWarPlanetComponent target,
        SWGlobalWarPlanetLocation location, SWGlobalWarTransitionKind kind, Guid generation, bool inFlight = false)
    {
        var origin = Comp<SWGlobalWarPlanetComponent>(source);
        var denied = new SWGlobalWarAccessDecision(false, "sw-global-war-access-denied", _revision,
            origin.OwnershipRevision, target.OwnershipRevision);
        if (source != destination &&
            (sourceLocation != SWGlobalWarPlanetLocation.Orbit || location != SWGlobalWarPlanetLocation.Orbit ||
             !_prototypes.Index(origin.Prototype).Connections.Contains(target.Prototype)))
            return denied with { Reason = "sw-global-war-access-route" };

        var owner = location == SWGlobalWarPlanetLocation.Orbit ? target.OrbitOwner : target.SurfaceOwner;
        var political = new SWGlobalWarEvaluateAccessEvent(actor, mind, faction, source, sourceLocation,
            destination, location, kind, generation,
            !inFlight && source == destination && sourceLocation == location || owner == faction);
        RaiseLocalEvent(ref political);
        return denied with { Allowed = political.Allowed, Reason = political.Allowed ? string.Empty : denied.Reason };
    }

    /// <summary>Revisions are server-owned; stale callbacks cannot change the current rule's permissions.</summary>
    public bool NotifyPolicyChanged(Guid generation)
    {
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation)
            return false;
        EnsureGeneration(generation);
        _revision = checked(_revision + 1);
        var changed = new SWGlobalWarPolicyChangedEvent(generation, _revision);
        RaiseLocalEvent(ref changed);
        return true;
    }

    private void OnOwnershipChanged(ref SWGlobalWarOwnershipChangedEvent args)
    {
        if (_rules.TryGetGlobalWarGamerule(out var rule) &&
            TryComp<SWGlobalWarPlanetComponent>(args.Planet, out var planet) && planet.GlobalWarGamerule == rule.Owner)
            NotifyPolicyChanged(rule.Comp.Generation);
    }

    private void EnsureGeneration(Guid generation)
    {
        if (_generation == generation)
            return;
        _generation = generation;
        _revision = 0;
    }
}
