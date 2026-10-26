#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarPlanetTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: TestGlobalWarAlpha
  name: Alpha
  surface: Empty
  orbit: Empty
  connections: [TestGlobalWarBeta]

- type: swGlobalWarPlanet
  id: TestGlobalWarBeta
  name: Beta
  surface: Empty
  orbit: Empty

- type: entity
  parent: SWGlobalWar
  id: TestGlobalWar
  components:
  - type: SWGlobalWarGamerule
    planets: [TestGlobalWarAlpha, TestGlobalWarBeta]
    emptyMapPauseDelay: 0
""";

    [Test]
    public async Task LoadsLinkedMapsAndStopsWithoutDeletingThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var ticker = server.System<GameTicker>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        EntityUid rule = default;
        EntityUid alpha = default;
        EntityUid surface = default;
        EntityUid orbit = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("TestGlobalWar", out rule), Is.True);
            Assert.That(planets.TryGetPlanet(rule, "TestGlobalWarAlpha", out var planet), Is.True);
            alpha = planet!.Value;
            Assert.That(server.Transform(alpha).MapID, Is.EqualTo(MapId.Nullspace));
            Assert.That(planets.TryGetPlanetMap(alpha, SWGlobalWarPlanetLocation.Surface, out var surfaceMap), Is.True);
            Assert.That(planets.TryGetPlanetMap(alpha, SWGlobalWarPlanetLocation.Orbit, out var orbitMap), Is.True);
            surface = surfaceMap!.Value;
            orbit = orbitMap!.Value;
            Assert.That(surface, Is.Not.EqualTo(orbit));
            Assert.That(maps.IsInitialized(surface), Is.True);
            Assert.That(maps.IsInitialized(orbit), Is.True);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(surface).Planet, Is.EqualTo(alpha));
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(orbit).Location,
                Is.EqualTo(SWGlobalWarPlanetLocation.Orbit));

            var ruleData = entities.GetComponent<SWGlobalWarGameruleComponent>(rule);
            Assert.That(ruleData.PlanetEntities.Count, Is.EqualTo(2));
            planets.InitializePlanets((rule, ruleData));
            Assert.That(planets.TryGetPlanetMap(alpha, SWGlobalWarPlanetLocation.Surface, out var sameMap), Is.True);
            Assert.That(sameMap, Is.EqualTo(surface));
            Assert.That(planets.CanTravel(rule, "TestGlobalWarAlpha", "TestGlobalWarBeta"), Is.True);
            Assert.That(planets.CanTravel(rule, "TestGlobalWarBeta", "TestGlobalWarAlpha"), Is.False);
            Assert.That(planets.CanTravel(rule, "TestGlobalWarAlpha", "TestGlobalWarAlpha"), Is.True);

            var item = entities.SpawnEntity(null, new EntityCoordinates(surface, Vector2.Zero));
            Assert.That(planets.TryGetPlanetForEntity(item, out var owner), Is.True);
            Assert.That(owner, Is.EqualTo(alpha));
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.True);
            Assert.That(maps.IsPaused(orbit), Is.True);
            Assert.That(server.MetaData(alpha).EntityPaused, Is.False);
            Assert.That(ticker.EndGameRule(rule), Is.True);
            Assert.That(maps.IsPaused(surface), Is.False);
            Assert.That(maps.IsPaused(orbit), Is.False);
            Assert.That(entities.EntityExists(alpha), Is.True);
            Assert.That(entities.EntityExists(surface), Is.True);
            Assert.That(planets.CanTravel(rule, "TestGlobalWarAlpha", "TestGlobalWarBeta"), Is.False);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() => Assert.That(maps.IsPaused(surface), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayersAndIndependentArrivalsKeepOnlyTheirLocationAwake()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var ticker = server.System<GameTicker>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        var session = server.PlayerMan.Sessions.Single();
        EntityUid rule = default;
        EntityUid surface = default;
        EntityUid orbit = default;
        EntityUid traveller = default;
        EntityUid otherTraveller = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("TestGlobalWar", out rule), Is.True);
            Assert.That(planets.TryGetPlanet(rule, "TestGlobalWarAlpha", out var planet), Is.True);
            Assert.That(planets.TryGetPlanetMap(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var surfaceMap), Is.True);
            Assert.That(planets.TryGetPlanetMap(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbitMap), Is.True);
            surface = surfaceMap!.Value;
            orbit = orbitMap!.Value;
            var player = entities.SpawnEntity(null, new EntityCoordinates(surface, Vector2.Zero));
            server.PlayerMan.SetAttachedEntity(session, player);
            traveller = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            otherTraveller = entities.SpawnEntity(null, MapCoordinates.Nullspace);
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.False);
            Assert.That(maps.IsPaused(orbit), Is.True);
            Assert.That(planets.TryPrepareArrival(rule, "TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                traveller, out var destination), Is.True);
            Assert.That(destination, Is.EqualTo(orbit));
            Assert.That(maps.IsPaused(orbit), Is.False);
            Assert.That(planets.TryPrepareArrival(rule, "TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                traveller, out _), Is.True);
            Assert.That(planets.TryPrepareArrival(rule, "TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                otherTraveller, out _), Is.True);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(orbit).IncomingTravellers.Count, Is.EqualTo(2));
            planets.ReleaseArrival(orbit, traveller);
            planets.ReleaseArrival(orbit, traveller);
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(orbit), Is.False, "The second arrival must retain its own reservation.");
            entities.DeleteEntity(otherTraveller);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(orbit), Is.True, "A deleted traveller must not leave a permanent reservation.");
            Assert.That(planets.TryPrepareArrival(rule, "TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                traveller, out _), Is.True);
            server.System<SharedTransformSystem>().SetCoordinates(traveller, new EntityCoordinates(orbit, Vector2.Zero));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(orbit).IncomingTravellers, Is.Empty);
            Assert.That(maps.IsPaused(orbit), Is.True, "An arrived NPC or empty ship must not hold a map awake forever.");
            server.PlayerMan.SetAttachedEntity(session, null);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.True);
            ticker.EndGameRule(rule);
        });
        await pair.CleanReturnAsync();
    }
}
