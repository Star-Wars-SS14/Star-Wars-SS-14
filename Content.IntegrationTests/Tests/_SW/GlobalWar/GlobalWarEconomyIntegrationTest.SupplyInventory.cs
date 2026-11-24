using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplySuitabilityChecksActualMedicineMagazinesAndRationContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var markets = server.System<SWGlobalWarMarketSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var containers = server.System<SharedContainerSystem>();
            var map = server.System<SharedMapSystem>().CreateMap(out var mapId);
            var position = new MapCoordinates(0, 0, mapId);
            const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
            var kolto = server.EntMan.SpawnEntity("SWKoltoMedipen", position);
            Assert.That(markets.IsSuitableSupplyItem(kolto, SWGlobalWarSupplyKind.Medicine, faction), Is.True);
            Assert.That(solutions.TryGetSolution(kolto, "pen", out var pen), Is.True);
            solutions.RemoveAllSolution(pen!.Value);
            Assert.That(solutions.TryAddReagent(pen.Value, "SWKolto", 14), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(kolto, SWGlobalWarSupplyKind.Medicine, faction), Is.False);
            solutions.RemoveAllSolution(pen.Value);
            Assert.That(solutions.TryAddReagent(pen.Value, "Water", 15), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(kolto, SWGlobalWarSupplyKind.Medicine, faction), Is.False);
            solutions.RemoveAllSolution(pen.Value);
            Assert.That(markets.IsSuitableSupplyItem(kolto, SWGlobalWarSupplyKind.Medicine, faction), Is.False);
            var bacta = server.EntMan.SpawnEntity("SWBactaMedipen", position);
            Assert.That(markets.IsSuitableSupplyItem(bacta, SWGlobalWarSupplyKind.Medicine, faction), Is.False);
            Assert.That(markets.IsSuitableSupplyItem(bacta, SWGlobalWarSupplyKind.Medicine, faction, true), Is.True);
            var wrongShell = server.EntMan.SpawnEntity("ChemicalMedipen", position);
            Assert.That(solutions.TryGetSolution(wrongShell, "pen", out var shell), Is.True);
            solutions.RemoveAllSolution(shell!.Value);
            Assert.That(solutions.TryAddReagent(shell.Value, "SWKolto", 15), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(wrongShell, SWGlobalWarSupplyKind.Medicine, faction), Is.False);

            var magazine = server.EntMan.SpawnEntity("SWH206Magazine", position);
            server.System<SharedBatterySystem>().SetCharge(magazine, 0);
            Assert.That(markets.IsSuitableSupplyItem(magazine, SWGlobalWarSupplyKind.Magazine, faction), Is.True);
            server.System<SharedBatterySystem>().SetCharge(magazine, 300);
            Assert.That(markets.IsSuitableSupplyItem(magazine, SWGlobalWarSupplyKind.Magazine, faction), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(magazine, SWGlobalWarSupplyKind.Magazine, SWGlobalWarFaction.Empire), Is.False);
            foreach (var prototype in new[] { "SWRepublicSuit", "SWImperialSuit" })
            {
                var armor = server.EntMan.SpawnEntity(prototype, position);
                Assert.That(markets.IsSuitableSupplyItem(armor, SWGlobalWarSupplyKind.Armor, faction), Is.True);
                Assert.That(markets.IsSuitableSupplyItem(armor, SWGlobalWarSupplyKind.Armor, SWGlobalWarFaction.Empire), Is.True);
            }

            var ration = server.EntMan.SpawnEntity("SWBoxMRE", position);
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.True);
            Assert.That(containers.TryGetContainer(ration, "storagebase", out var contents), Is.True);
            var flask = contents!.ContainedEntities.Single(e => server.EntMan.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID == "DrinkMREFlask");
            Assert.That(solutions.TryGetSolution(flask, "drink", out var drink), Is.True);
            solutions.RemoveAllSolution(drink!.Value);
            Assert.That(solutions.TryAddReagent(drink.Value, "Water", 1), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.False,
                "The right flask prototype with a partly consumed drink is not a complete ration.");
            server.EntMan.DeleteEntity(flask);
            var fullFlask = server.EntMan.SpawnEntity("DrinkMREFlask", position);
            Assert.That(containers.Insert(fullFlask, contents), Is.True);
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.True);
            var food = contents.ContainedEntities.Single(e => server.EntMan.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID == "SWCannedGorntMeat");
            Assert.That(solutions.TryGetSolution(food, "food", out var foodSolution), Is.True);
            solutions.RemoveAllSolution(foodSolution!.Value);
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.False);
            server.EntMan.DeleteEntity(food);
            var nested = server.EntMan.SpawnEntity("SWBoxMRE", position);
            Assert.That(containers.Insert(nested, contents), Is.False, "Normal inventory rejects nesting another full MRE box.");
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.False);
            foreach (var item in contents.ContainedEntities.ToArray())
                server.EntMan.DeleteEntity(item);
            Assert.That(markets.IsSuitableSupplyItem(ration, SWGlobalWarSupplyKind.Ration, faction), Is.False);
            server.EntMan.DeleteEntity(map);
        });
        await pair.CleanReturnAsync();
    }
}
