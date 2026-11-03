#nullable enable
using System.Linq;
using System;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
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

- type: entity
  parent: TestGlobalWar
  id: TestGlobalWarInvalid
  components:
  - type: SWGlobalWarGamerule
    planets: [TestGlobalWarAlpha, TestGlobalWarAlpha]
""";

    [Test]
    public async Task OnlyOneGlobalWarGameruleCanBeActiveAndTheSingletonIsReleased()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var ticker = server.System<GameTicker>();
        var globalWarGamerules = server.System<SWGlobalWarGameruleSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        EntityUid first = default;
        EntityUid replacement = default;
        EntityUid firstPlanet = default;
        EntityUid firstOrbit = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(globalWarGamerules.TryGetGlobalWarGamerule(out _), Is.False);
            Assert.That(planets.TryGetPlanet("TestGlobalWarAlpha", out _), Is.False);
            var pending = ticker.AddGameRule("TestGlobalWar");
            Assert.That(ticker.StartGameRule("TestGlobalWar", out first), Is.True);
            Assert.That(globalWarGamerules.TryGetGlobalWarGamerule(out var globalWarGamerule), Is.True);
            Assert.That(globalWarGamerule.Owner, Is.EqualTo(first));
            Assert.That(planets.TryGetPlanet("TestGlobalWarAlpha", out var planet), Is.True);
            firstPlanet = planet!.Value;
            Assert.That(entities.GetComponent<SWGlobalWarPlanetComponent>(firstPlanet).GlobalWarGamerule, Is.EqualTo(first));
            Assert.That(planets.TryEnsureLocation(firstPlanet, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            firstOrbit = orbit!.Value;

            Assert.That(ticker.StartGameRule("TestGlobalWar", out var duplicate), Is.False);
            Assert.That(ticker.IsGameRuleActive(duplicate), Is.False);
            Assert.That(entities.GetComponent<SWGlobalWarGameruleComponent>(duplicate).PlanetEntities, Is.Empty);
            // This one was added before the singleton existed; the Started guard must also reject it.
            ticker.StartGameRule(pending);
            Assert.That(ticker.IsGameRuleActive(pending), Is.False);
            Assert.That(entities.GetComponent<SWGlobalWarGameruleComponent>(pending).PlanetEntities, Is.Empty);
            Assert.That(globalWarGamerules.GlobalWarGamerule?.Owner, Is.EqualTo(first));
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(firstOrbit), Is.True);
            Assert.That(ticker.EndGameRule(first), Is.True);
            Assert.That(globalWarGamerules.TryGetGlobalWarGamerule(out _), Is.False);
            Assert.That(globalWarGamerules.GlobalWarGamerule, Is.Null);
            Assert.That(maps.IsPaused(firstOrbit), Is.False);

            Assert.Throws<ArgumentException>(() => ticker.StartGameRule("TestGlobalWarInvalid"));
            Assert.That(globalWarGamerules.GlobalWarGamerule, Is.Null, "A failed start must release the singleton.");
            Assert.That(ticker.StartGameRule("TestGlobalWar", out replacement), Is.True);
            Assert.That(globalWarGamerules.GlobalWarGamerule?.Owner, Is.EqualTo(replacement));
            Assert.That(planets.TryGetPlanet("TestGlobalWarAlpha", out var newPlanet), Is.True);
            Assert.That(newPlanet, Is.Not.EqualTo(firstPlanet));
            entities.DeleteEntity(first);
            Assert.That(globalWarGamerules.GlobalWarGamerule?.Owner, Is.EqualTo(replacement),
                "Deleting an ended rule must not clear its replacement.");
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(firstOrbit), Is.False, "Maps of an ended rule must not be paused by its replacement.");
            entities.DeleteEntity(replacement);
            Assert.That(globalWarGamerules.TryGetGlobalWarGamerule(out _), Is.False);
            Assert.That(globalWarGamerules.GlobalWarGamerule, Is.Null);
            Assert.That(ticker.StartGameRule("TestGlobalWar", out var last), Is.True);
            Assert.That(globalWarGamerules.GlobalWarGamerule?.Owner, Is.EqualTo(last));
            ticker.EndGameRule(last);
        });
        await pair.CleanReturnAsync();
    }

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
            Assert.That(planets.TryGetPlanet("TestGlobalWarAlpha", out var planet), Is.True);
            alpha = planet!.Value;
            Assert.That(server.Transform(alpha).MapID, Is.EqualTo(MapId.Nullspace));
            Assert.That(planets.TryEnsureLocation(alpha, SWGlobalWarPlanetLocation.Surface, out var surfaceMap, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(alpha, SWGlobalWarPlanetLocation.Orbit, out var orbitMap, out _), Is.True);
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
            planets.InitializePlanets();
            Assert.That(planets.TryEnsureLocation(alpha, SWGlobalWarPlanetLocation.Surface, out var sameMap, out _), Is.True);
            Assert.That(sameMap, Is.EqualTo(surface));
            Assert.That(planets.CanTravel("TestGlobalWarAlpha", "TestGlobalWarBeta"), Is.True);
            Assert.That(planets.CanTravel("TestGlobalWarBeta", "TestGlobalWarAlpha"), Is.False);
            Assert.That(planets.CanTravel("TestGlobalWarAlpha", "TestGlobalWarAlpha"), Is.True);

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
            Assert.That(planets.CanTravel("TestGlobalWarAlpha", "TestGlobalWarBeta"), Is.False);
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
            Assert.That(planets.TryGetPlanet("TestGlobalWarAlpha", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var surfaceMap, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(planet.Value, SWGlobalWarPlanetLocation.Orbit, out var orbitMap, out _), Is.True);
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
            Assert.That(planets.TryPrepareArrival("TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                traveller, out var destination), Is.True);
            Assert.That(destination, Is.EqualTo(orbit));
            Assert.That(maps.IsPaused(orbit), Is.False);
            Assert.That(planets.TryPrepareArrival("TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
                traveller, out _), Is.True);
            Assert.That(planets.TryPrepareArrival("TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
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
            Assert.That(planets.TryPrepareArrival("TestGlobalWarAlpha", SWGlobalWarPlanetLocation.Orbit,
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
