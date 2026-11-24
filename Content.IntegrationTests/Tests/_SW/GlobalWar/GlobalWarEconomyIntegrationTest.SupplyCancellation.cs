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
    [TestCase(SWGlobalWarSupplyKind.Medicine, 4, 10, 20, 2, 900, "SWKoltoMedipen")]
    [TestCase(SWGlobalWarSupplyKind.Ration, 3, 4, 4, 0, 60, "SWBoxMRE")]
    public async Task SupplyPurchaseCancellationPreservesStartedWholeBatch(SWGlobalWarSupplyKind kind,
        int ordered, int outputCount, int foodUsed, int steelUsed, int seconds, string prototype)
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
                const string world = "SWSpawnTestSupplySource";
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                Advance(1200);
                var line = model.SupplyPrograms[faction].Lines[kind];
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    faction, factory, arsenal, kind, ordered);
                Assert.That(markets.TryReserveSupplyPurchase(request, out var unstarted), Is.True);
                Assert.That(unstarted.Goods, Is.Empty);
                Assert.That(unstarted.ProductionQuantity, Is.EqualTo(ordered));
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Stock, Is.EqualTo(Q(100)));
                Assert.That(model.ExternalPendingReservationCount, Is.Zero);
                Assert.That(line.MarketRemainder, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(markets.TryBeginSupplyProduction(generation, unstarted.Production!.Value), Is.False);

                request = request with { Operation = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision };
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                var production = purchase.Production!.Value;
                Assert.That(markets.TryReserveSupplyProduction(new(generation, production, factory, faction, kind, 2),
                    out var batch), Is.True);
                Assert.That(batch.Quantity, Is.EqualTo(outputCount));
                Assert.That(batch.MarketQuantity, Is.EqualTo((int) decimal.Floor(outputCount * 0.75m)));
                Assert.That(batch.Duration, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved,
                    Is.EqualTo(ordered * SWGlobalWarSupplyProgram.UnitPrice(kind)));
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                Assert.That(markets.TryBeginSupplyProduction(generation, production), Is.True);
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(batch.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Producing));
                Assert.That(batch.Purchase, Is.Null);
                Assert.That(purchase.Production, Is.EqualTo(production), "The cancelled purchase retains its historical batch link.");
                Assert.That(purchase.Obligation.Outstanding, Is.Zero);
                Assert.That(purchase.Obligation.Cancelled, Is.EqualTo(ordered));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war));
                Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Stock, Is.EqualTo(Q(100 - foodUsed)));
                Assert.That(model.Markets[world].Goods["SWGlobalWarS"].Stock, Is.EqualTo(Q(100 - steelUsed)));
                Assert.That(line.MarketRemainder, Is.Zero);
                Assert.That(markets.TryReserveSupplyProduction(new(generation, Guid.NewGuid(), factory, faction, kind, 1),
                    out _), Is.False, "The detached physical batch still occupies the one base line.");
                var goods = Enumerable.Range(0, outputCount).Select(_ => server.EntMan.SpawnEntity(prototype,
                    markets.SupplyProductionOutputCoordinates(production))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, production, goods), Is.False);
                line.LocalPercent = 0;
                Advance(1201 + seconds);
                Assert.That(markets.TryCompleteSupplyProduction(generation, production, goods), Is.True);
                Assert.That(source!.ContainedEntities, Is.EquivalentTo(goods));
                Assert.That(goods.Count(e => Market(e)), Is.EqualTo(batch.MarketQuantity));
                Assert.That(line.MarketRemainder, Is.EqualTo(outputCount * 0.75m % 1m));
                Assert.That(markets.TryCompleteSupplyProduction(generation, production, goods), Is.True);
                Assert.That(line.MarketRemainder, Is.EqualTo(outputCount * 0.75m % 1m));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry));
                var next = request with { Operation = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision };
                Assert.That(markets.TryReserveSupplyPurchase(next, out var reuse), Is.True);
                Assert.That(reuse.Production, Is.Null, "A following order consumes the existing free market output first.");
                Assert.That(reuse.Goods.All(Market), Is.True);
                Assert.That(markets.TryCancelSupplyPurchase(generation, next.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(goods));

                bool Market(EntityUid entity)
                {
                    Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                    Assert.That(item.Payment, Is.Null);
                    return item.Market;
                }
                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign),
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
