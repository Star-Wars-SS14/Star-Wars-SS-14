using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.DesignTesting.Commands;
using Content.Server._SW.Economy.BankSystem;
using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
[TestOf(typeof(SWTaskCommand))]
[TestOf(typeof(SWScoreCommand))]
[TestOf(typeof(SWMoneyCommand))]
public sealed class SWDesignCommandsTest
{
    [Test]
    public async Task AddCompleteScoreAndMoney()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tasks = server.System<SWTasksGameRuleSystem>();
        var factions = server.System<SWFactionGameruleSystem>();
        var bank = server.System<SWBankSystem>();
        var console = server.ResolveDependency<IConsoleHost>();

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("TaskGamerule");
            ticker.StartRound();
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var republic = factions.GetFactionEntity("RepublicFaction")!.Value;
            var supplyBefore = bank.GetFactionAccount("RepublicFaction").Comp.CurrentAmount
                .GetValueOrDefault("SWSupplyScoresRepublic");
            Assert.That(tasks.GetLiveScore(republic), Is.Zero);

            // A steal task has no target requirement, so it is always given.
            console.ExecuteCommand("swtask add RepublicFaction StealIdCardsEmpire");
            var list = tasks.GetFactionTasks("RepublicFaction");
            var number = list.FindIndex(t => server.EntMan.GetComponent<MetaDataComponent>(t).EntityPrototype?.ID
                                             == "StealIdCardsEmpire") + 1;
            Assert.That(number, Is.GreaterThan(0), "swtask add did not give the task.");
            Assert.That(list[number - 1].Comp.Completeness.Float(), Is.Zero);

            console.ExecuteCommand($"swtask complete RepublicFaction {number}");
            Assert.That(list[number - 1].Comp.Completeness.Float(), Is.EqualTo(1f));
            Assert.That(tasks.GetLiveScore(republic), Is.EqualTo(15));

            var supplyAfter = bank.GetFactionAccount("RepublicFaction").Comp.CurrentAmount
                .GetValueOrDefault("SWSupplyScoresRepublic");
            Assert.That(supplyAfter - supplyBefore, Is.EqualTo(1000f), "Completing the task paid no reward.");

            // Republic leads 15 to 0, so it wins whether or not it completed every task.
            Assert.That(tasks.GetWinners(liveScore: true).Select(w => w.Owner), Does.Contain(republic.Owner));

            console.ExecuteCommand("swmoney EmpireFaction SWSupplyScoresEmpire 3000");
            Assert.That(bank.GetFactionAccount("EmpireFaction").Comp.CurrentAmount["SWSupplyScoresEmpire"],
                Is.EqualTo(3000f));

            console.ExecuteCommand("swscore");
        });

        // The forced task must stay completed after the task rule recomputes progress every tick.
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var republic = factions.GetFactionEntity("RepublicFaction")!.Value;
            Assert.That(tasks.GetLiveScore(republic), Is.EqualTo(15));
            ticker.ClearGameRules();
        });

        await pair.CleanReturnAsync();
    }
}
