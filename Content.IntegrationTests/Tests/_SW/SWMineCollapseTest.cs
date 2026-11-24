#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._SW.Gamerules.RefillOre;
using Content.Server.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWMineCollapseTest
{
    private const string Rule = "SWMineCollapseGamerule";
    private const string Marker = "SWRockWithOrWithoutOreRespawn";
    private const string Rock = "SWWallRockMining";

    [Test]
    public async Task FirstFillIsSilentAndSkipsMobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var ticker = entMan.System<GameTicker>();
        var lookup = entMan.System<EntityLookupSystem>();

        var freeSpot = new MapCoordinates(new Vector2(40, 40), testMap.MapId);
        var mobSpot = new MapCoordinates(new Vector2(60, 60), testMap.MapId);
        var rule = EntityUid.Invalid;
        var mob = EntityUid.Invalid;

        bool HasRock(MapCoordinates coordinates)
        {
            return lookup.GetEntitiesInRange(coordinates, 0.3f)
                .Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == Rock);
        }

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity(Marker, freeSpot);
            entMan.SpawnEntity(Marker, mobSpot);
            mob = entMan.SpawnEntity("MobHuman", mobSpot);
            ticker.StartGameRule(Rule, out rule);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<SWRespawnEntitiesGameruleComponent>(rule);

            Assert.That(HasRock(freeSpot), "The first fill must stock an empty marker.");
            Assert.That(HasRock(mobSpot), Is.False, "A rock must not spawn on a mob.");

            // The announced collapse only comes one full delay after the start.
            Assert.That(comp.NextSpawn, Is.GreaterThan(timing.CurTime + comp.Delay / 2));
            Assert.That(comp.WarningDispatched, Is.False);

            comp.NextWarning = timing.CurTime;
            comp.NextSpawn = timing.CurTime;
            entMan.DeleteEntity(mob);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(HasRock(mobSpot), "The next collapse must fill the marker once the mob has left.");
            ticker.EndGameRule(rule);
        });

        await pair.CleanReturnAsync();
    }
}
