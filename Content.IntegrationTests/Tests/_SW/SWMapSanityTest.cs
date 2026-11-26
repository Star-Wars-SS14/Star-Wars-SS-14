using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TaskSystem.SeparatedTaskPipeline;
using Content.Server._SW.TaskSystem.StealObjectTask;
using Content.Server._SW.TaskSystem.Subtask;
using Content.Server._SW.TaskSystem.TaskItemsContainer;
using Content.Server._SW.TransferSystem;
using Content.Server.GameTicking.Presets;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Maps;
using Content.Shared.Random;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Static checks of the maps that run SW faction tasks. They read the map files instead of loading them, so they
/// stay fast even for the 18 MB newBalmorra.
/// </summary>
[TestFixture]
public sealed class SWMapSanityTest
{
    private static readonly Regex ProtoLine = new(@"^- proto: (\S+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// On every map a task preset can pick, every faction with job slots has a task console, and every such faction
    /// with a steal task has a place to turn the items in: a pallet or a task items safe.
    /// </summary>
    [Test]
    public async Task FactionsHaveTaskConsolesAndTurnInPoints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var resMan = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var (factions, maps) = TaskFactionsAndMaps(protoMan, factory);
            Assert.That(maps, Is.Not.Empty, "No map runs a preset with faction tasks.");

            Assert.Multiple(() =>
            {
                foreach (var mapId in maps.OrderBy(m => m))
                {
                    var gameMap = protoMan.Index<GameMapPrototype>(mapId);
                    var mapProtos = MapEntityPrototypes(resMan, gameMap);

                    var consoles = new HashSet<string>();
                    var turnIn = new HashSet<string>();
                    foreach (var id in mapProtos)
                    {
                        if (!protoMan.TryIndex<EntityPrototype>(id, out var proto))
                            continue;
                        if (proto.TryGetComponent<TaskConsoleComponent>(out var console, factory))
                            consoles.Add(console.Faction);
                        if (proto.TryGetComponent<SWTransferPalletComponent>(out var pallet, factory))
                            turnIn.Add(pallet.OwnerFaction);
                        if (proto.TryGetComponent<SWTaskItemsContainerComponent>(out var safe, factory))
                            turnIn.Add(safe.Faction);
                    }

                    var jobSlots = JobSlots(gameMap, factory);
                    foreach (var factionId in factions.OrderBy(f => f))
                    {
                        var faction = protoMan.Index<EntityPrototype>(factionId);
                        if (!faction.TryGetComponent<SWFactionComponent>(out var factionComp, factory)
                            || !factionComp.RelatedJobs.Any(j => jobSlots.Contains(j.Id)))
                        {
                            continue;
                        }

                        Assert.That(consoles, Does.Contain(factionId),
                            $"{mapId}: faction {factionId} has job slots but no task console.");

                        var stealTasks = StealTasks(protoMan, factory, faction).ToList();
                        if (stealTasks.Count > 0)
                        {
                            Assert.That(turnIn, Does.Contain(factionId),
                                $"{mapId}: faction {factionId} has steal tasks ({string.Join(", ", stealTasks)}) " +
                                "but no SWTransferPallet or SWTaskItemsContainer to turn them in.");
                        }
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The factions of every task rule in a preset, and the maps those presets can pick.
    /// </summary>
    private static (HashSet<string> Factions, HashSet<string> Maps) TaskFactionsAndMaps(
        IPrototypeManager protoMan,
        IComponentFactory factory)
    {
        var factions = new HashSet<string>();
        var maps = new HashSet<string>();
        foreach (var preset in protoMan.EnumeratePrototypes<GamePresetPrototype>())
        {
            var presetFactions = new HashSet<string>();
            var hasTasks = false;
            foreach (var ruleId in preset.Rules)
            {
                var rule = protoMan.Index(ruleId);
                if (rule.TryGetComponent<SWFactionGameruleComponent>(out var factionRule, factory))
                    presetFactions.UnionWith(factionRule.FactionsPrototypes);
                if (rule.TryGetComponent<SWTaskGameruleComponent>(out var taskRule, factory) && !taskRule.ManualTasksOnly)
                    hasTasks = true;
            }

            if (!hasTasks || presetFactions.Count == 0 || preset.MapPool == null)
                continue;

            factions.UnionWith(presetFactions);
            maps.UnionWith(protoMan.Index<GameMapPoolPrototype>(preset.MapPool).Maps);
        }

        return (factions, maps);
    }

    private static HashSet<string> MapEntityPrototypes(IResourceManager resMan, GameMapPrototype gameMap)
    {
        var result = new HashSet<string>();
        using var reader = resMan.ContentFileReadText(gameMap.MapPath);
        while (reader.ReadLine() is { } line)
        {
            var match = ProtoLine.Match(line);
            if (match.Success)
                result.Add(match.Groups[1].Value);
        }
        return result;
    }

    private static HashSet<string> JobSlots(GameMapPrototype gameMap, IComponentFactory factory)
    {
        var jobsName = factory.GetComponentName<StationJobsComponent>();
        var result = new HashSet<string>();
        foreach (var station in gameMap.Stations.Values)
        {
            if (!station.StationComponentOverrides.TryGetValue(jobsName, out var entry))
                continue;

            foreach (var (job, slots) in ((StationJobsComponent) entry.Component).SetupAvailableJobs)
            {
                if (slots.Any(s => s != 0))
                    result.Add(job.Id);
            }
        }
        return result;
    }

    /// <summary>
    /// Tasks the faction can get, from its random task set and its scheduled pipeline, subtasks included, that need
    /// items turned in.
    /// </summary>
    private static IEnumerable<string> StealTasks(
        IPrototypeManager protoMan,
        IComponentFactory factory,
        EntityPrototype faction)
    {
        var taskIds = new HashSet<string>();
        if (faction.TryGetComponent<SWTaskContainerComponent>(out var container, factory)
            && container.AvailableTasks is { } setId)
        {
            taskIds.UnionWith(protoMan.Index<WeightedRandomPrototype>(setId).Weights.Keys);
        }

        if (faction.TryGetComponent<SWFactionSeparatedTaskPipelineComponent>(out var pipeline, factory))
            taskIds.UnionWith(pipeline.Tasks.Select(t => t.TaskId));

        var pending = new Queue<string>(taskIds);
        while (pending.TryDequeue(out var id))
        {
            if (!protoMan.TryIndex<EntityPrototype>(id, out var task)
                || !task.TryGetComponent<SWSubtaskContainerComponent>(out var subtasks, factory))
            {
                continue;
            }

            foreach (var subtask in subtasks.Subtasks)
            {
                if (taskIds.Add(subtask.TaskProto))
                    pending.Enqueue(subtask.TaskProto);
            }
        }

        return taskIds
            .Where(id => protoMan.TryIndex<EntityPrototype>(id, out var task)
                         && task.TryGetComponent<SWCollectObjectTaskComponent>(out _, factory))
            .OrderBy(id => id);
    }
}
