using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyAutomaticSendsFarmOutputToTheCapitalBeforeItsOwnArsenal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var savedTime = timing.TimeBase;
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWSpawnTestSupplyTransportRule"), Is.True);
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            var generation = campaign.Comp.Generation;
            var model = markets.Simulation!;
            const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
            const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
            model.SupplyParticipants = _ => 20;
            model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
            var factory = Store("SWSpawnTestSupplyTransportFarm", false);
            // The farm's own arsenal registers first, like a map that happens to bind before the capital's.
            Store("SWSpawnTestSupplyTransportFarm", true);
            var capital = Store("SWSpawnTestSupplyTransportHome", true);
            var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
            Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
            Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
            timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(451) - clock.Elapsed(campaign), timing.TimeBase.Item2);
            markets.Update(0);
            var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
            Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
            Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.True);
            timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(1201) - clock.Elapsed(campaign), timing.TimeBase.Item2);
            markets.Update(0);
            var purchases = (Dictionary<Guid, SWGlobalWarSupplyPurchase>) typeof(SWGlobalWarMarketSystem)
                .GetField("_supplyPurchases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(markets)!;
            Assert.That(purchases.Values.Single().Request.Recipient, Is.EqualTo(capital));

            EntityUid Store(string world, bool isArsenal)
            {
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, new Vector2(isArsenal ? 8 : 0, 0)));
                Assert.That(markets.TryRegisterSupplyStore(generation, store, isArsenal), Is.True);
                return store;
            }
        });
        await server.WaitPost(() =>
        {
            if (clock.TryGetCampaign(out var campaign))
                ticker.EndGameRule(campaign.Owner);
            timing.TimeBase = savedTime;
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyRemoteAutomaticWaitsWithoutPurchaseReservationsAndUsesOnlyCurrentWindow(bool nextWindow)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        using var scenario = RunScenario().GetEnumerator();
        var more = true;
        while (more)
        {
            await server.WaitAssertion(() => more = scenario.MoveNext());
            if (more)
                await pair.RunTicksSync(1);
        }
        await pair.CleanReturnAsync();

        IEnumerable<bool> RunScenario()
        {
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyTransportRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                model.SupplyParticipants = _ => 20;
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store("SWSpawnTestSupplyTransportFarm", false);
                var arsenal = Store("SWSpawnTestSupplyTransportHome", true);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                Advance(451);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Advance(1199);
                Change(SWGlobalWarFaction.Empire);
                Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.True);
                yield return true;
                Advance(1200);
                var first = markets.SupplyIntents.Single(i => i.Faction == faction && i.Kind == kind && i.Window == 1);
                Assert.That(first.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.TransportRoute));
                Assert.That(first.Orders, Is.Empty);
                Assert.That(model.SupplyPrograms[faction].Orders, Is.Empty);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                var places = (Dictionary<(EntityUid Store, SWGlobalWarSupplyKind Kind), int>) typeof(SWGlobalWarMarketSystem)
                    .GetField("_supplyPlaces", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(markets)!;
                Assert.That(places.GetValueOrDefault((arsenal, kind)), Is.Zero);
                Assert.That(markets.GetSupplyProductionWork(generation, factory).Single().Quantity, Is.EqualTo(5),
                    "Independent base production may reserve one recipe while procurement waits for its route.");
                var rawReceipts = model.ExternalReservationCount;
                var restoreAt = nextWindow ? 2500 : 1300;
                Advance(restoreAt);
                Assert.That(model.ExternalReservationCount, Is.EqualTo(rawReceipts));
                Assert.That(model.SupplyPrograms[faction].Orders, Is.Empty);
                Assert.That(model.Shipments.Any(s => s.SupplyManifest != null), Is.False);
                Assert.That(places.GetValueOrDefault((arsenal, kind)), Is.Zero);
                foreach (var entity in goods)
                {
                    Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                    Assert.That(item.Order, Is.Null);
                }
                var window = nextWindow ? 2 : 1;
                var current = markets.SupplyIntents.Single(i => i.Faction == faction && i.Kind == kind && i.Window == window);
                Assert.That(model.SupplyPrograms[faction].RemainingQuota(kind), Is.EqualTo(8));
                Assert.That(current.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.TransportRoute));
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Debit, faction, SWGlobalWarAccount.War,
                    ledger.GetBalance(faction, SWGlobalWarAccount.War).Available - 750,
                    Purpose: "remote-automatic-finite-budget-fixture"), out _), Is.True);
                Change(faction);
                yield return true;
                Advance(restoreAt + 1);
                Assert.That(current.Orders, Has.Count.EqualTo(1));
                if (nextWindow)
                    Assert.That(first.Orders, Is.Empty, "An expired intent must not consume the new window's decision.");
                var order = model.SupplyPrograms[faction].Orders.Values.Single();
                Assert.That(order.Window, Is.EqualTo(window));
                Assert.That(order.Quantity, Is.EqualTo(5));
                Assert.That(order.Outstanding, Is.EqualTo(5));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(750));
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                Advance(restoreAt + 10);
                Assert.That(order.Accepted, Is.EqualTo(5));
                Assert.That(order.Outstanding, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 750));
                Assert.That(current.Orders, Has.Count.EqualTo(1));
                Assert.That(model.SupplyPrograms[faction].RemainingQuota(kind), Is.EqualTo(3));

                EntityUid Store(string world, bool isArsenal)
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, isArsenal), Is.True);
                    return store;
                }
                void Change(SWGlobalWarFaction owner)
                {
                    Assert.That(planets.TryGetPlanet("SWSpawnTestSupplyTransportHome", out var planet), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                    var steps = server.System<SWGlobalWarCampaignStepSystem>();
                    Assert.That(steps.TryQueueOwnership(planet.Value, SWGlobalWarPlanetLocation.Orbit, owner,
                        state.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
                void Advance(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                }
            }
            finally
            {
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        }
    }
}
