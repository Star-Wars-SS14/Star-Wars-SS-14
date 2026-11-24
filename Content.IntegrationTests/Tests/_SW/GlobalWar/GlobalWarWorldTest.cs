using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Server._SW.GlobalWar;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Station.Components;
using Content.Shared.Station;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Content.Shared._SW.GlobalWar;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarWorldTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: WorldTestHome
  name: Home
  initialOwner: Republic
  surface: Empty
  orbit: Empty

- type: entity
  parent: SWGlobalWar
  id: WorldTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [WorldTestHome]
    emptyMapPauseDelay: 0
""";

    [Test]
    public async Task AtmosphericFireWakesAPausedMapAndAllowsSleepAfterFuelIsRemoved()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        var atmos = server.System<AtmosphereSystem>();
        EntityUid surface = default;
        EntityUid grid = default;
        SWGlobalWarLocationLease setup = default;
        await server.WaitAssertion(() =>
        {
            server.System<GameTicker>().StartGameRule("WorldTestRule", out _);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var planet), Is.True);
            var owner = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(planets.TryAcquireLocationLease(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                owner, Guid.NewGuid(), "test-room-setup", out setup), Is.True);
            surface = setup.Map;
            var room = server.ResolveDependency<IMapManager>().CreateGridEntity(server.Transform(surface).MapID);
            grid = room.Owner;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            maps.SetTiles(grid, room.Comp, tiles);
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x != 0 || y != 0)
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, x + .5f, y + .5f));
            }
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var gas = atmos.GetTileMixture(grid, surface, Vector2i.Zero, true);
            Assert.That(gas, Is.Not.Null);
            Assert.That(gas!.Immutable, Is.False);
            gas.SetMoles(Gas.Oxygen, 100);
            gas.SetMoles(Gas.Plasma, 100);
            atmos.HotspotExpose(grid, Vector2i.Zero, 1000, 50);
            Assert.That(server.EntMan.GetComponent<GridAtmosphereComponent>(grid).HotspotTilesCount, Is.GreaterThan(0));
            planets.ReleaseLocationLease(setup);
            maps.SetPaused(surface, true);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.False);
            Assert.That(server.EntMan.GetComponent<GridAtmosphereComponent>(grid).HotspotTilesCount, Is.GreaterThan(0));
            var gas = atmos.GetTileMixture(grid, surface, Vector2i.Zero, true)!;
            gas.SetMoles(Gas.Plasma, 0);
            gas.SetMoles(Gas.Tritium, 0);
            gas.SetMoles(Gas.Oxygen, 0);
        });
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GridAtmosphereComponent>(grid).HotspotTilesCount, Is.Zero);
            Assert.That(maps.IsPaused(surface), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedStationLoadRollsBackOnlyItsAttemptAndCanBeRetried()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var entities = server.EntMan;
            var failure = server.System<SWGlobalWarLoadFailureTestSystem>();
            server.System<GameTicker>().StartGameRule("WorldTestRule", out _);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var neighbor, out _), Is.True);
            var item = entities.SpawnEntity("Screwdriver", new EntityCoordinates(neighbor!.Value, 3, 4));
            var originalStations = entities.EntityQuery<StationDataComponent>(true).Select(s => s.Owner).ToArray();
            failure.FailNext = true;
            Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.False);
            Assert.That(failure.FailedStation, Is.Not.Null, "Inject failure after StationSystem creates the station.");
            Assert.That(entities.EntityExists(failure.FailedStation), Is.False);
            Assert.That(entities.EntityQuery<StationDataComponent>(true).Select(s => s.Owner), Is.EquivalentTo(originalStations));
            Assert.That(entities.EntityExists(neighbor), Is.True);
            Assert.That(server.Transform(item).Coordinates.Position, Is.EqualTo(new Vector2(3, 4)));
            Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            Assert.That(orbit, Is.Not.EqualTo(neighbor));
            Assert.That(entities.EntityQuery<StationDataComponent>(true).Count(), Is.EqualTo(originalStations.Length + 1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveTimerWakesAPausedMapAndAllowsSleepAfterFiring()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var maps = server.System<MapSystem>();
        EntityUid surface = default;
        EntityUid timer = default;
        await server.WaitAssertion(() =>
        {
            var planets = server.System<SWGlobalWarPlanetSystem>();
            server.System<GameTicker>().StartGameRule("WorldTestRule", out _);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            surface = map!.Value;
            timer = server.EntMan.SpawnEntity(null, new EntityCoordinates(surface, 3, 4));
            var component = server.EntMan.AddComponent<TimerTriggerComponent>(timer);
            component.Delay = TimeSpan.FromSeconds(5);
            component.Popup = null;
            Assert.That(server.System<TriggerSystem>().ActivateTimerTrigger((timer, component)), Is.True);
            maps.SetPaused(surface, true);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.False);
            Assert.That(server.EntMan.HasComponent<ActiveTimerTriggerComponent>(timer), Is.True);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<ActiveTimerTriggerComponent>(timer), Is.False);
            Assert.That(maps.IsPaused(surface), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CampaignStartsAll22LogicalSectorsWithoutLoadingPhysicalMaps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<GameTicker>().StartGameRule("SWGlobalWarCampaign", out var rule), Is.True);
            var registry = server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(rule);
            var expected = Enumerable.Range(0, 10).Select(i => $"SWGlobalWarR{i}")
                .Concat(Enumerable.Range(0, 10).Select(i => $"SWGlobalWarE{i}"))
                .Concat(new[] { "SWGlobalWarN0", "SWGlobalWarN1" }).ToArray();
            Assert.That(registry.PlanetEntities.Keys.Select(id => id.Id), Is.EquivalentTo(expected));
            Assert.That(registry.PlanetEntities.Values.Distinct().Count(), Is.EqualTo(22));
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            foreach (var (id, uid) in registry.PlanetEntities)
            {
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(uid);
                var owner = id.Id.StartsWith("SWGlobalWarR") ? SWGlobalWarFaction.Republic :
                    id.Id.StartsWith("SWGlobalWarE") ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Neutral;
                Assert.That(planet.OrbitOwner, Is.EqualTo(owner));
                Assert.That(planet.SurfaceOwner, Is.EqualTo(owner));
                Assert.That(planet.Maps, Is.Empty);
                Assert.That(planet.Locations.Values, Is.All.EqualTo(SWGlobalWarLocationState.Unloaded));
            }
            foreach (var side in new[] { "R", "E" })
            {
                var enclave = prototypes.Index<SWGlobalWarPlanetPrototype>($"SWGlobalWar{side}6");
                var capital = prototypes.Index<SWGlobalWarPlanetPrototype>($"SWGlobalWar{side}0");
                var force = prototypes.Index<SWGlobalWarPlanetPrototype>($"SWGlobalWar{side}9");
                Assert.That(enclave.Enclave, Is.True);
                Assert.That(enclave.Kind, Is.EqualTo(SWGlobalWarSectorKind.Mining));
                Assert.That(enclave.Connections.Select(id => id.Id), Does.Contain(capital.ID));
                Assert.That(capital.Connections.Select(id => id.Id), Does.Contain(enclave.ID));
                Assert.That(force.Enclave, Is.False);
                Assert.That(force.Kind, Is.EqualTo(SWGlobalWarSectorKind.Force));
            }
            Assert.That(prototypes.Index<SWGlobalWarPlanetPrototype>("SWGlobalWarN1").Kind,
                Is.EqualTo(SWGlobalWarSectorKind.Industry));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnershipChangesAreIndependentAndRejectStaleOrReusedOperations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            Assert.That(ticker.StartGameRule("WorldTestRule", out var rule), Is.True);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var uid), Is.True);
            var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(uid!.Value);
            var generation = server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            var operation = Guid.NewGuid();
            var listener = server.System<SWGlobalWarLoadFailureTestSystem>();
            listener.OwnershipChanges.Clear();
            Assert.That(planets.TryChangeOwnership(uid.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarFaction.Empire, 0, generation, operation, out var revision), Is.True);
            Assert.That(revision, Is.EqualTo(1));
            Assert.That(planet.OrbitOwner, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(planet.SurfaceOwner, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(listener.OwnershipChanges.Count, Is.EqualTo(1));
            Assert.That(listener.OwnershipChanges[0].Planet, Is.EqualTo(uid.Value));
            Assert.That(listener.OwnershipChanges[0].PreviousOwner, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(listener.OwnershipChanges[0].Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(planets.TryChangeOwnership(uid.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarFaction.Empire, 0, generation, operation, out revision), Is.True);
            Assert.That(revision, Is.EqualTo(1));
            Assert.That(planets.TryChangeOwnership(uid.Value, SWGlobalWarPlanetLocation.Surface,
                SWGlobalWarFaction.Empire, 1, generation, operation, out _), Is.False);
            Assert.That(planets.TryChangeOwnership(uid.Value, SWGlobalWarPlanetLocation.Surface,
                SWGlobalWarFaction.Empire, 0, generation, Guid.NewGuid(), out _), Is.False);
            Assert.That(planet.SurfaceOwner, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(ticker.EndGameRule(rule), Is.True);
            Assert.That(planets.TryChangeOwnership(uid.Value, SWGlobalWarPlanetLocation.Surface,
                SWGlobalWarFaction.Empire, 1, generation, Guid.NewGuid(), out _), Is.False);
            Assert.That(planet.OrbitOwner, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(planet.SurfaceOwner, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(listener.OwnershipChanges.Count, Is.EqualTo(1), "Replays and rejected mutations publish no extra event.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeasesKeepAVisitedMapAndItsEntitiesAliveWithoutRecreatingDestroyedMaps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        EntityUid planet = default;
        EntityUid surface = default;
        EntityUid item = default;
        SWGlobalWarLocationLease first = default;
        SWGlobalWarLocationLease second = default;
        await server.WaitAssertion(() =>
        {
            server.System<GameTicker>().StartGameRule("WorldTestRule", out _);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var uid), Is.True);
            planet = uid!.Value;
            Assert.That(planets.TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Surface, out _), Is.False);
            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            surface = map!.Value;
            item = server.EntMan.SpawnEntity("Screwdriver", new EntityCoordinates(surface, new Vector2(3, 4)));
            var holderA = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var holderB = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            Assert.That(planets.TryAcquireLocationLease(planet, SWGlobalWarPlanetLocation.Surface,
                holderA, Guid.NewGuid(), "arrival", out first), Is.True);
            Assert.That(planets.TryAcquireLocationLease(planet, SWGlobalWarPlanetLocation.Surface,
                holderB, Guid.NewGuid(), "production", out second), Is.True);
            planets.ReleaseLocationLease(first);
            planets.ReleaseLocationLease(first);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.False, "The remaining operation retains its own lease.");
            planets.ReleaseLocationLease(second);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.True);
            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            Assert.That(map, Is.EqualTo(surface));
            Assert.That(server.Transform(item).Coordinates.Position, Is.EqualTo(new Vector2(3, 4)));
            Assert.That(server.Transform(item).MapUid, Is.EqualTo(surface));
            Assert.That(planets.TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Orbit, out _), Is.False);
            server.EntMan.DeleteEntity(surface);
            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out _, out _), Is.False);
            Assert.That(server.EntMan.EntityExists(item), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}

/// <summary>Injects an exception after station creation but before LoadGameMap returns to its caller.</summary>
public sealed class SWGlobalWarLoadFailureTestSystem : EntitySystem
{
    [Dependency] private readonly SharedStationSystem _stations = default!;
    public bool FailNext;
    public EntityUid? FailedStation;
    public readonly List<SWGlobalWarOwnershipChangedEvent> OwnershipChanges = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PostGameMapLoad>(OnLoaded, after: [typeof(StationSystem)]);
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>(OnOwnershipChanged);
    }

    private void OnOwnershipChanged(ref SWGlobalWarOwnershipChangedEvent args)
    {
        OwnershipChanges.Add(args);
    }

    private void OnLoaded(PostGameMapLoad args)
    {
        if (!FailNext)
            return;
        FailNext = false;
        FailedStation = args.Grids.Select(grid => _stations.GetOwningStation(grid)).FirstOrDefault(s => s != null);
        throw new InvalidOperationException("Deliberate GlobalWar load failure after station creation.");
    }
}
