using System.Linq;
using Content.Server._SW.Gamerules.TimerSpeedup;
using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Shared._SW.TaskSystem;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
[TestOf(typeof(SWTimerSpeedupSystem))]
public sealed class SWTimerSpeedupTest
{
    private const string TestPreset = "SWTestPreset";
    private const string SpeedupRule = "SWTimerSpeedupGamerule";

    [Test]
    public async Task TestPresetIsHiddenAndFast()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        var preset = protoMan.Index<GamePresetPrototype>(TestPreset);
        Assert.Multiple(() =>
        {
            Assert.That(preset.ShowInVote, Is.False);
            Assert.That(preset.Rules.Select(r => r.Id), Does.Contain(SpeedupRule));
            Assert.That(preset.Rules.Select(r => r.Id), Does.Contain("TaskGamerule"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CvarAndRuleMultiply()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var ticker = server.System<GameTicker>();
        var speedup = server.System<SWTimerSpeedupSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(speedup.GetSpeedup(), Is.EqualTo(1f));

            cfg.SetCVar(SWVars.TimerSpeedup, 2f);
            Assert.That(speedup.GetSpeedup(), Is.EqualTo(2f));

            ticker.StartGameRule(SpeedupRule);
            Assert.That(speedup.GetSpeedup(), Is.EqualTo(20f));
            Assert.That(speedup.Scale(TimeSpan.FromMinutes(20)), Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(speedup.Scale(180f), Is.EqualTo(9f));

            cfg.SetCVar(SWVars.TimerSpeedup, 1f);
            ticker.ClearGameRules();
            Assert.That(speedup.GetSpeedup(), Is.EqualTo(1f));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Start tasks normally arrive at minute 3. With the x10 rule they must arrive within 18 seconds.
    /// </summary>
    [Test]
    public async Task StartTasksArriveTenTimesSooner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var factions = server.System<SWFactionGameruleSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule(SpeedupRule);
            ticker.StartGameRule("TaskGamerule");
            ticker.StartRound();
        });

        await server.WaitAssertion(() => Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound)));

        // 180 s / 10 = 18 s, plus one second of margin.
        await pair.RunTicksSync(timing.TickRate * 19);

        await server.WaitAssertion(() =>
        {
            var republic = factions.GetFactionEntity("RepublicFaction");
            Assert.That(republic, Is.Not.Null);
            var tasks = server.EntMan.GetComponent<SWTaskContainerComponent>(republic.Value);
            Assert.That(tasks.CurrentTasks, Is.Not.Empty, "Start tasks did not arrive within 19 s under x10.");
            ticker.ClearGameRules();
        });

        await pair.CleanReturnAsync();
    }
}
