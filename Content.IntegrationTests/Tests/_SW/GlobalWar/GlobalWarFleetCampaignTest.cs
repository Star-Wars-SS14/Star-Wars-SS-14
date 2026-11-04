#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Combat;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Fleet on the real campaign rule, real orbit maps, and a connected client that reports joint state errors.</summary>
[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarFleetCampaignTest
{
    private static readonly SWGlobalWarShipClass[] SmallClasses =
    [
        SWGlobalWarShipClass.Transport, SWGlobalWarShipClass.Fighter, SWGlobalWarShipClass.Bomber,
        SWGlobalWarShipClass.Frigate,
    ];

    [TestCase("SWGlobalWarR0", SWGlobalWarFaction.Republic)]
    [TestCase("SWGlobalWarE0", SWGlobalWarFaction.Empire)]
    public async Task EveryEnabledHullFitsCapitalPorts(string sector, SWGlobalWarFaction faction)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var fleet = server.System<SWGlobalWarFleetSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var anchors = server.System<SWGlobalWarMapAnchorSystem>();
            var loader = server.System<MapLoaderSystem>();
            var maps = server.System<SharedMapSystem>();
            Assert.That(server.System<GameTicker>().StartGameRule("SWGlobalWarCampaignProduction"), Is.True);
            Assert.That(planets.TryGetPlanet(sector, out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.True);
            var console = Anchor(anchors, sector, "shipyardPrimary");
            var transport = Anchor(anchors, sector, "transportConsole");
            var staging = maps.CreateMap(out var stagingId);
            var offset = 0;
            EntityUid Load(SWGlobalWarShipClass shipClass)
            {
                Assert.That(fleet.TryGetHullMap(shipClass, faction, out var path), Is.True, shipClass.ToString());
                Assert.That(loader.TryLoadGrid(stagingId, new ResPath(path!), out var loaded,
                    offset: new Vector2(++offset * 500, 0)), Is.True, path);
                return loaded!.Value;
            }
            foreach (var shipClass in SmallClasses)
                Assert.That(fleet.FindDelivery(Load(shipClass), console), Is.Not.Null,
                    $"{sector} {shipClass} at the shipyard port");
            Assert.That(fleet.FindDelivery(Load(SWGlobalWarShipClass.Transport), transport), Is.Not.Null,
                $"{sector} transport at the transport port");
            Assert.That(fleet.FindDelivery(Load(SWGlobalWarShipClass.Transport), console, "dockAssault", "clearAssault"),
                Is.Not.Null, $"{sector} starting transport at the assault port");
            server.EntMan.DeleteEntity(staging);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("SWGlobalWarR8", SWGlobalWarFaction.Republic, "SWGlobalWarRepublicPilot")]
    [TestCase("SWGlobalWarE8", SWGlobalWarFaction.Empire, "SWGlobalWarEmpirePilot")]
    public async Task EveryHullDocksAndABattleshipFliesFromDockworks(string sector, SWGlobalWarFaction faction, string pilotJob)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var entities = server.EntMan;
        var fleet = server.System<SWGlobalWarFleetSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var anchors = server.System<SWGlobalWarMapAnchorSystem>();
        var docking = server.System<DockingSystem>();
        var transform = server.System<SharedTransformSystem>();
        var travel = server.System<SWGlobalWarTravelSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var session = server.PlayerMan.Sessions.Single();
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded =
                server.ResolveDependency<Content.Server.Database.UserDbDataManager>().IsLoadComplete(session) &&
                preferences.TryGetCachedPreferences(session.UserId, out _));
        }
        var profile = (HumanoidCharacterProfile) preferences.GetPreferences(session.UserId).SelectedCharacter;
        await pair.SetJobPriorities(session.UserId, profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never))
            .Append(((ProtoId<JobPrototype>) pilotJob, JobPriority.High)).ToArray());
        EntityUid console = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "Empty");
            var ticker = server.System<GameTicker>();
            ticker.SetGamePreset("SWGlobalWar");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(planets.TryGetPlanet(sector, out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.True);
            console = Anchor(anchors, sector, "shipyardPrimary");
            transform.SetCoordinates(session.AttachedEntity!.Value, entities.GetComponent<TransformComponent>(console).Coordinates);
        });
        await pair.RunSeconds(2);
        EntityUid hull = default;
        foreach (var shipClass in Enum.GetValues<SWGlobalWarShipClass>())
        {
            await server.WaitAssertion(() =>
                Assert.That(fleet.TryGrantHull(console, faction, shipClass), Is.True, shipClass.ToString()));
            await pair.RunSeconds(4);
            await server.WaitAssertion(() =>
            {
                var orbit = entities.GetComponent<TransformComponent>(console).MapUid;
                hull = entities.EntityQuery<SWGlobalWarFleetShipComponent>().Single(c => c.Class == shipClass &&
                    entities.GetComponent<TransformComponent>(c.Owner).MapUid == orbit).Owner;
                Assert.That(docking.GetDocks(hull).Any(d => d.Comp.Docked), Is.True, $"{shipClass} docked");
                var mass = entities.GetComponent<PhysicsComponent>(hull).Mass;
                Assert.That(server.System<ShuttleSystem>().CanFTL(hull, out var reason), Is.True,
                    $"{shipClass} mass {mass}: {reason}");
                docking.UndockDocks(hull);
                transform.SetWorldPosition(hull, new Vector2(-400 - 120 * (int) shipClass, 600));
            });
            await pair.RunTicksSync(5);
        }
        // The last class is the battleship: the heaviest hull takes one real hop to a neighbouring own sector.
        string destination = default!;
        EntityUid bridge = default;
        await server.WaitAssertion(() =>
        {
            var body = session.AttachedEntity!.Value;
            bridge = entities.EntityQuery<SWGlobalWarTravelConsoleComponent>().Select(c => c.Owner)
                .First(c => entities.GetComponent<TransformComponent>(c).GridUid == hull);
            StandBesideReachable(server, body, bridge);
            destination = server.ResolveDependency<IPrototypeManager>().Index<SWGlobalWarPlanetPrototype>(sector).Connections
                .First(id => !id.Id.Contains("N")).Id;
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(travel.TryStartTravel(bridge, session.AttachedEntity!.Value, destination, out var reason), Is.True,
                $"{destination}: {reason}");
        });
        var arrived = false;
        for (var step = 0; step < 12 && !arrived; step++)
        {
            await server.WaitPost(() => timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(30), timing.TimeBase.Item2));
            await pair.RunTicksSync(30);
            await server.WaitPost(() => arrived = !entities.HasComponent<FTLComponent>(hull));
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(arrived, Is.True);
            Assert.That(planets.TryGetPlanet(destination, out var target), Is.True);
            Assert.That(planets.TryGetPhysicalLocation(hull, out var here, out _), Is.True);
            Assert.That(here, Is.EqualTo(target!.Value));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StartingFleetPurchaseFlightAndCaptureOnRealOrbits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var entities = server.EntMan;
        var ticker = server.System<GameTicker>();
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var anchors = server.System<SWGlobalWarMapAnchorSystem>();
        var fleet = server.System<SWGlobalWarFleetSystem>();
        var capture = server.System<SWGlobalWarCaptureSystem>();
        var travel = server.System<SWGlobalWarTravelSystem>();
        var docking = server.System<DockingSystem>();
        var transform = server.System<SharedTransformSystem>();
        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var clientUser = server.PlayerMan.Sessions.Single().UserId;
        await server.AddDummySessions(3);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<Content.Server.Database.UserDbDataManager>().IsLoadComplete(p) &&
                preferences.TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        // The connected client plays the head, so its view contains the shipyard during deliveries.
        var sessions = server.PlayerMan.Sessions.OrderBy(p => p.UserId != clientUser).ToArray();
        string[] jobs = ["SWGlobalWarRepublicHead", "SWGlobalWarEmpireHead", "SWGlobalWarRepublicPilot", "SWGlobalWarEmpireSoldier"];
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) preferences.GetPreferences(sessions[i].UserId).SelectedCharacter;
            await pair.SetJobPriorities(sessions[i].UserId, profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never))
                .Append(((ProtoId<JobPrototype>) jobs[i], JobPriority.High)).ToArray());
        }

        Guid generation = default;
        Entity<SWGlobalWarCampaignComponent> campaign = default;
        Entity<SWGlobalWarPoliticsComponent> state = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "Empty");
            ticker.SetGamePreset("SWGlobalWar");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(4));
            Assert.That(clock.TryGetCampaign(out campaign), Is.True);
            Assert.That(politics.TryGetPolitics(out state), Is.True);
            generation = campaign.Comp.Generation;
            foreach (var session in sessions)
                server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
        });

        SWGlobalWarAssignment Actor(int index)
        {
            Assert.That(registry.TryGetAssignment(sessions[index].UserId, out var assignment), Is.True);
            return assignment;
        }
        EntityUid Planet(string id)
        {
            Assert.That(planets.TryGetPlanet(id, out var planet), Is.True, id);
            return planet!.Value;
        }
        EntityUid[] Hulls(SWGlobalWarFaction faction) => entities.EntityQuery<SWGlobalWarFleetShipComponent>()
            .Select(c => c.Owner)
            .Where(s => ownership.TryGetOwner(s, generation, out var owner, out _) && owner == faction).ToArray();
        void StandBeside(EntityUid body, EntityUid console) => StandBesideReachable(server, body, console);
        async Task Advance(double seconds, int ticks = 45)
        {
            await server.WaitPost(() => timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds), timing.TimeBase.Item2));
            await pair.RunTicksSync(ticks);
        }
        bool Buy(int index, EntityUid console, SWGlobalWarShipClass shipClass) => fleet.TryExecute(sessions[index], console,
            generation, Actor(index).Revision, Guid.NewGuid(), shipClass, headDecision: true);

        // Starting fleet: two transports and a fighter, each already docked at its own port of the real capital orbit.
        EntityUid orbit = default;
        EntityUid station = default;
        EntityUid shipyard = default;
        EntityUid transportYard = default;
        await server.WaitAssertion(() =>
        {
            var r0 = Planet("SWGlobalWarR0");
            Assert.That(planets.TryEnsureLocation(r0, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
            orbit = map!.Value;
            Assert.That(planets.TryGetOrbitStation(r0, out var grid), Is.True);
            station = grid!.Value;
            shipyard = Anchor(anchors, "SWGlobalWarR0", "shipyardPrimary");
            transportYard = Anchor(anchors, "SWGlobalWarR0", "transportConsole");
            StandBeside(Actor(0).Body, shipyard);
            Assert.That(entities.HasComponent<JointComponent>(station), Is.False, "The station starts without joints.");
            Assert.That(entities.GetComponent<PhysicsComponent>(station).BodyType, Is.EqualTo(BodyType.Static),
                "Hubs and reception points bind only to a station that cannot move.");
            var reception = Anchor(anchors, "SWGlobalWarR0", "withdrawalReception");
            Assert.That(entities.GetComponent<SWGlobalWarReceptionComponent>(reception).Binding, Is.Not.Null);
        });
        await pair.RunSeconds(4);
        EntityUid firstTransport = default;
        await server.WaitAssertion(() =>
        {
            var hulls = Hulls(SWGlobalWarFaction.Republic);
            Assert.That(hulls, Has.Length.EqualTo(3), "Two transports and one fighter.");
            foreach (var hull in hulls)
            {
                Assert.That(entities.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit));
                Assert.That(docking.GetDocks(hull).Any(d => d.Comp.Docked), Is.True, entities.ToPrettyString(hull).ToString());
            }
            foreach (var port in new[] { "dockTransport", "dockAssault", "dockShipyard" })
                Assert.That(entities.GetComponent<DockingComponent>(Anchor(anchors, "SWGlobalWarR0", port)).Docked, Is.True, port);
            firstTransport = entities.GetComponent<DockingComponent>(Anchor(anchors, "SWGlobalWarR0", "dockTransport")).DockedWith is { } shipPort
                ? entities.GetComponent<TransformComponent>(shipPort).GridUid!.Value : default;
        });

        // Every port frees, so the engine removes the station's joint component; the next purchase must still be clean.
        var initialBudget = 0L;
        await server.WaitAssertion(() =>
        {
            var clearOfStation = 0;
            foreach (var hull in Hulls(SWGlobalWarFaction.Republic))
            {
                docking.UndockDocks(hull);
                transform.SetWorldPosition(hull, new Vector2(-300 - 80 * clearOfStation++, 300));
            }
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<JointComponent>(station), Is.False, "The engine removes an empty joint component.");
            var ledger = server.System<SWGlobalWarLedgerSystem>();
            initialBudget = ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Available;
            StandBeside(Actor(0).Body, transportYard);
            Assert.That(Buy(0, transportYard, SWGlobalWarShipClass.Transport), Is.True);
            Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Available,
                Is.EqualTo(initialBudget - 10000));
        });
        await Advance(125);
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(Hulls(SWGlobalWarFaction.Republic), Has.Length.EqualTo(4));
            Assert.That(entities.GetComponent<DockingComponent>(Anchor(anchors, "SWGlobalWarR0", "dockTransport")).Docked, Is.True);
            Assert.That(server.System<SWGlobalWarLedgerSystem>().GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Available,
                Is.EqualTo(initialBudget - 10000));
        });

        // War opens neutral space to military crews.
        await server.WaitPost(() => timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromMinutes(61) - clock.Elapsed(campaign), timing.TimeBase.Item2));
        await pair.RunTicksSync(45);
        await server.WaitAssertion(() =>
        {
            Assert.That(politics.Execute(sessions[0], new SWGlobalWarPoliticalCommand(generation, Guid.NewGuid(), Actor(0).Revision,
                SWGlobalWarPoliticalAction.DeclareWar, state.Comp.RelationsRevision)).Success, Is.True);
        });

        // A pilot flies the first transport through real FTL hops to Alderaan.
        EntityUid bridge = default;
        await server.WaitAssertion(() =>
        {
            bridge = entities.EntityQuery<SWGlobalWarTravelConsoleComponent>().Select(c => c.Owner)
                .First(c => entities.GetComponent<TransformComponent>(c).GridUid == firstTransport);
            StandBeside(Actor(2).Body, bridge);
        });
        await pair.RunSeconds(2);
        foreach (var destination in new[] { "SWGlobalWarR1", "SWGlobalWarR3", "SWGlobalWarN0" })
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(travel.TryStartTravel(bridge, Actor(2).Body, destination, out var reason), Is.True,
                    $"{destination}: {reason}");
                Assert.That(entities.HasComponent<FTLComponent>(firstTransport), Is.True);
            });
            var arrived = false;
            for (var step = 0; step < 12 && !arrived; step++)
            {
                await Advance(30, 30);
                await server.WaitPost(() => arrived = !entities.HasComponent<FTLComponent>(firstTransport));
            }
            await server.WaitAssertion(() =>
            {
                Assert.That(arrived, Is.True, $"FTL to {destination} did not finish");
                Assert.That(planets.TryGetPhysicalLocation(firstTransport, out var here, out var location), Is.True);
                Assert.That(here, Is.EqualTo(Planet(destination)));
                Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Orbit));
            });
        }

        // By this time Alderaan's civil war has given it to one side; the other side's fighter takes its real console.
        var attacker = 0;
        var winner = SWGlobalWarFaction.Neutral;
        await server.WaitAssertion(() =>
        {
            var previous = entities.GetComponent<SWGlobalWarPlanetComponent>(Planet("SWGlobalWarN0")).OrbitOwner;
            attacker = previous == SWGlobalWarFaction.Republic ? 3 : 2;
            winner = previous == SWGlobalWarFaction.Republic ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Republic;
            var console = Anchor(anchors, "SWGlobalWarN0", "capturePrimary");
            StandBeside(Actor(attacker).Body, console);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var console = Anchor(anchors, "SWGlobalWarN0", "capturePrimary");
            var view = capture.GetView(sessions[attacker], console);
            Assert.That(view.CanStart, Is.True, view.ToString());
            Assert.That(capture.TryExecute(sessions[attacker], console, generation, Actor(attacker).Revision, Guid.NewGuid(), false), Is.True);
        });
        await pair.RunSeconds(2);
        await Advance(310);
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var n0 = entities.GetComponent<SWGlobalWarPlanetComponent>(Planet("SWGlobalWarN0"));
            Assert.That(n0.OrbitOwner, Is.EqualTo(winner),
                capture.GetView(sessions[attacker], Anchor(anchors, "SWGlobalWarN0", "capturePrimary")).ToString());
        });

        // An enemy soldier takes Coruscant's orbit: the same console, opposite side, real capital map.
        await server.WaitAssertion(() =>
        {
            var console = Anchor(anchors, "SWGlobalWarR0", "capturePrimary");
            transform.SetCoordinates(Actor(0).Body, entities.GetComponent<TransformComponent>(firstTransport).Coordinates);
            StandBeside(Actor(3).Body, console);
            // Arrival by any real route wakes the sleeping map; the soldier is teleported here, so wake it explicitly.
            Assert.That(planets.TryEnsureLocation(Planet("SWGlobalWarR0"), SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.True);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var console = Anchor(anchors, "SWGlobalWarR0", "capturePrimary");
            var view = capture.GetView(sessions[3], console);
            Assert.That(view.CanStart, Is.True,
                $"{view} powered {server.System<Content.Server.Power.EntitySystems.PowerReceiverSystem>().IsPowered(console)} paused {entities.GetComponent<MetaDataComponent>(console).EntityPaused} relations {state.Comp.Relations} hostility {politics.CanCapture(Actor(3).Body, Planet("SWGlobalWarR0"), SWGlobalWarPlanetLocation.Orbit, generation)}");
            Assert.That(capture.TryExecute(sessions[3], console, generation, Actor(3).Revision, Guid.NewGuid(), false), Is.True);
        });
        await pair.RunSeconds(2);
        await Advance(310);
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var r0 = entities.GetComponent<SWGlobalWarPlanetComponent>(Planet("SWGlobalWarR0"));
            Assert.That(r0.OrbitOwner, Is.EqualTo(SWGlobalWarFaction.Empire),
                capture.GetView(sessions[3], Anchor(anchors, "SWGlobalWarR0", "capturePrimary")).ToString());
            Assert.That(r0.SurfaceOwner, Is.EqualTo(SWGlobalWarFaction.Republic), "Surface and orbit change hands separately.");
        });
        await pair.CleanReturnAsync();
    }

    private static void StandBesideReachable(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid body, EntityUid console)
    {
        var transform = server.System<SharedTransformSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var center = server.EntMan.GetComponent<TransformComponent>(console).Coordinates;
        foreach (var offset in new[] { new Vector2(0, -1), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(1, 0) })
        {
            transform.SetCoordinates(body, center.Offset(offset));
            if (interaction.InRangeUnobstructed(body, console, range: 2f))
                return;
        }
        Assert.Fail($"No reachable floor beside {server.EntMan.ToPrettyString(console)}");
    }

    private static EntityUid Anchor(SWGlobalWarMapAnchorSystem anchors, string sector, string anchor)
    {
        Assert.That(anchors.TryResolveAnchor(new SWGlobalWarAnchorRef
        {
            Sector = sector, Location = SWGlobalWarPlanetLocation.Orbit, AnchorId = anchor,
        }, out var uid), Is.True, $"{sector} {anchor}");
        return uid!.Value;
    }
}
