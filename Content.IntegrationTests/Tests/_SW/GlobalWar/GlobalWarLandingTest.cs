using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared.Mind;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarLandingTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: LandingTestHome
  name: Landing test
  initialOwner: Republic
  surface: Empty
  orbit: Empty

- type: entity
  parent: SWGlobalWar
  id: LandingTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [LandingTestHome]

- type: entity
  parent: SWGlobalWarPlanetTeleporter
  id: LandingTestSurfaceA
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: landingA
    kind: Landing
    links:
      peer: {sector: LandingTestHome, location: Orbit, anchorId: landingA}

- type: entity
  parent: SWGlobalWarPlanetTeleporter
  id: LandingTestSurfaceB
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: landingB
    kind: Landing
    links:
      peer: {sector: LandingTestHome, location: Orbit, anchorId: landingB}

- type: entity
  parent: SWGlobalWarPlanetTeleporter
  id: LandingTestOrbitA
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: landingA
    kind: Landing
    links:
      peer: {sector: LandingTestHome, location: Surface, anchorId: landingA}

- type: entity
  parent: SWGlobalWarPlanetTeleporter
  id: LandingTestOrbitB
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: landingB
    kind: Landing
    links:
      peer: {sector: LandingTestHome, location: Surface, anchorId: landingB}
""";

    [Test]
    public async Task RestartingOnlyTheRuleResolvesNewPairsAndKeepsOldMaps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var ticker = server.System<GameTicker>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var anchors = server.System<SWGlobalWarMapAnchorSystem>();
            var manager = server.ResolveDependency<IMapManager>();
            var maps = server.System<MapSystem>();
            var reference = new SWGlobalWarAnchorRef
            {
                Sector = "LandingTestHome", Location = SWGlobalWarPlanetLocation.Orbit, AnchorId = "landingA",
            };
            EntityUid? oldMap = null;
            EntityUid? oldReceiver = null;
            EntityUid? oldRule = null;
            for (var round = 0; round < 2; round++)
            {
                Assert.That(ticker.StartGameRule("LandingTestRule", out var rule), Is.True);
                Assert.That(planets.TryGetPlanet("LandingTestHome", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var surface, out _), Is.True);
                Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
                var ground = manager.CreateGridEntity(server.Transform(surface!.Value).MapID);
                var upper = manager.CreateGridEntity(server.Transform(orbit!.Value).MapID);
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                maps.SetTiles(ground.Owner, ground.Comp, tiles);
                maps.SetTiles(upper.Owner, upper.Comp, tiles);
                var source = entities.SpawnEntity("LandingTestSurfaceA", new EntityCoordinates(ground.Owner, .5f, .5f));
                var receiver = entities.SpawnEntity("LandingTestOrbitA", new EntityCoordinates(upper.Owner, .5f, .5f));
                Assert.That(anchors.TryResolveAnchor(reference, out var resolved), Is.True);
                Assert.That(resolved, Is.EqualTo(receiver));
                var user = entities.SpawnEntity(null, new EntityCoordinates(ground.Owner, .5f, 1.5f));
                var mind = server.System<SharedMindSystem>().CreateMind(server.PlayerMan.Sessions.Single().UserId);
                server.System<SharedMindSystem>().TransferTo(mind, user);
                Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic,
                    entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation), Is.True);
                Assert.That(server.System<SWGlobalWarPlanetTeleporterSystem>().TryTeleport(source, user, out var reason), Is.True, reason);
                Assert.That(server.Transform(user).MapUid, Is.EqualTo(orbit));
                if (round == 0)
                {
                    oldMap = orbit;
                    oldReceiver = receiver;
                    oldRule = rule;
                    Assert.That(ticker.EndGameRule(rule), Is.True);
                    Assert.That(anchors.TryResolveAnchor(reference, out _), Is.False);
                }
                else
                {
                    Assert.That(entities.EntityExists(oldMap), Is.True);
                    Assert.That(entities.EntityExists(oldReceiver), Is.True);
                    entities.DeleteEntity(oldRule!.Value);
                    Assert.That(anchors.TryResolveAnchor(reference, out resolved), Is.True);
                    Assert.That(resolved, Is.EqualTo(receiver), "Late cleanup of the old rule must not clear the replacement.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TeleporterTowsACargoCrateButNeverPeople()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        EntityUid user = default;
        EntityUid crate = default;
        EntityUid source = default;
        EntityUid ground = default;
        EntityUid? surface = default;
        EntityUid hiding = default;
        EntityUid patient = default;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var maps = server.System<MapSystem>();
            var manager = server.ResolveDependency<IMapManager>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var teleporters = server.System<SWGlobalWarPlanetTeleporterSystem>();
            var pulling = server.System<PullingSystem>();
            var crates = server.System<SharedEntityStorageSystem>();
            var transforms = server.System<SharedTransformSystem>();
            server.System<GameTicker>().StartGameRule("LandingTestRule", out var rule);
            Assert.That(planets.TryGetPlanet("LandingTestHome", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out surface, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            var lower = manager.CreateGridEntity(server.Transform(surface!.Value).MapID);
            ground = lower.Owner;
            var upper = manager.CreateGridEntity(server.Transform(orbit!.Value).MapID);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -2; x <= 4; x++)
            for (var y = -2; y <= 4; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            maps.SetTiles(lower.Owner, lower.Comp, tiles);
            maps.SetTiles(upper.Owner, upper.Comp, tiles);
            source = entities.SpawnEntity("LandingTestSurfaceA", new EntityCoordinates(ground, .5f, .5f));
            entities.SpawnEntity("LandingTestOrbitA", new EntityCoordinates(upper.Owner, .5f, .5f));
            user = entities.SpawnEntity("MobHuman", new EntityCoordinates(ground, .5f, 1.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(server.PlayerMan.Sessions.Single().UserId);
            server.System<SharedMindSystem>().TransferTo(mind, user);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic,
                entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation), Is.True);

            crate = entities.SpawnEntity("SWCrateWood", new EntityCoordinates(ground, 1.5f, 1.5f));
            var cargo = entities.SpawnEntity("SWKoltoMedipen", new EntityCoordinates(ground, 1.5f, 1.5f));
            Assert.That(crates.Insert(cargo, crate), Is.True);
            Assert.That(pulling.TryStartPull(user, crate), Is.True);
            Assert.That(teleporters.TryTeleport(source, user, out var reason), Is.True, reason);
            Assert.That(server.Transform(user).MapUid, Is.EqualTo(orbit));
            Assert.That(server.Transform(crate).MapUid, Is.EqualTo(orbit), "The towed crate is not left behind.");
            Assert.That(server.Transform(user).Coordinates, Is.Not.EqualTo(server.Transform(crate).Coordinates));
            Assert.That(entities.GetComponent<EntityStorageComponent>(crate).Contents.ContainedEntities, Is.EquivalentTo(new[] { cargo }));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            Assert.That(entities.GetComponent<PullerComponent>(user).Pulling, Is.EqualTo(crate),
                "The worker keeps towing the same crate after the jump.");
            Assert.That(server.System<PullingSystem>().TryStopPull(crate, entities.GetComponent<PullableComponent>(crate)), Is.True);
            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(ground, .5f, 1.5f));
            var stowaway = entities.SpawnEntity("MobHuman", new EntityCoordinates(ground, 1.5f, 1.5f));
            hiding = entities.SpawnEntity("SWCrateWood", new EntityCoordinates(ground, 1.5f, -.5f));
            Assert.That(server.System<SharedEntityStorageSystem>().Insert(stowaway, hiding), Is.True);
            patient = entities.SpawnEntity("MobHuman", new EntityCoordinates(ground, 1.5f, 1.5f));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            // Nobody crosses without the transition check inside a crate or on a leash.
            var entities = server.EntMan;
            var teleporters = server.System<SWGlobalWarPlanetTeleporterSystem>();
            var pulling = server.System<PullingSystem>();
            Assert.That(pulling.TryStartPull(user, hiding), Is.True);
            Assert.That(teleporters.TryTeleport(source, user, out _), Is.False);
            Assert.That(pulling.TryStopPull(hiding, entities.GetComponent<PullableComponent>(hiding)), Is.True);
            Assert.That(pulling.TryStartPull(user, patient), Is.True);
            Assert.That(teleporters.TryTeleport(source, user, out _), Is.False);
            Assert.That(server.Transform(user).MapUid, Is.EqualTo(surface));
        });
        await pair.RunSeconds(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplicitPairsStaySeparateAndBlockedOrMissingExitsDoNotMoveTheUser()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var maps = server.System<MapSystem>();
            var manager = server.ResolveDependency<IMapManager>();
            var transforms = server.System<SharedTransformSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var teleporters = server.System<SWGlobalWarPlanetTeleporterSystem>();
            var anchors = server.System<SWGlobalWarMapAnchorSystem>();
            server.System<GameTicker>().StartGameRule("LandingTestRule", out var rule);
            Assert.That(planets.TryGetPlanet("LandingTestHome", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var surface, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            var groundGrid = manager.CreateGridEntity(server.Transform(surface!.Value).MapID);
            var orbitGrid = manager.CreateGridEntity(server.Transform(orbit!.Value).MapID);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -2; x <= 8; x++)
            for (var y = -2; y <= 2; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            maps.SetTiles(groundGrid.Owner, groundGrid.Comp, tiles);
            maps.SetTiles(orbitGrid.Owner, orbitGrid.Comp, tiles);
            var a = entities.SpawnEntity("LandingTestSurfaceA", new EntityCoordinates(groundGrid.Owner, .5f, .5f));
            var b = entities.SpawnEntity("LandingTestSurfaceB", new EntityCoordinates(groundGrid.Owner, 5.5f, .5f));
            var oa = entities.SpawnEntity("LandingTestOrbitA", new EntityCoordinates(orbitGrid.Owner, .5f, .5f));
            var ob = entities.SpawnEntity("LandingTestOrbitB", new EntityCoordinates(orbitGrid.Owner, 5.5f, .5f));
            var user = entities.SpawnEntity(null, new EntityCoordinates(groundGrid.Owner, .5f, 1.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(server.PlayerMan.Sessions.Single().UserId);
            server.System<SharedMindSystem>().TransferTo(mind, user);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic,
                entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation), Is.True);
            Assert.That(teleporters.TryTeleport(a, user, out var reason), Is.True, reason);
            Assert.That(server.Transform(user).MapUid, Is.EqualTo(orbit));
            Assert.That(Vector2.Distance(server.Transform(user).LocalPosition, server.Transform(oa).LocalPosition), Is.LessThan(2));
            transforms.SetCoordinates(user, new EntityCoordinates(groundGrid.Owner, 5.5f, 1.5f));
            Assert.That(teleporters.TryTeleport(b, user, out reason), Is.True, reason);
            Assert.That(Vector2.Distance(server.Transform(user).LocalPosition, server.Transform(ob).LocalPosition), Is.LessThan(2));

            transforms.SetCoordinates(user, new EntityCoordinates(groundGrid.Owner, .5f, 1.5f));
            var before = server.Transform(user).Coordinates;
            foreach (var offset in new[] { Vector2.Zero, Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
                entities.SpawnEntity("WallSolid", new EntityCoordinates(orbitGrid.Owner, new Vector2(.5f, .5f) + offset));
            Assert.That(teleporters.TryTeleport(a, user, out _), Is.False);
            Assert.That(server.Transform(user).Coordinates, Is.EqualTo(before));
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(orbit.Value).IncomingTravellers, Is.Empty);

            entities.DeleteEntity(ob);
            var missing = new SWGlobalWarAnchorRef
            {
                Sector = "LandingTestHome", Location = SWGlobalWarPlanetLocation.Orbit, AnchorId = "landingB",
            };
            Assert.That(anchors.TryResolveAnchor(missing, out _), Is.False);
            transforms.SetCoordinates(user, new EntityCoordinates(groundGrid.Owner, 5.5f, 1.5f));
            before = server.Transform(user).Coordinates;
            Assert.That(teleporters.TryTeleport(b, user, out _), Is.False);
            Assert.That(server.Transform(user).Coordinates, Is.EqualTo(before));
        });
        await pair.CleanReturnAsync();
    }
}
