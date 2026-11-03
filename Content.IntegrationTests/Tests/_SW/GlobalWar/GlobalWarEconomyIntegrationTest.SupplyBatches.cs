using System;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyDistributionRemainderSurvivesNewOperationsAndOnlyAdvancesOnOutput()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var markets = server.System<SWGlobalWarMarketSystem>();
            var clock = server.System<SWGlobalWarCampaignSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var factory = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                var line = markets.Simulation!.SupplyPrograms[faction].Lines[kind];
                var request = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(request, out var cancelled), Is.True);
                Assert.That(cancelled.MarketQuantity, Is.EqualTo(3));
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(line.MarketRemainder, Is.Zero);
                var units = new System.Collections.Generic.HashSet<Guid>();
                for (var batch = 0; batch < 2; batch++)
                {
                    request = request with { Operation = Guid.NewGuid() };
                    Assert.That(markets.TryReserveSupplyProduction(request, out var production), Is.True);
                    Assert.That(production.MarketQuantity, Is.EqualTo(batch == 0 ? 3 : 4));
                    Assert.That(production.MarketRemainderBefore, Is.EqualTo(batch == 0 ? 0m : 0.75m));
                    Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                    var output = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                        markets.SupplyProductionOutputCoordinates(request.Operation))).ToArray();
                    Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, output), Is.False);
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds((batch + 1) * 451) -
                        clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                    Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, output), Is.True);
                    Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, output), Is.True);
                    Assert.That(line.MarketRemainder, Is.EqualTo(batch == 0 ? 0.75m : 0.5m));
                    var marketCount = 0;
                    foreach (var entity in output)
                    {
                        Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                        Assert.That(units.Add(item.Unit), Is.True);
                        Assert.That(item.Source, Is.EqualTo(request.Operation));
                        Assert.That(item.Payment, Is.Null);
                        Assert.That(item.Origin, Is.EqualTo(SWGlobalWarSupplyOrigin.Industrial));
                        if (item.Market)
                            marketCount++;
                    }
                    Assert.That(marketCount, Is.EqualTo(batch == 0 ? 3 : 4));
                }
                Assert.That(units, Has.Count.EqualTo(10));
                request = request with { Operation = Guid.NewGuid() };
                Assert.That(markets.TryReserveSupplyProduction(request, out var explicitlyStopped), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(explicitlyStopped.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Cancelled));
                Assert.That(line.MarketRemainder, Is.EqualTo(0.5m), "Explicit production cancellation does not claim a fractional output.");
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

    [Test]
    public async Task SupplyMedicinePurchaseReservesWholeBatchesAndKeepsUnpaidSurplus()
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
            var transform = server.System<SharedTransformSystem>();
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
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string world = "SWSpawnTestSupplySource";
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var destination = new EntityCoordinates(map.Value, new Vector2(10, 0));
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, destination);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                // A complete 20-item initial output leaves the default line's allocation remainder exactly zero.
                var first = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 4);
                Assert.That(markets.TryReserveSupplyProduction(first, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, first.Operation), Is.True);
                Advance(2400);
                var initial = Output(first.Operation, 20);
                Assert.That(markets.TryCompleteSupplyProduction(generation, first.Operation, initial), Is.True);
                // All but two market units have physically left this factory before procurement begins.
                foreach (var issued in initial[2..])
                {
                    Assert.That(containers.Remove(issued, source!), Is.True);
                    transform.SetCoordinates(issued, new EntityCoordinates(map.Value, new Vector2(20, 0)));
                }
                Assert.That(source!.ContainedEntities, Is.EquivalentTo(initial[..2]));
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    faction, factory, arsenal, kind, 8);
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                Assert.That(purchase.Goods, Is.EquivalentTo(initial[..2]));
                Assert.That(purchase.ProductionQuantity, Is.EqualTo(6));
                Assert.That(markets.TryReserveSupplyProduction(new(generation, purchase.Production!.Value, factory,
                    faction, kind, 2), out var planned), Is.True);
                Assert.That(planned.Quantity, Is.EqualTo(10));
                Assert.That(planned.MarketQuantity, Is.EqualTo(7));
                Assert.That(planned.LocalPercent, Is.EqualTo(25));
                Assert.That(planned.Duration, Is.EqualTo(TimeSpan.FromSeconds(900)));
                var line = model.SupplyPrograms[faction].Lines[kind];
                Assert.That(line.MarketRemainder, Is.Zero);
                line.LocalPercent = 100;
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(8));
                Assert.That(model.SupplyPrograms[faction].Orders, Has.Count.EqualTo(1));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
                var food = model.Markets[world].Goods["SWGlobalWarF"];
                var steel = model.Markets[world].Goods["SWGlobalWarS"];
                Assert.That(food.Reserved, Is.EqualTo(Q(20)));
                Assert.That(steel.Reserved, Is.EqualTo(Q(2)));
                Assert.That(markets.TryBeginSupplyProduction(generation, purchase.Production!.Value), Is.True);
                Assert.That(food.Stock, Is.EqualTo(Q(40)));
                Assert.That(steel.Stock, Is.EqualTo(Q(94)));
                var manufactured = Output(purchase.Production.Value, 10);
                Advance(3299);
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured), Is.False);
                Assert.That(line.MarketRemainder, Is.Zero, "Reserved fractions do not advance before actual output.");
                Advance(3301);
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured[..9]), Is.False);
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured), Is.True);
                Assert.That(purchase.Goods, Is.EquivalentTo(initial[..2].Concat(manufactured[..6])));
                Assert.That(source.ContainedEntities, Is.EquivalentTo(manufactured[6..]));
                Assert.That(line.MarketRemainder, Is.EqualTo(0.5m));
                Assert.That(manufactured.Count(e => Market(e)), Is.EqualTo(7));
                Assert.That(manufactured[6..].Count(e => Market(e)), Is.EqualTo(1));
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured), Is.True);
                Assert.That(line.MarketRemainder, Is.EqualTo(0.5m));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(8));
                Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry));
                var bag = server.EntMan.SpawnEntity("ClothingBackpack", position);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, bag, purchase.Goods), Is.True);
                transform.SetCoordinates(bag, destination);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                    accounts.Comp.Revision, purchase.Goods), Is.True);
                Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.EqualTo(8));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war - 1200));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 1200));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(purchase.Obligation.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Complete));
                Assert.That(model.SupplyPrograms[faction].RemainingQuota(kind), Is.Zero);
                Assert.That(markets.TryReserveSupplyPurchase(request with
                {
                    Operation = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision, Quantity = 1,
                }, out _), Is.False, "The four extra physical outputs do not create a new procurement quota.");
                Assert.That(source.ContainedEntities, Is.EquivalentTo(manufactured[6..]));
                Assert.That(initial.Concat(manufactured).All(server.EntMan.EntityExists), Is.True);
                Advance(3600);
                var next = request with
                {
                    Operation = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision, Quantity = 2,
                };
                Assert.That(markets.TryReserveSupplyPurchase(next, out _), Is.False,
                    "Only one free unit is Market; a future 100% local line cannot automatically requisition the other three.");
                var one = next with { Quantity = 1 };
                Assert.That(markets.TryReserveSupplyPurchase(one, out var marketOnly), Is.True);
                Assert.That(marketOnly.Goods, Is.EqualTo(new[] { manufactured[6] }));
                Assert.That(markets.TryCancelSupplyPurchase(generation, one.Operation), Is.True);
                var local = next with
                {
                    Operation = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision, Quantity = 4, AllowLocal = true,
                };
                Assert.That(markets.TryReserveSupplyPurchase(local, out var explicitlyAllowed), Is.True);
                Assert.That(explicitlyAllowed.Goods, Is.EquivalentTo(manufactured[6..]));
                Assert.That(markets.TryCancelSupplyPurchase(generation, local.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(manufactured[6..]));
                Assert.That(manufactured[6..].Count(e => Market(e)), Is.EqualTo(1), "A setting does not relabel old stock.");
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 1200));

                bool Market(EntityUid entity)
                {
                    Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                    return item.Market;
                }

                EntityUid[] Output(Guid production, int count)
                {
                    return Enumerable.Range(0, count).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                        markets.SupplyProductionOutputCoordinates(production))).ToArray();
                }
                void Advance(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
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
