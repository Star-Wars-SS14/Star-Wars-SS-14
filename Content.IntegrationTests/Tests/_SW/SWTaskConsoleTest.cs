#nullable enable
using Content.Server._SW.TaskSystem;
using Content.Server.GameTicking;
using Content.Shared._SW.TaskSystem;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWTaskConsoleTest
{
    private const string Republic = "RepublicFaction";
    private const string Mandalorian = "MandalorianFaction";

    [TestCase("SWTaskConsoleRepublic", Republic, "SWRepublicGeneral", true)]
    [TestCase("SWTaskConsoleRepublic", Republic, "SWRepublicLieutenant", true)]
    [TestCase("SWTaskConsoleRepublic", Republic, "SWJedi", true)]
    [TestCase("SWTaskConsoleRepublic", Republic, "SWRepublicCommanderMedic", false)]
    [TestCase("SWTaskConsoleRepublic", Republic, "SWRepublicSoldier", false)]
    [TestCase("SWTaskConsoleRepublic", "EmpireFaction", "SWEmpireMoff", false)]
    [TestCase("SWTaskConsoleEmpire", "EmpireFaction", "SWEmpireLieutenant", true)]
    [TestCase("SWTaskConsoleEmpire", "EmpireFaction", "SWSobrikOfficer", false)]
    [TestCase("SWTaskConsoleMandalorian", Mandalorian, "SWMandalorianLeader", true)]
    [TestCase("SWTaskConsoleMandalorian", Mandalorian, "SWMandalorianWarrior", false)]
    [TestCase("SWTaskConsoleBlackSun", "BlackSunFaction", "SWBlackSun", true)]
    [TestCase("SWTaskConsoleBlackSun", "BlackSunFaction", "SWBlackSunMercenary", false)]
    [TestCase("SWTaskConsoleExchange", "ExchangeFaction", "SWExchangeCaptain", true)]
    [TestCase("SWTaskConsoleExchange", "ExchangeFaction", "SWExchangeOperative", false)]
    public async Task OnlyOfficersOpen(string consoleProto, string faction, string job, bool allowed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity(consoleProto, map.GridCoords);
            var comp = entMan.GetComponent<TaskConsoleComponent>(console);

            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var member = entMan.EnsureComponent<SWFactionMemberComponent>(user);
            member.Faction = faction;
            member.Job = job;

            Assert.That(server.System<SWSharedTaskConsoleystem>().IsOfficer(user, (console, comp)), Is.EqualTo(allowed));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RankThresholdComesFromComponent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var console = entMan.SpawnEntity("SWTaskConsoleRepublic", map.GridCoords);
            var comp = entMan.GetComponent<TaskConsoleComponent>(console);
            comp.MinOfficerRank = 70;

            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var member = entMan.EnsureComponent<SWFactionMemberComponent>(user);
            member.Faction = Republic;
            member.Job = "SWRepublicLieutenant";

            Assert.That(server.System<SWSharedTaskConsoleystem>().IsOfficer(user, (console, comp)), Is.False,
                "Rank 60 is below the threshold of 70");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StateIsPushedOnOpenAndOnChange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var ui = server.System<UserInterfaceSystem>();
        var tasks = server.System<SWTasksGameRuleSystem>();
        var console = EntityUid.Invalid;
        var tasksOnOpen = 0;

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("FactionsOnlyGamerule");
            tasks.AddTask(Republic, "SWTaskPreserveCell");

            console = entMan.SpawnEntity("SWTaskConsoleRepublic", map.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(ui.TryGetUiState<TaskConsoleBounderUserInterfaceState>(console, TaskConsoleUiKey.Key, out _), Is.False);

            ui.OpenUi(console, TaskConsoleUiKey.Key, user);
            Assert.That(ui.TryGetUiState<TaskConsoleBounderUserInterfaceState>(console, TaskConsoleUiKey.Key, out var state));
            Assert.That(state!.Tasklist, Is.Not.Empty);
            tasksOnOpen = state.Tasklist.Count;

            tasks.AddTask(Republic, "SWTaskSabotageFactory");
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<TaskConsoleBounderUserInterfaceState>(console, TaskConsoleUiKey.Key, out var state));
            Assert.That(state!.Tasklist, Has.Count.GreaterThan(tasksOnOpen), "An open console must follow new tasks");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("sw-task-console-title")]
    [TestCase("sw-task-console-summary")]
    [TestCase("sw-task-console-percent")]
    [TestCase("sw-task-console-score")]
    [TestCase("sw-task-console-reward")]
    [TestCase("sw-task-console-officers-only")]
    public async Task TextsExist(string key)
    {
        await using var pair = await PoolManager.GetServerClient();
        Assert.That(pair.Server.ResolveDependency<ILocalizationManager>().HasString(key), key);
        await pair.CleanReturnAsync();
    }
}
