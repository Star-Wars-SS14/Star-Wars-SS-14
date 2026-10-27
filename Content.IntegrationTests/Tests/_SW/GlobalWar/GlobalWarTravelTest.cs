#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarTravelTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: TravelTestAlpha
  name: Alpha
  surface: Empty
  orbit: Empty
  connections: [TravelTestBeta]

- type: swGlobalWarPlanet
  id: TravelTestBeta
  name: Beta
  surface: Empty
  orbit: Empty

- type: entity
  parent: SWGlobalWar
  id: TravelTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [TravelTestAlpha, TravelTestBeta]
    emptyMapPauseDelay: 0

- type: entity
  parent: SWGlobalWarTravelConsole
  id: TravelTestConsole
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: TravelTestExclusion
  components:
  - type: FTLExclusion
    range: 10000
""";

    [Test]
    public async Task ValidatesRoutesAndSafelyTransfersTheWholeShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var ticker = server.System<GameTicker>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var travel = server.System<SWGlobalWarTravelSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var maps = server.System<MapSystem>();
        var transforms = server.System<SharedTransformSystem>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid alphaOrbit = default;
        EntityUid betaOrbit = default;
        EntityUid betaSurface = default;
        EntityUid ship = default;
        EntityUid console = default;
        EntityUid player = default;
        EntityUid cargo = default;
        EntityUid exclusion = default;
        SWGlobalWarTravelConsoleState initialState = default!;
        SWGlobalWarTravelConsoleState transitState = default!;
        SWGlobalWarTravelConsoleState arrivedState = default!;

        await server.WaitAssertion(() =>
        {
            config.SetCVar(CCVars.FTLStartupTime, 0.1f);
            config.SetCVar(CCVars.FTLTravelTime, 1f);
            config.SetCVar(CCVars.FTLArrivalTime, 0.2f);
            config.SetCVar(CCVars.FTLCooldown, 0.1f);
            Assert.That(ticker.StartGameRule("TravelTestRule", out var rule), Is.True);
            Assert.That(planets.TryGetPlanet("TravelTestAlpha", out var alpha), Is.True);
            Assert.That(planets.TryGetPlanet("TravelTestBeta", out var beta), Is.True);
            Assert.That(planets.TryGetPlanetMap(alpha!.Value, SWGlobalWarPlanetLocation.Orbit, out var a), Is.True);
            Assert.That(planets.TryGetPlanetMap(beta!.Value, SWGlobalWarPlanetLocation.Orbit, out var b), Is.True);
            Assert.That(planets.TryGetPlanetMap(beta.Value, SWGlobalWarPlanetLocation.Surface, out var surface), Is.True);
            alphaOrbit = a!.Value;
            betaOrbit = b!.Value;
            betaSurface = surface!.Value;

            var stationGrid = mapManager.CreateGridEntity(server.Transform(betaOrbit).MapID);
            maps.SetTiles(stationGrid.Owner, stationGrid.Comp,
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StationDataComponent>(station);
            server.System<StationSystem>().AddGridToStation(station, stationGrid);
            // Empty game maps have no station grids; register this synthetic test station.
#pragma warning disable RA0002
            entities.GetComponent<SWGlobalWarPlanetComponent>(beta.Value).StationGrids.Add(stationGrid);
#pragma warning restore RA0002
            var destination = entities.EnsureComponent<FTLDestinationComponent>(betaOrbit);
            destination.Enabled = true;
            destination.RequireCoordinateDisk = false;

            var grid = mapManager.CreateGridEntity(server.Transform(alphaOrbit).MapID);
            ship = grid.Owner;
            maps.SetTiles(grid.Owner, grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(1, 0), new Tile(1)),
                (new Vector2i(1, 1), new Tile(1)),
            });
            // Planet-local coordinates must not impose the normal 256 m FTL range on interplanetary travel.
            transforms.SetLocalPosition(ship, new Vector2(1000, 1000));
            console = entities.SpawnEntity("TravelTestConsole", new EntityCoordinates(ship, 0.5f, 0.5f));
            Assert.That(server.Transform(console).Anchored, Is.True);
            player = entities.SpawnEntity(null, new EntityCoordinates(ship, 0.5f, 1.5f));
            cargo = entities.SpawnEntity(null, new EntityCoordinates(ship, 1.5f, 1.5f));
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(betaOrbit), Is.True);
            var state = travel.GetState(console);
            initialState = state;
            Assert.That(state.CurrentPlanet, Is.EqualTo("Alpha"));
            Assert.That(state.Destinations.Single(d => d.Id == "TravelTestBeta").Available, Is.True);
            Assert.That(state.Destinations.Single(d => d.Id == "TravelTestAlpha").Available, Is.False);
            Assert.That(travel.TryStartTravel(console, player, "TravelTestAlpha", out _), Is.False);
            Assert.That(travel.TryStartTravel(console, player, "MissingPlanet", out _), Is.False);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False);

            // FTLFree must reject exclusion zones, including when the destination was asleep.
            exclusion = entities.SpawnEntity("TravelTestExclusion", new EntityCoordinates(betaOrbit, Vector2.Zero));
            Assert.That(travel.TryStartTravel(console, player, "TravelTestBeta", out _), Is.False);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(betaOrbit).IncomingTravellers, Is.Empty);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            entities.DeleteEntity(exclusion);

            // Removing an aborted FTL must release its reservation even without a completion event.
            shuttles.DefaultStartupTime = 10f;
            Assert.That(travel.TryStartTravel(console, player, "TravelTestBeta", out var cancelReason), Is.True, cancelReason);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), null);
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(alphaOrbit), Is.False, "Startup must continue even if the crew disconnects.");
            entities.RemoveComponent<FTLComponent>(ship);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(betaOrbit).IncomingTravellers, Is.Empty);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            shuttles.DefaultStartupTime = 0.1f;
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            Assert.That(travel.TryStartTravel(console, player, "TravelTestBeta", out var reason), Is.True, reason);
            Assert.That(maps.IsPaused(betaOrbit), Is.False);
            Assert.That(maps.IsPaused(betaSurface), Is.True);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(betaOrbit).IncomingTravellers, Contains.Item(ship));
            Assert.That(travel.TryStartTravel(console, player, "TravelTestBeta", out _), Is.False);

            // Occupy every exit AFTER departure was scheduled: the final arrival check must wait.
            exclusion = entities.SpawnEntity("TravelTestExclusion", new EntityCoordinates(betaOrbit, Vector2.Zero));
        });

        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FTLComponent>(ship).State, Is.EqualTo(FTLState.Arriving));
            Assert.That(server.Transform(ship).MapUid, Is.Not.EqualTo(betaOrbit));
            Assert.That(maps.IsPaused(betaOrbit), Is.False);
            transitState = travel.GetState(console);
            Assert.That(transitState.CurrentPlanet, Is.EqualTo("Alpha"));
            Assert.That(transitState.Destinations.All(d => !d.Available), Is.True);
            entities.DeleteEntity(exclusion);
        });

        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(ship).MapUid, Is.EqualTo(betaOrbit));
            Assert.That(server.Transform(player).MapUid, Is.EqualTo(betaOrbit));
            Assert.That(server.Transform(cargo).GridUid, Is.EqualTo(ship));
            Assert.That(server.Transform(cargo).MapUid, Is.EqualTo(betaOrbit));
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(betaOrbit).IncomingTravellers, Is.Empty);
            Assert.That(maps.IsPaused(alphaOrbit), Is.True);
            Assert.That(maps.IsPaused(betaSurface), Is.True);
            // Connections are directional. Beta has no route back to Alpha.
            Assert.That(travel.TryStartTravel(console, player, "TravelTestAlpha", out _), Is.False);
            arrivedState = travel.GetState(console);
            Assert.That(arrivedState.CurrentPlanet, Is.EqualTo("Beta"));
            Assert.That(arrivedState.Destinations.All(d => !d.Available), Is.True);
            var center = server.System<SharedMapSystem>().GetGridPosition(ship);
            Assert.That(shuttles.FTLFree(ship, new EntityCoordinates(betaOrbit, center), Angle.Zero, null, checkRange: false), Is.True);
        });
        await pair.Client.WaitAssertion(() =>
        {
            // Exercise XAML loading and UI state changes on the client, including the hyperspace state.
            using var menu = new SWGlobalWarTravelConsoleMenu();
            menu.UpdateState(initialState);
            menu.UpdateState(transitState);
            menu.UpdateState(arrivedState);
        });
        await pair.CleanReturnAsync();
    }
}
