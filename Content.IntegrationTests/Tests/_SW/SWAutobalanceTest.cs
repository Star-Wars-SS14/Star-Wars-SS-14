#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._SW.Autobalance;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._SW;
using Content.Shared.CCVar;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWAutobalanceTest
{
    private const string Rule = "SWAutoBalanceGamerule";
    private const string Map = "SWAutobalanceTestMap";

    private static readonly ProtoId<JobPrototype> Republic = "SWRepublicSoldier";
    private static readonly ProtoId<JobPrototype> RepublicMedic = "SWRepublicMedic";
    private static readonly ProtoId<JobPrototype> Empire = "SWEmpireSoldier";

    [TestPrototypes]
    private static readonly string Prototypes = @$"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Republic}: [ 6, 6 ]
            {RepublicMedic}: [ 2, 2 ]
            {Empire}: [ 8, 8 ]
";

    [TestCase(5, 5, 2)]
    [TestCase(3, 5, 2)]
    [TestCase(2, 5, 2)]
    [TestCase(1, 0, 0)]
    [TestCase(5, 0, 0)]
    public async Task RemovesOnlyExcessAtRoundStart(int republic, int empire, int republicLeft)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule(Rule));

            var assigned = new Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)>();
            for (var i = 0; i < republic; i++)
            {
                assigned[new NetUserId(System.Guid.NewGuid())] = (Republic, EntityUid.Invalid);
            }

            for (var i = 0; i < empire; i++)
            {
                assigned[new NetUserId(System.Guid.NewGuid())] = (Empire, EntityUid.Invalid);
            }

            var ev = new SWModifyAssignedJobsEvent(assigned);
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);

            Assert.That(assigned.Values.Count(x => x.Item1 == Republic), Is.EqualTo(republicLeft));
            Assert.That(assigned.Values.Count(x => x.Item1 == Empire), Is.EqualTo(empire));
            Assert.That(assigned.Values.Count(x => x.Item1 == null), Is.EqualTo(republic - republicLeft));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A death gives the Republic budget back, and a respawn does not eat it, but the used slot never returns.
    /// </summary>
    [Test]
    public async Task CountsLivingPlayersInsteadOfSpawns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();
        var mobState = server.System<MobStateSystem>();

        await server.AddDummySessions(2);
        await pair.RunTicksSync(5);

        var users = server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        await pair.SetJobPriorities(users[0], (Republic, JobPriority.High));
        await pair.SetJobPriorities(users[1], (Empire, JobPriority.High));
        await pair.SetJobPriorities(users[2], (Empire, JobPriority.High));

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() =>
        {
            ticker.StartGameRule(Rule);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        EntityUid Body(int user)
        {
            return server.PlayerMan.SessionsDict[users[user]].AttachedEntity!.Value;
        }

        // 2 Empire / 2 - 1 Republic = no budget.
        await server.WaitAssertion(() =>
        {
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(0));
            Assert.That(RepublicSlots(pair, RepublicMedic), Is.EqualTo(0));
            mobState.ChangeMobState(Body(0), MobState.Dead);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(1), "A dead Republic player must free the budget");
            Assert.That(RepublicSlots(pair, RepublicMedic), Is.EqualTo(1));
            mobState.ChangeMobState(Body(1), MobState.Dead);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(0), "Only 1 Empire alive gives no Republic budget");
            mobState.ChangeMobState(Body(1), MobState.Alive);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(1));
        });

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Autobalance never opened Republic on an empty server. This test pins that behaviour: the owner decides on it.
    /// </summary>
    [Test]
    public async Task FirstRepublicPlayerCannotJoinWithoutEmpire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        await pair.SetJobPriorities((Republic, JobPriority.High));
        ticker.ToggleReadyAll(true);
        await server.WaitPost(() =>
        {
            ticker.StartGameRule(Rule);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.PlayerMan.SessionsDict[pair.Client.User!.Value].AttachedEntity, Is.Null);
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(0));
        });

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A tutorial graduate gets one slot with no budget for it. The recount after the spawn must neither take it away
    /// before the spawn nor leave a second free slot after it.
    /// </summary>
    [Test]
    public async Task GraduateSlotIsGoodForOneSpawn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();
        var autobalance = server.System<SWAutobalanceGameruleSystem>();

        // The graduate stays in the lobby through the round start, not ready.
        var dummies = await server.AddDummySessions(1);
        await pair.RunTicksSync(5);
        await pair.SetJobPriorities((Empire, JobPriority.High));
        ticker.ToggleReady(pair.Player!, true);
        await server.WaitPost(() =>
        {
            ticker.StartGameRule(Rule);
            ticker.StartRound();
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var query = server.EntMan.EntityQueryEnumerator<StationJobsComponent>();
            Assert.That(query.MoveNext(out var station, out _));
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(0), "1 Empire player gives no Republic budget");

            autobalance.GrantExtraSlot(station, Republic);
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(1));

            ticker.MakeJoinGame(dummies[0], station, Republic);
            Assert.That(dummies[0].AttachedEntity, Is.Not.Null);
            Assert.That(RepublicSlots(pair, Republic), Is.EqualTo(0), "The slot must be gone after one spawn");
        });

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();
    }

    private static int? RepublicSlots(TestPair pair, ProtoId<JobPrototype> job)
    {
        var jobs = pair.Server.System<StationJobsSystem>();
        var query = pair.Server.EntMan.EntityQueryEnumerator<StationJobsComponent>();
        Assert.That(query.MoveNext(out var station, out _));
        Assert.That(jobs.TryGetJobSlot(station, job, out var slots));
        return slots;
    }
}
