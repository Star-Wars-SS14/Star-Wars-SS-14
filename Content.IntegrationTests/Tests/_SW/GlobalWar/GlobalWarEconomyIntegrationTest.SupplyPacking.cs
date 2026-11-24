using System;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyPaymentOverflowRestoresExactPackedMagazineLocations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var markets = server.System<SWGlobalWarMarketSystem>();
            var ledger = server.System<SWGlobalWarLedgerSystem>();
            var clock = server.System<SWGlobalWarCampaignSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var containers = server.System<SharedContainerSystem>();
            var storageSystem = server.System<SharedStorageSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Magazine;
                // Packing rollback is independent of the separately tested 75/25 output allocation.
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                Assert.That(containers.TryGetContainer(arsenal, "sw-program-supply", out var receiving), Is.True);
                var wire = server.EntMan.SpawnEntity("SWAdvancedCables10", position);
                Assert.That(containers.Insert(wire, source!), Is.True);
                var production = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 2);
                Assert.That(markets.TryReserveSupplyProduction(production, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, production.Operation), Is.True);
                var magazines = Enumerable.Range(0, 2).Select(_ => server.EntMan.SpawnEntity("SWH206Magazine",
                    markets.SupplyProductionOutputCoordinates(production.Operation))).ToArray();
                foreach (var magazine in magazines)
                    server.System<SharedBatterySystem>().SetCharge(magazine, 0);
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(1200) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                markets.Update(0);
                Assert.That(markets.TryCompleteSupplyProduction(generation, production.Operation, magazines), Is.True);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    faction, factory, arsenal, kind, 2);
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                var bag = server.EntMan.SpawnEntity("ClothingBackpack", position);
                var storage = server.EntMan.GetComponent<StorageComponent>(bag);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, bag, magazines), Is.True);
                // Preserve the collection receipt while arranging the same actual items through the normal storage API.
                foreach (var magazine in magazines)
                    Assert.That(containers.Insert(magazine, source!), Is.True);
                var first = new ItemStorageLocation(Angle.FromDegrees(-90), new Vector2i(1, 0));
                var second = new ItemStorageLocation(Angle.FromDegrees(-90), new Vector2i(0, 1));
                Assert.That(storageSystem.InsertAt((bag, storage), (magazines[0], null), first, out _,
                    playSound: false, stackAutomatically: false), Is.True);
                Assert.That(storageSystem.InsertAt((bag, storage), (magazines[1], null), second, out _,
                    playSound: false, stackAutomatically: false), Is.True);
                for (var y = 0; y <= 3; y++)
                for (var x = 0; x <= 6; x++)
                {
                    if (y == 0 && x is 1 or 2 || y == 1 && x is 0 or 1)
                        continue;
                    var filler = server.EntMan.SpawnEntity("Pen", position);
                    Assert.That(storageSystem.InsertAt((bag, storage), (filler, null),
                        new ItemStorageLocation(Angle.Zero, new Vector2i(x, y)), out _,
                        playSound: false, stackAutomatically: false), Is.True, $"Filler cell ({x},{y}) must be free.");
                }
                Assert.That(storage.SavedLocations, Is.Empty);
                var original = storage.StoredItems.ToDictionary();
                var originalEntities = storage.Container.ContainedEntities.ToArray();
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War);
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Credit, faction, SWGlobalWarAccount.Industry,
                    long.MaxValue - industry.Balance, Purpose: "packed-supply-fixture"), out _), Is.True);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                    accounts.Comp.Revision, magazines), Is.False);
                Assert.That(storage.Container.ContainedEntities, Is.EquivalentTo(originalEntities));
                Assert.That(storage.StoredItems, Is.EquivalentTo(original));
                Assert.That(storage.StoredItems[magazines[0]], Is.EqualTo(first));
                Assert.That(storage.StoredItems[magazines[1]], Is.EqualTo(second));
                Assert.That(receiving!.ContainedEntities, Is.Empty);
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(2));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War), Is.EqualTo(war));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(long.MaxValue));
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(storage.Container.ContainedEntities, Is.EquivalentTo(originalEntities));
                Assert.That(storage.StoredItems, Is.EquivalentTo(original));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
            }
            finally
            {
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        });
        await pair.CleanReturnAsync();
    }
}
