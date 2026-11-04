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
    public async Task SupplyPurchaseUsesReadyGoodsBeforeFundingOnlyMissingProduction()
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
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.T2;
                // Use an explicit full-market line here; default allocation and indivisible batches have their own test.
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
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
                AddInputs();
                var initial = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 2);
                Assert.That(markets.TryReserveSupplyProduction(initial, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, initial.Operation), Is.True);
                Advance(1200);
                var ready = Output(initial.Operation);
                Assert.That(markets.TryCompleteSupplyProduction(generation, initial.Operation, ready), Is.True);
                var electronics = model.Markets[world].Goods["SWGlobalWarE"];
                Assert.That(electronics.Stock, Is.EqualTo(Q(96)));
                var otherUse = Guid.NewGuid();
                Assert.That(model.TryReserveStock(new(generation, otherUse, world, "SWGlobalWarE", Q(92), faction,
                    "supply-last-input-fixture"), out _), Is.True);
                Assert.That(model.TryCommitStock(generation, otherUse, out _), Is.True);
                Assert.That(electronics.Stock, Is.EqualTo(Q(4)));
                AddInputs();
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    faction, factory, arsenal, kind, 4);
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                Assert.That(purchase.Goods, Is.EquivalentTo(ready));
                Assert.That(purchase.ProductionQuantity, Is.EqualTo(2));
                Assert.That(purchase.Production, Is.Not.Null);
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(purchase.Obligation.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Reserved));
                Assert.That(model.SupplyPrograms[faction].Orders, Has.Count.EqualTo(1));
                Assert.That(electronics.Stock, Is.EqualTo(Q(4)));
                Assert.That(electronics.Reserved, Is.EqualTo(Q(4)));
                Assert.That(source!.ContainedEntities, Is.Empty);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(6000));
                Assert.That(model.TryReserveStock(new(generation, Guid.NewGuid(), world, "SWGlobalWarE", Q(1), faction,
                    "competing-supply-consumer"), out _), Is.False);
                Assert.That(markets.TryCancelSupplyProduction(generation, purchase.Production!.Value), Is.False,
                    "A production consumer cannot detach the inputs from their funded purchase.");
                Assert.That(markets.TryBeginSupplyProduction(generation, purchase.Production.Value), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, purchase.Production.Value), Is.True);
                Assert.That(electronics.Stock, Is.Zero);
                Assert.That(purchase.Obligation.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Producing));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.Zero);
                var carriers = ready.Select(_ => server.EntMan.SpawnEntity("SWBoxWood", position)).ToArray();
                for (var i = 0; i < ready.Length; i++)
                    Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carriers[i], [ready[i]]), Is.True);
                var manufactured = Output(purchase.Production.Value);
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured), Is.False);
                Advance(1441);
                Assert.That(markets.TryCompleteSupplyProduction(generation, purchase.Production.Value, manufactured), Is.True);
                Assert.That(purchase.Goods, Is.EquivalentTo(ready.Concat(manufactured)));
                Assert.That(purchase.ProductionQuantity, Is.Zero);
                Assert.That(source.ContainedEntities, Is.Empty, "The allocated output stays reserved for the same purchase.");
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(model.SupplyPrograms[faction].Orders, Has.Count.EqualTo(1));
                foreach (var carrier in carriers)
                    server.System<SharedTransformSystem>().SetCoordinates(carrier, destination);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                    accounts.Comp.Revision, ready), Is.True);
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(2));
                Assert.That(purchase.Obligation.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Ready));
                Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.EqualTo(2));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war - 3000));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 3000));
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(manufactured));
                Assert.That(electronics.Stock, Is.Zero, "Cancellation must not refund the consumed production inputs.");
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(2));
                Assert.That(purchase.Obligation.Cancelled, Is.EqualTo(2));
                Assert.That(purchase.Obligation.Outstanding, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);

                void AddInputs()
                {
                    for (var i = 0; i < 2; i++)
                    {
                        Assert.That(containers.Insert(server.EntMan.SpawnEntity("SWBlasterFrame", position), source!), Is.True);
                        Assert.That(containers.Insert(server.EntMan.SpawnEntity("SWBlasterBarrel", position), source!), Is.True);
                    }
                }
                EntityUid[] Output(Guid production)
                {
                    var guns = Enumerable.Range(0, 2).Select(_ => server.EntMan.SpawnEntity("SWH206Renegade",
                        markets.SupplyProductionOutputCoordinates(production))).ToArray();
                    foreach (var gun in guns)
                        if (containers.TryGetContainer(gun, "gun_magazine", out var magazine))
                            foreach (var inherited in magazine.ContainedEntities.ToArray())
                                server.EntMan.DeleteEntity(inherited);
                    return guns;
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
