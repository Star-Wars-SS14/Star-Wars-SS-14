using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._SW.GlobalWar.UI;
using Content.IntegrationTests.Pair;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server._SW;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Death, the term, the way back and the verdict, through the same calls the real clients trigger.</summary>
[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarRespawnTest
{
    private const float DelaySeconds = 2f;

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  parent: SWPoliticsTestRule
  id: SWRespawnTestRule
  components:
  - type: SWGlobalWarSpawnConfiguration
    respawnDelay: 2

- type: gamePreset
  id: SWRespawnTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWRespawnTestRule]

- type: entity
  parent: SWRespawnTestRule
  id: SWRespawnEndTestRule
  components:
  - type: SWGlobalWarCampaign
    maximumDuration: 1

- type: gamePreset
  id: SWRespawnEndTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWRespawnEndTestRule]

- type: entity
  parent: SWRespawnEndTestRule
  id: SWRespawnLimitTestRule
  components:
  - type: SWGlobalWarCampaign
    maximumDuration: 1
    timeLimitVictory: true

- type: gamePreset
  id: SWRespawnLimitTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWRespawnLimitTestRule]

- type: job
  id: SWRespawnTestJedi
  name: job-name-captain
  playTimeTracker: SWRespawnTestJediTracker
  setPreference: true
  startingGear: ForceUserJediGear
  icon: JobIconCaptain

- type: job
  id: SWRespawnTestSith
  name: job-name-captain
  playTimeTracker: SWRespawnTestSithTracker
  setPreference: true
  startingGear: ForceUserSithGear
  icon: JobIconCaptain

- type: playTimeTracker
  id: SWRespawnTestJediTracker

- type: playTimeTracker
  id: SWRespawnTestSithTracker

- type: entity
  parent: SWRespawnTestRule
  id: SWRespawnJediTestRule
  components:
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWRespawnTestJedi: {faction: Republic, role: ForceUser, force: true}
      SWPoliticsTestCivilian: {faction: Republic, role: Civilian}
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWRespawnTestJedi: [1, 1]
            SWPoliticsTestCivilian: [20, 20]

- type: gamePreset
  id: SWRespawnJediTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWRespawnJediTestRule]

- type: entity
  parent: SWRespawnTestRule
  id: SWRespawnSithTestRule
  components:
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWRespawnTestSith: {faction: Republic, role: ForceUser, force: true}
      SWPoliticsTestCivilian: {faction: Republic, role: Civilian}
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWRespawnTestSith: [1, 1]
            SWPoliticsTestCivilian: [20, 20]

- type: gamePreset
  id: SWRespawnSithTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWRespawnSithTestRule]
""";

    private sealed record Round(
        TestPair Pair,
        GameTicker Ticker,
        SWGlobalWarSpawnSystem Spawn,
        SWGlobalWarAssignmentsSystem Registry,
        MobStateSystem Mobs,
        ICommonSession Player,
        EntityUid Station);

    private static async Task<Round> StartRound(TestPair pair, string preset = "SWRespawnTestPreset")
    {
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var player = server.PlayerMan.Sessions.Single();
        EntityUid station = default;
        await pair.SetJobPriorities(("SWPoliticsTestSoldier", JobPriority.High));
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset(preset);
            ticker.ToggleReadyAll(false);
            ticker.StartRound();
            Assert.That(spawn.TryGetRoleStation(SWGlobalWarFaction.Republic, out station), Is.True);
        });
        return new Round(pair, ticker, spawn, server.System<SWGlobalWarAssignmentsSystem>(),
            server.System<MobStateSystem>(), player, station);
    }

    private static async Task Finish(Round round)
    {
        await round.Pair.Server.WaitPost(() => round.Ticker.RestartRound());
        await round.Pair.CleanReturnAsync();
    }

    private static int DelayTicks(Round round)
    {
        return (int) (DelaySeconds * round.Pair.Server.Timing.TickRate);
    }

    private static EntityUid Join(Round round, string job)
    {
        round.Ticker.MakeJoinGame(round.Player, round.Station, job);
        Assert.That(round.Player.AttachedEntity, Is.Not.Null);
        return round.Player.AttachedEntity!.Value;
    }

    [Test]
    public async Task TermRunsFromConfirmedDeathAndRevivalCancelsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair);
        var server = pair.Server;
        EntityUid body = default;
        Guid life = default;
        await server.WaitAssertion(() =>
        {
            body = Join(round, "SWPoliticsTestSoldier");
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out life), Is.True);
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out var message), Is.False, "Alive.");
            Assert.That(message, Is.EqualTo(Loc("sw-global-war-spawn-life-active")));
            Assert.That(round.Spawn.GetRespawnReadyAt(round.Player.UserId), Is.Null);
            round.Mobs.ChangeMobState(body, MobState.Dead);
            Assert.That(round.Spawn.GetRespawnReadyAt(round.Player.UserId),
                Is.EqualTo(server.Timing.CurTime + TimeSpan.FromSeconds(DelaySeconds)));
        });
        await pair.RunTicksSync(DelayTicks(round) - 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out _), Is.False, "Term not over.");
            round.Mobs.ChangeMobState(body, MobState.Alive);
            Assert.That(round.Spawn.GetRespawnReadyAt(round.Player.UserId), Is.Null);
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out _), Is.False, "Revived before commit.");
            round.Mobs.ChangeMobState(body, MobState.Dead);
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out _), Is.False, "A new death is a new term.");
            Assert.That(round.Player.AttachedEntity, Is.EqualTo(body), "A refusal changes nothing.");
        });
        await Finish(round);
    }

    [Test]
    public async Task CivilianReturnEndsTheOldLifeForGood()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair);
        var server = pair.Server;
        var mind = server.System<MindSystem>();
        var client = pair.Client.System<SWGlobalWarEntrySystem>();
        EntityUid oldBody = default;
        EntityUid oldMind = default;
        Guid life = default;
        await server.WaitAssertion(() =>
        {
            oldBody = Join(round, "SWPoliticsTestSoldier");
            Assert.That(round.Registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.Soldier), Is.EqualTo(4));
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out life), Is.True);
            oldMind = mind.GetMind(oldBody)!.Value;
            round.Mobs.ChangeMobState(oldBody, MobState.Dead);
            Assert.That(round.Registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.Soldier), Is.EqualTo(5),
                "Death frees an ordinary place at once.");
        });
        await pair.RunTicksSync(DelayTicks(round) + 2);
        Assert.That(client.RespawnState?.Life, Is.EqualTo(life), "The dead client knows its term.");
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, true, out var forceMessage), Is.False,
                "Holds no Force place.");
            Assert.That(forceMessage, Is.EqualTo(Loc("sw-global-war-respawn-force-not-carrier")));
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out var message), Is.True, message);
            var newBody = round.Player.AttachedEntity!.Value;
            Assert.That(newBody, Is.Not.EqualTo(oldBody));
            Assert.That(round.Registry.TryGetAssignment(round.Player.UserId, out var assignment), Is.True);
            Assert.That(assignment.Role, Is.EqualTo(SWGlobalWarRole.Civilian));
            Assert.That(assignment.Body, Is.EqualTo(newBody));
            Assert.That(server.EntMan.HasComponent<SWGlobalWarEndedLifeComponent>(oldBody), Is.True);
            Assert.That(server.EntMan.GetComponent<MindContainerComponent>(oldBody).Mind, Is.Null);
            Assert.That(server.EntMan.GetComponent<MindComponent>(oldMind).UserId, Is.Null);
            Assert.That(round.Spawn.TryRespawn(round.Player, life, false, out var repeat), Is.False, "One commit only.");
            Assert.That(repeat, Is.EqualTo(Loc("sw-global-war-respawn-stale")));
            Assert.That(round.Player.AttachedEntity, Is.EqualTo(newBody));

            // A later revival gives neither the old player nor anyone else a playable character.
            round.Mobs.ChangeMobState(oldBody, MobState.Alive);
            Assert.That(server.EntMan.GetComponent<MindContainerComponent>(oldBody).Mind, Is.Null);
            var stranger = mind.CreateMind(null);
            mind.TransferTo(stranger, oldBody);
            Assert.That(server.EntMan.GetComponent<MindContainerComponent>(oldBody).Mind, Is.Null);
            Assert.That(stranger.Comp.OwnedEntity, Is.Null);
        });
        await pair.RunTicksSync(5);
        Assert.That(client.RespawnState, Is.Null, "The window goes away with the old life.");
        await Finish(round);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ForceReturnReplacesThePlaceAndStripsTheOldBody(bool force)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair);
        var server = pair.Server;
        var tags = server.System<TagSystem>();
        EntityUid oldBody = default;
        Guid life = default;
        Guid oldCarrier = default;
        await server.WaitAssertion(() =>
        {
            oldBody = Join(round, "SWPoliticsTestForce");
            tags.AddTag(oldBody, "ForceUser");
            server.EntMan.AddComponent<SWPushAbilityComponent>(oldBody);
            Assert.That(round.Registry.TryGetForceCarrier(round.Player.UserId, out var carrier), Is.True);
            oldCarrier = carrier.Id;
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out life), Is.True);
            round.Mobs.ChangeMobState(oldBody, MobState.Dead);
            Assert.That(round.Registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.ForceUser), Is.Zero,
                "A body that can still be revived holds the place.");
        });
        await pair.RunTicksSync(DelayTicks(round) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, force, out var message), Is.True, message);
            var newBody = round.Player.AttachedEntity!.Value;
            Assert.That(server.EntMan.HasComponent<SWPushAbilityComponent>(oldBody), Is.False);
            Assert.That(tags.HasTag(oldBody, "ForceUser"), Is.False);
            Assert.That(round.Registry.TryGetAssignment(round.Player.UserId, out var assignment), Is.True);
            if (force)
            {
                Assert.That(assignment.Role, Is.EqualTo(SWGlobalWarRole.ForceUser));
                Assert.That(round.Registry.TryGetForceCarrier(round.Player.UserId, out var carrier), Is.True);
                Assert.That(carrier.Id, Is.Not.EqualTo(oldCarrier));
                Assert.That(carrier.Body, Is.EqualTo(newBody));
                Assert.That(round.Registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.ForceUser), Is.Zero);
            }
            else
            {
                Assert.That(assignment.Role, Is.EqualTo(SWGlobalWarRole.Civilian));
                Assert.That(round.Registry.TryGetForceCarrier(round.Player.UserId, out _), Is.False);
                Assert.That(round.Registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.ForceUser), Is.EqualTo(1));
            }
        });
        await Finish(round);
    }

    [TestCase("SWRespawnJediTestPreset", "SWRespawnTestJedi", "SWEnergySwordJedi")]
    [TestCase("SWRespawnSithTestPreset", "SWRespawnTestSith", "SWEnergySwordSith")]
    public async Task ForceReturnGivesNoSecondSaber(string preset, string job, string saber)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        // A new player's profile has a random species, and a Colicoid's pockets need a backpack these kits do not have.
        var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>();
        var user = pair.Server.PlayerMan.Sessions.Single().UserId;
        var profile = (HumanoidCharacterProfile) prefs.GetPreferences(user).Characters[0];
        await pair.Server.WaitPost(() => prefs.SetProfile(user, 0, profile.WithSpecies("Human")).Wait());
        var round = await StartRound(pair, preset);
        var server = pair.Server;
        var inventory = server.System<InventorySystem>();
        EntityUid oldBody = default;
        EntityUid? oldSaber = null;
        Guid life = default;
        await server.WaitAssertion(() =>
        {
            oldBody = Join(round, job);
            Assert.That(inventory.TryGetSlotEntity(oldBody, "pocket1", out oldSaber), Is.True, "The first entry has one.");
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(oldSaber!.Value).EntityPrototype!.ID, Is.EqualTo(saber));
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out life), Is.True);
            round.Mobs.ChangeMobState(oldBody, MobState.Dead);
        });
        await pair.RunTicksSync(DelayTicks(round) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, true, out var message), Is.True, message);
            var newBody = round.Player.AttachedEntity!.Value;
            Assert.That(inventory.TryGetSlotEntity(newBody, "pocket1", out _), Is.False, "A return adds no saber.");
            Assert.That(inventory.TryGetSlotEntity(oldBody, "pocket1", out var kept), Is.True);
            Assert.That(kept, Is.EqualTo(oldSaber), "The old saber stays on the old body.");
        });
        await Finish(round);
    }

    [Test]
    public async Task LostForceWorldRefusesTheSpecialReturnAndKeepsThePlace()
    {
        // Fresh: the return waits for a periodic sync, and a test that jumped the clock back leaves that gate far ahead.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true, DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair);
        var server = pair.Server;
        var rules = server.System<Content.Server._SW.GlobalWar.SWGlobalWarGameruleSystem>();
        Guid life = default;
        await server.WaitAssertion(() =>
        {
            var body = Join(round, "SWPoliticsTestForce");
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out life), Is.True);
            round.Mobs.ChangeMobState(body, MobState.Dead);
            Assert.That(rules.TryGetGlobalWarGamerule(out var rule), Is.True);
            var world = rule.Comp.PlanetEntities["SWSpawnTestForce"];
            var planet = server.EntMan.GetComponent<Content.Server._SW.GlobalWar.SWGlobalWarPlanetComponent>(world);
            Assert.That(server.System<SWGlobalWarCampaignStepSystem>().TryQueueOwnership(world,
                SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire, planet.OwnershipRevision,
                rule.Comp.Generation, Guid.NewGuid()), Is.True);
        });
        await pair.RunTicksSync(DelayTicks(round) + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(round.Spawn.TryRespawn(round.Player, life, true, out var message), Is.False);
            Assert.That(message, Is.EqualTo(Loc("sw-global-war-spawn-force-world-lost")));
            Assert.That(round.Registry.TryGetForceCarrier(round.Player.UserId, out _), Is.True);
            Assert.That(round.Spawn.TryGetLife(round.Player.UserId, out var same), Is.True);
            Assert.That(same, Is.EqualTo(life));
        });
        await pair.RunTicksSync(70);
        var state = pair.Client.System<SWGlobalWarEntrySystem>().RespawnState;
        await server.WaitAssertion(() => Assert.That(state?.ForceDenied, Is.EqualTo(Loc("sw-global-war-spawn-force-world-lost"))));
        Assert.That(state?.CivilianDenied, Is.Null, "The civilian way stays open.");
        await Finish(round);
    }

    [Test]
    public async Task LobbyEntryGetsAServerAnswer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair);
        var server = pair.Server;
        var client = pair.Client.System<SWGlobalWarEntrySystem>();
        var answers = new List<SWGlobalWarJoinResultEvent>();
        client.JoinResult += answers.Add;
        await server.WaitAssertion(() =>
        {
            // The role of the other side's station is not a place this request may enter.
            round.Ticker.MakeJoinGame(round.Player, round.Station, "SWSpawnTestEmpire");
        });
        await pair.RunTicksSync(5);
        Assert.That(answers, Has.Count.EqualTo(1));
        Assert.That(answers[0].Success, Is.False);
        Assert.That(answers[0].Message, Is.Not.Empty);
        await server.WaitAssertion(() => Join(round, "SWPoliticsTestSoldier"));
        await pair.RunTicksSync(5);
        client.JoinResult -= answers.Add;
        Assert.That(answers, Has.Count.EqualTo(2));
        Assert.That(answers[1].Success, Is.True, answers[1].Message);
        await server.WaitAssertion(() => Assert.That(answers[1].Message, Does.Contain(Loc("sw-global-war-republic"))));
        await Finish(round);
    }

    [Test]
    public async Task TimeLimitTellsThePlayerTheWarIsNotOver()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair, "SWRespawnEndTestPreset");
        var server = pair.Server;
        await server.WaitAssertion(() => Join(round, "SWPoliticsTestSoldier"));
        await pair.RunTicksSync(60);
        var result = pair.Client.System<SWGlobalWarRoundEndSystem>().Last;
        Assert.That(round.Ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Outcome, Is.EqualTo(SWGlobalWarOutcome.TimeLimit));
        Assert.That(result.Side, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(result.RepublicWorlds, Is.EqualTo(3));
        Assert.That(result.EmpireWorlds, Is.EqualTo(1));
        await Finish(round);
    }

    [Test]
    public async Task DecidedCampaignRestartsTheRoundByItself()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CCVars.RoundRestartTime, 1));
        var round = await StartRound(pair, "SWRespawnEndTestPreset");
        await pair.Server.WaitAssertion(() => Join(round, "SWPoliticsTestSoldier"));
        await pair.RunTicksSync(60);
        Assert.That(round.Ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        await pair.RunSeconds(3);
        Assert.That(round.Ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby),
            "Nothing else restarts a campaign round once it is decided.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAdminEndedRoundIsNotEndedAgainByTheCampaign()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair, "SWRespawnEndTestPreset");
        await pair.Server.WaitPost(() =>
        {
            Join(round, "SWPoliticsTestSoldier");
            round.Ticker.EndRound("admin");
        });
        // The limit falls due in PostRound; a second EndRound would trip its InRound assertion on the server.
        await pair.RunTicksSync(60);
        Assert.That(round.Ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        await Finish(round);
    }

    [Test]
    public async Task TimeLimitVictoryGoesToTheSideHoldingMoreLocations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var round = await StartRound(pair, "SWRespawnLimitTestPreset");
        var server = pair.Server;
        await server.WaitAssertion(() => Join(round, "SWPoliticsTestSoldier"));
        await pair.RunTicksSync(60);
        var result = pair.Client.System<SWGlobalWarRoundEndSystem>().Last;
        Assert.That(round.Ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Outcome, Is.EqualTo(SWGlobalWarOutcome.RepublicVictory));
        Assert.That(result.LimitRepublic, Is.EqualTo(6));
        Assert.That(result.LimitEmpire, Is.EqualTo(2));
        await Finish(round);
    }

    [Test]
    public async Task ProductionCampaignDecidesAtTheLimitAndSellsNoBattleships()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var prototype = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>()
                .Index<Robust.Shared.Prototypes.EntityPrototype>("SWGlobalWarCampaignProduction");
            Assert.That(prototype.TryGetComponent<SWGlobalWarCampaignComponent>(out var campaign,
                server.ResolveDependency<Robust.Shared.GameObjects.IComponentFactory>()), Is.True);
            Assert.That(campaign!.TimeLimitVictory, Is.True);
            Assert.That(campaign.DisabledShipClasses, Does.Contain(SWGlobalWarShipClass.Battleship));
            Assert.That(campaign.DisabledShipClasses, Has.Count.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    private static string Loc(string key)
    {
        return Robust.Shared.Localization.Loc.GetString(key);
    }
}
