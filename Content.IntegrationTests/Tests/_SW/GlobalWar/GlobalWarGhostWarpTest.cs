using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.GhostWarp;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Ghost;
using Content.Shared.Warps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarGhostWarpTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: GhostWarpTestHome
  name: Home
  initialOwner: Republic
  surface: Empty
  orbit: Empty

- type: swGlobalWarPlanet
  id: GhostWarpTestFar
  name: Far
  initialOwner: Empire
  surface: Empty

- type: entity
  parent: SWGlobalWar
  id: GhostWarpTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [GhostWarpTestHome, GhostWarpTestFar]
    emptyMapPauseDelay: 0
""";

    [Test]
    public async Task GhostWarpListsEveryLocationAndWakesTheOneItReaches()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        await pair.CreateTestMap();
        var session = server.PlayerMan.Sessions.Single();
        EntityUid ghost = default;
        EntityUid planet = default;
        EntityUid surface = default;
        await server.WaitAssertion(() =>
        {
            server.System<GameTicker>().StartGameRule("GhostWarpTestRule", out _);
            Assert.That(planets.TryGetPlanet("GhostWarpTestHome", out var home), Is.True);
            planet = home!.Value;
            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            surface = map!.Value;
            ghost = entMan.SpawnEntity("MobObserver", pair.TestMap!.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, ghost);
        });
        // With no player there and no pause delay the surface goes to sleep.
        await pair.RunSeconds(2);

        await pair.Client.WaitPost(() => pair.Client.EntMan.EntityNetManager.SendSystemNetworkMessage(new GhostWarpsRequestEvent()));
        await pair.RunTicksSync(10);

        NetEntity surfaceWarp = default;
        NetEntity orbitWarp = default;
        NetEntity farWarp = default;
        EntityUid far = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<MetaDataComponent>(surface).EntityPaused, Is.True);
            Assert.That(planets.TryGetPlanet("GhostWarpTestFar", out var farPlanet), Is.True);
            far = farPlanet!.Value;
            var beacons = entMan.AllEntities<SWGlobalWarGhostWarpComponent>().ToArray();
            // Every surface and orbit that exists, loaded or not; the far world has no orbit.
            Assert.That(beacons.Select(b => (b.Comp.Planet, b.Comp.Location)), Is.EquivalentTo(new[]
            {
                (planet, SWGlobalWarPlanetLocation.Surface), (planet, SWGlobalWarPlanetLocation.Orbit),
                (far, SWGlobalWarPlanetLocation.Surface),
            }));
            foreach (var beacon in beacons)
            {
                Assert.That(entMan.GetComponent<WarpPointComponent>(beacon).Location, Is.Not.Empty);
                var net = entMan.GetNetEntity(beacon);
                if (beacon.Comp.Planet == far)
                    farWarp = net;
                else if (beacon.Comp.Location == SWGlobalWarPlanetLocation.Surface)
                    surfaceWarp = net;
                else
                    orbitWarp = net;
            }
        });

        await Warp(pair, surfaceWarp);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(ghost).MapUid, Is.EqualTo(surface));
            Assert.That(entMan.GetComponent<MetaDataComponent>(surface).EntityPaused, Is.False,
                "A ghost that warped here keeps the location awake.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(ghost).EntityPaused, Is.False);
        });

        // The test client is the host admin, so it may load an orbit nobody visited.
        await Warp(pair, orbitWarp);
        await server.WaitAssertion(() =>
        {
            Assert.That(planets.TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Orbit, out var orbit), Is.True);
            Assert.That(server.Transform(ghost).MapUid, Is.EqualTo(orbit));
            server.ResolveDependency<IAdminManager>().DeAdmin(session);
        });
        await pair.RunTicksSync(5);

        // Without admin rights an unvisited world stays unloaded and the ghost stays where it was.
        await Warp(pair, farWarp);
        await server.WaitAssertion(() =>
        {
            Assert.That(planets.TryGetPlanetMap(far, SWGlobalWarPlanetLocation.Surface, out _), Is.False);
            Assert.That(planets.TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Orbit, out var orbit), Is.True);
            Assert.That(server.Transform(ghost).MapUid, Is.EqualTo(orbit));
        });

        // Leaving the location gives the lease back, so the map can sleep again.
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(ghost, pair.TestMap!.GridCoords));
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(planets.TryGetPlanetMap(planet, SWGlobalWarPlanetLocation.Orbit, out var orbit), Is.True);
            Assert.That(entMan.GetComponent<MetaDataComponent>(surface).EntityPaused, Is.True);
            Assert.That(entMan.GetComponent<MetaDataComponent>(orbit!.Value).EntityPaused, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    private static async Task Warp(Content.IntegrationTests.Pair.TestPair pair, NetEntity target)
    {
        await pair.Client.WaitPost(() =>
            pair.Client.EntMan.EntityNetManager.SendSystemNetworkMessage(new GhostWarpToTargetRequestEvent(target)));
        await pair.RunTicksSync(10);
        await pair.RunSeconds(2);
    }
}
