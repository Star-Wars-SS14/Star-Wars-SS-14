using Content.Server._SW.GlobalWar.Mapping;
using Content.Server.Destructible;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Hands.Components;
using Content.Shared.Power;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._SW.GlobalWar.Market;

public sealed partial class SWGlobalWarMarketSystem
{
    [Dependency] private readonly SWGlobalWarPlanetSystem _hubPlanets = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _hubAnchors = default!;
    [Dependency] private readonly SharedMapSystem _hubMaps = default!;
    [Dependency] private readonly ActionBlockerSystem _hubBlocker = default!;
    [Dependency] private readonly SharedInteractionSystem _hubInteraction = default!;
    [Dependency] private readonly SharedDoAfterSystem _hubDoAfter = default!;

    private void InitializeHubs()
    {
        SubscribeLocalEvent<SWGlobalWarHubComponent, MapInitEvent>(OnHubMapInit);
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnHubLocationReady,
            after: [typeof(SWGlobalWarMapAnchorSystem)]);
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>(OnHubOwnershipChanged);
        SubscribeLocalEvent<SWGlobalWarHubComponent, SWGlobalWarHubDoAfterEvent>(OnHubDoAfter);
    }

    private void OnHubMapInit(Entity<SWGlobalWarHubComponent> ent, ref MapInitEvent args)
    {
        BindHub(ent);
    }

    private void OnHubLocationReady(ref SWGlobalWarLocationReadyEvent args)
    {
        if (args.Location != SWGlobalWarPlanetLocation.Orbit)
            return;
        var query = AllEntityQuery<SWGlobalWarHubComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hub, out var transform))
            if (transform.MapUid == args.Map)
                BindHub((uid, hub));
    }

    private void BindHub(Entity<SWGlobalWarHubComponent> ent)
    {
        var transform = Transform(ent);
        if (ent.Comp.Binding != null || !_globalWarGamerule.TryGetGlobalWarGamerule(out var rule) ||
            !_hubPlanets.TryGetPhysicalLocation(ent, out var planet, out var location) ||
            location != SWGlobalWarPlanetLocation.Orbit || transform.MapUid is not { } map ||
            transform.GridUid is not { } grid || transform.ParentUid != grid || !transform.Anchored ||
            !_hubPlanets.IsPlanetStationGrid(planet, grid) || !TryComp<PhysicsComponent>(grid, out var physics) ||
            physics.BodyType != BodyType.Static || !TryComp<MapGridComponent>(grid, out var gridData) ||
            _hubMaps.GetTileRef(grid, gridData, transform.Coordinates).Tile.IsEmpty)
            return;
        ent.Comp.Binding = new(rule.Comp.Generation, planet, map, transform.Coordinates,
            Transform(grid).Coordinates, Transform(grid).LocalRotation);
    }

    private bool TryGetWorkingHub(EntityUid hub, Guid generation, out SWGlobalWarHubBinding binding,
        out SWGlobalWarMarketState market)
    {
        binding = default!;
        market = default!;
        if (TerminatingOrDeleted(hub) || !TryComp<SWGlobalWarHubComponent>(hub, out var component) ||
            component.Binding is not { } bound || bound.Generation != generation ||
            TerminatingOrDeleted(bound.Planet) || TerminatingOrDeleted(bound.Map) ||
            TerminatingOrDeleted(bound.Coordinates.EntityId) ||
            !TryComp<SWGlobalWarMapAnchorComponent>(hub, out var anchor) ||
            anchor.AnchorId != "logisticsHub" || anchor.Kind != SWGlobalWarAnchorKind.Logistics ||
            !_hubPlanets.TryGetPhysicalLocation(hub, out var planet, out var location) ||
            planet != bound.Planet || location != SWGlobalWarPlanetLocation.Orbit)
            return false;
        var transform = Transform(hub);
        var grid = bound.Coordinates.EntityId;
        if (transform.MapUid != bound.Map || transform.ParentUid != grid || !transform.Anchored ||
            transform.Coordinates != bound.Coordinates || Transform(grid).Coordinates != bound.GridCoordinates ||
            Transform(grid).LocalRotation != bound.GridRotation || !_hubPlanets.IsPlanetStationGrid(planet, grid) ||
            !TryComp<PhysicsComponent>(grid, out var physics) || physics.BodyType != BodyType.Static ||
            !TryComp<MapGridComponent>(grid, out var gridData) ||
            _hubMaps.GetTileRef(grid, gridData, transform.Coordinates).Tile.IsEmpty ||
            _hubMaps.IsPaused(bound.Map) || !this.IsPowered(hub, EntityManager) ||
            TryComp<DestructibleComponent>(hub, out var destructible) && destructible.IsBroken ||
            !_hubAnchors.TryResolveAnchor(new SWGlobalWarAnchorRef
            {
                Sector = Comp<SWGlobalWarPlanetComponent>(planet).Prototype,
                Location = location, AnchorId = "logisticsHub",
            }, out var registered) || registered != hub || Simulation == null ||
            !Simulation.Markets.TryGetValue(Comp<SWGlobalWarPlanetComponent>(planet).Prototype.Id, out market!))
            return false;
        binding = bound;
        return true;
    }

    private bool CanReachHub(EntityUid body, EntityUid hub, SWGlobalWarHubBinding binding)
    {
        return !TerminatingOrDeleted(body) && Transform(body).MapUid == binding.Map &&
            TryComp<HandsComponent>(body, out var hands) && hands.Count > 0 &&
            _hubBlocker.CanInteract(body, hub) && _hubInteraction.InRangeUnobstructed(body, hub, range: 2f);
    }
}
