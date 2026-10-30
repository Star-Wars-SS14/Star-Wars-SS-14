#nullable enable
using System.Linq;
using Content.Server.Station.Systems;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Medical.Healing;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Blasters deal Heat, so every medic must start with something that heals Heat.
/// </summary>
[TestFixture]
public sealed class SWMedicKitTest
{
    private static readonly ProtoId<StartingGearPrototype>[] MedicGear =
    [
        "EmpireMedicalGear",
        "SobrikMedicGear",
        "RepublicMedicGear",
        "RepublicCommanderMedicGear",
    ];

    [Test]
    public async Task MedicsStartWithHeatHealing()
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
            foreach (var gear in MedicGear)
            {
                var medic = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
                spawning.EquipStartingGear(medic, gear);

                Assert.That(inventory.TryGetSlotEntity(medic, "back", out var back), $"{gear} has no backpack.");
                var stored = entMan.GetComponent<StorageComponent>(back!.Value).Container.ContainedEntities;
                var kit = stored.FirstOrDefault(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "SWFirstAidKitFilled");
                Assert.That(kit, Is.Not.EqualTo(default(EntityUid)), $"{gear} has no first aid kit in the backpack.");

                var contents = entMan.GetComponent<StorageComponent>(kit).Container.ContainedEntities;
                Assert.That(contents.Any(e => entMan.TryGetComponent<HealingComponent>(e, out var healing)
                                              && healing.Damage.DamageDict.TryGetValue("Heat", out var heat)
                                              && heat < 0),
                    $"The kit of {gear} heals no Heat.");

                entMan.DeleteEntity(medic);
            }
        });

        await pair.CleanReturnAsync();
    }
}
