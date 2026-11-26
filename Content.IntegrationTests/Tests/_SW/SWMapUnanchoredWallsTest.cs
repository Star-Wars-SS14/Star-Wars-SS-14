#nullable enable
using System.Linq;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// A wall saved with "anchored: False" is a heavy dynamic body: it does not seal air, and the physics solver
/// moves it around after every round start. It happens when the floor under a wall is removed in the mapper.
/// Reads the map files as text, because SW maps cannot be loaded in tests yet (see PostMapInitTest.GameMaps).
/// </summary>
[TestFixture]
public sealed class SWMapUnanchoredWallsTest
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    [Test]
    public async Task GameMapsHaveNoUnanchoredWalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var tags = server.ResolveDependency<IEntityManager>().System<TagSystem>();

        var maps = protoMan.EnumeratePrototypes<GameMapPrototype>()
            .Select(m => m.MapPath)
            .Where(p => p.ToString().StartsWith("/Maps/SW/"))
            .Distinct()
            .ToList();

        Assert.That(maps, Is.Not.Empty);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var map in maps)
                {
                    var proto = string.Empty;
                    using var reader = resources.ContentFileReadText(map);

                    while (reader.ReadLine() is { } line)
                    {
                        if (line.StartsWith("- proto: "))
                            proto = line["- proto: ".Length..].Trim();
                        else if (line.Trim() == "anchored: False")
                            Assert.That(IsWall(proto, protoMan, factory, tags), Is.False, $"{map}: unanchored {proto}.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static bool IsWall(string proto, IPrototypeManager protoMan, IComponentFactory factory, TagSystem tags)
    {
        return protoMan.TryIndex<EntityPrototype>(proto, out var entity)
               && entity.TryGetComponent<TagComponent>(out var tagComp, factory)
               && tags.HasTag(tagComp, WallTag);
    }
}
