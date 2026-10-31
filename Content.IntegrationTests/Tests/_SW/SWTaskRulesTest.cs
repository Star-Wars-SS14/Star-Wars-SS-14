using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Content.Server._SW.TaskSystem.DynamicDescription.BasedOnJob;
using Content.Server._SW.TaskSystem.GetScoreByTargetJob;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// The SW task rules: who wins, how many points a task gives, and whether task prototypes name real jobs.
/// </summary>
[TestFixture]
[TestOf(typeof(SWTasksGameRuleSystem))]
public sealed class SWTaskRulesTest
{
    private const string Republic = "RepublicFaction";
    private const string Empire = "EmpireFaction";
    private const string Mandalorians = "MandalorianFaction";

    private sealed record FactionSetup(string Proto, int Score, int TaskScore, params bool[] TasksDone);

    private static readonly object[] WinnerCases =
    {
        new object[]
        {
            "Higher score wins",
            new[] { new FactionSetup(Republic, 30, 1, true, false), new FactionSetup(Empire, 20, 1, false) },
            new[] { Republic },
        },
        new object[]
        {
            "Equal score: both majors win",
            new[] { new FactionSetup(Republic, 20, 1, false), new FactionSetup(Empire, 20, 1, false) },
            new[] { Republic, Empire },
        },
        new object[]
        {
            "No tasks and 0:0: both majors win, not a draw",
            new[] { new FactionSetup(Republic, 0, 1), new FactionSetup(Empire, 0, 1) },
            new[] { Republic, Empire },
        },
        new object[]
        {
            "All tasks done beats a higher score",
            new[] { new FactionSetup(Republic, 30, 1, false), new FactionSetup(Empire, 5, 1, true) },
            new[] { Empire },
        },
        new object[]
        {
            "Both majors did all tasks: draw",
            new[] { new FactionSetup(Republic, 30, 1, true), new FactionSetup(Empire, 5, 1, true) },
            Array.Empty<string>(),
        },
        new object[]
        {
            "Minor with all tasks done and more points than majors wins",
            new[]
            {
                new FactionSetup(Republic, 20, 1, false),
                new FactionSetup(Empire, 10, 1, false),
                new FactionSetup(Mandalorians, 0, 25, true, true),
            },
            new[] { Mandalorians },
        },
        new object[]
        {
            "Minor with all tasks done but fewer points loses",
            new[]
            {
                new FactionSetup(Republic, 20, 1, false),
                new FactionSetup(Empire, 10, 1, false),
                new FactionSetup(Mandalorians, 0, 5, true),
            },
            new[] { Republic },
        },
    };

    [Test]
    public async Task CalculateWinner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tasks = server.System<SWTasksGameRuleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (object[] testCase in WinnerCases)
                {
                    var name = (string) testCase[0];
                    var setups = (FactionSetup[]) testCase[1];
                    var expected = (string[]) testCase[2];

                    var spawned = new List<EntityUid>();
                    foreach (var setup in setups)
                    {
                        spawned.AddRange(SpawnFaction(entMan, setup));
                    }

                    var winners = tasks.GetWinners()
                        .Select(w => entMan.GetComponent<MetaDataComponent>(w).EntityPrototype!.ID)
                        .OrderBy(id => id);
                    Assert.That(winners, Is.EqualTo(expected.OrderBy(id => id)), name);

                    foreach (var uid in spawned)
                    {
                        entMan.DeleteEntity(uid);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<EntityUid> SpawnFaction(IEntityManager entMan, FactionSetup setup)
    {
        var faction = entMan.SpawnEntity(setup.Proto, MapCoordinates.Nullspace);
        yield return faction;

        entMan.GetComponent<SWFactionComponent>(faction).CurrentScore = setup.Score;
        var container = entMan.GetComponent<SWTaskContainerComponent>(faction);
        foreach (var done in setup.TasksDone)
        {
            var task = entMan.SpawnEntity("SWTaskBase", MapCoordinates.Nullspace);
            var taskComp = entMan.GetComponent<SWTaskComponent>(task);
            taskComp.Completeness = done ? 1.0 : 0.0;
            taskComp.Score = setup.TaskScore;
            container.CurrentTasks.Add((task, taskComp));
            yield return task;
        }
    }

    /// <summary>
    /// A task items safe counts inserted items for its owning faction, so a steal task can be finished through it.
    /// </summary>
    [Test]
    public async Task SafeCompletesStealTask()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var tasks = server.System<SWTasksGameRuleSystem>();
        var storage = server.System<SharedEntityStorageSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("TaskGamerule");
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            tasks.AddTask(Republic, "StealIdCardsEmpire");
            var safe = entMan.SpawnEntity("SWTaskObjectsSafeRepublic", map.GridCoords);
            var republic = server.System<SWFactionGameruleSystem>().GetFactionEntity(Republic)!.Value;

            for (var inserted = 1; inserted <= 3; inserted++)
            {
                var card = entMan.SpawnEntity("SWIDCardEmpireMoff", map.GridCoords);
                Assert.That(storage.Insert(card, safe), $"Could not put card {inserted} into the safe.");
                Assert.That(tasks.GetLiveScore(republic), Is.EqualTo(inserted == 3 ? 15 : 0));
            }

            ticker.ClearGameRules();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every target job a task can pick must have a score; a missing key throws at round end.
    /// Also checks the score a completed task gives for each target job.
    /// </summary>
    [Test]
    public async Task TaskScoreByTargetJob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var tasks = server.System<SWTasksGameRuleSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID))
                {
                    if (proto.Abstract
                        || !proto.TryGetComponent<SWGetScoreByTargetJobComponent>(out var scores, factory)
                        || !proto.TryGetComponent<SWTargetSelectorComponent>(out var selector, factory)
                        || !selector.SelectTargetOnInit)
                    {
                        continue;
                    }

                    foreach (var job in selector.Jobs)
                    {
                        if (!scores.JobsToScores.TryGetValue(job, out var expected))
                        {
                            Assert.Fail($"{proto.ID}: target job {job} has no score in SWGetScoreByTargetJob.");
                            continue;
                        }

                        var task = entMan.SpawnEntity(proto.ID, MapCoordinates.Nullspace);
                        entMan.GetComponent<SWTargetSelectorComponent>(task).JobOfTarget = job;
                        var score = tasks.GetTaskScore((task, entMan.GetComponent<SWTaskComponent>(task)));
                        Assert.That(score, Is.EqualTo(expected), $"{proto.ID}: score for target {job}");
                        entMan.DeleteEntity(task);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Task prototypes name jobs as plain strings, so a typo (such as a Cyrillic letter) is not caught by the
    /// prototype loader and the job is silently never a target.
    /// </summary>
    [Test]
    public async Task TaskJobIdsExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID))
                {
                    var jobs = new List<(string Field, string Job)>();
                    if (proto.TryGetComponent<SWTargetSelectorComponent>(out var selector, factory))
                        jobs.AddRange(selector.Jobs.Select(j => ("SWTargetSelector.jobs", j)));
                    if (proto.TryGetComponent<SWGetScoreByTargetJobComponent>(out var scores, factory))
                        jobs.AddRange(scores.JobsToScores.Keys.Select(j => ("SWGetScoreByTargetJob.jobsToScores", j)));
                    if (proto.TryGetComponent<SWDescriptionBasedOnJobOfTargetComponent>(out var desc, factory))
                        jobs.AddRange(desc.JobsToDescriptions.Keys.Select(j => ("SWDescriptionBasedOnJobOfTarget", j)));

                    foreach (var (field, job) in jobs)
                    {
                        Assert.That(protoMan.HasIndex<JobPrototype>(job),
                            $"{proto.ID}: {field} names job \"{job}\", which does not exist.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
