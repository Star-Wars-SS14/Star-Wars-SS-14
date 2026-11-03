using System.Numerics;
using Content.Server.Popups;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Access;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Gateway;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar;

/// <summary>Links the surface and orbit endpoints of each planet and handles interaction-based travel.</summary>
public sealed class SWGlobalWarPlanetTeleporterSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SWGlobalWarAccessPolicySystem _policy = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private const float ExitDistance = 1f;
    private const float ExitHalfSize = .35f;
    /// <summary>
    /// A pull joint sent in the same state as the jump fails on a client that still has one body on the old map.
    /// </summary>
    private static readonly TimeSpan TowResumeDelay = TimeSpan.FromSeconds(0.5);
    private const float TowResumeRange = 2.5f;
    private readonly List<(EntityUid User, EntityUid Load, TimeSpan At)> _towsToResume = new();

    private static readonly Vector2[] ExitDirections =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0), Vector2.Zero,
    };


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, MapUidChangedEvent>(OnMapChanged);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnLocationReady, after: [typeof(SWGlobalWarMapAnchorSystem)]);
        SubscribeLocalEvent<SWGlobalWarPlanetStoppedEvent>(OnPlanetStopped);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnInit(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref ComponentInit args)
    {
        // Receive map changes even when an endpoint moves together with its parent grid.
        _metadata.AddFlag(ent.Owner, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnMapInit(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref MapInitEvent args) => Register(ent);
    private void OnMapChanged(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref MapUidChangedEvent args) => Register(ent);
    private void OnAnchorChanged(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref AnchorStateChangedEvent args) => Register(ent);
    private void OnShutdown(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref ComponentShutdown args) => Unregister(ent);

    private void OnLocationReady(ref SWGlobalWarLocationReadyEvent args)
    {
        RefreshPlanet(args.Planet);
    }

    private void OnPlanetStopped(ref SWGlobalWarPlanetStoppedEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarPlanetTeleporterComponent>();
        while (query.MoveNext(out var uid, out var teleporter))
        {
            if (teleporter.Planet == args.Planet)
                Unregister((uid, teleporter));
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        // Registrations are owned by the round-scoped anchor system.
        _towsToResume.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        for (var i = _towsToResume.Count - 1; i >= 0; i--)
        {
            var (user, load, at) = _towsToResume[i];
            if (_timing.CurTime < at)
                continue;
            _towsToResume.RemoveAt(i);
            if (!TerminatingOrDeleted(user) && !TerminatingOrDeleted(load) &&
                _transform.InRange(user, load, TowResumeRange))
                _pulling.TryStartPull(user, load);
        }
    }

    private void OnActivate(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        if (!TryTeleport(ent.Owner, args.User, out var reason))
            _popup.PopupEntity(reason, ent.Owner, args.User);
        else
            args.WasLogged = true;
    }

    /// <summary>A soldier walks up with a rifle in hand, and a click with it should still mean the way out.</summary>
    private void OnInteractUsing(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!TryTeleport(ent.Owner, args.User, out var reason))
            _popup.PopupEntity(reason, ent.Owner, args.User);
    }

    private void OnExamine(Entity<SWGlobalWarPlanetTeleporterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        Register(ent);
        args.PushMarkup(Loc.GetString(ent.Comp.LinkedTeleporter == null
            ? "sw-global-war-teleporter-unlinked"
            : ent.Comp.Location == SWGlobalWarPlanetLocation.Orbit
                ? "sw-global-war-teleporter-to-surface" : "sw-global-war-teleporter-to-orbit"));
    }

    /// <summary>Refreshes endpoints after loading maps or before use, including endpoints on paused maps.</summary>
    /// <param name="planet">Planet controller whose registrations may need updating.</param>
    private void RefreshPlanet(EntityUid planet)
    {
        var query = AllEntityQuery<SWGlobalWarPlanetTeleporterComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Planet == planet ||
                TryComp<SWGlobalWarPlanetMapComponent>(Transform(uid).MapUid, out var map) && map.Planet == planet)
                Register((uid, component));
        }
    }

    /// <summary>Derives an endpoint's planet and role from its map and updates both sides of the pair.</summary>
    /// <param name="ent">Endpoint being initialized, moved or anchored.</param>
    private void Register(Entity<SWGlobalWarPlanetTeleporterComponent> ent)
    {
        if (TerminatingOrDeleted(ent.Owner) || !Transform(ent.Owner).Anchored ||
            !TryComp<SWGlobalWarPlanetMapComponent>(Transform(ent.Owner).MapUid, out var map) ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var globalWarGamerule) ||
            !TryComp<SWGlobalWarPlanetComponent>(map.Planet, out var planet) ||
            planet.GlobalWarGamerule != globalWarGamerule.Owner ||
            !_planets.TryGetPlanetMap(map.Planet, map.Location, out var expectedMap) || expectedMap != Transform(ent.Owner).MapUid)
        {
            Unregister(ent);
            return;
        }

        ent.Comp.Planet = map.Planet;
        ent.Comp.Location = map.Location;
        RefreshLinks(map.Planet);
    }

    private void Unregister(Entity<SWGlobalWarPlanetTeleporterComponent> ent)
    {
        var previous = ent.Comp.Planet;
        ent.Comp.Planet = null;
        SetLink(ent, null);
        if (previous is { } planet)
            RefreshLinks(planet);
    }

    private void RefreshLinks(EntityUid planet)
    {
        var query = AllEntityQuery<SWGlobalWarPlanetTeleporterComponent, SWGlobalWarMapAnchorComponent>();
        while (query.MoveNext(out var uid, out var teleporter, out var anchor))
        {
            if (teleporter.Planet != planet)
                continue;
            EntityUid? target = null;
            if (_anchors.TryGetLink(uid, "peer", out var peer) &&
                peer.Sector == Comp<SWGlobalWarPlanetComponent>(planet).Prototype &&
                peer.Location != teleporter.Location && _anchors.TryResolveAnchor(peer, out var candidate) &&
                TryComp<SWGlobalWarPlanetTeleporterComponent>(candidate, out var opposite) &&
                opposite.Planet == planet && Transform(candidate.Value).Anchored)
                target = candidate;
            SetLink((uid, teleporter), target);
        }
    }

    private void SetLink(Entity<SWGlobalWarPlanetTeleporterComponent> ent, EntityUid? target)
    {
        ent.Comp.LinkedTeleporter = target;
        if (!TerminatingOrDeleted(ent.Owner))
            _appearance.SetData(ent.Owner, GatewayVisuals.Active, target != null);
    }

    /// <summary>Validates an interaction, wakes the opposite location and transfers the user to its endpoint.</summary>
    /// <param name="teleporter">Source endpoint on the surface or orbit.</param>
    /// <param name="user">Entity activating the endpoint; carried inventory moves with it.</param>
    /// <param name="reason">Localized rejection reason, or an empty string on success.</param>
    public bool TryTeleport(EntityUid teleporter, EntityUid user, out string reason)
    {
        reason = Loc.GetString("sw-global-war-teleporter-denied");
        if (TerminatingOrDeleted(user) || !TryComp<SWGlobalWarPlanetTeleporterComponent>(teleporter, out var source) ||
            !_blocker.CanInteract(user, teleporter) || !_interaction.InRangeUnobstructed(user, teleporter) ||
            Transform(user).Anchored || _containers.IsEntityInContainer(user) ||
            TryComp<BuckleComponent>(user, out var buckle) && buckle.Buckled)
            return false;

        Register((teleporter, source));
        reason = Loc.GetString("sw-global-war-teleporter-unlinked");
        if (source.Planet is not { } planet)
            return false;

        if (!_anchors.TryGetLink(teleporter, "peer", out var peer) ||
            peer.Sector != Comp<SWGlobalWarPlanetComponent>(planet).Prototype || peer.Location == source.Location ||
            !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule))
            return false;

        var decision = _policy.EvaluateTransition(user, planet, peer.Location, SWGlobalWarTransitionKind.Landing,
            rule.Comp.Generation);
        if (!decision.Allowed)
        {
            reason = Loc.GetString(decision.Reason);
            return false;
        }
        if (!_planets.TryEnsureLocation(planet, peer.Location, out _, out _))
            return false;

        RefreshPlanet(planet);
        RefreshLinks(planet);
        if (source.LinkedTeleporter is not { } target || TerminatingOrDeleted(target) ||
            !TryComp<SWGlobalWarPlanetTeleporterComponent>(target, out var destination) ||
            destination.LinkedTeleporter != teleporter || destination.Planet != planet || destination.Location == source.Location)
            return false;

        var prototype = Comp<SWGlobalWarPlanetComponent>(planet).Prototype;
        if (!_planets.TryPrepareArrival(prototype, destination.Location, user, out var map))
            return false;

        try
        {
            if (Transform(target).MapUid != map)
                return false;

            if (!TryFindExit(target, out var coordinates))
            {
                reason = Loc.GetString("sw-global-war-teleporter-blocked");
                return false;
            }

            // Never silently leave a patient or cargo behind. A cargo crate is the only towed load that crosses,
            // because the policy above judged only the user and nobody can be inside it.
            var load = TryComp<PullerComponent>(user, out var pulling) ? pulling.Pulling : null;
            if (TryComp<PullableComponent>(user, out var attached) && attached.BeingPulled ||
                load != null && !IsCargoLoad(load.Value))
            {
                reason = Loc.GetString("sw-global-war-teleporter-linked-load");
                return false;
            }
            var loadCoordinates = coordinates;
            if (load != null && !TryFindExit(target, out loadCoordinates, coordinates))
            {
                reason = Loc.GetString("sw-global-war-teleporter-blocked");
                return false;
            }

            decision = _policy.EvaluateTransition(user, planet, peer.Location, SWGlobalWarTransitionKind.Landing,
                rule.Comp.Generation);
            if (!decision.Allowed)
            {
                reason = Loc.GetString(decision.Reason);
                return false;
            }

            _adminLog.Add(LogType.Teleport, LogImpact.Low,
                $"{ToPrettyString(user):player} used {ToPrettyString(teleporter)} to travel to {ToPrettyString(target)} on planet {ToPrettyString(planet)}");
            _audio.PlayPvs(source.DepartureSound, teleporter);
            // The pull joint cannot span two maps: release it, move both, then take the same crate again.
            if (load != null && !_pulling.TryStopPull(load.Value, Comp<PullableComponent>(load.Value), user))
                return false;
            _transform.SetCoordinates(user, coordinates);
            if (TryComp<PhysicsComponent>(user, out var body))
                _physics.SetLinearVelocity(user, Vector2.Zero, body: body);
            if (load != null)
            {
                _transform.SetCoordinates(load.Value, loadCoordinates);
                if (TryComp<PhysicsComponent>(load.Value, out var loadBody))
                    _physics.SetLinearVelocity(load.Value, Vector2.Zero, body: loadBody);
                _towsToResume.Add((user, load.Value, _timing.CurTime + TowResumeDelay));
            }
            _audio.PlayPvs(destination.ArrivalSound, target);
            reason = string.Empty;
            return true;
        }
        finally
        {
            _planets.ReleaseArrival(map.Value, user);
        }
    }

    /// <summary>A towed crate or box carrying no mob or mind. Anyone inside would skip the transition policy.</summary>
    private bool IsCargoLoad(EntityUid load)
    {
        return (HasComp<EntityStorageComponent>(load) || HasComp<StorageComponent>(load)) && !HoldsPerson(load);
    }

    private bool HoldsPerson(EntityUid entity)
    {
        if (HasComp<MobStateComponent>(entity) || TryComp<MindContainerComponent>(entity, out var mind) && mind.HasMind)
            return true;
        var children = Transform(entity).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HoldsPerson(child))
                return true;
        }
        return false;
    }

    /// <summary>Checks nearby exits against solid bodies and missing grid tiles, preferring the front of the receiver.</summary>
    /// <param name="target">Receiving endpoint whose local rotation determines the preferred exit direction.</param>
    /// <param name="coordinates">Free exit in the endpoint's parent coordinates, avoiding parenting the user to the machine.</param>
    /// <param name="taken">An exit already given to someone travelling together with this one.</param>
    private bool TryFindExit(EntityUid target, out EntityCoordinates coordinates, EntityCoordinates? taken = null)
    {
        coordinates = default;
        var transform = Transform(target);
        foreach (var direction in ExitDirections)
        {
            var candidate = transform.Coordinates.Offset(transform.LocalRotation.RotateVec(direction * ExitDistance));
            if (candidate == taken)
                continue;
            if (TryComp<MapGridComponent>(transform.GridUid, out var grid) &&
                _maps.GetTileRef(transform.GridUid!.Value, grid, candidate).Tile.IsEmpty)
                continue;

            var position = _transform.ToMapCoordinates(candidate);
            var bounds = Box2.CenteredAround(position.Position, new Vector2(ExitHalfSize * 2));
            if (_lookup.AnyEntitiesIntersecting(position.MapId, bounds, LookupFlags.Static | LookupFlags.Dynamic))
                continue;

            coordinates = candidate;
            return true;
        }
        return false;
    }
}
