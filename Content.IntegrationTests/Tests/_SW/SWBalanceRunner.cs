using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Pair;
using Content.Server._SW.DesignTesting.Bots;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Headless balance runner: loads an SW map on a server that ticks as fast as the CPU allows, spawns soldier bots
/// at the Republic and Empire job spawn points and lets them fight. Writes one JSON file per pass and a summary.
/// Explicit: CI never runs it. Settings come from environment variables, see <see cref="Settings"/>.
/// </summary>
[TestFixture]
[Explicit]
[Category("SWBalance")]
public sealed class SWBalanceRunner
{
    private const string Republic = "RepublicFaction";
    private const string Empire = "EmpireFaction";

    /// <summary>
    /// The side of a square cell that fight positions are grouped into, in tiles.
    /// </summary>
    private const int CellSize = 16;

    private sealed record Settings(
        string Map,
        string Mode,
        float Gap,
        int Passes,
        int BotsPerSide,
        double MaxMinutes,
        SWBotObjective Objective,
        string OutDir)
    {
        public static Settings FromEnvironment()
        {
            string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
                ? v
                : fallback;

            return new Settings(
                Env("SW_BALANCE_MAP", "oldBalmora"),
                Env("SW_BALANCE_MODE", "arena"),
                float.Parse(Env("SW_BALANCE_GAP", "16"), System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(Env("SW_BALANCE_PASSES", "5")),
                int.Parse(Env("SW_BALANCE_BOTS", "10")),
                double.Parse(Env("SW_BALANCE_MINUTES", "10"), System.Globalization.CultureInfo.InvariantCulture),
                Enum.Parse<SWBotObjective>(Env("SW_BALANCE_OBJECTIVE", nameof(SWBotObjective.EnemyBase)), true),
                Env("SW_BALANCE_OUT", Path.Combine(Path.GetTempPath(), "sw-balance")));
        }
    }

    private sealed class Death
    {
        public string Faction { get; set; } = "";
        public double Time { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    private sealed class PassResult
    {
        public int Pass { get; set; }
        public string Map { get; set; } = "";
        public string Mode { get; set; } = "";
        public string Objective { get; set; } = "";
        public int BotsPerSide { get; set; }
        public string Winner { get; set; } = "";
        public string EndReason { get; set; } = "";
        public double GameSeconds { get; set; }
        public double RealSeconds { get; set; }
        public double Speedup { get; set; }
        public double? FirstContactSeconds { get; set; }
        public double? FirstDeathSeconds { get; set; }
        public Dictionary<string, int> Kills { get; set; } = new();
        public Dictionary<string, int> Alive { get; set; } = new();
        public Dictionary<string, double> Survival { get; set; } = new();
        public float BaseDistance { get; set; }
        public Dictionary<string, float[]> Start { get; set; } = new();
        public List<Death> Deaths { get; set; } = new();
        public List<string> FightCells { get; set; } = new();
        public Dictionary<string, float> MeanDistanceWalked { get; set; } = new();
    }

    [Test]
    public async Task Run()
    {
        var settings = Settings.FromEnvironment();
        Directory.CreateDirectory(settings.OutDir);
        var results = new List<PassResult>();

        // One server for all passes: each pass loads the map and deletes it afterwards.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Fresh = true });
        // SW maps log errors while loading and unloading (oldBalmorra: ThrownItem with a broken entity reference).
        // They are known map problems, not a reason to stop measuring. The pair is dirty and never reused.
        pair.ServerLogHandler.FailureLevel = null;

        for (var pass = 1; pass <= settings.Passes; pass++)
        {
            var result = await RunPass(pair, settings, pass);
            results.Add(result);
            await File.WriteAllTextAsync(Path.Combine(settings.OutDir, $"pass_{pass}.json"), ToJson(result));
            TestContext.Progress.WriteLine(
                $"pass {pass}: winner {result.Winner} ({result.EndReason}), game {result.GameSeconds:0}s, " +
                $"real {result.RealSeconds:0.0}s, x{result.Speedup:0.0}, " +
                $"first contact {result.FirstContactSeconds:0}s, " +
                $"kills R {result.Kills[Republic]} / E {result.Kills[Empire]}");
        }

        var summary = Summarize(settings, results);
        await File.WriteAllTextAsync(Path.Combine(settings.OutDir, "summary.json"), ToJson(summary));
        TestContext.Progress.WriteLine($"summary: {ToJson(summary)}");
        TestContext.Progress.WriteLine($"output: {settings.OutDir}");
        await pair.CleanReturnAsync();
    }

    private static async Task<PassResult> RunPass(TestPair pair, Settings settings, int pass)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var bots = server.System<SWCombatBotSystem>();
        var mobState = server.System<MobStateSystem>();
        var xformSys = server.System<SharedTransformSystem>();
        var loader = server.System<MapLoaderSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var gameMap = server.ProtoMan.Index<GameMapPrototype>(settings.Map);

        var result = new PassResult
        {
            Pass = pass,
            Map = settings.Map,
            Mode = settings.Mode,
            Objective = settings.Mode == "arena" ? nameof(SWBotObjective.Point) : settings.Objective.ToString(),
            BotsPerSide = settings.BotsPerSide,
        };

        var sides = new Dictionary<string, List<EntityUid>>();
        var spawnPoints = new Dictionary<EntityUid, Vector2>();
        EntityUid mapUid = default;
        await server.WaitAssertion(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            Assert.That(loader.TryLoadMap(gameMap.MapPath, out var map, out _, options),
                $"Could not load {gameMap.MapPath}");
            mapUid = map!.Value;

            var bases = new Dictionary<string, MapCoordinates>();
            foreach (var faction in new[] { Republic, Empire })
            {
                var pos = bots.GetBasePosition(faction);
                Assert.That(pos, Is.Not.Null, $"{settings.Map} has no spawn point or task console of {faction}.");
                bases[faction] = pos!.Value;
            }

            result.BaseDistance = (bases[Republic].Position - bases[Empire].Position).Length();

            // Arena: both sides start a short gap apart halfway between the bases and walk at each other. It measures
            // the fight itself; the bases mode also measures the walk, which the NPC pathfinder handles badly on
            // Balmora (no path beyond about 40-80 tiles).
            var starts = new Dictionary<string, MapCoordinates>(bases);
            if (settings.Mode == "arena")
            {
                var arena = FindArena(interaction, bases[Republic], bases[Empire], settings.Gap);
                Assert.That(arena, Is.Not.Null, "No open ground for an arena near the middle of the map.");
                starts[Republic] = arena!.Value.Republic;
                starts[Empire] = arena.Value.Empire;
            }

            foreach (var faction in new[] { Republic, Empire })
            {
                var enemy = faction == Republic ? Empire : Republic;
                var startPos = starts[faction].Position;
                result.Start[faction] = new[] { MathF.Round(startPos.X), MathF.Round(startPos.Y) };
                sides[faction] = settings.Mode == "arena"
                    ? bots.SpawnBots(faction,
                        settings.BotsPerSide,
                        starts[faction],
                        SWBotObjective.Point,
                        starts[enemy])
                    : bots.SpawnBots(faction, settings.BotsPerSide, starts[faction], settings.Objective);
                foreach (var bot in sides[faction])
                {
                    spawnPoints[bot] = xformSys.GetWorldPosition(bot);
                }
            }
        });

        var down = new HashSet<EntityUid>();
        var fightCells = new Dictionary<(int, int), int>();
        var start = timing.CurTime;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var maxTime = TimeSpan.FromMinutes(settings.MaxMinutes);
        var tickRate = (int) timing.TickRate;

        while (true)
        {
            await server.WaitRunTicks(tickRate);
            var gameTime = (timing.CurTime - start).TotalSeconds;
            var finished = false;

            await server.WaitAssertion(() =>
            {
                foreach (var (faction, members) in sides)
                {
                    foreach (var bot in members)
                    {
                        if (down.Contains(bot))
                            continue;

                        var pos = xformSys.GetWorldPosition(bot);
                        if (entMan.Deleted(bot) || !mobState.IsAlive(bot))
                        {
                            down.Add(bot);
                            result.FirstDeathSeconds ??= gameTime;
                            result.Deaths.Add(new Death { Faction = faction, Time = gameTime, X = pos.X, Y = pos.Y });
                            AddCell(fightCells, pos, 3);
                            continue;
                        }

                        if (bots.IsEngaged(bot))
                        {
                            result.FirstContactSeconds ??= gameTime;
                            AddCell(fightCells, pos, 1);
                        }
                    }
                }

                var aliveRepublic = sides[Republic].Count(b => !down.Contains(b));
                var aliveEmpire = sides[Empire].Count(b => !down.Contains(b));
                if (aliveRepublic == 0 || aliveEmpire == 0)
                {
                    result.EndReason = "elimination";
                    finished = true;
                }
                else if (timing.CurTime - start >= maxTime)
                {
                    result.EndReason = "time limit";
                    finished = true;
                }

                if (!finished)
                    return;

                foreach (var (faction, members) in sides)
                {
                    var walked = members.Where(b => !entMan.Deleted(b))
                        .Select(b => (xformSys.GetWorldPosition(b) - spawnPoints[b]).Length())
                        .DefaultIfEmpty(0f)
                        .Average();
                    result.MeanDistanceWalked[faction] = MathF.Round(walked, 1);
                }
            });

            if (finished)
                break;
        }

        stopwatch.Stop();
        result.GameSeconds = Math.Round((timing.CurTime - start).TotalSeconds, 1);
        result.RealSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
        result.Speedup = Math.Round(result.GameSeconds / Math.Max(result.RealSeconds, 0.001), 2);

        foreach (var faction in sides.Keys)
        {
            var alive = sides[faction].Count(b => !down.Contains(b));
            result.Alive[faction] = alive;
            result.Survival[faction] = Math.Round((double) alive / sides[faction].Count, 2);
            var enemy = faction == Republic ? Empire : Republic;
            result.Kills[faction] = sides[enemy].Count(down.Contains);
        }

        result.Winner = result.Alive[Republic] == result.Alive[Empire]
            ? "draw"
            : result.Alive[Republic] > result.Alive[Empire] ? Republic : Empire;
        result.FightCells = fightCells.OrderByDescending(c => c.Value)
            .Take(5)
            .Select(c => $"x {c.Key.Item1 * CellSize}..{(c.Key.Item1 + 1) * CellSize}, " +
                         $"y {c.Key.Item2 * CellSize}..{(c.Key.Item2 + 1) * CellSize}: {c.Value}")
            .ToList();

        await server.WaitPost(() => entMan.DeleteEntity(mapUid));
        await server.WaitRunTicks(5);
        return result;
    }

    /// <summary>
    /// Searches outwards from the middle between the bases for a strip of open ground: both start points and three
    /// parallel lines between them free of walls, so both squads see each other at once.
    /// </summary>
    private static (MapCoordinates Republic, MapCoordinates Empire)? FindArena(
        SharedInteractionSystem interaction,
        MapCoordinates republicBase,
        MapCoordinates empireBase,
        float gap)
    {
        var mapId = republicBase.MapId;
        var mid = (republicBase.Position + empireBase.Position) / 2;
        var along = Vector2.Normalize(empireBase.Position - republicBase.Position);
        var across = new Vector2(-along.Y, along.X);

        for (var radius = 0; radius <= 120; radius += 4)
        {
            var steps = radius == 0 ? 1 : 12;
            for (var i = 0; i < steps; i++)
            {
                var angle = MathF.Tau * i / steps;
                var centre = mid + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                foreach (var dir in new[] { along, across })
                {
                    var side = new Vector2(-dir.Y, dir.X);
                    var open = true;
                    foreach (var shift in new[] { -2f, 0f, 2f })
                    {
                        var from = new MapCoordinates(centre - dir * gap / 2 + side * shift, mapId);
                        var to = new MapCoordinates(centre + dir * gap / 2 + side * shift, mapId);
                        if (!interaction.InRangeUnobstructed(from, to, gap + 1, CollisionGroup.Impassable))
                        {
                            open = false;
                            break;
                        }
                    }

                    if (open)
                    {
                        return (new MapCoordinates(centre - dir * gap / 2, mapId),
                            new MapCoordinates(centre + dir * gap / 2, mapId));
                    }
                }
            }
        }

        return null;
    }

    private static void AddCell(Dictionary<(int, int), int> cells, Vector2 pos, int weight)
    {
        var key = ((int) MathF.Floor(pos.X / CellSize), (int) MathF.Floor(pos.Y / CellSize));
        cells[key] = cells.GetValueOrDefault(key) + weight;
    }

    private static object Summarize(Settings settings, List<PassResult> results)
    {
        double? Mean(Func<PassResult, double?> f)
        {
            var values = results.Select(f).Where(v => v != null).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 2);
        }

        return new
        {
            settings.Map,
            settings.Mode,
            Objective = settings.Mode == "arena" ? nameof(SWBotObjective.Point) : settings.Objective.ToString(),
            settings.Passes,
            settings.BotsPerSide,
            Wins = new
            {
                Republic = results.Count(r => r.Winner == Republic),
                Empire = results.Count(r => r.Winner == Empire),
                Draw = results.Count(r => r.Winner == "draw"),
            },
            EndedByElimination = results.Count(r => r.EndReason == "elimination"),
            MeanGameSeconds = Mean(r => r.GameSeconds),
            MeanFirstContactSeconds = Mean(r => r.FirstContactSeconds),
            MeanKillsRepublic = Mean(r => r.Kills[Republic]),
            MeanKillsEmpire = Mean(r => r.Kills[Empire]),
            MeanSurvivalRepublic = Mean(r => r.Survival[Republic]),
            MeanSurvivalEmpire = Mean(r => r.Survival[Empire]),
            MeanSpeedup = Mean(r => r.Speedup),
            TotalRealSeconds = Math.Round(results.Sum(r => r.RealSeconds), 1),
        };
    }

    private static string ToJson(object value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }
}
