#nullable enable
using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarTravelTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: TravelTestAlpha
  name: Alpha
  initialOwner: Republic
  surface: Empty
  orbit: Empty
  connections: [TravelTestBeta]

- type: swGlobalWarPlanet
  id: TravelTestBeta
  name: Beta
  initialOwner: Republic
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
            Assert.That(planets.TryEnsureLocation(alpha!.Value, SWGlobalWarPlanetLocation.Orbit, out var a, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(beta!.Value, SWGlobalWarPlanetLocation.Orbit, out var b, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(beta.Value, SWGlobalWarPlanetLocation.Surface, out var surface, out _), Is.True);
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
            var mind = server.System<SharedMindSystem>().CreateMind(server.PlayerMan.Sessions.Single().UserId);
            server.System<SharedMindSystem>().TransferTo(mind, player);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic,
                entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation), Is.True);
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task MixedFactionPassengerRemainsSubjectToFtlPolicyAfterDisconnect(bool dock)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var player = server.PlayerMan.Sessions.Single();
        var pilotSession = await server.AddDummySession();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var transforms = server.System<SharedTransformSystem>();
        var shuttles = server.System<ShuttleSystem>();
        EntityUid ship = default;
        EntityUid passenger = default;
        EntityUid sourceMap = default;
        EntityUid destinationMap = default;
        EntityUid targetGrid = default;
        void Depart()
        {
            if (dock)
                shuttles.FTLToDock(ship, entities.GetComponent<ShuttleComponent>(ship), targetGrid);
            else
                shuttles.FTLToCoordinates(ship, entities.GetComponent<ShuttleComponent>(ship),
                    new EntityCoordinates(destinationMap, 1000, 1000), Angle.Zero);
        }
        await server.WaitAssertion(() =>
        {
            server.System<GameTicker>().StartGameRule("TravelTestRule", out var rule);
            var generation = entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            Assert.That(planets.TryGetPlanet("TravelTestAlpha", out var source), Is.True);
            Assert.That(planets.TryGetPlanet("TravelTestBeta", out var destination), Is.True);
            Assert.That(planets.TryEnsureLocation(source!.Value, SWGlobalWarPlanetLocation.Orbit, out var sm, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(destination!.Value, SWGlobalWarPlanetLocation.Orbit, out var dm, out _), Is.True);
            sourceMap = sm!.Value;
            destinationMap = dm!.Value;
            var manager = server.ResolveDependency<IMapManager>();
            var grid = manager.CreateGridEntity(server.Transform(sourceMap).MapID);
            ship = grid.Owner;
            server.System<MapSystem>().SetTiles(ship, grid.Comp,
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(1, 0), new Tile(1)) });
            var target = manager.CreateGridEntity(server.Transform(destinationMap).MapID);
            targetGrid = target.Owner;
            server.System<MapSystem>().SetTile(target, Vector2i.Zero, new Tile(1));
            var pilot = entities.SpawnEntity(null, new EntityCoordinates(ship, .5f, .5f));
            passenger = entities.SpawnEntity(null, new EntityCoordinates(ship, 1.5f, .5f));
            var minds = server.System<SharedMindSystem>();
            var pilotMind = minds.CreateMind(pilotSession.UserId);
            var passengerMind = minds.CreateMind(player.UserId);
            minds.TransferTo(pilotMind, pilot);
            minds.TransferTo(passengerMind, passenger);
            var identities = server.System<SWGlobalWarIdentitySystem>();
            Assert.That(identities.TryEnroll(pilotMind, SWGlobalWarFaction.Republic, generation), Is.True);
            Assert.That(identities.TryEnroll(passengerMind, SWGlobalWarFaction.Empire, generation), Is.True);
            Assert.That(pilotSession.UserId, Is.Not.EqualTo(player.UserId));
            Assert.That(server.Transform(pilot).GridUid, Is.EqualTo(ship));
            Assert.That(server.Transform(passenger).GridUid, Is.EqualTo(ship));
            Depart();
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("SSD passenger test"));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(player.Status, Is.EqualTo(Robust.Shared.Enums.SessionStatus.Disconnected));
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetIdentity(passenger, out _, out var faction), Is.True);
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Empire));
            Depart();
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            Assert.That(server.Transform(ship).MapUid, Is.EqualTo(sourceMap));
            Assert.That(server.Transform(passenger).GridUid, Is.EqualTo(ship));
            transforms.SetCoordinates(passenger, new EntityCoordinates(sourceMap, 5000, 5000));
            Depart();
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false, false, false)]
    [TestCase(false, true, false, false)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, false, false)]
    [TestCase(false, true, true, false)]
    [TestCase(true, true, true, false)]
    [TestCase(true, false, false, true)]
    public async Task OrdinaryFtlChecksPassengersAndReevaluatesOwnershipBeforeArrival(
        bool dock, bool captured, bool sourceCaptured, bool realDocks)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var transforms = server.System<SharedTransformSystem>();
        EntityUid ship = default;
        EntityUid actor = default;
        EntityUid sourceMap = default;
        EntityUid destinationMap = default;
        EntityUid destinationPlanet = default;
        EntityUid sourcePlanet = default;
        EntityUid targetGrid = default;
        EntityUid sourceDock = default;
        EntityUid targetDock = default;
        Angle expectedDockAngle = default;
        Guid generation = default;
        await server.WaitAssertion(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(CCVars.FTLStartupTime, .1f);
            config.SetCVar(CCVars.FTLTravelTime, 4f);
            config.SetCVar(CCVars.FTLArrivalTime, .2f);
            config.SetCVar(CCVars.FTLCooldown, .1f);
            server.System<GameTicker>().StartGameRule("TravelTestRule", out var rule);
            generation = entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            Assert.That(planets.TryGetPlanet("TravelTestAlpha", out var source), Is.True);
            sourcePlanet = source!.Value;
            Assert.That(planets.TryGetPlanet("TravelTestBeta", out var destination), Is.True);
            destinationPlanet = destination!.Value;
            Assert.That(planets.TryEnsureLocation(source!.Value, SWGlobalWarPlanetLocation.Orbit, out var sm, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(destinationPlanet, SWGlobalWarPlanetLocation.Orbit, out var dm, out _), Is.True);
            sourceMap = sm!.Value;
            destinationMap = dm!.Value;
            var manager = server.ResolveDependency<IMapManager>();
            var maps = server.System<MapSystem>();
            var grid = manager.CreateGridEntity(server.Transform(sourceMap).MapID);
            ship = grid.Owner;
            maps.SetTiles(ship, grid.Comp, realDocks
                ? new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(0, 1), new Tile(1)), (new Vector2i(0, 2), new Tile(1)) }
                : new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(1, 0), new Tile(1)) });
            var target = manager.CreateGridEntity(server.Transform(destinationMap).MapID);
            targetGrid = target.Owner;
            maps.SetTiles(target.Owner, target.Comp, realDocks
                ? new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(0, 1), new Tile(1)), (new Vector2i(0, 2), new Tile(1)), (new Vector2i(-1, 2), new Tile(1)), (new Vector2i(1, 2), new Tile(1)) }
                : new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)) });
            if (realDocks)
            {
                transforms.SetLocalPosition(targetGrid, new Vector2(50, 50));
                transforms.SetLocalRotation(targetGrid, new Angle(.4));
                sourceDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(ship, .5f, .5f));
                targetDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(targetGrid, .5f, .5f));
                Assert.That(server.System<DockingSystem>().GetDockingConfig(ship, targetGrid), Is.Not.Null);
            }
            actor = entities.SpawnEntity(null, new EntityCoordinates(ship, .5f, .5f));
            Assert.That(server.Transform(actor).GridUid, Is.EqualTo(ship));
            var minds = server.System<SharedMindSystem>();
            var mind = minds.CreateMind(server.PlayerMan.Sessions.Single().UserId);
            minds.TransferTo(mind, actor);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);
            var passenger = entities.SpawnEntity(null, realDocks
                ? new EntityCoordinates(ship, .5f, 1.5f)
                : new EntityCoordinates(ship, 1.5f, .5f));
            Assert.That(server.Transform(passenger).GridUid, Is.EqualTo(ship));
            minds.TransferTo(minds.CreateMind(null), passenger);
            var before = server.Transform(ship).Coordinates;
            void Depart()
            {
                if (dock)
                    shuttles.FTLToDock(ship, entities.GetComponent<ShuttleComponent>(ship), target.Owner);
                else
                    shuttles.FTLToCoordinates(ship, entities.GetComponent<ShuttleComponent>(ship),
                        new EntityCoordinates(destinationMap, 1000, 1000), Angle.Zero);
            }
            Depart();
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False, "One permitted pilot cannot transport an unauthorized passenger.");
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            Assert.That(server.Transform(ship).Coordinates, Is.EqualTo(before));
            transforms.SetCoordinates(passenger, new EntityCoordinates(sourceMap, 5000, 5000));
            Depart();
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.True);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(ship).MapUid, Is.Not.EqualTo(sourceMap));
            Assert.That(server.Transform(ship).MapUid, Is.Not.EqualTo(destinationMap));
            if (realDocks)
            {
                transforms.SetLocalRotation(targetGrid, new Angle(.9));
                var docking = server.System<DockingSystem>().GetDockingConfig(ship, targetGrid);
                Assert.That(docking, Is.Not.Null);
                expectedDockAngle = docking!.Angle + transforms.GetWorldRotation(targetGrid);
            }
            if (captured)
                Assert.That(planets.TryChangeOwnership(destinationPlanet, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, 0, generation, Guid.NewGuid(), out _), Is.True);
            if (sourceCaptured)
                Assert.That(planets.TryChangeOwnership(sourcePlanet, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, 0, generation, Guid.NewGuid(), out _), Is.True);
        });
        await pair.RunSeconds(6);
        if (sourceCaptured)
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(server.Transform(ship).MapUid, Is.Not.EqualTo(sourceMap));
                Assert.That(server.Transform(ship).MapUid, Is.Not.EqualTo(destinationMap));
                Assert.That(server.Transform(actor).GridUid, Is.EqualTo(ship));
                Assert.That(entities.GetComponent<FTLComponent>(ship).State, Is.EqualTo(FTLState.Arriving));
                Assert.That(entities.GetComponent<SWGlobalWarTravelComponent>(ship).Returning, Is.True);
                Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(destinationMap).IncomingTravellers, Is.Empty);
                Assert.That(planets.TryChangeOwnership(sourcePlanet, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Republic, 1, generation, Guid.NewGuid(), out _), Is.True);
            });
            await pair.RunSeconds(12);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(server.Transform(ship).MapUid, Is.EqualTo(captured ? sourceMap : destinationMap));
            Assert.That(server.Transform(actor).GridUid, Is.EqualTo(ship));
            if (realDocks)
            {
                Assert.That(entities.GetComponent<DockingComponent>(sourceDock).DockedWith, Is.EqualTo(targetDock));
                Assert.That(entities.GetComponent<DockingComponent>(targetDock).DockedWith, Is.EqualTo(sourceDock));
                Assert.That(transforms.GetWorldRotation(ship).EqualsApprox(expectedDockAngle, .01), Is.True);
            }
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False);
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(destinationMap).IncomingTravellers, Is.Empty);
            Assert.That(entities.GetComponent<SWGlobalWarPlanetMapComponent>(sourceMap).IncomingTravellers, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HelmFtlGoesOnlyAlongHyperroutes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var shuttles = server.System<ShuttleSystem>();
        await server.WaitAssertion(() =>
        {
            server.System<GameTicker>().StartGameRule("TravelTestRule", out var rule);
            var generation = entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            Assert.That(planets.TryGetPlanet("TravelTestAlpha", out var alpha), Is.True);
            Assert.That(planets.TryGetPlanet("TravelTestBeta", out var beta), Is.True);
            Assert.That(planets.TryEnsureLocation(alpha!.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(alpha.Value, SWGlobalWarPlanetLocation.Surface, out var surface, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(beta!.Value, SWGlobalWarPlanetLocation.Orbit, out var next, out _), Is.True);
            // A surface is never a jump target, so the helm's map does not list it.
            Assert.That(entities.HasComponent<FTLDestinationComponent>(surface!.Value), Is.False);

            var manager = server.ResolveDependency<IMapManager>();
            var grid = manager.CreateGridEntity(server.Transform(orbit!.Value).MapID);
            var ship = grid.Owner;
            server.System<MapSystem>().SetTiles(ship, grid.Comp,
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(1, 0), new Tile(1)) });
            var pilot = entities.SpawnEntity(null, new EntityCoordinates(ship, .5f, .5f));
            var minds = server.System<SharedMindSystem>();
            var mind = minds.CreateMind(server.PlayerMan.Sessions.Single().UserId);
            minds.TransferTo(mind, pilot);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);

            void Jump(EntityUid map) => shuttles.FTLToCoordinates(ship, entities.GetComponent<ShuttleComponent>(ship),
                new EntityCoordinates(map, 1000, 1000), Angle.Zero);
            // A hop inside the current orbit used to start a flight that never found its way out of hyperspace.
            Jump(orbit.Value);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False, "No jump inside the current orbit.");
            Jump(surface.Value);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.False, "No jump to a surface.");
            Assert.That(entities.HasComponent<SWGlobalWarTravelComponent>(ship), Is.False);
            Jump(next!.Value);
            Assert.That(entities.HasComponent<FTLComponent>(ship), Is.True, "A jump along the route still goes.");
        });
        await pair.CleanReturnAsync();
    }
}
