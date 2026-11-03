using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Exercises the real GameTicker and network command before adding campaign-specific spawn selection.</summary>
[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarSpawnHookTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: job
  id: SWSpawnHookJob
  name: job-name-captain
  playTimeTracker: SWSpawnHookTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: playTimeTracker
  id: SWSpawnHookTracker

- type: gameMap
  id: SWSpawnHookMap
  mapName: Hook test map
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationJobs
        availableJobs:
          SWSpawnHookJob: [1, 1]
""";

    [Test]
    public async Task RejectedPreparationKeepsLobbyMindSlotsAndSuccessfulPlayerListUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
            Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var probe = server.System<SWGlobalWarSpawnHookTestSystem>();
        var jobs = server.System<StationJobsSystem>();
        var player = server.PlayerMan.Sessions.Single();
        await pair.SetJobPriorities(("SWSpawnHookJob", JobPriority.High));
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnHookMap");
            Assert.That(((HumanoidCharacterProfile)server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(player.UserId).SelectedCharacter).JobPriorities["SWSpawnHookJob"], Is.EqualTo(JobPriority.High));
            ticker.SetGamePreset("sandbox");
            probe.Reset(player.UserId);
            ticker.ToggleReadyAll(true);
            Assert.That(player.ContentData()!.Mind, Is.Null);
            ticker.StartRound();
            Assert.That(server.System<Content.Server._SW.GlobalWar.SWGlobalWarGameruleSystem>()
                .TryGetGlobalWarGamerule(out _), Is.False);
        });
        await pair.RunTicksSync(10);
        EntityUid station = default;
        await server.WaitAssertion(() =>
        {
            station = server.EntMan.EntityQuery<StationJobsComponent>().Single().Owner;
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay));
            Assert.That(player.ContentData()!.Mind, Is.Null);
            Assert.That(player.AttachedEntity, Is.Null);
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.Zero);
            Assert.That(probe.Preparations, Is.EqualTo(1));
            Assert.That(probe.LastLateJoin, Is.False);
            Assert.That(probe.AssignedPlayers, Is.Zero);
            Assert.That(jobs.TryGetJobSlot(station, "SWSpawnHookJob", out var slots), Is.True);
            Assert.That(slots, Is.EqualTo(1));
            Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out _), Is.False);
        });
        var command = $"joingame SWSpawnHookJob {server.EntMan.GetNetEntity(station).Id}";
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand(command));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(probe.Preparations, Is.EqualTo(2));
            Assert.That(probe.LastLateJoin, Is.True);
            Assert.That(player.ContentData()!.Mind, Is.Null);
            Assert.That(player.AttachedEntity, Is.Null);
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.Zero);
            Assert.That(jobs.TryGetJobSlot(station, "SWSpawnHookJob", out var slots), Is.True);
            Assert.That(slots, Is.EqualTo(1));
            Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out _), Is.False);
            probe.Reject = false;
        });
        await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand(command));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(probe.Preparations, Is.EqualTo(3));
            Assert.That(probe.CompletedLateJoin, Is.True);
            Assert.That(player.ContentData()!.Mind, Is.Not.Null);
            Assert.That(player.AttachedEntity, Is.Not.Null);
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(1));
            Assert.That(jobs.TryGetJobSlot(station, "SWSpawnHookJob", out var slots), Is.True);
            Assert.That(slots, Is.Zero);
            Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out var assigned), Is.True);
            Assert.That(assigned, Has.Count.EqualTo(1));
            probe.User = null;
            ticker.RestartRound();
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }
}

public sealed class SWGlobalWarSpawnHookTestSystem : EntitySystem
{
    public NetUserId? User;
    public bool Reject;
    public int Preparations;
    public bool LastLateJoin;
    public bool CompletedLateJoin;
    public int AssignedPlayers;

    public void Reset(NetUserId user)
    {
        User = user;
        Reject = true;
        Preparations = 0;
        LastLateJoin = false;
        CompletedLateJoin = false;
        AssignedPlayers = 0;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarBeforeSpawnEvent>(OnPrepare);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnAssigned);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnComplete);
    }

    private void OnPrepare(ref SWGlobalWarBeforeSpawnEvent args)
    {
        if (args.Player.UserId != User)
            return;
        Preparations++;
        LastLateJoin = args.LateJoin;
        args.Cancelled |= Reject;
    }

    private void OnAssigned(RulePlayerJobsAssignedEvent args)
    {
        if (User != null)
            AssignedPlayers += args.Players.Count(p => p.UserId == User);
    }

    private void OnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.Player.UserId == User)
            CompletedLateJoin = args.LateJoin;
    }
}
