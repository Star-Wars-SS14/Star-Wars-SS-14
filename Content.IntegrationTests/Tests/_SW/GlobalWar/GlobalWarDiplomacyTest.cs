using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Systems;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map.Components;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Access;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Content.Server.Database;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Identity;
using Content.Shared._SW.Vehicle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Buckle;
using Robust.Shared.Network;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]

// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed partial class GlobalWarDiplomacyTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  parent: SWGlobalWarPointMarker
  id: SWWithdrawalTestReception
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: withdrawalReception
    kind: Spawn
    spawnRadius: 0
  - type: SWGlobalWarReception
    placementArea: 101,101,106,106
    routeArea: 100,100,115,107
    publicExits:
    - 111,102
    - 111,104

- type: job
  id: SWDiplomacyTestEHead
  name: job-name-captain
  playTimeTracker: SWDiplomacyTestEHeadTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWDiplomacyTestESoldier
  name: job-name-captain
  playTimeTracker: SWDiplomacyTestESoldierTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWDiplomacyTestIntelligence
  name: job-name-captain
  playTimeTracker: SWDiplomacyTestIntelligenceTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: playTimeTracker
  id: SWDiplomacyTestEHeadTracker

- type: playTimeTracker
  id: SWDiplomacyTestESoldierTracker

- type: playTimeTracker
  id: SWDiplomacyTestIntelligenceTracker

- type: swGlobalWarPlanet
  id: SWDiplomacyTestN0
  name: Neutral zero
  initialOwner: Neutral
  surface: Empty
  orbit: Empty

- type: swGlobalWarPlanet
  id: SWDiplomacyTestN1
  name: Neutral one
  initialOwner: Neutral
  surface: Empty
  orbit: Empty

- type: entity
  parent: SWPoliticsTestRule
  id: SWDiplomacyTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestRCapital, SWSpawnTestECapital, SWSpawnTestIndustry, SWSpawnTestForce, SWDiplomacyTestN0, SWDiplomacyTestN1]
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWPoliticsTestHead: {faction: Republic, role: Head}
      SWPoliticsTestCivilian: {faction: Republic, role: Civilian}
      SWPoliticsTestSoldier: {faction: Republic, role: Soldier}
      SWPoliticsTestIndustry: {faction: Republic, role: IndustryMinister}
      SWPoliticsTestSenior: {faction: Republic, role: SeniorOfficer}
      SWPoliticsTestForce: {faction: Republic, role: ForceUser, force: true}
      SWDiplomacyTestEHead: {faction: Empire, role: Head}
      SWDiplomacyTestESoldier: {faction: Empire, role: Soldier}
      SWDiplomacyTestIntelligence: {faction: Republic, role: Intelligence}
    stations:
      Empire:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWDiplomacyTestEHead: [1, 1]
            SWDiplomacyTestESoldier: [5, 5]
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWPoliticsTestHead: [1, 1]
            SWPoliticsTestCivilian: [-1, -1]
            SWPoliticsTestSoldier: [5, 5]
            SWPoliticsTestForce: [1, 1]
            SWDiplomacyTestIntelligence: [1, 1]

- type: gamePreset
  id: SWDiplomacyTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWDiplomacyTestRule]

- type: entity
  parent: SWDiplomacyTestRule
  id: SWWithdrawalTestRule
  components:
  - type: SWGlobalWarCampaign
    peaceDuration: 0

- type: gamePreset
  id: SWWithdrawalTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWWithdrawalTestRule]

- type: entity
  parent: SWWithdrawalTestRule
  id: SWWithdrawalIntegrationRule
  components:
  - type: SWGlobalWarCampaign
    neutralFundingDuration: 0
    annexationDuration: 2

- type: gamePreset
  id: SWWithdrawalIntegrationPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWWithdrawalIntegrationRule]

- type: swGlobalWarPlanet
  id: SWWithdrawalFlightSource
  name: Hostile departure
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  connections: [SWWithdrawalFlightTarget]

- type: swGlobalWarPlanet
  id: SWWithdrawalFlightTarget
  name: Hostile destination
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap

- type: entity
  parent: SWWithdrawalTestRule
  id: SWWithdrawalFlightRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestRCapital, SWSpawnTestECapital, SWSpawnTestIndustry, SWSpawnTestForce, SWDiplomacyTestN0, SWDiplomacyTestN1, SWWithdrawalFlightSource, SWWithdrawalFlightTarget]
  - type: SWGlobalWarCampaign
    withdrawalDuration: 3

- type: gamePreset
  id: SWWithdrawalFlightPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWWithdrawalFlightRule]
""";

    [TestCase("fleet-boarding")]
    [TestCase("explosion-policy")]
    [TestCase("capture-core")]
    [TestCase("sponsorship")]
    [TestCase("succession-funding")]
    [TestCase("neutral")]
    [TestCase("truce-policy")]
    [TestCase("appointment-location")]
    [TestCase("appointment-race")]
    [TestCase("appointment-intelligence")]
    [TestCase("appointment-force")]
    [TestCase("trade")]
    [TestCase("time-limit")]
    [TestCase("surrender")]
    [TestCase("mutual-surrender")]
    [TestCase("step-forward")]
    [TestCase("step-reverse")]
    [TestCase("step-runtime")]
    [TestCase("generation")]
    [TestCase("trade-evidence")]
    [TestCase("withdraw-body")]
    [TestCase("withdraw-no-point")]
    [TestCase("withdraw-vehicle")]
    [TestCase("withdraw-passenger")]
    [TestCase("withdraw-lazy")]
    [TestCase("withdraw-chair")]
    [TestCase("withdraw-pull")]
    [TestCase("withdraw-ssd")]
    [TestCase("withdraw-integration")]
    [TestCase("withdraw-ship")]
    [TestCase("withdraw-ship-mixed")]
    [TestCase("withdraw-ship-mixed-blocked")]
    [TestCase("withdraw-ship-mixed-safety")]
    [TestCase("withdraw-ship-mixed-surface-safety")]
    [TestCase("withdraw-ship-mixed-flow")]
    [TestCase("withdraw-ship-mixed-callback-war")]
    [TestCase("withdraw-ship-mixed-callback-owner")]
    [TestCase("withdraw-ship-foreign")]
    [TestCase("withdraw-ship-unknown")]
    [TestCase("withdraw-ship-ftl")]
    [TestCase("withdraw-ship-dock")]
    [TestCase("withdraw-ship-ownership")]
    [TestCase("withdraw-ship-independent")]
    public async Task RealGovernmentsExecuteDiplomacy(string scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var originalTime = timing.TimeBase;
        var originalTick = timing.CurTick;
        var networkUser = server.PlayerMan.Sessions.Single().UserId;
        await server.AddDummySessions(6);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var sessions = server.PlayerMan.Sessions.ToArray();
        if (scenario == "withdraw-ssd")
        {
            var networkIndex = Array.FindIndex(sessions, session => session.UserId == networkUser);
            (sessions[2], sessions[networkIndex]) = (sessions[networkIndex], sessions[2]);
        }
        string[] jobs = ["SWPoliticsTestHead", "SWDiplomacyTestEHead", "SWPoliticsTestSoldier",
            "SWDiplomacyTestESoldier", "SWDiplomacyTestIntelligence", "SWPoliticsTestForce", "SWPoliticsTestCivilian"];
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            await pair.SetJobPriorities(sessions[i].UserId, profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never))
                .Append(((ProtoId<JobPrototype>) jobs[i], JobPriority.High)).ToArray());
            if (scenario.StartsWith("withdraw-ship-mixed", StringComparison.Ordinal))
            {
                var preferences = server.ResolveDependency<IServerPreferencesManager>();
                var user = sessions[i].UserId;
                await server.WaitPost(() => preferences.SetProfile(user, 0,
                    ((HumanoidCharacterProfile) preferences.GetPreferences(user).SelectedCharacter).WithSpecies("Human")).Wait());
            }
        }
        Entity<SWGlobalWarPoliticsComponent> state = default;
        Entity<SWGlobalWarCampaignComponent> campaign = default;
        Action? afterRuntimeStep = null;
        Func<Task>? runtimeScenario = null;
        var runtimeWithdrawal = scenario is "withdraw-vehicle" or "withdraw-passenger" or "withdraw-lazy" or
            "withdraw-chair" or "withdraw-pull" or "withdraw-ssd" or "withdraw-integration" ||
            scenario.StartsWith("withdraw-ship", StringComparison.Ordinal);
        var assertionsCompleted = false;
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset(scenario is "withdraw-ship-ftl" or "withdraw-ship-dock" or "withdraw-ship-mixed-flow" or
                    "withdraw-ship-ownership" or "withdraw-ship-independent" ? "SWWithdrawalFlightPreset" :
                    scenario == "withdraw-integration" ? "SWWithdrawalIntegrationPreset" :
                    runtimeWithdrawal ? "SWWithdrawalTestPreset" : "SWDiplomacyTestPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(7));
                Assert.That(politics.TryGetPolitics(out state), Is.True);
                Assert.That(clock.TryGetCampaign(out campaign), Is.True);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                originalTime = timing.TimeBase;
                originalTick = timing.CurTick;
                var gen = state.Comp.Generation;
                var r = SWGlobalWarFaction.Republic;
                var e = SWGlobalWarFaction.Empire;
                var n0 = state.Comp.Neutrals[(ProtoId<SWGlobalWarPlanetPrototype>) "SWDiplomacyTestN0"];
                var n1 = state.Comp.Neutrals[(ProtoId<SWGlobalWarPlanetPrototype>) "SWDiplomacyTestN1"];
                var rCapital = Planet("SWSpawnTestRCapital");
                var eCapital = Planet("SWSpawnTestECapital");
                switch (scenario)
                {
                    case "fleet-boarding":
                    {
                        var fleet = server.System<SWGlobalWarFleetSystem>();
                        var captureSystem = server.System<SWGlobalWarCaptureSystem>();
                        var transform = server.System<SharedTransformSystem>();
                        var maps = server.System<SharedMapSystem>();
                        var docking = server.System<DockingSystem>();
                        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
                        Assert.That(planets.TryEnsureLocation(rCapital, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                        Assert.That(planets.TryGetOrbitStation(rCapital, out var station), Is.True);
                        var grid = station!.Value;
                        server.System<SharedPhysicsSystem>().SetBodyType(grid, BodyType.Static);
                        var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                        var tiles = new List<(Vector2i, Tile)>();
                        for (var x = -8; x <= 8; x++)
                        for (var y = -8; y <= 10; y++) tiles.Add((new Vector2i(x, y), plating));
                        maps.SetTiles(grid, server.EntMan.GetComponent<MapGridComponent>(grid), tiles);
                        maps.SetPaused(map!.Value, false);
                        var console = server.EntMan.SpawnEntity("SWCombatTestShipyard", new EntityCoordinates(grid, 3.5f, 8.5f));
                        var dock = server.EntMan.SpawnEntity("SWCombatTestDock", new EntityCoordinates(grid, .5f, 10.5f));
                        transform.SetLocalRotation(dock, Angle.FromDegrees(180));
                        var clearance = server.EntMan.SpawnEntity("SWCombatTestClearance", new EntityCoordinates(grid, .5f, .5f));
                        server.System<Content.Server.Power.EntitySystems.PowerNetSystem>().Update(0);
                        transform.SetCoordinates(Assignment(0).Body, new EntityCoordinates(grid, 3.5f, 7.5f));
                        var initial = ledger.GetBalance(r, SWGlobalWarAccount.War);
                        var request = Guid.NewGuid();
                        Assert.That(Order(request), Is.True);
                        Assert.That(Order(request), Is.True, "A retry must not reserve twice.");
                        Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.War).Available, Is.EqualTo(initial.Available - 40000));
                        var cancelledOrder = fleet.GetView(sessions[0], console).Orders.Single().Id;
                        Assert.That(fleet.TryExecute(sessions[0], console, gen, Assignment(0).Revision, Guid.NewGuid(),
                            SWGlobalWarShipClass.Fighter, cancelledOrder, true), Is.True);
                        Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.War).Available, Is.EqualTo(initial.Available));
                        Assert.That(Order(Guid.NewGuid()), Is.True);
                        server.EntMan.DeleteEntity(clearance);
                        EntityUid purchasedShip = default;
                        EntityUid bridge = default;
                        runtimeScenario = async () =>
                        {
                            await Tick(200, () =>
                            {
                                Assert.That(fleet.GetView(sessions[0], console).Orders, Has.Length.EqualTo(1));
                                Assert.That(Ships(), Is.Empty, "No explicit clearance means no physical issuance.");
                                Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.War).Available, Is.EqualTo(initial.Available - 40000));
                                clearance = server.EntMan.SpawnEntity("SWCombatTestClearance", new EntityCoordinates(grid, .5f, .5f));
                            });
                            await Tick(202, () =>
                            {
                                Assert.That(fleet.GetView(sessions[0], console).Orders, Is.Empty);
                                purchasedShip = Ships().Single();
                                Assert.That(ownership.TryGetOwner(purchasedShip, gen, out var owner, out _), Is.True);
                                Assert.That(owner, Is.EqualTo(r));
                                Assert.That(server.EntMan.GetComponent<DockingComponent>(dock).Docked, Is.True);
                                Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.War).Available, Is.EqualTo(initial.Available - 40000));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(purchasedShip).ParentUid,
                                    Is.EqualTo(server.EntMan.GetComponent<TransformComponent>(purchasedShip).MapUid));
                                Assert.That(Order(Guid.NewGuid()), Is.True);
                            });
                            await Tick(400, () =>
                            {
                                Assert.That(Ships(), Has.Length.EqualTo(1), "An occupied real dock must retain the next order.");
                                var waiting = fleet.GetView(sessions[0], console).Orders.Single();
                                Assert.That(waiting.Status, Is.EqualTo("waiting"));
                                Assert.That(fleet.TryExecute(sessions[0], console, gen, Assignment(0).Revision, Guid.NewGuid(),
                                    SWGlobalWarShipClass.Fighter, waiting.Id, true), Is.True);
                                docking.UndockDocks(purchasedShip);
                                var position = transform.GetWorldPosition(purchasedShip);
                                var angle = transform.GetWorldRotation(purchasedShip);
                                transform.SetLocalPosition(grid, new Vector2(20, 20));
                                transform.SetLocalRotation(grid, Angle.FromDegrees(30));
                                Assert.That(transform.GetWorldPosition(purchasedShip), Is.EqualTo(position));
                                Assert.That(transform.GetWorldRotation(purchasedShip), Is.EqualTo(angle));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(purchasedShip).ParentUid,
                                    Is.EqualTo(server.EntMan.GetComponent<TransformComponent>(purchasedShip).MapUid),
                                    "An undocked paid vessel must not remain parented to the station.");
                                bridge = server.EntMan.EntityQuery<ShuttleConsoleComponent>()
                                    .Select(c => c.Owner).First(c => server.EntMan.GetComponent<TransformComponent>(c).GridUid == purchasedShip);
                                var bridgePosition = server.EntMan.GetComponent<TransformComponent>(bridge).Coordinates;
                                transform.SetCoordinates(Assignment(2).Body,
                                    new EntityCoordinates(purchasedShip, bridgePosition.Position + new Vector2(0, -2)));
                                transform.SetCoordinates(Assignment(3).Body,
                                    new EntityCoordinates(purchasedShip, bridgePosition.Position + new Vector2(0, -1)));
                                Assert.That(server.System<SWGlobalWarShipControlSystem>().CanOperate(bridge, Assignment(2).Body, out _), Is.True);
                                Assert.That(server.System<SWGlobalWarShipControlSystem>().CanOperate(bridge, Assignment(3).Body, out _), Is.False);
                            });
                            await Tick(3600, () =>
                            {
                                Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                                var bridgeView = captureSystem.GetView(sessions[3], bridge);
                                Assert.That(bridgeView.CanStart, Is.True, $"Bridge view: {bridgeView}");
                                Assert.That(captureSystem.TryExecute(sessions[3], bridge, gen, Assignment(3).Revision,
                                    Guid.NewGuid(), false), Is.True);
                            });
                            await Tick(3601, () =>
                            {
                                Assert.That(captureSystem.GetView(sessions[3], bridge).Status, Is.EqualTo(SWGlobalWarCaptureStatus.Contested));
                                MoveToSurface(Assignment(2).Body, rCapital);
                            });
                            await Tick(3902, () =>
                            {
                                Assert.That(ownership.TryGetOwner(purchasedShip, gen, out var owner, out var shipRevision), Is.True);
                                Assert.That(owner, Is.EqualTo(e));
                                Assert.That(shipRevision, Is.EqualTo(1));
                                Assert.That(server.System<SWGlobalWarShipControlSystem>().CanOperate(bridge, Assignment(3).Body, out _), Is.True);
                                Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.War).Available, Is.EqualTo(initial.Available - 40000),
                                    "Boarding must not charge or refund a hull purchase.");
                            });
                        };
                        break;

                        bool Order(Guid id) => fleet.TryExecute(sessions[0], console, gen, Assignment(0).Revision,
                            id, SWGlobalWarShipClass.Fighter, headDecision: true);
                        EntityUid[] Ships() => server.EntMan.EntityQuery<SWGlobalWarFleetShipComponent>()
                            .Select(c => c.Owner).ToArray();
                        async Task Tick(double seconds, Action assertion)
                        {
                            await server.WaitPost(() => timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) -
                                clock.Elapsed(campaign), timing.TimeBase.Item2));
                            await pair.RunTicksSync(60);
                            await server.WaitAssertion(assertion);
                        }
                    }
                    case "explosion-policy":
                    {
                        var explosions = server.System<ExplosionSystem>();
                        var maps = server.System<SharedMapSystem>();
                        var outside = maps.CreateMap();
                        var outsidePosition = new MapCoordinates(Vector2.Zero,
                            server.EntMan.GetComponent<MapComponent>(outside).MapId);
                        var outsiderA = server.EntMan.SpawnEntity(null, new EntityCoordinates(outside, Vector2.Zero));
                        var outsiderB = server.EntMan.SpawnEntity(null, new EntityCoordinates(outside, Vector2.One));
                        var before = Queued().Length;
                        explosions.QueueExplosion(outsidePosition, "SWAntiVehicleRocketExplosion", 1, 25, 1,
                            null, user: outsiderA);
                        explosions.QueueExplosion(outsidePosition, "SWAntiVehicleRocketExplosion", 1, 25, 1,
                            null, user: outsiderB);
                        Assert.That(Queued().Length, Is.EqualTo(before + 1),
                            "Different initiators must retain upstream coalescing outside campaign locations.");
                        Assert.That(Queued().Single(q => q.Epicenter.MapId == outsidePosition.MapId).TotalIntensity,
                            Is.EqualTo(2));

                        Assert.That(planets.TryEnsureLocation(eCapital, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                        maps.SetPaused(map!.Value, false);
                        var coordinates = new EntityCoordinates(map.Value, new Vector2(40, 40));
                        var epicenter = server.System<SharedTransformSystem>().ToMapCoordinates(coordinates);
                        var wall = server.EntMan.SpawnEntity("WallSolid", coordinates);
                        var rocket = server.EntMan.SpawnEntity("SWBulletAntiVehicleRocket", coordinates);
                        server.EntMan.GetComponent<ProjectileComponent>(rocket).Shooter = Assignment(2).Body;
                        var grenade = server.EntMan.SpawnEntity("ExGrenade", coordinates);
                        var timer = new ActiveTimerTriggerEvent(Assignment(4).Body);
                        server.EntMan.EventBus.RaiseLocalEvent(grenade, ref timer);
                        timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(3600) -
                            clock.Elapsed(campaign), timing.TimeBase.Item2);
                        politics.Update(0);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);

                        foreach (var victim in new[] { wall, Assignment(6).Body })
                        {
                            var attempt = new SWExplosionAttemptEvent(epicenter, rocket, victim);
                            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref attempt);
                            Assert.That(attempt.User, Is.EqualTo(Assignment(2).Body));
                            Assert.That(attempt.Cancelled, Is.False, "A civilian collision victim is not the initiator.");
                        }
                        explosions.QueueExplosion(epicenter, "SWAntiVehicleRocketExplosion", 50, 25, 10,
                            rocket, maxTileBreak: 0, user: wall);
                        Assert.That(Queued().Single(q => q.Epicenter.MapId == epicenter.MapId).User,
                            Is.EqualTo(Assignment(2).Body));
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        foreach (var victim in new[] { wall, Assignment(6).Body })
                        {
                            Assert.That(server.System<TriggerSystem>().Trigger(rocket, victim), Is.False,
                                "The collision-trigger path must reject a wartime projectile after peace.");
                            var attempt = new SWExplosionAttemptEvent(epicenter, rocket, victim);
                            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref attempt);
                            Assert.That(attempt.Cancelled, Is.True);
                            Assert.That(attempt.User, Is.EqualTo(Assignment(2).Body));
                        }
                        server.EntMan.DeleteEntity(Assignment(4).Body);
                        var deletedOwner = new SWExplosionAttemptEvent(epicenter, grenade, null);
                        server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref deletedOwner);
                        Assert.That(deletedOwner.Cancelled, Is.True, "A removed grenade owner is not environmental damage.");
                        var natural = new SWExplosionAttemptEvent(epicenter, null, null);
                        server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref natural);
                        Assert.That(natural.Cancelled, Is.False, "Anonymous environmental explosions remain possible.");
                        // A queued, previously authorized explosion is rechecked immediately before processing.
                        runtimeScenario = async () =>
                        {
                            // Processing is budgeted; a previous unrelated explosion may consume the first tick.
                            await pair.RunTicksSync(12);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(Queued().Any(q => q.Epicenter.MapId == epicenter.MapId), Is.False);
                                Assert.That(server.EntMan.GetComponent<Content.Shared.Damage.Components.DamageableComponent>(wall)
                                    .TotalDamage, Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero), "Truce before processing must prevent queued explosive damage.");
                            });
                        };
                        break;

                        QueuedExplosion[] Queued() =>
                            ((HashSet<QueuedExplosion>) typeof(ExplosionSystem).GetField("_queuedExplosions",
                                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(explosions)!).ToArray();
                    }
                    case "capture-core":
                    {
                        var captureSystem = server.System<SWGlobalWarCaptureSystem>();
                        var transform = server.System<SharedTransformSystem>();
                        var maps = server.System<SharedMapSystem>();
                        Assert.That(planets.TryEnsureLocation(eCapital, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                        Assert.That(planets.TryGetOrbitStation(eCapital, out var grid), Is.True);
                        var gridData = server.EntMan.GetComponent<MapGridComponent>(grid!.Value);
                        server.System<SharedPhysicsSystem>().SetBodyType(grid.Value, BodyType.Static);
                        var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                        var tiles = new System.Collections.Generic.List<(Vector2i, Tile)>();
                        for (var x = -8; x <= 8; x++)
                        for (var y = -8; y <= 8; y++)
                            tiles.Add((new Vector2i(x, y), plating));
                        maps.SetTiles(grid.Value, gridData, tiles);
                        maps.SetPaused(map!.Value, false);
                        var position = new EntityCoordinates(grid.Value, .5f, .5f);
                        var console = server.EntMan.SpawnEntity("SWCombatTestCapture", position);
                        server.System<Content.Server.Power.EntitySystems.PowerNetSystem>().Update(0);
                        transform.SetCoordinates(Assignment(2).Body, new EntityCoordinates(grid.Value, .5f, 1.5f));
                        transform.SetCoordinates(Assignment(3).Body, new EntityCoordinates(grid.Value, 1.5f, .5f));
                        Assert.That(Start(), Is.False, "Peace must prevent capture.");
                        runtimeScenario = async () =>
                        {
                            await server.WaitAssertion(() =>
                            {
                                SetTime(3600);
                                politics.Update(0);
                                Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                                Assert.That(Start(), Is.True);
                            });
                            await Tick(3601, () =>
                            {
                                Assert.That(captureSystem.GetView(sessions[2], console).Status, Is.EqualTo(SWGlobalWarCaptureStatus.Contested));
                                Assert.That(captureSystem.GetView(sessions[2], console).Progress, Is.Zero);
                                MoveToSurface(Assignment(3).Body, eCapital);
                            });
                            await Tick(3602, () =>
                            {
                                Assert.That(captureSystem.GetView(sessions[2], console).Progress, Is.EqualTo(1).Within(.05));
                                server.EntMan.DeleteEntity(console);
                            });
                            await Tick(3603, () =>
                            {
                                console = server.EntMan.SpawnEntity("SWCombatTestCapture", position);
                                server.System<Content.Server.Power.EntitySystems.PowerNetSystem>().Update(0);
                                Assert.That(captureSystem.GetView(sessions[2], console).Progress, Is.EqualTo(1).Within(.05),
                                    "Replacing a destroyed device at the same address must preserve the objective.");
                                MoveToSurface(Assignment(2).Body, eCapital);
                            });
                            await Tick(3604, () => { });
                            await Tick(3606.1, () =>
                            {
                                Assert.That(captureSystem.GetView(sessions[2], console).Status, Is.EqualTo(SWGlobalWarCaptureStatus.Idle));
                                Assert.That(captureSystem.GetView(sessions[2], console).Progress, Is.Zero);
                                transform.SetCoordinates(Assignment(2).Body, new EntityCoordinates(grid.Value, .5f, 1.5f));
                                Assert.That(Start(), Is.True);
                            });
                            await Tick(3607.1, () => { });
                            await Tick(3608.1, () => { });
                            long capturedRevision = 0;
                            await Tick(3609.2, () =>
                            {
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital).OrbitOwner, Is.EqualTo(r));
                                capturedRevision = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital).OwnershipRevision;
                            });
                            await Tick(3610.2, () =>
                            {
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital).OwnershipRevision,
                                    Is.EqualTo(capturedRevision), "Completion must commit once.");
                                Assert.That(Start(), Is.False, "Cannot capture your own objective.");
                            });
                        };
                        break;

                        bool Start() => captureSystem.TryExecute(sessions[2], console, gen, Assignment(2).Revision,
                            Guid.NewGuid(), false);
                        void SetTime(double seconds)
                        {
                            timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) -
                                clock.Elapsed(campaign), timing.TimeBase.Item2);
                        }
                        async Task Tick(double seconds, Action assertion)
                        {
                            await server.WaitPost(() => SetTime(seconds));
                            // Let the engine advance its own tick and PVS buffers. Never forge CurTick before spawning.
                            await pair.RunTicksSync(1);
                            await server.WaitAssertion(assertion);
                        }
                    }
                    case "appointment-location":
                    case "appointment-race":
                    case "appointment-intelligence":
                    case "appointment-force":
                    {
                        var index = scenario == "appointment-force" ? 5 :
                            scenario == "appointment-intelligence" ? 4 : scenario == "appointment-race" ? 6 : 2;
                        var original = Assignment(index);
                        At(3600);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        var offeredAtHome = scenario == "appointment-race" ? OfferRole("SWPoliticsTestSoldier") : Guid.Empty;
                        MoveToSurface(original.Body, eCapital);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        var originalOrder = politics.GetWithdrawal(sessions[index]);
                        Assert.That(planets.TryEnsureLocation(eCapital, SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.True);
                        var entityIds = server.EntMan.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet();
                        var itemParents = server.EntMan.EntityQuery<ItemComponent>()
                            .ToDictionary(c => c.Owner, c => server.EntMan.GetComponent<TransformComponent>(c.Owner).ParentUid);
                        if (scenario is "appointment-location" or "appointment-force")
                        {
                            Assert.That(originalOrder, Is.Not.Null);
                            var promotion = OfferRole("SWPoliticsTestSenior");
                            Accept(promotion, false);
                            var civilianOffer = OfferRole("SWPoliticsTestIndustry");
                            Accept(civilianOffer, true);
                            At(3601);
                            Assert.That(politics.GetWithdrawal(sessions[index])!.Id, Is.EqualTo(originalOrder!.Id));
                            Assert.That(politics.GetWithdrawal(sessions[index])!.Deadline, Is.EqualTo(originalOrder.Deadline));
                            Assert.That(politics.CanAcquireMarketGoods(original.Body, gen),
                                Is.EqualTo(scenario != "appointment-force"));
                            if (scenario == "appointment-force")
                            {
                                Assert.That(registry.TryGetForceCarrier(original.User, out var persistent), Is.True);
                                Assert.That(persistent.Body, Is.EqualTo(original.Body));
                                Assert.That(persistent.Mind, Is.EqualTo(original.Mind));
                                Assert.That(politics.GetWithdrawalView(sessions[index])!.Active, Is.True);
                                Assert.That(politics.GetWithdrawal(sessions[index])!.Status,
                                    Is.EqualTo(SWGlobalWarWithdrawalStatus.Countdown));
                                Accept(OfferRole("SWPoliticsTestSenior"), false);
                                var resigningHead = Assignment(0);
                                Assert.That(registry.TryExecute(sessions[0], new(gen, Guid.NewGuid(),
                                    SWGlobalWarAppointmentAction.Resign, resigningHead.Revision, resigningHead.User,
                                    resigningHead.Revision), out _), Is.True);
                                Assert.That(registry.TryGetSuccession(r, original.User, out var forceVacancy, out var forceStage,
                                    out _, out var forceEligible), Is.True);
                                Assert.That(forceEligible, Is.True);
                                Assert.That(registry.TryExecute(sessions[index], new(gen, Guid.NewGuid(),
                                    SWGlobalWarAppointmentAction.ClaimHead, Assignment(index).Revision, default, 0,
                                    Vacancy: forceVacancy, Stage: forceStage), out _), Is.True);
                                politics.Update(0);
                                Assert.That(Assignment(index).Role, Is.EqualTo(SWGlobalWarRole.Head));
                                Assert.That(Assignment(index).Body, Is.EqualTo(original.Body));
                                Assert.That(Assignment(index).Mind, Is.EqualTo(original.Mind));
                                Assert.That(registry.TryGetForceCarrier(original.User, out _), Is.True);
                                Assert.That(politics.GetWithdrawalView(sessions[index])!.Active, Is.True);
                                Assert.That(politics.CanAcquireMarketGoods(original.Body, gen), Is.False);
                                Assert.That(politics.GetWithdrawal(sessions[index])!.Deadline, Is.EqualTo(originalOrder.Deadline));
                                break;
                            }
                            Assert.That(politics.GetWithdrawal(sessions[index])!.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.MayRemain));
                        }
                        var offer = offeredAtHome != Guid.Empty ? offeredAtHome : OfferRole("SWPoliticsTestSoldier");
                        var denied = Accept(offer, false);
                        var unchanged = Assignment(index);
                        if (scenario == "appointment-location")
                        {
                            At(originalOrder!.Deadline.TotalSeconds + 1);
                            Accept(offer, false);
                            Assert.That(politics.GetWithdrawal(sessions[index])!.Deadline, Is.EqualTo(originalOrder.Deadline));
                            Assert.That(politics.GetWithdrawal(sessions[index])!.Id, Is.EqualTo(originalOrder.Id));
                            Assert.That(politics.GetWithdrawalView(sessions[index])!.Active, Is.False);
                        }
                        // An orbit owned by our side does not legalize a military appointment on the foreign surface.
                        Change(eCapital, SWGlobalWarPlanetLocation.Orbit, r);
                        Accept(offer, false);
                        MoveToOrbit(original.Body, eCapital);
                        Assert.That(registry.GetCharacterView(sessions[index], server.EntMan.GetNetEntity(original.Body))
                            .Offers.Single(o => o.Id == offer).Failure, Is.EqualTo(SWGlobalWarAppointmentFailure.None));
                        Assert.That(registry.TryExecute(sessions[index], denied, out _, out var replayFailure), Is.False);
                        Assert.That(replayFailure, Is.EqualTo(SWGlobalWarAppointmentFailure.LocationForbidden));
                        Assert.That(registry.TryExecute(sessions[index], denied with { Job = "SWPoliticsTestSenior" },
                            out _, out replayFailure), Is.False);
                        Assert.That(replayFailure, Is.EqualTo(SWGlobalWarAppointmentFailure.InvalidRequest));
                        Assert.That(Assignment(index), Is.EqualTo(unchanged));
                        var success = Accept(offer, true);
                        // Replaying that success after another appointment must not reinstate the soldier role.
                        var spy = OfferRole("SWDiplomacyTestIntelligence");
                        MoveToSurface(original.Body, eCapital);
                        Accept(spy, true);
                        var afterSpy = Assignment(index);
                        Assert.That(registry.TryExecute(sessions[index], success, out _, out replayFailure), Is.True);
                        Assert.That(replayFailure, Is.EqualTo(SWGlobalWarAppointmentFailure.None));
                        Assert.That(Assignment(index), Is.EqualTo(afterSpy));
                        break;

                        Guid OfferRole(ProtoId<JobPrototype> job)
                        {
                            var sender = Assignment(0);
                            var target = Assignment(index);
                            // Free the one spy slot before offering it to another recipient.
                            if (job == "SWDiplomacyTestIntelligence" && index != 4 &&
                                registry.TryGetAssignment(sessions[4].UserId, out var spyAssignment))
                                Assert.That(registry.TryExecute(sessions[4], new(gen, Guid.NewGuid(),
                                    SWGlobalWarAppointmentAction.Resign, spyAssignment.Revision, spyAssignment.User,
                                    spyAssignment.Revision), out _), Is.True);
                            Assert.That(registry.TryExecute(sessions[0], new(gen, Guid.NewGuid(),
                                SWGlobalWarAppointmentAction.Offer, sender.Revision, target.User, target.Revision, job),
                                out var id), Is.True);
                            return id;
                        }

                        SWGlobalWarAppointmentCommand Accept(Guid id, bool expected)
                        {
                            var before = Assignment(index);
                            var command = new SWGlobalWarAppointmentCommand(gen, Guid.NewGuid(),
                                SWGlobalWarAppointmentAction.Accept, before.Revision, default, 0, Offer: id);
                            var view = registry.GetCharacterView(sessions[index], server.EntMan.GetNetEntity(original.Body));
                            Assert.That(view.Offers.Single(o => o.Id == id).Failure, Is.EqualTo(expected
                                ? SWGlobalWarAppointmentFailure.None : SWGlobalWarAppointmentFailure.LocationForbidden));
                            Assert.That(registry.GetCharacterView(sessions[0], server.EntMan.GetNetEntity(Assignment(0).Body))
                                .Outgoing.Single(o => o.Id == id).Failure, Is.EqualTo(SWGlobalWarAppointmentFailure.None));
                            Assert.That(registry.TryExecute(sessions[index], command, out _, out var failure), Is.EqualTo(expected));
                            Assert.That(failure, Is.EqualTo(expected ? SWGlobalWarAppointmentFailure.None :
                                SWGlobalWarAppointmentFailure.LocationForbidden));
                            if (!expected)
                            {
                                Assert.That(Assignment(index), Is.EqualTo(before));
                                Assert.That(registry.GetCharacterView(sessions[index], server.EntMan.GetNetEntity(original.Body))
                                    .Offers.Select(o => o.Id), Does.Contain(id));
                            }
                            Assert.That(Assignment(index).Body, Is.EqualTo(original.Body));
                            Assert.That(Assignment(index).Mind, Is.EqualTo(original.Mind));
                            Assert.That(server.EntMan.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet(),
                                Is.EquivalentTo(entityIds));
                            foreach (var (item, parent) in itemParents)
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(item).ParentUid, Is.EqualTo(parent));
                            return command;
                        }
                    }
                    case "withdraw-ship-ftl":
                    case "withdraw-ship-dock":
                        var flightSource = Planet("SWWithdrawalFlightSource");
                        var flightTarget = Planet("SWWithdrawalFlightTarget");
                        Assert.That(planets.TryEnsureLocation(flightSource, SWGlobalWarPlanetLocation.Orbit, out var sourceMap, out _), Is.True);
                        Assert.That(planets.TryEnsureLocation(flightTarget, SWGlobalWarPlanetLocation.Orbit, out var targetMap, out _), Is.True);
                        server.CfgMan.SetCVar(CCVars.FTLStartupTime, .1f);
                        server.CfgMan.SetCVar(CCVars.FTLTravelTime, 30f);
                        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, .2f);
                        server.CfgMan.SetCVar(CCVars.FTLCooldown, .1f);
                        var ftlMaps = server.System<Robust.Server.GameObjects.MapSystem>();
                        var ftlManager = server.ResolveDependency<IMapManager>();
                        var departing = FlightGrid(sourceMap!.Value, 1000);
                        server.EntMan.AddComponent<SWWithdrawalFtlProbeComponent>(departing);
                        var oldTarget = FlightGrid(targetMap!.Value, 1000);
                        var departureDock = server.EntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(departing, .5f, .5f));
                        var oldDock = server.EntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(oldTarget, .5f, .5f));
                        Assert.That(server.System<DockingSystem>().GetDockingConfig(departing, oldTarget), Is.Not.Null);
                        Assert.That(server.System<SWGlobalWarShipOwnershipSystem>().RegisterShip(departing, gen, r), Is.True);
                        var passengerBody = Assignment(2).Body;
                        var passengerOwnedMind = Assignment(2).Mind;
                        server.System<SharedTransformSystem>().SetCoordinates(passengerBody, new EntityCoordinates(departing, .5f, 1.5f));
                        var carried = server.EntMan.SpawnEntity("Pen", new EntityCoordinates(departing, .5f, 2.5f));
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        var completions = 0;
                        probe.FtlCompleted = completed =>
                        {
                            if (completed == departing)
                                completions++;
                        };
                        server.System<ShuttleSystem>().FTLToDock(departing,
                            server.EntMan.GetComponent<ShuttleComponent>(departing), oldTarget);
                        Assert.That(server.EntMan.HasComponent<FTLComponent>(departing), Is.True);
                        runtimeScenario = async () =>
                        {
                            if (scenario == "withdraw-ship-dock")
                            {
                                await CheckDockedWithdrawal(pair, gen, state.Owner, departing, oldTarget,
                                    departureDock, oldDock, carried, Assignment(3).Body,
                                    (index, action) => Act(index, action), FlightGrid);
                                return;
                            }
                            await pair.RunSeconds(1);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(server.EntMan.GetComponent<FTLComponent>(departing).State, Is.EqualTo(FTLState.Travelling));
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarTravelComponent>(departing).DockTarget, Is.EqualTo(oldTarget));
                                Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                                Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                            });
                            await pair.RunSeconds(4);
                            var exclusions = new System.Collections.Generic.List<EntityUid>();
                            EntityUid selectedMap = default;
                            await server.WaitAssertion(() =>
                            {
                                var flight = server.EntMan.GetComponent<SWGlobalWarTravelComponent>(departing);
                                Assert.That(flight.Withdrawal, Is.Not.Null);
                                Assert.That(flight.DockTarget, Is.Null);
                                Assert.That(flight.DestinationPlanet, Is.EqualTo(rCapital));
                                selectedMap = flight.DestinationMap;
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(departing).MapUid, Is.Not.EqualTo(selectedMap));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(passengerBody).GridUid, Is.EqualTo(departing));
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(targetMap.Value).IncomingTravellers, Does.Not.Contain(departing));
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(selectedMap).IncomingTravellers, Does.Contain(departing));
                                foreach (var planet in server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(state.Owner).PlanetEntities.Values)
                                {
                                    if (server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).OrbitOwner != r)
                                        continue;
                                    Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
                                    exclusions.Add(server.EntMan.SpawnEntity("TravelTestExclusion", new EntityCoordinates(orbit!.Value, default)));
                                }
                            });
                            await pair.RunSeconds(32);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(completions, Is.Zero);
                                Assert.That(server.EntMan.GetComponent<FTLComponent>(departing).State, Is.EqualTo(FTLState.Arriving));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(departing).MapUid, Is.Not.EqualTo(selectedMap));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(passengerBody).GridUid, Is.EqualTo(departing));
                                foreach (var exclusion in exclusions)
                                    server.EntMan.DeleteEntity(exclusion);
                            });
                            await pair.RunSeconds(12);
                            await server.WaitAssertion(CheckArrivedFlight);
                            await pair.RunSeconds(35);
                            await server.WaitAssertion(CheckArrivedFlight);

                            void CheckArrivedFlight()
                            {
                                Assert.That(completions, Is.EqualTo(1));
                                Assert.That(server.EntMan.HasComponent<FTLComponent>(departing), Is.False);
                                Assert.That(server.EntMan.HasComponent<SWGlobalWarTravelComponent>(departing), Is.False);
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(departing).MapUid, Is.EqualTo(selectedMap));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(carried).GridUid, Is.EqualTo(departing));
                                Assert.That(server.EntMan.GetComponent<TransformComponent>(passengerBody).GridUid, Is.EqualTo(departing));
                                Assert.That(server.EntMan.GetComponent<MindComponent>(passengerOwnedMind).OwnedEntity, Is.EqualTo(passengerBody));
                                Assert.That(server.EntMan.GetComponent<DockingComponent>(departureDock).DockedWith, Is.Null);
                                Assert.That(server.EntMan.GetComponent<DockingComponent>(oldDock).DockedWith, Is.Null);
                                foreach (var map in new[] { sourceMap.Value, targetMap.Value, selectedMap })
                                    Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetMapComponent>(map).IncomingTravellers, Does.Not.Contain(departing));
                            }
                        };
                        break;

                        EntityUid FlightGrid(EntityUid map, float offset)
                        {
                            var grid = ftlManager.CreateGridEntity(server.EntMan.GetComponent<TransformComponent>(map).MapID);
                            ftlMaps.SetTiles(grid.Owner, grid.Comp, new System.Collections.Generic.List<(Vector2i, Tile)>
                            {
                                (new Vector2i(0, 0), new Tile(1)), (new Vector2i(0, 1), new Tile(1)),
                                (new Vector2i(0, 2), new Tile(1)),
                            });
                            server.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, new Vector2(offset, offset));
                            return grid.Owner;
                        }
                    case "withdraw-ship":
                    case "withdraw-ship-mixed":
                    case "withdraw-ship-mixed-blocked":
                    case "withdraw-ship-mixed-safety":
                    case "withdraw-ship-mixed-surface-safety":
                    case "withdraw-ship-mixed-flow":
                    case "withdraw-ship-mixed-callback-war":
                    case "withdraw-ship-mixed-callback-owner":
                    case "withdraw-ship-foreign":
                    case "withdraw-ship-unknown":
                    case "withdraw-ship-ownership":
                    case "withdraw-ship-independent":
                        var sourceLocation = scenario.Contains("surface", StringComparison.Ordinal)
                            ? SWGlobalWarPlanetLocation.Surface : SWGlobalWarPlanetLocation.Orbit;
                        Assert.That(planets.TryEnsureLocation(eCapital, sourceLocation, out var shipSource, out _), Is.True);
                        Assert.That(planets.TryEnsureLocation(rCapital, SWGlobalWarPlanetLocation.Orbit, out _, out _), Is.True);
                        Assert.That(planets.TryGetOrbitStation(rCapital, out var homeStation), Is.True);
                        var mapManager = server.ResolveDependency<IMapManager>();
                        var shipGrid = mapManager.CreateGridEntity(server.EntMan.GetComponent<TransformComponent>(shipSource!.Value).MapID);
                        var ship = shipGrid.Owner;
                        var mapSystem = server.System<Robust.Server.GameObjects.MapSystem>();
                        var shipTiles = new System.Collections.Generic.List<(Vector2i, Tile)>();
                        for (var tileX = 0; tileX < 5; tileX++)
                        for (var tileY = 0; tileY < 5; tileY++)
                            shipTiles.Add((new Vector2i(tileX, tileY), new Tile(1)));
                        mapSystem.SetTiles(ship, shipGrid.Comp, shipTiles);
                        // Surface spawn fixtures already occupy (1000, 1000). Overlapping grids can reparent passengers.
                        var transforms = server.System<SharedTransformSystem>();
                        transforms.SetLocalPosition(ship, new Vector2(1200, 1200));
                        var shipBounds = transforms.GetWorldMatrix(ship).TransformBox(shipGrid.Comp.LocalAABB);
                        foreach (var otherGrid in server.EntMan.EntityQuery<MapGridComponent>())
                        {
                            if (otherGrid.Owner == ship ||
                                server.EntMan.GetComponent<TransformComponent>(otherGrid.Owner).MapUid != shipSource)
                                continue;
                            var otherBounds = transforms.GetWorldMatrix(otherGrid.Owner).TransformBox(otherGrid.LocalAABB);
                            Assert.That(shipBounds.Intersects(otherBounds), Is.False,
                                $"Withdrawal ship {shipBounds} must be separate from grid {otherGrid.Owner}: {otherBounds}.");
                        }
                        Assert.That(server.EntMan.HasComponent<ShuttleComponent>(ship), Is.True);
                        var shipOwnership = server.System<SWGlobalWarShipOwnershipSystem>();
                        Assert.That(shipOwnership.RegisterShip(homeStation!.Value, gen, r), Is.False,
                            "A station grid cannot acquire withdrawal rights by being labelled a ship.");
                        if (scenario != "withdraw-ship-unknown")
                        {
                            var owner = scenario == "withdraw-ship-foreign" ? e : r;
                            Assert.That(shipOwnership.RegisterShip(ship, gen, owner), Is.True,
                                "Explicit fixture registration represents a committed construction, not pilot/StationMember inference.");
                            Assert.That(shipOwnership.RegisterShip(ship, gen, owner), Is.True);
                            Assert.That(shipOwnership.RegisterShip(ship, gen, owner == r ? e : r), Is.False);
                        }
                        var sailor = Assignment(2).Body;
                        var sailorMind = Assignment(2).Mind;
                        server.System<SharedTransformSystem>().SetCoordinates(sailor, new EntityCoordinates(ship, 2.5f, 2.5f));
                        var foreignSailor = Assignment(3).Body;
                        EntityUid receptionAnchor = default;
                        if (scenario.StartsWith("withdraw-ship-mixed", StringComparison.Ordinal))
                        {
                            server.System<SharedTransformSystem>().SetCoordinates(foreignSailor, new EntityCoordinates(ship, 3.5f, 2.5f));
                            // A distinct human reception on the current orbit, never a vacuum FTL arrival or latejoin point.
                            var receptionGrid = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital).StationGrids
                                .First(g => server.EntMan.GetComponent<TransformComponent>(g).MapUid == shipSource);
                            var receptionTiles = new System.Collections.Generic.List<(Vector2i, Tile)>();
                            // Connect the fixture to an existing station tile, so removal cannot split off an artificial island.
                            var existing = mapSystem.GetAllTiles(receptionGrid,
                                server.EntMan.GetComponent<MapGridComponent>(receptionGrid)).First().GridIndices;
                            for (var x = Math.Min(existing.X, 103); x <= Math.Max(existing.X, 103); x++)
                                receptionTiles.Add((new Vector2i(x, existing.Y), new Tile(1)));
                            for (var y = Math.Min(existing.Y, 103); y <= Math.Max(existing.Y, 103); y++)
                                receptionTiles.Add((new Vector2i(103, y), new Tile(1)));
                            for (var x = 100; x < 115; x++)
                            for (var y = 100; y < 107; y++)
                                receptionTiles.Add((new Vector2i(x, y), new Tile(1)));
                            mapSystem.SetTiles(receptionGrid, server.EntMan.GetComponent<MapGridComponent>(receptionGrid), receptionTiles);
                            server.System<SharedPhysicsSystem>().SetBodyType(receptionGrid, BodyType.Static);
                            receptionAnchor = server.EntMan.SpawnEntity("SWWithdrawalTestReception", new EntityCoordinates(receptionGrid, 103.5f, 103.5f));
                            // Ambient atmosphere is explicit test setup, not proof of a production orbit's life support.
                            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                            air.SetMoles(Gas.Oxygen, 21.824f);
                            air.SetMoles(Gas.Nitrogen, 82.103f);
                            server.System<AtmosphereSystem>().SetMapAtmosphere(shipSource.Value, false, air);
                        }
                        if (scenario == "withdraw-ship-mixed-blocked")
                        {
                            foreach (var anchor in server.EntMan.EntityQuery<SWGlobalWarMapAnchorComponent>().ToArray())
                            {
                                if (planets.TryGetPhysicalLocation(anchor.Owner, out var anchorPlanet, out var anchorLocation) &&
                                    anchorPlanet == eCapital && anchorLocation == SWGlobalWarPlanetLocation.Orbit)
                                    server.EntMan.DeleteEntity(anchor.Owner);
                            }
                        }
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        if (scenario is not ("withdraw-ship-mixed-flow" or "withdraw-ship-ownership" or "withdraw-ship-independent"))
                        {
                            Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                            Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        }
                        runtimeScenario = async () =>
                        {
                            if (scenario.Contains("-callback-", StringComparison.Ordinal))
                            {
                                await CheckReceptionCallback(pair, scenario, foreignSailor, ship, eCapital,
                                    receptionAnchor, gen, (index, action) => Act(index, action));
                                return;
                            }
                            if (scenario is "withdraw-ship-ownership" or "withdraw-ship-independent")
                            {
                                await CheckShipGroups(pair, scenario, gen, state.Owner, ship, sailor,
                                    foreignSailor, rCapital, eCapital, (index, action) => Act(index, action));
                                return;
                            }
                            EntityUid? safetyDoor = null;
                            if (scenario.EndsWith("-safety", StringComparison.Ordinal))
                                safetyDoor = await CheckReceptionSafety(pair, foreignSailor, eCapital, sourceLocation,
                                    receptionAnchor, gen);
                            if (scenario == "withdraw-ship-mixed-flow")
                                await CheckReceptionPressureFlow(pair, foreignSailor, ship, eCapital, receptionAnchor,
                                    gen, (index, action) => Act(index, action));
                            await pair.RunSeconds(scenario == "withdraw-ship-mixed-flow" ? 6 : 121);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(planets.TryGetPhysicalLocation(ship, out var currentShipPlanet, out _), Is.True);
                                Assert.That(planets.TryGetPhysicalLocation(sailor, out var currentSailorPlanet, out _), Is.True);
                                Assert.That(currentSailorPlanet, Is.EqualTo(rCapital));
                                Assert.That(server.EntMan.GetComponent<MindComponent>(sailorMind).OwnedEntity, Is.EqualTo(sailor));
                                Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned));
                                if (scenario is "withdraw-ship" or "withdraw-ship-mixed" or "withdraw-ship-mixed-flow" || scenario.EndsWith("-safety", StringComparison.Ordinal))
                                {
                                    var visitorTx = server.EntMan.GetComponent<TransformComponent>(foreignSailor);
                                    Assert.That(currentShipPlanet, Is.EqualTo(rCapital),
                                        $"Visitor map/grid={visitorTx.MapUid}/{visitorTx.GridUid}; " +
                                        $"status={politics.GetWithdrawal(sessions[3])?.Status}; " +
                                        $"ship-order={state.Comp.ShipWithdrawals.FirstOrDefault(p => p.Key == ship).Value?.Status}; " +
                                        $"doorPowered={(safetyDoor is { } d ? server.EntMan.GetComponent<Content.Shared.Doors.Components.AirlockComponent>(d).Powered : null)}; " +
                                        $"doorState={(safetyDoor is { } d2 ? server.EntMan.GetComponent<Content.Shared.Doors.Components.DoorComponent>(d2).State : null)}");
                                    Assert.That(server.EntMan.GetComponent<TransformComponent>(sailor).GridUid,
                                        scenario == "withdraw-ship-mixed-flow" ? Is.Not.EqualTo(ship) : Is.EqualTo(ship));
                                    if (scenario.StartsWith("withdraw-ship-mixed", StringComparison.Ordinal))
                                    {
                                        Assert.That(planets.TryGetPhysicalLocation(foreignSailor, out var visitorPlanet, out var visitorLocation), Is.True);
                                        Assert.That(visitorPlanet, Is.EqualTo(eCapital));
                                        Assert.That(visitorLocation, Is.EqualTo(sourceLocation));
                                        Assert.That(server.EntMan.GetComponent<TransformComponent>(foreignSailor).GridUid, Is.Not.EqualTo(ship));
                                        Assert.That(politics.GetWithdrawal(sessions[3])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.MayRemain));
                                        if (safetyDoor != null)
                                            TestContext.Out.WriteLine($"Reception completed: source={sourceLocation}; " +
                                                $"visitorPlanet={visitorPlanet}; visitorGrid={visitorTx.GridUid}; " +
                                                $"ship={ship}; shipPlanet={currentShipPlanet}; ownPassengerPlanet={currentSailorPlanet}; " +
                                                $"visitorStatus={politics.GetWithdrawal(sessions[3])?.Status}.");
                                    }
                                }
                                else
                                {
                                    Assert.That(currentShipPlanet, Is.EqualTo(eCapital));
                                    Assert.That(server.EntMan.GetComponent<TransformComponent>(sailor).GridUid, Is.Not.EqualTo(ship));
                                    if (scenario == "withdraw-ship-mixed-blocked")
                                    {
                                        Assert.That(server.EntMan.GetComponent<TransformComponent>(foreignSailor).GridUid, Is.EqualTo(ship));
                                        Assert.That(politics.GetWithdrawal(sessions[3]), Is.Null,
                                            "A lawful visitor remains with the carrier while the independent group leaves.");
                                    }
                                }
                            });
                        };
                        break;
                    case "withdraw-integration":
                        Assert.That(planets.TryEnsureLocation(Planet("SWDiplomacyTestN0"), SWGlobalWarPlanetLocation.Surface,
                            out var neutralMap, out _), Is.True);
                        var visitingBodies = new[] { Assignment(2).Body, Assignment(3).Body };
                        var visitingMinds = new[] { Assignment(2).Mind, Assignment(3).Mind };
                        var visitors = new EntityUid[2];
                        var neutralContainers = new Container[2];
                        for (var index = 0; index < 2; index++)
                        {
                            var outer = server.EntMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(neutralMap!.Value, default));
                            var inner = server.EntMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(neutralMap.Value, default));
                            var storage = server.System<SharedContainerSystem>();
                            Assert.That(storage.Insert(inner, storage.EnsureContainer<Container>(outer, "outer"), force: true), Is.True);
                            neutralContainers[index] = storage.EnsureContainer<Container>(inner, "body");
                            Assert.That(storage.Insert(visitingBodies[index], neutralContainers[index], force: true), Is.True);
                            visitors[index] = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                            server.System<SharedMindSystem>().Visit(visitingMinds[index], visitors[index]);
                        }
                        runtimeScenario = async () =>
                        {
                            await pair.RunSeconds(4);
                            var loser = -1;
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(n0.Phase, Is.EqualTo(SWGlobalWarNeutralPhase.Integrated));
                                loser = n0.Claimant == r ? 1 : 0;
                                Assert.That(politics.GetWithdrawal(sessions[loser + 2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Countdown));
                                Assert.That(politics.GetWithdrawal(sessions[3 - loser]), Is.Null, "The winning garrison remains lawful.");
                            });
                            await pair.RunSeconds(121);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(politics.GetWithdrawal(sessions[loser + 2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned));
                                Assert.That(neutralContainers[loser].Contains(visitingBodies[loser]), Is.False);
                                Assert.That(neutralContainers[1 - loser].Contains(visitingBodies[1 - loser]), Is.True);
                                for (var index = 0; index < 2; index++)
                                {
                                    var mind = server.EntMan.GetComponent<MindComponent>(visitingMinds[index]);
                                    Assert.That(mind.OwnedEntity, Is.EqualTo(visitingBodies[index]));
                                    Assert.That(mind.VisitingEntity, Is.EqualTo(visitors[index]));
                                    server.System<SharedMindSystem>().UnVisit(visitingMinds[index]);
                                }
                            });
                        };
                        break;
                    case "withdraw-chair":
                    case "withdraw-pull":
                    case "withdraw-ssd":
                        var linkedBody = Assignment(2).Body;
                        var linkedMind = Assignment(2).Mind;
                        var foreignPuller = Assignment(3).Body;
                        MoveToOrbit(linkedBody, eCapital);
                        MoveToOrbit(foreignPuller, eCapital);
                        var sourceCoordinates = server.EntMan.GetComponent<TransformComponent>(linkedBody).Coordinates;
                        var carrier = server.EntMan.SpawnEntity(scenario == "withdraw-chair" ? "Chair" : "CrateGenericSteel", sourceCoordinates);
                        Container? nestedBody = null;
                        if (scenario == "withdraw-chair")
                            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(linkedBody, linkedBody, carrier), Is.True);
                        else if (scenario == "withdraw-pull")
                        {
                            var pulling = server.System<PullingSystem>();
                            var linkedHands = server.System<SharedHandsSystem>();
                            foreach (var actor in new[] { linkedBody, foreignPuller })
                            foreach (var held in linkedHands.EnumerateHeld(actor).ToArray())
                                Assert.That(linkedHands.TryDrop(actor, held, checkActionBlocker: false), Is.True);
                            Assert.That(pulling.TryStartPull(linkedBody, carrier), Is.True);
                            Assert.That(pulling.TryStartPull(foreignPuller, linkedBody), Is.True);
                        }
                        else
                        {
                            var storage = server.System<SharedContainerSystem>();
                            var inner = server.EntMan.SpawnEntity("CrateGenericSteel", sourceCoordinates);
                            Assert.That(storage.Insert(inner, storage.EnsureContainer<Container>(carrier, "outer"), force: true), Is.True);
                            nestedBody = storage.EnsureContainer<Container>(inner, "body");
                            Assert.That(storage.Insert(linkedBody, nestedBody, force: true), Is.True);
                        }
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        var linkedDeadline = politics.GetWithdrawal(sessions[2])!.Deadline;
                        runtimeScenario = async () =>
                        {
                            if (scenario == "withdraw-ssd")
                            {
                                await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Withdrawal SSD test"));
                                await pair.RunTicksSync(5);
                                await server.WaitAssertion(() =>
                                {
                                    Assert.That(server.PlayerMan.Sessions.Any(s => s.UserId == networkUser), Is.False);
                                    Assert.That(server.PlayerMan.ValidSessionId(networkUser), Is.False);
                                    Assert.That(server.EntMan.GetComponent<MindComponent>(linkedMind).UserId, Is.EqualTo(networkUser));
                                    Assert.That(server.EntMan.GetComponent<MindComponent>(linkedMind).OwnedEntity, Is.EqualTo(linkedBody));
                                });
                            }
                            await pair.RunSeconds(121);
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned));
                                Assert.That(politics.GetWithdrawal(sessions[2])?.Deadline, Is.EqualTo(linkedDeadline));
                                Assert.That(server.EntMan.GetComponent<MindComponent>(linkedMind).OwnedEntity, Is.EqualTo(linkedBody));
                                Assert.That(server.EntMan.GetComponent<BuckleComponent>(linkedBody).BuckledTo, Is.Null);
                                Assert.That(server.EntMan.GetComponent<PullableComponent>(linkedBody).Puller, Is.Null);
                                Assert.That(server.EntMan.GetComponent<PullerComponent>(linkedBody).Pulling, Is.Null);
                                Assert.That(server.EntMan.GetComponent<PullerComponent>(foreignPuller).Pulling, Is.Null);
                                Assert.That(nestedBody?.Contains(linkedBody) ?? false, Is.False);
                                Assert.That(planets.TryGetPhysicalLocation(carrier, out var carrierPlanet, out _), Is.True);
                                Assert.That(carrierPlanet, Is.EqualTo(eCapital), "Foreign carrier is not appropriated.");
                            });
                            if (scenario == "withdraw-ssd")
                            {
                                pair.Client.SetConnectTarget(server);
                                await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>()
                                    .ClientConnect(null!, 0, sessions[2].Name));
                                await pair.RunTicksSync(10);
                                await server.WaitAssertion(() =>
                                {
                                    Assert.That(server.PlayerMan.Sessions.Single(s => s.UserId == networkUser).AttachedEntity, Is.EqualTo(linkedBody));
                                    Assert.That(politics.GetWithdrawal(sessions[2])?.Deadline, Is.EqualTo(linkedDeadline));
                                });
                            }
                        };
                        break;
                    case "withdraw-vehicle":
                    case "withdraw-passenger":
                    case "withdraw-lazy":
                        var industry = Planet("SWSpawnTestIndustry");
                        if (scenario == "withdraw-lazy")
                        {
                            // Home industry surfaces are warm from the round start, so this fallback needs no new map.
                            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(industry).Maps, Does.ContainKey(SWGlobalWarPlanetLocation.Surface));
                            foreach (var anchor in server.EntMan.EntityQuery<SWGlobalWarMapAnchorComponent>().ToArray())
                            {
                                if (planets.TryGetPhysicalLocation(anchor.Owner, out var home, out var location) &&
                                    home == rCapital && location == SWGlobalWarPlanetLocation.Surface)
                                    server.EntMan.DeleteEntity(anchor.Owner);
                            }
                        }
                        var driver = Assignment(2).Body;
                        var driverMind = Assignment(2).Mind;
                        MoveToOrbit(driver, eCapital);
                        var vehicle = server.EntMan.SpawnEntity("SWSpider", server.EntMan.GetComponent<TransformComponent>(driver).Coordinates);
                        var vehicleState = server.EntMan.GetComponent<SWVehicleComponent>(vehicle);
                        var vehiclePosition = server.EntMan.GetComponent<TransformComponent>(vehicle).Coordinates;
                        var hands = server.System<SharedHandsSystem>();
                        foreach (var held in hands.EnumerateHeld(driver).ToArray())
                            Assert.That(hands.TryDrop(driver, held, checkActionBlocker: false), Is.True);
                        var personalItems = server.EntMan.EntityQuery<ItemComponent>()
                            .Select(c => c.Owner).Where(IsPersonalItem).ToHashSet();
                        Assert.That(personalItems, Is.Not.Empty);
                        server.System<SWVehicleSystem>().TryOccupyRole((vehicle, vehicleState), driver, "driver");
                        Assert.That(server.EntMan.HasComponent<SWInVehicleComponent>(driver), Is.True);
                        Assert.That(server.EntMan.HasComponent<RelayInputMoverComponent>(driver), Is.True);
                        Assert.That(hands.EnumerateHeld(driver).Count(x => server.EntMan.HasComponent<VirtualItemComponent>(x)), Is.EqualTo(2));
                        var seat = server.EntMan.GetComponent<BuckleComponent>(driver).BuckledTo;
                        Assert.That(seat, Is.Not.Null);
                        EntityUid? passenger = null;
                        EntityUid? passengerMind = null;
                        Container? passengerContainer = null;
                        if (scenario == "withdraw-passenger")
                        {
                            passenger = server.EntMan.SpawnEntity("PersonalAI", vehiclePosition);
                            passengerMind = server.System<SharedMindSystem>().CreateMind(sessions[6].UserId);
                            server.System<SharedMindSystem>().TransferTo(passengerMind.Value, passenger.Value);
                            Assert.That(server.EntMan.HasComponent<SWGlobalWarIdentityComponent>(passengerMind.Value), Is.False);
                            passengerContainer = server.System<SharedContainerSystem>().EnsureContainer<Container>(driver, "personal-passenger");
                            Assert.That(server.System<SharedContainerSystem>().Insert(passenger.Value, passengerContainer, force: true), Is.True);
                        }
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        var withdrawalDeadline = politics.GetWithdrawal(sessions[2])!.Deadline;
                        runtimeScenario = async () =>
                        {
                            await pair.RunSeconds(121);
                            if (passenger != null)
                            {
                                await server.WaitAssertion(() =>
                                {
                                    Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.WaitingForSeparation));
                                    Assert.That(passengerContainer!.Contains(passenger.Value), Is.True);
                                    Assert.That(server.EntMan.HasComponent<SWInVehicleComponent>(driver), Is.True);
                                    Assert.That(server.EntMan.HasComponent<RelayInputMoverComponent>(driver), Is.True);
                                    Assert.That(server.EntMan.GetComponent<BuckleComponent>(driver).BuckledTo, Is.EqualTo(seat));
                                    Assert.That(vehicleState.RolesInfo["driver"].Occupant, Is.EqualTo(server.EntMan.GetNetEntity(driver)));
                                    // A local separation removes the obstruction; retry retains the original deadline.
                                    Assert.That(server.System<SharedContainerSystem>().Remove(passenger.Value, passengerContainer,
                                        force: true, destination: vehiclePosition), Is.True);
                                });
                                await pair.RunSeconds(6);
                            }
                            await server.WaitAssertion(() =>
                            {
                                Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned));
                                Assert.That(politics.GetWithdrawal(sessions[2])?.Deadline, Is.EqualTo(withdrawalDeadline));
                                Assert.That(server.EntMan.HasComponent<SWInVehicleComponent>(driver), Is.False);
                                Assert.That(server.EntMan.HasComponent<RelayInputMoverComponent>(driver), Is.False);
                                Assert.That(server.EntMan.GetComponent<BuckleComponent>(driver).BuckledTo, Is.Null);
                                Assert.That(vehicleState.RolesInfo["driver"].Occupant, Is.Null);
                                Assert.That(hands.EnumerateHeld(driver).Any(x => server.EntMan.HasComponent<VirtualItemComponent>(x)), Is.False);
                                Assert.That(planets.TryGetPhysicalLocation(vehicle, out var vehiclePlanet, out _), Is.True);
                                Assert.That(vehiclePlanet, Is.EqualTo(eCapital));
                                Assert.That(server.EntMan.GetComponent<MindComponent>(driverMind).OwnedEntity, Is.EqualTo(driver));
                                Assert.That(planets.TryGetPhysicalLocation(driver, out var driverPlanet, out _), Is.True);
                                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(driverPlanet).SurfaceOwner, Is.EqualTo(r));
                                if (scenario == "withdraw-lazy")
                                    Assert.That(driverPlanet, Is.EqualTo(industry), "Fallback surface loaded during ordinary campaign Update.");
                                foreach (var personalItem in personalItems)
                                    Assert.That(IsPersonalItem(personalItem), Is.True, $"Personal item {personalItem} remains with the same body.");
                                if (passenger != null)
                                {
                                    Assert.That(server.EntMan.GetComponent<MindComponent>(passengerMind!.Value).OwnedEntity, Is.EqualTo(passenger));
                                    Assert.That(planets.TryGetPhysicalLocation(passenger.Value, out var passengerPlanet, out _), Is.True);
                                    Assert.That(passengerPlanet, Is.EqualTo(eCapital));
                                }
                                var item = server.EntMan.SpawnEntity("Pen", server.EntMan.GetComponent<TransformComponent>(driver).Coordinates);
                                Assert.That(hands.TryPickup(driver, item), Is.True, "Real hands are usable after deferred controls are deleted.");
                            });
                        };
                        break;
                    case "withdraw-body":
                    case "withdraw-no-point":
                        // Preload return candidates before deterministic clock injection; map loading creates real entities
                        // and must run with the engine's current PVS tick, not the manually advanced policy boundary tick.
                        foreach (var planet in server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(state.Owner).PlanetEntities.Values)
                            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out _, out _), Is.True);
                        var returningBody = Assignment(2).Body;
                        var returningMind = Assignment(2).Mind;
                        MoveToOrbit(returningBody, eCapital);
                        MoveToOrbit(Assignment(3).Body, rCapital);
                        var containers = server.System<SharedContainerSystem>();
                        var crate = server.EntMan.SpawnEntity("CrateGenericSteel", server.EntMan.GetComponent<TransformComponent>(returningBody).Coordinates);
                        var bodyContainer = containers.EnsureContainer<Container>(crate, "withdrawal-test");
                        Assert.That(containers.Insert(returningBody, bodyContainer, force: true), Is.True);
                        var visitor = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                        server.System<SharedMindSystem>().Visit(returningMind, visitor);
                        server.System<MobStateSystem>().ChangeMobState(returningBody, MobState.Critical);
                        var existingIds = server.EntMan.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet();
                        if (scenario == "withdraw-no-point")
                        {
                            foreach (var anchor in server.EntMan.EntityQuery<SWGlobalWarMapAnchorComponent>().ToArray())
                            {
                                if (planets.TryGetPhysicalLocation(anchor.Owner, out var anchorPlanet, out _) &&
                                    server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(anchorPlanet).SurfaceOwner == r)
                                    server.EntMan.DeleteEntity(anchor.Owner);
                            }
                            existingIds = server.EntMan.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet();
                        }
                        At(3600);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(politics.GetWithdrawal(sessions[2])?.Deadline, Is.EqualTo(TimeSpan.FromSeconds(3720)));
                        Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Countdown));
                        Assert.That(politics.CanAcquireMarketGoods(returningBody, gen), Is.False);
                        At(3719);
                        Assert.That(bodyContainer.Contains(returningBody), Is.True);
                        At(3720);
                        Assert.That(politics.GetWithdrawal(sessions[3])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned),
                            "A failed Republic destination cannot prevent the independent Empire group.");
                        if (scenario == "withdraw-body")
                        {
                            Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Returned));
                            Assert.That(bodyContainer.Contains(returningBody), Is.False);
                            Assert.That(planets.TryGetPhysicalLocation(returningBody, out var returnedPlanet, out _), Is.True);
                            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(returnedPlanet).SurfaceOwner, Is.EqualTo(r));
                            Assert.That(server.EntMan.GetComponent<MobStateComponent>(returningBody).CurrentState, Is.EqualTo(MobState.Critical));
                        }
                        else
                        {
                            Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.WaitingForPoint));
                            Assert.That(bodyContainer.Contains(returningBody), Is.True);
                            At(3726);
                            Assert.That(politics.GetWithdrawal(sessions[2])?.Deadline, Is.EqualTo(TimeSpan.FromSeconds(3720)));
                            Assert.That(bodyContainer.Contains(returningBody), Is.True);
                            Assert.That(Act(0, SWGlobalWarPoliticalAction.BreakTruce), Is.True);
                            Assert.That(politics.GetWithdrawal(sessions[2])?.Status, Is.EqualTo(SWGlobalWarWithdrawalStatus.Cancelled));
                            Assert.That(bodyContainer.Contains(returningBody), Is.True);
                        }
                        Assert.That(server.EntMan.GetComponent<MindComponent>(returningMind).OwnedEntity, Is.EqualTo(returningBody));
                        Assert.That(server.EntMan.GetComponent<MindComponent>(returningMind).VisitingEntity, Is.EqualTo(visitor));
                        Assert.That(server.EntMan.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet(), Is.EquivalentTo(existingIds));
                        server.System<SharedMindSystem>().UnVisit(returningMind);
                        break;
                    case "sponsorship":
                        var first = Command(0, SWGlobalWarPoliticalAction.Sponsor) with
                            { Sector = "SWDiplomacyTestN0", Amount = 20_000 };
                        var notifications = server.System<SWPoliticsNotificationProbeSystem>();
                        var notified = false;
                        var politicalNotified = false;
                        void CheckFundingReceipt()
                        {
                            Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(430_000));
                            Assert.That(n0.RepublicContribution, Is.EqualTo(20_000));
                            Assert.That(state.Comp.Requests[(sessions[0].UserId, first.Request)].Success, Is.True);
                            Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                            Assert.That(accounts.Comp.Journal.Count(x => x.Command.Action == SWGlobalWarLedgerAction.Debit), Is.EqualTo(1));
                        }
                        notifications.PoliticsChanged = () =>
                        {
                            notifications.PoliticsChanged = null;
                            politicalNotified = true;
                            CheckFundingReceipt();
                            Assert.That(politics.Execute(sessions[0], first).Success, Is.True);
                        };
                        notifications.LedgerChanged = () =>
                        {
                            notifications.LedgerChanged = null;
                            notified = true;
                            CheckFundingReceipt();
                            Assert.That(politics.Execute(sessions[0], first).Success, Is.True);
                            Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(430_000));
                        };
                        Assert.That(politics.Execute(sessions[0], first).Success, Is.True);
                        Assert.That(notified, Is.True);
                        Assert.That(politicalNotified, Is.True);
                        Assert.That(politics.Execute(sessions[0], first).Success, Is.True);
                        Assert.That(politics.Execute(sessions[0], first with { Amount = 1 }).Success, Is.False);
                        // No cap per sector: any amount the treasury holds.
                        Assert.That(Sponsor(0, "SWDiplomacyTestN0", 40_000), Is.True);
                        Assert.That(Sponsor(0, "SWDiplomacyTestN0", 390_001), Is.False);
                        Assert.That(Sponsor(1, "SWDiplomacyTestN0", 30_000), Is.True);
                        Assert.That(Sponsor(0, "SWDiplomacyTestN1", 30_000), Is.True);
                        Assert.That(Sponsor(1, "SWDiplomacyTestN1", 30_000), Is.True);
                        Assert.That(n0.RepublicContribution, Is.EqualTo(60_000));
                        Assert.That(n0.EmpireContribution, Is.EqualTo(30_000));
                        Assert.That(n1.RepublicContribution, Is.EqualTo(30_000));
                        Assert.That(n1.EmpireContribution, Is.EqualTo(30_000));
                        Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(360_000));
                        Assert.That(ledger.GetBalance(e, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(390_000));
                        Assert.That(Sponsor(0, "SWDiplomacyTestN0", long.MaxValue), Is.False);
                        Assert.That(Sponsor(0, "SWDiplomacyTestN0", 0), Is.False);
                        Assert.That(SWGlobalWarPoliticsSystem.RepublicProbability(0, 0), Is.EqualTo(.5));
                        Assert.That(SWGlobalWarPoliticsSystem.RepublicProbability(100, 0), Is.EqualTo(101d / 102).Within(1e-15));
                        Assert.That(SWGlobalWarPoliticsSystem.RepublicProbability(30_000, 30_000), Is.EqualTo(.5));
                        Assert.That(SWGlobalWarPoliticsSystem.RepublicProbability(long.MaxValue, long.MaxValue), Is.EqualTo(.5));
                        break;
                    case "succession-funding":
                        Assert.That(Sponsor(0, "SWDiplomacyTestN0", 29_999), Is.True);
                        var old = Assignment(0);
                        Assert.That(registry.TryExecute(sessions[0], new SWGlobalWarAppointmentCommand(gen,
                            Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign, old.Revision, old.User, old.Revision), out _), Is.True);
                        Assert.That(registry.TryGetSuccession(r, sessions[6].UserId,
                            out var vacancy, out var stage, out _, out var eligible), Is.True);
                        Assert.That(eligible, Is.True);
                        var civilian = Assignment(6);
                        Assert.That(registry.TryExecute(sessions[6], new SWGlobalWarAppointmentCommand(gen,
                            Guid.NewGuid(), SWGlobalWarAppointmentAction.ClaimHead, civilian.Revision, civilian.User,
                            civilian.Revision, Vacancy: vacancy, Stage: stage), out _), Is.True);
                        Assert.That(Sponsor(6, "SWDiplomacyTestN0", 1), Is.True);
                        Assert.That(n0.RepublicContribution, Is.EqualTo(30_000), "The side keeps its contribution across a new head.");
                        At(1800, update: false);
                        Assert.That(Sponsor(6, "SWDiplomacyTestN1", 1), Is.False);
                        Assert.That(n1.RepublicContribution, Is.Zero);
                        Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(420_000));
                        break;
                    case "neutral":
                        At(1800);
                        Assert.That(n0.Phase, Is.EqualTo(SWGlobalWarNeutralPhase.Annexing));
                        Assert.That(n0.Remaining, Is.EqualTo(TimeSpan.FromSeconds(600)));
                        var draw = n0.DrawnWinner;
                        var drawOperation = n0.DrawOperation;
                        var challenging = n0.Claimant == r ? 1 : 0;
                        var soldier = challenging == 0 ? 2 : 3;
                        Assert.That(Act(challenging, SWGlobalWarPoliticalAction.OpenBorderConflict, "SWDiplomacyTestN0"), Is.True);
                        At(1900);
                        var capture = Guid.NewGuid();
                        Assert.That(politics.SetNeutralCapture(Assignment(soldier).Body, "SWDiplomacyTestN0", gen, capture, true), Is.True);
                        Assert.That(politics.SetNeutralCapture(Assignment(soldier).Body, "SWDiplomacyTestN0", gen, capture, true), Is.True);
                        At(1929);
                        Assert.That(n0.Remaining, Is.EqualTo(TimeSpan.FromSeconds(500)),
                            "An active F token retains the timer during absence grace and contesting.");
                        At(1930);
                        Assert.That(politics.SetNeutralCapture(Assignment(soldier).Body, "SWDiplomacyTestN0", gen, capture, false), Is.True);
                        At(1940);
                        Assert.That(n0.Remaining, Is.EqualTo(TimeSpan.FromSeconds(490)));
                        var claimant = n0.Claimant == r ? e : r;
                        Change(n0.Planet, SWGlobalWarPlanetLocation.Orbit, claimant);
                        Assert.That(n0.Claimant, Is.EqualTo(claimant));
                        Assert.That(n0.Remaining, Is.EqualTo(TimeSpan.FromSeconds(600)));
                        At(2539);
                        Assert.That(n0.Phase, Is.EqualTo(SWGlobalWarNeutralPhase.Annexing));
                        At(2540);
                        Assert.That(n0.Phase, Is.EqualTo(SWGlobalWarNeutralPhase.Integrated));
                        Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(n0.Planet).SurfaceOwner, Is.EqualTo(claimant));
                        Assert.That(n0.DrawnWinner, Is.EqualTo(draw));
                        Assert.That(n0.DrawOperation, Is.EqualTo(drawOperation));
                        break;
                    case "truce-policy":
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.False);
                        At(1800);
                        challenging = n0.Claimant == r ? 1 : 0;
                        Assert.That(Act(challenging, SWGlobalWarPoliticalAction.OpenBorderConflict, "SWDiplomacyTestN0"), Is.True);
                        Assert.That(politics.CanCapture(Assignment(4).Body, n0.Planet, SWGlobalWarPlanetLocation.Orbit, gen), Is.False);
                        Assert.That(politics.CanConductHostility(Assignment(4).Body, n0.Planet, gen), Is.False);
                        var force = Assignment(5);
                        MoveToOrbit(force.Body, eCapital);
                        Assert.That(CanLand(force.Body, eCapital), Is.False);
                        Assert.That(registry.TryExecute(sessions[5], new SWGlobalWarAppointmentCommand(gen, Guid.NewGuid(),
                            SWGlobalWarAppointmentAction.Resign, force.Revision, force.User, force.Revision), out _), Is.True);
                        Assert.That(CanLand(force.Body, eCapital), Is.False, "A live Force carrier remains restricted after resignation.");
                        MoveToOrbit(Assignment(4).Body, eCapital);
                        Assert.That(CanLand(Assignment(4).Body, eCapital), Is.True);
                        // Hold this neutral unresolved so the truce check tests the phase and not an expired annexation.
                        capture = Guid.NewGuid();
                        soldier = challenging == 0 ? 2 : 3;
                        Assert.That(politics.SetNeutralCapture(Assignment(soldier).Body, "SWDiplomacyTestN0", gen, capture, true), Is.True);
                        At(3599);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.False);
                        At(3600);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(state.Comp.Relations, Is.EqualTo(SWGlobalWarRelations.War));
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.OfferTruce), Is.True);
                        Assert.That(state.Comp.Relations, Is.EqualTo(SWGlobalWarRelations.Truce));
                        Assert.That(n0.BorderConflict, Is.Null);
                        Assert.That(n0.ActiveCaptures, Is.Empty);
                        var revision = state.Comp.Revision;
                        Assert.That(Act(challenging, SWGlobalWarPoliticalAction.OpenBorderConflict, "SWDiplomacyTestN0"), Is.False);
                        Assert.That(state.Comp.Revision, Is.EqualTo(revision));
                        Assert.That(politics.CanCapture(Assignment(soldier).Body, n0.Planet, SWGlobalWarPlanetLocation.Orbit, gen), Is.False);
                        Assert.That(Act(1, SWGlobalWarPoliticalAction.BreakTruce), Is.True);
                        Assert.That(politics.CanCapture(Assignment(soldier).Body, n0.Planet, SWGlobalWarPlanetLocation.Orbit, gen), Is.True);
                        break;
                    case "trade":
                        var commodity = server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<SWGlobalWarCommodityPrototype>().First().ID;
                        var trade = new SWGlobalWarTradeRequest(gen, commodity, r, SWGlobalWarFaction.Neutral,
                            "SWDiplomacyTestN0", "SWSpawnTestECapital", "SWDiplomacyTestN0", "SWSpawnTestECapital", SWGlobalWarTradeStage.Receipt);
                        Assert.That(politics.EvaluateTrade(trade).Allowed, Is.True, "The trade contract does not use the FTL graph.");
                        Assert.That(politics.Execute(sessions[0], Command(0, SWGlobalWarPoliticalAction.SetSanction) with
                            { Commodity = commodity, Enabled = true }).Success, Is.True);
                        Assert.That(politics.EvaluateTrade(trade).Reason, Is.EqualTo("sw-global-war-trade-sanction"));
                        Assert.That(politics.Execute(sessions[0], Command(0, SWGlobalWarPoliticalAction.SetSanction) with
                            { Commodity = commodity }).Success, Is.True);
                        Assert.That(politics.EvaluateTrade(trade).Allowed, Is.True);
                        Change(eCapital, SWGlobalWarPlanetLocation.Orbit, r);
                        Assert.That(politics.EvaluateTrade(trade).Reason, Is.EqualTo("sw-global-war-trade-blockade"));
                        Change(eCapital, SWGlobalWarPlanetLocation.Orbit, e);
                        At(3600);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        foreach (var phase in Enum.GetValues<SWGlobalWarTradeStage>())
                            Assert.That(politics.EvaluateTrade(trade with { Stage = phase }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(trade with { Generation = Guid.NewGuid() }).Allowed, Is.False);
                        break;
                    case "trade-evidence":
                        commodity = server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<SWGlobalWarCommodityPrototype>().First().ID;
                        At(3600);
                        Assert.That(Act(0, SWGlobalWarPoliticalAction.DeclareWar), Is.True);
                        var returning = new SWGlobalWarTradeRequest(gen, commodity, r, r, "SWSpawnTestRCapital",
                            "SWSpawnTestForce", "SWSpawnTestRCapital", "SWSpawnTestForce", SWGlobalWarTradeStage.Receipt,
                            SWGlobalWarTradePosition.InTransit);
                        Change(rCapital, SWGlobalWarPlanetLocation.Surface, e);
                        Change(rCapital, SWGlobalWarPlanetLocation.Orbit, e);
                        Assert.That(politics.EvaluateTrade(returning).Allowed, Is.True, "The cargo already left the captured port.");
                        Assert.That(politics.EvaluateTrade(returning with { Position = SWGlobalWarTradePosition.AtNode }).Allowed, Is.False);
                        var captureOperation = Change(eCapital, SWGlobalWarPlanetLocation.Surface, r);
                        Change(eCapital, SWGlobalWarPlanetLocation.Orbit, r);
                        Assert.That(politics.Execute(sessions[1], Command(1, SWGlobalWarPoliticalAction.SetSanction) with
                            { Commodity = commodity, Enabled = true }).Success, Is.True);
                        var batch = Guid.NewGuid();
                        var evidence = new SWGlobalWarTradeCaptureEvidence(gen, captureOperation, Guid.NewGuid(),
                            "SWSpawnTestECapital", batch, 10, e, r);
                        var domestic = new SWGlobalWarTradeRequest(gen, commodity, e, r, "SWSpawnTestECapital",
                            "SWSpawnTestForce", "SWSpawnTestECapital", "SWSpawnTestForce", SWGlobalWarTradeStage.Dispatch,
                            Batch: batch, Quantity: 10);
                        Assert.That(politics.EvaluateTrade(domestic).Allowed, Is.False, "CargoOwner alone is not capture evidence.");
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence }).Allowed, Is.True);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence, Quantity = 11 }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence with { Batch = Guid.NewGuid() } }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence with { Generation = Guid.NewGuid() } }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence with { CaptureOperation = Guid.NewGuid() } }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence, Recipient = "SWSpawnTestRCapital" }).Allowed, Is.False);
                        Assert.That(politics.EvaluateTrade(domestic with
                        {
                            Sender = "SWDiplomacyTestN0", Current = "SWDiplomacyTestN0", CargoOwner = SWGlobalWarFaction.Neutral,
                        }).Allowed, Is.False, "A neutral resale retains the original embargo.");
                        Change(eCapital, SWGlobalWarPlanetLocation.Orbit, e);
                        Assert.That(politics.EvaluateTrade(domestic with { Capture = evidence }).Reason, Is.EqualTo("sw-global-war-trade-blockade"));
                        break;
                    case "step-forward":
                    case "step-reverse":
                    case "step-runtime":
                        if (scenario != "step-runtime")
                        {
                            Change(eCapital, SWGlobalWarPlanetLocation.Surface, r);
                            At(1, update: false);
                        }
                        else
                        {
                            Assert.That(steps.TryQueueOwnership(eCapital, SWGlobalWarPlanetLocation.Surface, r,
                                0, gen, Guid.NewGuid()), Is.True);
                        }
                        var ownershipCallbacks = 0;
                        probe.OwnershipChanged = _ =>
                        {
                            ownershipCallbacks++;
                            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital).OrbitOwner, Is.EqualTo(r));
                            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(rCapital).SurfaceOwner, Is.EqualTo(e));
                            Assert.That(state.Comp.Outcome, Is.EqualTo(SWGlobalWarOutcome.None));
                        };
                        void Queue(EntityUid planet, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner)
                        {
                            Assert.That(steps.TryQueueOwnership(planet, location, owner,
                                server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).OwnershipRevision +
                                (scenario == "step-runtime" && planet == eCapital && location == SWGlobalWarPlanetLocation.Orbit ? 1 : 0),
                                gen, Guid.NewGuid()), Is.True);
                        }
                        if (scenario == "step-reverse")
                            Queue(rCapital, SWGlobalWarPlanetLocation.Surface, e);
                        else
                            Queue(eCapital, SWGlobalWarPlanetLocation.Orbit, r);
                        Assert.That(politics.Execute(sessions[0], Command(0, SWGlobalWarPoliticalAction.StateMessage) with
                            { Text = "A political request between producers must not arbitrate the step." }).Success, Is.True);
                        Assert.That(state.Comp.Outcome, Is.EqualTo(SWGlobalWarOutcome.None));
                        if (scenario == "step-reverse")
                            Queue(eCapital, SWGlobalWarPlanetLocation.Orbit, r);
                        else
                            Queue(rCapital, SWGlobalWarPlanetLocation.Surface, e);
                        void CheckStep()
                        {
                            Assert.That(ownershipCallbacks, Is.EqualTo(scenario == "step-runtime" ? 3 : 2));
                            Assert.That(state.Comp.Outcome, Is.EqualTo(SWGlobalWarOutcome.None));
                            Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Running));
                            probe.OwnershipChanged = null;
                        }
                        if (scenario == "step-runtime")
                            afterRuntimeStep = CheckStep;
                        else
                        {
                            Assert.That(steps.CommitCampaignStep(gen, timing.CurTick), Is.True);
                            CheckStep();
                        }
                        break;
                    case "generation":
                        var oldCallback = false;
                        var oldOperation = Guid.NewGuid();
                        Assert.That(steps.TryQueueOwnership(rCapital, SWGlobalWarPlanetLocation.Surface, e,
                            server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(rCapital).OwnershipRevision,
                            gen, oldOperation, _ => oldCallback = true), Is.True);
                        ticker.RestartRound();
                        ticker.ToggleReadyAll(false);
                        afterRuntimeStep = () =>
                        {
                        ticker.StartRound();
                        Assert.That(politics.TryGetPolitics(out var successor), Is.True);
                        Assert.That(successor.Comp.Generation, Is.Not.EqualTo(gen));
                        Assert.That(steps.CommitCampaignStep(gen, timing.CurTick), Is.False);
                        Assert.That(oldCallback, Is.False);
                        var successorCapital = Planet("SWSpawnTestRCapital");
                        Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(successorCapital).SurfaceOwner, Is.EqualTo(r));
                        Assert.That(steps.TryQueueOwnership(successorCapital, SWGlobalWarPlanetLocation.Surface, e, 0,
                            gen, oldOperation), Is.False);
                        Assert.That(steps.TryQueueOwnership(successorCapital, SWGlobalWarPlanetLocation.Surface, e, 0,
                            successor.Comp.Generation, oldOperation), Is.True);
                        Assert.That(steps.CommitCampaignStep(successor.Comp.Generation, timing.CurTick), Is.True);
                        Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(successorCapital).SurfaceOwner, Is.EqualTo(e));
                        Assert.That(oldCallback, Is.False);
                        Assert.That(successor.Comp.Neutrals.Values.All(n => n.RepublicContribution == 0 && n.EmpireContribution == 0), Is.True);
                        Assert.That(ledger.GetBalance(r, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(450_000));
                        };
                        break;
                    case "time-limit":
                        var staleFunding = Command(0, SWGlobalWarPoliticalAction.Sponsor) with
                            { Sector = "SWDiplomacyTestN0", Amount = 1 };
                        At(10799);
                        Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Running));
                        At(10800);
                        Assert.That(state.Comp.Outcome, Is.EqualTo(SWGlobalWarOutcome.TimeLimit));
                        Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Ending));
                        Assert.That(politics.Execute(sessions[0], staleFunding).Success, Is.False);
                        break;
                    case "surrender":
                    case "mutual-surrender":
                        var outcomes = 0;
                        var roundEnds = 0;
                        var transferCallbacks = 0;
                        probe.RoundEnd = () => roundEnds++;
                        probe.Outcome = outcomeEvent =>
                        {
                            outcomes++;
                            Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Ending));
                            var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(eCapital);
                            var previous = data.SurfaceOwner;
                            Assert.That(planets.TryChangeOwnership(eCapital, SWGlobalWarPlanetLocation.Surface, r,
                                data.OwnershipRevision, gen, Guid.NewGuid(), out _), Is.False);
                            Assert.That(data.SurfaceOwner, Is.EqualTo(previous));
                        };
                        At(10799);
                        var confirmation = politics.Execute(sessions[0], Command(0, SWGlobalWarPoliticalAction.PrepareSurrender));
                        Assert.That(confirmation.Success, Is.True);
                        var second = politics.Execute(sessions[1], Command(1, SWGlobalWarPoliticalAction.PrepareSurrender));
                        Assert.That(second.Success, Is.True);
                        var surrender = Command(0, SWGlobalWarPoliticalAction.ConfirmSurrender) with { Confirmation = confirmation.Confirmation };
                        Assert.That(politics.Execute(sessions[0], surrender).Success, Is.True);
                        Assert.That(politics.Execute(sessions[0], surrender).Success, Is.True);
                        if (scenario == "mutual-surrender")
                            Assert.That(politics.Execute(sessions[1], Command(1, SWGlobalWarPoliticalAction.ConfirmSurrender) with
                                { Confirmation = second.Confirmation }).Success, Is.True);
                        At(10800);
                        if (scenario == "surrender")
                        {
                            Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Capitulating));
                            probe.OwnershipChanged = _ =>
                            {
                                transferCallbacks++;
                                foreach (var planet in server.EntMan.GetComponent<SWGlobalWarGameruleComponent>(state.Owner).PlanetEntities.Values)
                                {
                                    var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet);
                                    Assert.That(data.OrbitOwner, Is.Not.EqualTo(r));
                                    Assert.That(data.SurfaceOwner, Is.Not.EqualTo(r));
                                }
                            };
                            At(11099);
                        }
                        Assert.That(state.Comp.Outcome, Is.EqualTo(scenario == "surrender"
                            ? SWGlobalWarOutcome.EmpireVictory : SWGlobalWarOutcome.MutualSurrender));
                        Assert.That(steps.CommitCampaignStep(gen, timing.CurTick), Is.False);
                        Assert.That(outcomes, Is.EqualTo(1));
                        Assert.That(roundEnds, Is.EqualTo(1));
                        if (scenario == "surrender")
                            Assert.That(transferCallbacks, Is.GreaterThan(1));
                        break;
                }

                SWGlobalWarAssignment Assignment(int index)
                {
                    Assert.That(registry.TryGetAssignment(sessions[index].UserId, out var assignment), Is.True);
                    return assignment;
                }

                SWGlobalWarPoliticalCommand Command(int index, SWGlobalWarPoliticalAction action)
                {
                    return new SWGlobalWarPoliticalCommand(gen, Guid.NewGuid(), Assignment(index).Revision, action,
                        state.Comp.RelationsRevision);
                }

                bool Act(int index, SWGlobalWarPoliticalAction action, string? sector = null)
                {
                    return politics.Execute(sessions[index], Command(index, action) with
                        { Sector = sector == null ? (ProtoId<SWGlobalWarPlanetPrototype>?) null : sector }).Success;
                }

                bool Sponsor(int index, string sector, long amount)
                {
                    return politics.Execute(sessions[index], Command(index, SWGlobalWarPoliticalAction.Sponsor) with
                        { Sector = sector, Amount = amount }).Success;
                }

                void At(double seconds, bool update = true)
                {
                    // Deterministic boundary injection; actual auto_pause_empty is a separate runtime check.
                    timing.CurTick += 1;
                    var delta = TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign);
                    timing.TimeBase = (timing.TimeBase.Item1 + delta, timing.TimeBase.Item2);
                    if (update)
                    {
                        politics.Update(0);
                        steps.CommitCampaignStep(gen, timing.CurTick);
                    }
                }

                EntityUid Planet(string id)
                {
                    Assert.That(planets.TryGetPlanet(id, out var planet), Is.True);
                    return planet!.Value;
                }

                Guid Change(EntityUid planet, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner)
                {
                    var elapsed = clock.Elapsed(campaign);
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + elapsed - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet);
                    var committed = false;
                    var operation = Guid.NewGuid();
                    Assert.That(steps.TryQueueOwnership(planet, location, owner, data.OwnershipRevision, gen,
                        operation, success => committed = success), Is.True);
                    Assert.That(steps.CommitCampaignStep(gen, timing.CurTick), Is.True);
                    Assert.That(committed, Is.True);
                    return operation;
                }

                void MoveToOrbit(EntityUid body, EntityUid planet)
                {
                    Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                    server.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(map!.Value, default));
                }

                void MoveToSurface(EntityUid body, EntityUid planet)
                {
                    Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                    server.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(map!.Value, default));
                }

                bool CanLand(EntityUid body, EntityUid planet)
                {
                    return server.System<SWGlobalWarAccessPolicySystem>().EvaluateTransition(body, planet,
                        SWGlobalWarPlanetLocation.Surface, SWGlobalWarTransitionKind.Landing, gen).Allowed;
                }

                bool IsPersonalItem(EntityUid item)
                {
                    if (!server.EntMan.TryGetComponent<TransformComponent>(item, out var transform) ||
                        server.EntMan.HasComponent<VirtualItemComponent>(item))
                        return false;
                    var ancestor = transform.ParentUid;
                    while (ancestor.IsValid() && server.EntMan.TryGetComponent<TransformComponent>(ancestor, out var parent))
                    {
                        if (ancestor == Assignment(2).Body)
                            return true;
                        ancestor = parent.ParentUid;
                    }
                    return false;
                }
            });
            if (runtimeScenario != null)
                await runtimeScenario();
            if (afterRuntimeStep != null)
            {
                await pair.RunTicksSync(2);
                await server.WaitAssertion(afterRuntimeStep);
            }
            assertionsCompleted = true;
        }
        catch (Exception error)
        {
            TestContext.Error.WriteLine(error);
            throw;
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.System<SWPoliticsNotificationProbeSystem>().LedgerChanged = null;
                probe.PoliticsChanged = null;
                probe.OwnershipChanged = null;
                probe.Outcome = null;
                probe.RoundEnd = null;
                probe.FtlCompleted = null;
                probe.DoorChecked = null;
                if (!runtimeWithdrawal && scenario is not ("step-runtime" or "generation" or "capture-core" or "explosion-policy" or "fleet-boarding"))
                {
                    timing.TimeBase = originalTime;
                    timing.CurTick = originalTick;
                }
                // A dirty failed pair is disposed by the pool. Do not replace the original failure with teardown errors.
                if (assertionsCompleted)
                {
                    ticker.RestartRound();
                    ticker.SetGamePreset(originalPreset);
                }
            });
        }
        await pair.CleanReturnAsync();
    }
}
