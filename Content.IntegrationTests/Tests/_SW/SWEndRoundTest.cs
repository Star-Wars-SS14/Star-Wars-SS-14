using Content.Server._SW.EndRound;
using Content.Server.GameTicking;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
[TestOf(typeof(SWEndRoundGameruleSystem))]
public sealed class SWEndRoundTest
{
    /// <summary>
    /// The round ends when every member of Republic or Empire is down, and not before.
    /// </summary>
    [Test]
    public async Task RoundEndsByElimination()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var mobState = server.System<MobStateSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            ticker.StartGameRule("SWEndRoundGamerule", out var rule);
            entMan.GetComponent<SWEndRoundGameruleComponent>(rule).DelayBeforeFirstCheck = TimeSpan.Zero;
            ticker.StartRound();
        });

        EntityUid republican = default;
        EntityUid imperial = default;
        await server.WaitAssertion(() =>
        {
            republican = SpawnMember(entMan, map, "RepublicFaction");
            imperial = SpawnMember(entMan, map, "EmpireFaction");
        });

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "Round ended while both sides live.");
            mobState.ChangeMobState(imperial, MobState.Critical);
        });

        // Critical counts as not alive for the check.
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PostRound),
                "Round did not end after the last Empire member went down.");
            Assert.That(mobState.IsAlive(republican));
            ticker.ClearGameRules();
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnMember(IEntityManager entMan, TestMapData map, string faction)
    {
        var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
        entMan.EnsureComponent<SWFactionMemberComponent>(mob).Faction = faction;
        return mob;
    }
}
