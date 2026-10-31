#nullable enable
using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Own turrets shoot a humanoid with no ID, so the card of a job must reach the id slot on every species.
/// </summary>
[TestFixture]
public sealed class SWJobIdCardTest
{
    [Test]
    public async Task EverySpeciesWearsItsJobCard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var spawning = entMan.System<StationSpawningSystem>();
        var inventory = entMan.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            var gears = protoMan.EnumeratePrototypes<JobPrototype>()
                .Where(job => job.StartingGear != null)
                .Select(job => job.StartingGear!.Value)
                .Distinct()
                // Without a jumpsuit the id slot of most species stays closed, and upstream gear leaves the uniform to the loadout.
                .Where(gear => protoMan.Index(gear).Equipment.ContainsKey("id") && protoMan.Index(gear).Equipment.ContainsKey("jumpsuit"))
                .ToList();
            Assert.That(gears, Is.Not.Empty);

            var species = protoMan.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).ToList();
            Assert.That(species.Select(s => s.ID), Does.Contain("Colicoid"));

            Assert.Multiple(() =>
            {
                foreach (var kind in species)
                {
                    foreach (var gear in gears)
                    {
                        var mob = entMan.SpawnEntity(kind.Prototype, testMap.GridCoords);
                        spawning.EquipStartingGear(mob, gear);
                        Assert.That(inventory.TryGetSlotEntity(mob, "id", out _), $"{kind.ID} in {gear} has no card.");
                        if (protoMan.Index(gear).Equipment.ContainsKey("belt"))
                            Assert.That(inventory.TryGetSlotEntity(mob, "belt", out _), $"{kind.ID} in {gear} has no belt.");
                        entMan.DeleteEntity(mob);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
