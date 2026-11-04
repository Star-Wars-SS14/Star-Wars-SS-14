#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._SW.Tutorial;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.GameTicking;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;
using ClientGameTicker = Content.Client.GameTicking.Managers.ClientGameTicker;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Maps;
using Content.Shared._SW.Tutorial;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarSpawnTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: job
  id: SWSpawnTestRepublic
  name: job-name-captain
  playTimeTracker: SWSpawnTestRepublicTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWSpawnTestEmpire
  name: job-name-engineer
  playTimeTracker: SWSpawnTestEmpireTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWSpawnTestForceRole
  name: job-name-chaplain
  playTimeTracker: SWSpawnTestForceTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: playTimeTracker
  id: SWSpawnTestRepublicTracker

- type: playTimeTracker
  id: SWSpawnTestEmpireTracker

- type: playTimeTracker
  id: SWSpawnTestForceTracker

- type: gameMap
  id: SWSpawnTestMap
  mapName: Spawn test map
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: SWSpawnTestRoleStation
      components:
      - type: StationJobs
        availableJobs:
          SWSpawnTestRepublic: [80, 80]
          SWSpawnTestEmpire: [80, 80]
          SWSpawnTestForceRole: [80, 80]

- type: entity
  parent: [BaseStation, BaseStationJobsSpawning]
  id: SWSpawnTestRoleStation

- type: entity
  parent: SWGlobalWarPointMarker
  id: SWSpawnTestCivilianPoint
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: arrivalCivilian
    kind: Spawn

- type: entity
  parent: SWGlobalWarPointMarker
  id: SWSpawnTestForcePoint
  components:
  - type: SWGlobalWarMapAnchor
    anchorId: arrivalForce
    kind: Spawn

- type: swGlobalWarPlanet
  id: SWSpawnTestRCapital
  name: Republic capital
  initialOwner: Republic
  kind: Capital
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap

- type: swGlobalWarPlanet
  id: SWSpawnTestECapital
  name: Empire capital
  initialOwner: Empire
  kind: Capital
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap

- type: swGlobalWarPlanet
  id: SWSpawnTestIndustry
  name: Republic industry
  initialOwner: Republic
  kind: Industry
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap

- type: swGlobalWarPlanet
  id: SWSpawnTestForce
  name: Republic Force world
  initialOwner: Republic
  kind: Force
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap

- type: entity
  parent: SWGlobalWar
  id: SWSpawnTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestRCapital, SWSpawnTestECapital, SWSpawnTestIndustry, SWSpawnTestForce]
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWSpawnTestRepublic: {faction: Republic}
      SWSpawnTestEmpire: {faction: Empire}
      SWSpawnTestForceRole: {faction: Republic, force: true}
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWSpawnTestRepublic: [31, 31]
            SWSpawnTestForceRole: [1, 1]
      Empire:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWSpawnTestEmpire: [31, 31]

- type: gamePreset
  id: SWSpawnTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestRule]
""";

    [TestCase(20)]
    [TestCase(60)]
    public async Task MassStartUsesDistinctPreparedCellsAndPublishesOnlyRoleStations(int count)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var identity = server.System<SWGlobalWarIdentitySystem>();
        var jobs = server.System<StationJobsSystem>();
        var probe = server.System<SWGlobalWarSpawnFixtureSystem>();
        var clientTicker = pair.Client.System<ClientGameTicker>();
        await server.AddDummySessions(count - 1);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True, "All mass-start users must have loaded preferences and bans.");
        var sessions = server.PlayerMan.Sessions.ToArray();
        var expected = new Dictionary<NetUserId, (string Job, SWGlobalWarFaction Faction)>();
        for (var i = 0; i < sessions.Length; i++)
        {
            var republic = i < count / 2;
            var job = republic ? "SWSpawnTestRepublic" : "SWSpawnTestEmpire";
            expected.Add(sessions[i].UserId, (job, republic ? SWGlobalWarFaction.Republic : SWGlobalWarFaction.Empire));
            var profile = (HumanoidCharacterProfile)server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            await pair.SetJobPriorities(sessions[i].UserId, profile.JobPriorities.Keys
                .Where(j => j.Id != job).Select(j => (j, JobPriority.Never))
                .Append(((ProtoId<JobPrototype>)job, JobPriority.High)).ToArray());
        }
        var snapshots = new List<(Dictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>> Jobs,
            Dictionary<NetEntity, string> Names)>();
        void Snapshot(IReadOnlyDictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>> value)
        {
            snapshots.Add((value.ToDictionary(p => p.Key, p => new Dictionary<ProtoId<JobPrototype>, int?>(p.Value)),
                clientTicker.StationNames.ToDictionary(p => p.Key, p => p.Value)));
        }
        await pair.Client.WaitPost(() => clientTicker.LobbyJobsAvailableUpdated += Snapshot);
        var roleStations = new HashSet<EntityUid>();
        var roleNetIds = new HashSet<NetEntity>();
        var expectedJobs = new Dictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>>();
        var expectedNames = new Dictionary<NetEntity, string>();
        try
        {
            await server.WaitAssertion(() =>
            {
                probe.Assigned.Clear();
                probe.Allocations.Clear();
                probe.Rejections.Clear();
                probe.Collect = true;
                probe.PlaceKnownObstacle = true;
                probe.KnownObstacles.Clear();
                ticker.LoadGameMap(server.ProtoMan.Index<GameMapPrototype>("SWSpawnTestMap"), out _);
                Assert.That(server.EntMan.EntityQuery<StationJobsComponent>(), Is.Not.Empty,
                    "A competing physical station must exist before StartRound.");
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                // No RunTicks or physics update can separate coincident bodies before these assertions.
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(count),
                    $"Allocation: {string.Join(",", probe.Allocations)}; rejected: {string.Join(",", probe.Rejections)}");
                Assert.That(probe.Assigned, Is.EquivalentTo(sessions.Select(p => p.UserId)));
                var bodies = new HashSet<EntityUid>();
                var minds = new HashSet<EntityUid>();
                var positions = new HashSet<EntityCoordinates>();
                foreach (var player in sessions)
                {
                    Assert.That(player.AttachedEntity, Is.Not.Null, $"Missing body for {player.UserId}");
                    var body = player.AttachedEntity!.Value;
                    Assert.That(bodies.Add(body), Is.True);
                    Assert.That(identity.TryGetActor(player, body, out var mind, out var faction), Is.True);
                    Assert.That(minds.Add(mind), Is.True);
                    Assert.That(faction, Is.EqualTo(expected[player.UserId].Faction));
                    Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.JoinedGame));
                    Assert.That(planets.TryGetPhysicalLocation(body, out var planet, out var location), Is.True);
                    Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Surface));
                    Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).Prototype.Id,
                        Is.EqualTo(faction == SWGlobalWarFaction.Republic ? "SWSpawnTestRCapital" : "SWSpawnTestECapital"));
                    var coordinates = server.EntMan.GetComponent<TransformComponent>(body).Coordinates;
                    Assert.That(positions.All(p => p.EntityId != coordinates.EntityId ||
                        Vector2.DistanceSquared(p.Position, coordinates.Position) >= 1f), Is.True);
                    Assert.That(positions.Add(coordinates), Is.True);
                    Assert.That(coordinates.X, Is.InRange(-3.5f, 4.5f));
                    Assert.That(coordinates.Y, Is.InRange(-3.5f, 4.5f));
                    var grid = server.EntMan.GetComponent<MapGridComponent>(coordinates.EntityId);
                    Assert.That(server.System<SharedMapSystem>().GetTileRef(coordinates.EntityId, grid, coordinates).Tile.IsEmpty, Is.False);
                    var bodyTransform = server.EntMan.GetComponent<TransformComponent>(body);
                    Assert.That(probe.KnownObstacles.TryGetValue(bodyTransform.MapUid!.Value, out var obstacle), Is.True);
                    Assert.That(server.System<EntityLookupSystem>().GetWorldAABB(body)
                        .Intersects(server.System<EntityLookupSystem>().GetWorldAABB(obstacle)), Is.False);
                    Assert.That(spawn.TryGetRoleStation(faction, out var station), Is.True);
                    roleStations.Add(station);
                    roleNetIds.Add(server.EntMan.GetNetEntity(station));
                    Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out var assigned), Is.True);
                    Assert.That(assigned!.Select(j => j.Id), Is.EquivalentTo(new[] { expected[player.UserId].Job }));
                    Assert.That(jobs.TryGetJobSlot(station, expected[player.UserId].Job, out var slots), Is.True);
                    Assert.That(slots, Is.EqualTo(31 - count / 2));
                }
                foreach (var faction in new[] {SWGlobalWarFaction.Republic, SWGlobalWarFaction.Empire})
                {
                    Assert.That(spawn.TryGetRoleStation(faction, out var roleStation), Is.True);
                    var net = server.EntMan.GetNetEntity(roleStation);
                    var name = faction == SWGlobalWarFaction.Republic ? "Republic admissions" : "Empire admissions";
                    server.System<StationSystem>().RenameStation(roleStation, name);
                    expectedNames.Add(net, name);
                    expectedJobs.Add(net, faction == SWGlobalWarFaction.Republic
                        ? new() { ["SWSpawnTestRepublic"] = 31 - count / 2, ["SWSpawnTestForceRole"] = 1 }
                        : new() { ["SWSpawnTestEmpire"] = 31 - count / 2 });
                }
                var physical = server.EntMan.EntityQuery<StationJobsComponent>()
                    .Where(s => !roleStations.Contains(s.Owner)).ToArray();
                Assert.That(physical, Is.Not.Empty);
                foreach (var station in physical)
                {
                    Assert.That(server.EntMan.HasComponent<StationSpawningComponent>(station.Owner), Is.True);
                    Assert.That(server.EntMan.GetComponent<StationDataComponent>(station.Owner).Grids, Is.Not.Empty);
                    Assert.That(station.PlayerJobs, Is.Empty);
                    Assert.That(jobs.TryGetJobSlot(station.Owner, "SWSpawnTestRepublic", out var slots), Is.True);
                    Assert.That(slots, Is.EqualTo(80));
                }
            });
            await pair.RunTicksSync(5);
            var before = snapshots.Count;
            await server.WaitAssertion(() =>
            {
                Assert.That(planets.TryGetPlanet("SWSpawnTestIndustry", out var industry), Is.True);
                // The base supply program needs home industry from the start, but only its surface.
                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(industry!.Value).Maps.Keys,
                    Is.EquivalentTo(new[] { SWGlobalWarPlanetLocation.Surface }));
                var oldStations = server.EntMan.EntityQuery<StationJobsComponent>().Select(s => s.Owner).ToHashSet();
                Assert.That(planets.TryEnsureLocation(industry.Value, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                var added = server.EntMan.EntityQuery<StationJobsComponent>().Where(s => !oldStations.Contains(s.Owner)).ToArray();
                Assert.That(added, Has.Length.EqualTo(1));
                var physical = added.Single();
                Assert.That(server.EntMan.HasComponent<StationSpawningComponent>(physical.Owner), Is.True);
                var grids = server.EntMan.GetComponent<StationDataComponent>(physical.Owner).Grids;
                Assert.That(grids, Is.Not.Empty);
                Assert.That(grids.All(g => server.EntMan.GetComponent<TransformComponent>(g).MapUid == map), Is.True);
                Assert.That(physical.PlayerJobs, Is.Empty);
                Assert.That(jobs.GetJobs(physical.Owner), Is.EquivalentTo(new Dictionary<ProtoId<JobPrototype>, int?>
                {
                    ["SWSpawnTestRepublic"] = 80, ["SWSpawnTestEmpire"] = 80, ["SWSpawnTestForceRole"] = 80,
                }));
            });
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(snapshots.Count, Is.GreaterThan(before));
                foreach (var snapshot in snapshots)
                {
                    Assert.That(snapshot.Jobs.Keys, Is.EquivalentTo(roleNetIds));
                    Assert.That(snapshot.Names, Is.EquivalentTo(expectedNames));
                    foreach (var (station, contents) in expectedJobs)
                        Assert.That(snapshot.Jobs[station], Is.EquivalentTo(contents));
                }
            });
        }
        finally
        {
            await pair.Client.WaitPost(() => clientTicker.LobbyJobsAvailableUpdated -= Snapshot);
            await server.WaitPost(() =>
            {
                probe.Collect = false;
                probe.PlaceKnownObstacle = false;
                probe.KnownObstacles.Clear();
                probe.Assigned.Clear();
                probe.Allocations.Clear();
                probe.Rejections.Clear();
                ticker.RestartRound();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }

    [TestCase("deleted", false)]
    [TestCase("occupied", false)]
    [TestCase("owner-flip", false)]
    [TestCase("wrong-station", false)]
    [TestCase("force-full", false)]
    [TestCase("force-home-lost", false)]
    [TestCase("force", true)]
    [TestCase("fallback", true)]
    public async Task ClientLateJoinRechecksThePreparedDestinationBeforeCreatingALife(string scenario, bool succeeds)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var player = server.PlayerMan.Sessions.Single();
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var rules = server.System<SWGlobalWarGameruleSystem>();
        var jobs = server.System<StationJobsSystem>();
        var probe = server.System<SWGlobalWarSpawnFixtureSystem>();
        var force = scenario.StartsWith("force", StringComparison.Ordinal);
        var job = force ? "SWSpawnTestForceRole" : "SWSpawnTestRepublic";
        EntityUid station = default;
        EntityUid requestedStation = default;
        var beforeSlots = force ? 1 : 31;
        var mutated = false;
        void Capture(string id)
        {
            Assert.That(planets.TryGetPlanet(id, out var planet), Is.True);
            Assert.That(rules.TryGetGlobalWarGamerule(out var rule), Is.True);
            var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
            Assert.That(planets.TryChangeOwnership(planet.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarFaction.Empire, data.OwnershipRevision, rule.Comp.Generation, Guid.NewGuid(), out _), Is.True);
        }
        try
        {
            await server.WaitAssertion(() =>
            {
                probe.DeleteSpawnAnchors = scenario == "deleted";
                probe.OccupySpawnZones = scenario == "occupied";
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestPreset");
                ticker.ToggleReadyAll(false);
                ticker.StartRound();
                Assert.That(spawn.TryGetRoleStation(SWGlobalWarFaction.Republic, out station), Is.True);
                requestedStation = station;
                if (scenario == "wrong-station")
                    Assert.That(spawn.TryGetRoleStation(SWGlobalWarFaction.Empire, out requestedStation), Is.True);
                if (scenario == "force-full")
                {
                    Assert.That(jobs.TrySetJobSlot(station, job, 0), Is.True);
                    beforeSlots = 0;
                }
                if (scenario == "force-home-lost")
                    Capture("SWSpawnTestForce");
                if (scenario == "fallback")
                    Capture("SWSpawnTestRCapital");
                if (scenario == "owner-flip")
                {
                    probe.BeforeValidation = args =>
                    {
                        if (args.Player.UserId != player.UserId || args.Cancelled)
                            return;
                        Capture("SWSpawnTestRCapital");
                        mutated = true;
                    };
                }
                Assert.That(player.ContentData()!.Mind, Is.Null);
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.Zero);
            });
            var command = $"joingame {job} {server.EntMan.GetNetEntity(requestedStation).Id}";
            await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand(command));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(jobs.TryGetJobSlot(station, job, out var slots), Is.True);
                Assert.That(slots, Is.EqualTo(beforeSlots - (succeeds ? 1 : 0)));
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(succeeds ? 1 : 0));
                Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out _), Is.EqualTo(succeeds));
                if (scenario == "owner-flip")
                    Assert.That(mutated, Is.True, "The ownership change must occur after preparation, before validation.");
                if (!succeeds)
                {
                    Assert.That(player.ContentData()!.Mind, Is.Null);
                    Assert.That(player.AttachedEntity, Is.Null);
                    Assert.That(server.System<SWGlobalWarIdentitySystem>().CanJoin(player.UserId, SWGlobalWarFaction.Empire), Is.True);
                    return;
                }
                Assert.That(player.AttachedEntity, Is.Not.Null);
                Assert.That(planets.TryGetPhysicalLocation(player.AttachedEntity!.Value, out var planet, out var location), Is.True);
                Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Surface));
                Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).Prototype.Id,
                    Is.EqualTo(force ? "SWSpawnTestForce" : "SWSpawnTestIndustry"));
                Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetActor(player, player.AttachedEntity.Value,
                    out _, out var faction), Is.True);
                Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                probe.DeleteSpawnAnchors = false;
                probe.OccupySpawnZones = false;
                probe.BeforeValidation = null;
                ticker.RestartRound();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }

    [TestCase(true, SWTutorialSide.Republic)]
    [TestCase(true, SWTutorialSide.Empire)]
    [TestCase(false, SWTutorialSide.Republic)]
    [TestCase(false, SWTutorialSide.Empire)]
    public async Task LegacyTutorialSideRequestCannotGrantCampaignSlots(bool campaign, SWTutorialSide side)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var player = server.PlayerMan.Sessions.Single();
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var jobs = server.System<StationJobsSystem>();
        var tutorial = server.System<SWTutorialSystem>();
        // Seed only eligibility for the existing graduate bonus; this does not claim to exercise the course.
        var graduates = (HashSet<NetUserId>)typeof(SWTutorialSystem)
            .GetField("_graduates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tutorial)!;
        var oldBonus = server.CfgMan.GetCVar(SWVars.TutorialGraduateExtraSlot);
        var stations = new List<EntityUid>();
        var job = side == SWTutorialSide.Republic ? "SWRepublicSoldier" : "SWEmpireSoldier";
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                server.CfgMan.SetCVar(SWVars.TutorialGraduateExtraSlot, true);
                ticker.SetGamePreset(campaign ? "SWSpawnTestPreset" : "sandbox");
                ticker.ToggleReadyAll(false);
                ticker.StartRound();
                graduates.Add(player.UserId);
                foreach (var station in server.EntMan.EntityQuery<StationJobsComponent>())
                {
                    stations.Add(station.Owner);
                    Assert.That(jobs.TrySetJobSlot(station.Owner, job, 0, createSlot: true), Is.True);
                }
                Assert.That(stations, Is.Not.Empty);
            });
            await pair.Client.WaitPost(() => pair.Client.EntMan.EntityNetManager
                .SendSystemNetworkMessage(new SWTutorialJoinSideEvent(side)));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(campaign ? 0 : 1));
                Assert.That(graduates.Contains(player.UserId), Is.EqualTo(campaign));
                if (campaign)
                {
                    Assert.That(player.ContentData()!.Mind, Is.Null);
                    Assert.That(player.AttachedEntity, Is.Null);
                }
                else
                    Assert.That(player.AttachedEntity, Is.Not.Null);
                foreach (var station in stations)
                {
                    Assert.That(jobs.TryGetJobSlot(station, job, out var slots), Is.True);
                    Assert.That(slots, Is.Zero, "Rejected admission must not leave a newly granted slot.");
                    if (campaign)
                        Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out _), Is.False);
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                graduates.Remove(player.UserId);
                server.CfgMan.SetCVar(SWVars.TutorialGraduateExtraSlot, oldBonus);
                ticker.RestartRound();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealRoundSpawnReconnectAndSecondRoundKeepIdentityBoundToTheRightLife()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var player = server.PlayerMan.Sessions.Single();
        var identity = server.System<SWGlobalWarIdentitySystem>();
        var rules = server.System<SWGlobalWarGameruleSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        await pair.SetJobPriorities(("SWSpawnTestRepublic", JobPriority.High));
        await server.WaitAssertion(() => Assert.That(
            ((HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(player.UserId).SelectedCharacter).JobPriorities["SWSpawnTestRepublic"],
            Is.EqualTo(JobPriority.High)));
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);
        EntityUid originalBody = default;
        EntityUid originalMind = default;
        Guid originalGeneration = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(player.AttachedEntity, Is.Not.Null);
            originalBody = player.AttachedEntity!.Value;
            Assert.That(identity.TryGetActor(player, originalBody, out originalMind, out var faction), Is.True);
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(planets.TryGetPhysicalLocation(originalBody, out var planet, out var location), Is.True);
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).Prototype.Id, Is.EqualTo("SWSpawnTestRCapital"));
            Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Surface));
            Assert.That(identity.CanJoin(player.UserId, SWGlobalWarFaction.Empire), Is.False);
            Assert.That(rules.TryGetGlobalWarGamerule(out var rule), Is.True);
            originalGeneration = rule.Comp.Generation;
            Assert.That(planets.TryGetPlanet("SWSpawnTestIndustry", out var industry), Is.True);
            Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(industry!.Value).Maps.Keys,
                Is.EquivalentTo(new[] { SWGlobalWarPlanetLocation.Surface }));
        });
        var name = player.Name;
        var user = player.UserId;
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Reconnect test"));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.PlayerMan.Sessions, Is.Empty);
            Assert.That(identity.TryGetIdentity(originalBody, out var mind, out var faction), Is.True);
            Assert.That(mind, Is.EqualTo(originalMind));
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));
        });
        pair.Client.SetConnectTarget(server);
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, name));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var reconnected = server.PlayerMan.Sessions.Single();
            Assert.That(reconnected, Is.Not.SameAs(player));
            Assert.That(reconnected.UserId, Is.EqualTo(user));
            Assert.That(reconnected.AttachedEntity, Is.EqualTo(originalBody));
            Assert.That(reconnected.ContentData()!.Mind, Is.EqualTo(originalMind));
            Assert.That(identity.CanJoin(user, SWGlobalWarFaction.Empire), Is.False);
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(1));
            ticker.RestartRound();
        });
        await pair.SetJobPriorities(("SWSpawnTestEmpire", JobPriority.High));
        await server.WaitPost(() =>
        {
            ticker.SetGamePreset("SWSpawnTestPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var reconnected = server.PlayerMan.Sessions.Single();
            Assert.That(reconnected.AttachedEntity, Is.Not.Null);
            Assert.That(identity.TryGetActor(reconnected, reconnected.AttachedEntity!.Value, out _, out var faction), Is.True);
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(rules.TryGetGlobalWarGamerule(out var rule), Is.True);
            Assert.That(rule.Comp.Generation, Is.Not.EqualTo(originalGeneration));
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(1));
            ticker.RestartRound();
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }
}

/// <summary>Creates real prepared map geometry for this test manifest after normal lazy map initialization.</summary>
public sealed class SWGlobalWarSpawnFixtureSystem : EntitySystem
{
    public bool Collect;
    public bool DeleteSpawnAnchors;
    public bool PlaceKnownObstacle;
    public readonly Dictionary<EntityUid, EntityUid> KnownObstacles = new();
    public bool OccupySpawnZones;
    public Action<SWGlobalWarBeforeSpawnEvent>? BeforeValidation;
    public Action<RulePlayerSpawningEvent>? BeforeRound;
    public readonly List<NetUserId> Assigned = new();
    public readonly List<string> Allocations = new();
    public readonly List<string> Rejections = new();
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarLocationReadyEvent>(OnReady);
        SubscribeLocalEvent<RulePlayerSpawningEvent>(args => BeforeRound?.Invoke(args),
            after: [typeof(SWGlobalWarSpawnSystem)]);
        SubscribeLocalEvent<SWGlobalWarBeforeSpawnEvent>((ref SWGlobalWarBeforeSpawnEvent args) =>
            BeforeValidation?.Invoke(args), after: [typeof(SWGlobalWarSpawnSystem)]);
        SubscribeLocalEvent<Content.Shared._SW.SWModifyAssignedJobsEvent>((ref Content.Shared._SW.SWModifyAssignedJobsEvent args) =>
        {
            if (Collect)
                Allocations.AddRange(args.AssignedJobs.Select(p => $"{p.Key}={p.Value}"));
        });
        SubscribeLocalEvent<SWGlobalWarValidateSpawnEvent>((ref SWGlobalWarValidateSpawnEvent args) =>
        {
            if (Collect && args.Cancelled)
                Rejections.Add($"{args.Player.UserId}={args.Reason}");
        }, after: [typeof(SWGlobalWarSpawnSystem)]);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(args =>
        {
            if (Collect)
                Assigned.AddRange(args.Players.Select(p => p.UserId));
        });
    }

    private void OnReady(ref SWGlobalWarLocationReadyEvent args)
    {
        var prototype = Comp<SWGlobalWarPlanetComponent>(args.Planet).Prototype;
        if (args.Location != SWGlobalWarPlanetLocation.Surface ||
            !prototype.Id.StartsWith("SWSpawnTest", StringComparison.Ordinal))
            return;
        var grid = _mapManager.CreateGridEntity(Transform(args.Map).MapID);
        _transforms.SetCoordinates(grid, new EntityCoordinates(args.Map, new Vector2(1000, 1000)));
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = -4; x <= 4; x++)
        for (var y = -4; y <= 4; y++)
            tiles.Add((new Vector2i(x, y), new Tile(1)));
        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        var point = Spawn(Comp<SWGlobalWarPlanetComponent>(args.Planet).Prototype.Id == "SWSpawnTestForce"
            ? "SWSpawnTestForcePoint" : "SWSpawnTestCivilianPoint", new EntityCoordinates(grid, .5f, .5f));
        if (PlaceKnownObstacle)
            KnownObstacles.Add(args.Map, Spawn("WallSolid", new EntityCoordinates(grid, .5f, .5f)));
        if (DeleteSpawnAnchors)
            Del(point);
        if (OccupySpawnZones)
        {
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                Spawn("WallSolid", new EntityCoordinates(grid, x + .5f, y + .5f));
        }
    }
}
