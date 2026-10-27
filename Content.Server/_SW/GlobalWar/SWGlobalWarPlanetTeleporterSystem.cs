using System.Linq;
using System.Numerics;
using Content.Server.Popups;
using Content.Shared._SW.GlobalWar;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Gateway;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._SW.GlobalWar;

/// <summary>Links the surface and orbit endpoints of each planet and handles interaction-based travel.</summary>
public sealed class SWGlobalWarPlanetTeleporterSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarGameruleSystem _globalWarGamerule = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private const float ExitDistance = 1f;
    private const float ExitHalfSize = .35f;
    private static readonly Vector2[] ExitDirections =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0), Vector2.Zero,
    };

    private readonly Dictionary<(EntityUid Planet, SWGlobalWarPlanetLocation Location), HashSet<EntityUid>> _endpoints = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, MapUidChangedEvent>(OnMapChanged);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SWGlobalWarPlanetTeleporterComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SWGlobalWarPlanetComponent, SWGlobalWarPlanetLoadedEvent>(OnPlanetLoaded);
        SubscribeLocalEvent<SWGlobalWarPlanetComponent, SWGlobalWarPlanetStoppedEvent>(OnPlanetStopped);
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

    private void OnPlanetLoaded(Entity<SWGlobalWarPlanetComponent> ent, ref SWGlobalWarPlanetLoadedEvent args)
    {
        RefreshPlanet(ent.Owner);
    }

    private void OnPlanetStopped(Entity<SWGlobalWarPlanetComponent> ent, ref SWGlobalWarPlanetStoppedEvent args)
    {
        var query = AllEntityQuery<SWGlobalWarPlanetTeleporterComponent>();
        while (query.MoveNext(out var uid, out var teleporter))
        {
            if (teleporter.Planet == ent.Owner)
                Unregister((uid, teleporter));
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _endpoints.Clear();
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

        if (ent.Comp.Planet == map.Planet && ent.Comp.Location == map.Location &&
            _endpoints.TryGetValue((map.Planet, map.Location), out var registered) && registered.Contains(ent.Owner))
            return;

        Unregister(ent);
        ent.Comp.Planet = map.Planet;
        ent.Comp.Location = map.Location;
        var key = (map.Planet, map.Location);
        if (!_endpoints.TryGetValue(key, out var endpoints))
            _endpoints[key] = endpoints = new HashSet<EntityUid>();
        endpoints.Add(ent.Owner);
        if (endpoints.Count > 1)
            Log.Warning($"Planet {ToPrettyString(map.Planet)} has multiple {map.Location} teleporters; its surface/orbit link is disabled.");
        RefreshLinks(map.Planet);
    }

    private void Unregister(Entity<SWGlobalWarPlanetTeleporterComponent> ent)
    {
        if (ent.Comp.Planet is not { } planet)
            return;

        if (_endpoints.TryGetValue((planet, ent.Comp.Location), out var endpoints))
        {
            endpoints.Remove(ent.Owner);
            if (endpoints.Count == 0)
                _endpoints.Remove((planet, ent.Comp.Location));
        }
        ent.Comp.Planet = null;
        SetLink(ent, null);
        RefreshLinks(planet);
    }

    private void RefreshLinks(EntityUid planet)
    {
        _endpoints.TryGetValue((planet, SWGlobalWarPlanetLocation.Surface), out var surface);
        _endpoints.TryGetValue((planet, SWGlobalWarPlanetLocation.Orbit), out var orbit);
        var paired = surface?.Count == 1 && orbit?.Count == 1;
        if (surface != null)
        {
            foreach (var uid in surface)
            {
                if (!TerminatingOrDeleted(uid) && TryComp<SWGlobalWarPlanetTeleporterComponent>(uid, out var component))
                    SetLink((uid, component), paired ? orbit!.Single() : (EntityUid?) null);
            }
        }
        if (orbit != null)
        {
            foreach (var uid in orbit)
            {
                if (!TerminatingOrDeleted(uid) && TryComp<SWGlobalWarPlanetTeleporterComponent>(uid, out var component))
                    SetLink((uid, component), paired ? surface!.Single() : (EntityUid?) null);
            }
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

        RefreshPlanet(planet);
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

            // Fall back to the receiver itself when no free exit is available nearby.
            if (!TryFindExit(target, out var coordinates))
                coordinates = Transform(target).Coordinates;

            // Pull joints cannot span maps; held inventory remains attached to the user normally.
            if (TryComp<PullableComponent>(user, out var pullable) && pullable.BeingPulled)
                _pulling.TryStopPull(user, pullable);
            if (TryComp<PullerComponent>(user, out var puller) && TryComp<PullableComponent>(puller.Pulling, out var pulled))
                _pulling.TryStopPull(puller.Pulling!.Value, pulled);

            _adminLog.Add(LogType.Teleport, LogImpact.Low,
                $"{ToPrettyString(user):player} used {ToPrettyString(teleporter)} to travel to {ToPrettyString(target)} on planet {ToPrettyString(planet)}");
            _audio.PlayPvs(source.DepartureSound, teleporter);
            _transform.SetCoordinates(user, coordinates);
            if (TryComp<PhysicsComponent>(user, out var body))
                _physics.SetLinearVelocity(user, Vector2.Zero, body: body);
            _audio.PlayPvs(destination.ArrivalSound, target);
            reason = string.Empty;
            return true;
        }
        finally
        {
            _planets.ReleaseArrival(map.Value, user);
        }
    }

    /// <summary>Checks nearby exits against solid bodies and missing grid tiles, preferring the front of the receiver.</summary>
    /// <param name="target">Receiving endpoint whose local rotation determines the preferred exit direction.</param>
    /// <param name="coordinates">Free exit in the endpoint's parent coordinates, avoiding parenting the user to the machine.</param>
    private bool TryFindExit(EntityUid target, out EntityCoordinates coordinates)
    {
        coordinates = default;
        var transform = Transform(target);
        foreach (var direction in ExitDirections)
        {
            var candidate = transform.Coordinates.Offset(transform.LocalRotation.RotateVec(direction * ExitDistance));
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
