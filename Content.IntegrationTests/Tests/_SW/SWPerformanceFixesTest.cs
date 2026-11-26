#nullable enable
using System.Linq;
using Content.Server._SW.Artillery;
using Content.Server._SW.Gamerules.Respawn;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Server.Temperature.Systems;
using Content.Shared._SW.Artillery;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Ghost;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWPerformanceFixesTest
{
    private const string Barrel = "SWPerformanceTestBarrel";

    [TestPrototypes]
    private static readonly string Prototypes = @$"
- type: entity
  id: {Barrel}
  components:
  - type: Temperature
    currentTemperature: 200
  - type: SWTriggerOnHighTemperature
    threshold: 270
  - type: DeleteOnTrigger
";

    [Test]
    public async Task RespawnActionOnlyForDeadPlayersAndOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var ghosts = server.System<GhostSystem>();
        var minds = server.System<MindSystem>();
        var roles = server.System<RoleSystem>();
        var actions = server.System<SharedActionsSystem>();

        EntityUid player = default, observer = default, admin = default;

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("SWRespawnGamerule", out var rule);
            entMan.GetComponent<SWRespawnGameruleComponent>(rule).DelayBeforeCanRespawn = 0;

            var playerMind = minds.CreateMind(null);
            player = ghosts.SpawnGhost((playerMind.Owner, playerMind.Comp), map.GridCoords)!.Value;

            var observerMind = minds.CreateMind(null);
            observer = ghosts.SpawnGhost((observerMind.Owner, observerMind.Comp), map.GridCoords)!.Value;
            roles.MindAddRole(observerMind.Owner, "MindRoleObserver");

            admin = entMan.SpawnEntity(GameTicker.AdminObserverPrototypeName, map.GridCoords);
            var adminMind = minds.CreateMind(null);
            minds.TransferTo(adminMind.Owner, admin);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponentOrNull<SWWaitingRespawnComponent>(player);
            Assert.That(comp?.RespawnActionId, Is.Not.Null, "A dead player's ghost gets the action");
            Assert.That(entMan.HasComponent<SWWaitingRespawnComponent>(observer), Is.False);
            Assert.That(entMan.HasComponent<SWWaitingRespawnComponent>(admin), Is.False);
        });

        var action = entMan.GetComponent<SWWaitingRespawnComponent>(player).RespawnActionId;
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SWWaitingRespawnComponent>(player).RespawnActionId, Is.EqualTo(action));
            var count = actions.GetActions(player).Count(a => a.Owner == action);
            Assert.That(count, Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeatingTriggersBarrel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var temperature = server.System<TemperatureSystem>();
        var barrel = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            barrel = entMan.SpawnEntity(Barrel, map.GridCoords);
            temperature.ForceChangeTemperature(barrel, 260);
        });

        await pair.RunTicksSync(2);
        Assert.That(entMan.Deleted(barrel), Is.False);

        await server.WaitPost(() => temperature.ForceChangeTemperature(barrel, 280));
        await pair.RunTicksSync(2);
        Assert.That(entMan.Deleted(barrel), Is.True);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArtilleryStateFollowsOnlyOpenConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var console = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        ArtilleryConsoleInterfaceState? closedState = null;

        await server.WaitPost(() =>
        {
            console = entMan.SpawnEntity("SWArtilleryConsole", map.GridCoords);
            user = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState(console, ArtilleryConsoleUiKey.Key, out closedState));
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<ArtilleryConsoleInterfaceState>(console, ArtilleryConsoleUiKey.Key, out var state));
            Assert.That(state, Is.SameAs(closedState), "A closed console must not send state");

            ui.OpenUi(console, ArtilleryConsoleUiKey.Key, user);
            Assert.That(ui.TryGetUiState(console, ArtilleryConsoleUiKey.Key, out closedState));
            Assert.That(closedState, Is.Not.SameAs(state), "Opening must refresh the state");
        });

        var opened = closedState;
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<ArtilleryConsoleInterfaceState>(console, ArtilleryConsoleUiKey.Key, out var state));
            Assert.That(state!.CountDownLeft, Is.LessThan(opened!.CountDownLeft), "An open console follows the countdown");
        });

        await pair.CleanReturnAsync();
    }
}
