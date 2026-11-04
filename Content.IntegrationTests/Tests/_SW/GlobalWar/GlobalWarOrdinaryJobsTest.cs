using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Checks the modified allocation hooks outside a campaign with genuinely selectable fixture jobs.</summary>
[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarOrdinaryJobsTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: gameMap
  id: SWOrdinaryJobsMap
  mapName: Ordinary job allocation test
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationJobs
        availableJobs:
          SWAdmissionWeightedR: [1, 1]
          SWSpawnTestRepublic: [-1, -1]
          SWSpawnTestEmpire: [-1, -1]
""";

    [TestCase("preference")]
    [TestCase("weight")]
    [TestCase("competition")]
    public async Task OrdinaryRoundRetainsPreferencesWeightsAndFiniteSlotCompetition(string scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        if (scenario == "competition")
            await server.AddDummySessions(5);
        var loaded = false;
        for (var i = 0; i < 100 && !loaded; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                prefs.TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var players = server.PlayerMan.Sessions.ToArray();
        var leader = players[players.Length / 2].UserId;
        foreach (var player in players)
        {
            var profile = (HumanoidCharacterProfile)prefs.GetPreferences(player.UserId).SelectedCharacter;
            var choices = scenario switch
            {
                "preference" => new[] { ("SWSpawnTestRepublic", JobPriority.Medium), ("SWSpawnTestEmpire", JobPriority.High) },
                "weight" => new[] { ("SWAdmissionWeightedR", JobPriority.Low), ("SWSpawnTestEmpire", JobPriority.High) },
                _ when player.UserId == leader => new[] { ("SWAdmissionWeightedR", JobPriority.High), ("SWSpawnTestRepublic", JobPriority.Medium) },
                _ => new[] { ("SWAdmissionWeightedR", JobPriority.Medium), ("SWSpawnTestRepublic", JobPriority.High) },
            };
            await pair.SetJobPriorities(player.UserId, profile.JobPriorities.Keys
                .Select(j => (j, JobPriority.Never))
                .Concat(choices.Select(c => ((ProtoId<JobPrototype>)c.Item1, c.Item2))).ToArray());
        }
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWOrdinaryJobsMap");
                ticker.SetGamePreset("sandbox");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(server.System<SWGlobalWarGameruleSystem>().TryGetGlobalWarGamerule(out _), Is.False);
                var station = server.EntMan.EntityQuery<StationJobsComponent>().Single().Owner;
                var jobs = server.System<StationJobsSystem>();
                foreach (var player in players)
                {
                    var expected = scenario == "preference" ? "SWSpawnTestEmpire" :
                        scenario == "weight" || player.UserId == leader ? "SWAdmissionWeightedR" : "SWSpawnTestRepublic";
                    Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.JoinedGame));
                    Assert.That(player.AttachedEntity, Is.Not.Null);
                    Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out var assigned), Is.True);
                    Assert.That(assigned!.Select(j => j.Id), Is.EquivalentTo(new[] { expected }));
                }
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(players.Length));
                Assert.That(jobs.TryGetJobSlot(station, "SWAdmissionWeightedR", out var remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(scenario == "preference" ? 1 : 0));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(originalPreset);
            });
        }
        await pair.CleanReturnAsync();
    }
}
