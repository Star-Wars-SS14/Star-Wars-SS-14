#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._SW.Droids;
using Content.Shared._SW.Droids;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// A defence droid shut in a room reaches its commander through the door on "follow" and "gather".
/// The GlobalWar maps use CM airlocks, whose access reader is empty: pathfinding counted them as locked.
/// </summary>
[TestFixture]
public sealed class SWDroidDoorPathTest
{
    // Follow: the commander stands 16 tiles from the door. Gather: the console is 40 tiles from it.
    [TestCase(SWDroidOrderType.Follow, 24)]
    [TestCase(SWDroidOrderType.GoTo, 48)]
    public async Task DroidLeavesClosedRoomThroughDoor(SWDroidOrderType order, int targetX)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        var transform = server.System<SharedTransformSystem>();
        EntityUid droid = default;
        EntityUid commander = default;

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid;
            var floor = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -2; x <= targetX + 2; x++)
            for (var y = -2; y <= 10; y++)
                tiles.Add((new Vector2i(x, y), floor));
            server.System<SharedMapSystem>().SetTiles(grid, grid.Comp, tiles);

            // A closed 7x7 room with one airlock in its east wall.
            for (var x = 0; x <= 8; x++)
            for (var y = 0; y <= 8; y++)
            {
                if (x is > 0 and < 8 && y is > 0 and < 8)
                    continue;
                var at = new EntityCoordinates(grid, x + 0.5f, y + 0.5f);
                if (x == 8 && y == 4)
                {
                    var door = entities.SpawnEntity("CMAirlock", at);
                    server.System<SharedPowerReceiverSystem>().SetNeedsPower(door, false);
                }
                else
                {
                    entities.SpawnEntity("WallSolid", at);
                }
            }

            droid = entities.SpawnEntity("SWGlobalWarDroidRepublic", new EntityCoordinates(grid, 4.5f, 4.5f));
            commander = entities.SpawnEntity("MobHuman", new EntityCoordinates(grid, targetX + 0.5f, 4.5f));
        });
        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SWDroidCommandableComponent>(droid).Faction, Is.EqualTo("RepublicFaction"));
            Assert.That(server.System<SWDroidCommandSystem>().TrySetDefenceOrder(droid, commander, "RepublicFaction", 100,
                order, entities.GetComponent<TransformComponent>(commander).Coordinates), Is.True);
        });

        var distance = float.MaxValue;
        for (var second = 0; second < 60 && distance > 3f; second++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() => distance = Vector2.Distance(transform.GetWorldPosition(droid),
                transform.GetWorldPosition(commander)));
        }

        Assert.That(distance, Is.LessThanOrEqualTo(3f), $"The droid is still {distance:0.0} m away on {order}.");
        await pair.CleanReturnAsync();
    }
}
