using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyPurchasePaysOnlyActualPartialAcceptanceAndCancelsUnpaidRemainder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        var containers = server.System<SharedContainerSystem>();
        var transform = server.System<SharedTransformSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
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
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                // Identity counting has its own real-session test. This scenario fixes N to exercise a four-item budget.
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.T2;
                // This financial/capture fixture explicitly allocates its complete initial output to the market.
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var sourcePosition = new EntityCoordinates(map!.Value, Vector2.Zero);
                var receivingPosition = new EntityCoordinates(map.Value, new Vector2(10, 0));
                var factory = server.EntMan.SpawnEntity(null, sourcePosition);
                var arsenal = server.EntMan.SpawnEntity(null, receivingPosition);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                Assert.That(containers.TryGetContainer(arsenal, "sw-program-supply", out var receiving), Is.True);
                for (var i = 0; i < 4; i++)
                {
                    Assert.That(containers.Insert(server.EntMan.SpawnEntity("SWBlasterFrame", sourcePosition), source!), Is.True);
                    Assert.That(containers.Insert(server.EntMan.SpawnEntity("SWBlasterBarrel", sourcePosition), source!), Is.True);
                }
                var productionRequest = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 4);
                Assert.That(markets.TryReserveSupplyProduction(productionRequest, out var production), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, productionRequest.Operation), Is.True);
                var guns = Enumerable.Range(0, 4).Select(_ => server.EntMan.SpawnEntity("SWH206Renegade",
                    markets.SupplyProductionOutputCoordinates(productionRequest.Operation))).ToArray();
                foreach (var gun in guns)
                {
                    Assert.That(containers.TryGetContainer(gun, "gun_magazine", out var magazine), Is.True);
                    foreach (var inherited in magazine!.ContainedEntities.ToArray())
                        server.EntMan.DeleteEntity(inherited);
                }
                yield return true;
                Advance(1200);
                Assert.That(markets.TryCompleteSupplyProduction(generation, productionRequest.Operation, guns), Is.True);
                var program = model.SupplyPrograms[faction];
                Assert.That(program.RemainingQuota(kind), Is.EqualTo(4));
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), Revision(), faction,
                    factory, arsenal, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request with { ObservedRevision = request.ObservedRevision + 1 }, out _), Is.False);
                Money(SWGlobalWarAccount.War, SWGlobalWarLedgerAction.Debit,
                    ledger.GetBalance(faction, SWGlobalWarAccount.War).Available - 5999);
                Assert.That(markets.TryReserveSupplyPurchase(request with { ObservedRevision = Revision() }, out _), Is.False);
                Assert.That(source!.ContainedEntities, Is.EquivalentTo(guns));
                Assert.That(program.Orders, Is.Empty);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Money(SWGlobalWarAccount.War, SWGlobalWarLedgerAction.Credit, 10000);
                request = request with { ObservedRevision = Revision() };
                var warBefore = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industryBefore = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                var reserveCallbacks = 0;
                probe.LedgerChanged = () =>
                {
                    reserveCallbacks++;
                    Assert.That(program.Orders[request.Operation].Outstanding, Is.EqualTo(4));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(6000));
                    Assert.That(source.ContainedEntities, Is.Empty);
                    Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.False);
                };
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                probe.LedgerChanged = null;
                Assert.That(reserveCallbacks, Is.EqualTo(1));
                Assert.That(markets.TryReserveSupplyPurchase(request, out var replay), Is.True);
                Assert.That(replay, Is.SameAs(purchase));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(warBefore));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore));
                Assert.That(program.RemainingQuota(kind), Is.Zero);
                Assert.That(markets.TryReserveSupplyPurchase(request with { Operation = Guid.NewGuid(), ObservedRevision = Revision() }, out _), Is.False);

                var carrier = server.EntMan.SpawnEntity("SWBoxWood", sourcePosition);
                var cargo = server.EntMan.GetComponent<StorageComponent>(carrier).Container;
                Assert.That(server.System<SharedStorageSystem>().CanInsert(carrier, guns[0], out var loadFailure), Is.True,
                    $"The actual carrier must accept the supplied weapon: {loadFailure}");
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, guns[..2]), Is.False,
                    "The real wooden box cannot hold two rifles; a failed multi-item collection restores the first.");
                Assert.That(cargo.ContainedEntities, Is.Empty);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, guns[..1]), Is.True);
                var secondCarrier = server.EntMan.SpawnEntity("SWBoxWood", sourcePosition);
                var secondCargo = server.EntMan.GetComponent<StorageComponent>(secondCarrier).Container;
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, secondCarrier, guns[1..2]), Is.True);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, guns[..1]), Is.True);
                Assert.That(cargo.ContainedEntities.Concat(secondCargo.ContainedEntities), Is.EquivalentTo(guns[..2]));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(purchase.Obligation.Stage, Is.EqualTo(SWGlobalWarSupplyStage.InTransit));
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation, Revision(), guns[..2]), Is.False,
                    "A carrier at the factory has not physically reached the receiving arsenal.");
                transform.SetCoordinates(carrier, receivingPosition);
                transform.SetCoordinates(secondCarrier, receivingPosition);
                var industryCredit = long.MaxValue - industryBefore;
                Money(SWGlobalWarAccount.Industry, SWGlobalWarLedgerAction.Credit, industryCredit);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation, Revision(), guns[..2]), Is.False);
                Assert.That(cargo.ContainedEntities.Concat(secondCargo.ContainedEntities), Is.EquivalentTo(guns[..2]),
                    "Failed payment rolls the prepared goods back to both actual carriers.");
                Assert.That(receiving!.ContainedEntities, Is.Empty);
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(6000));
                Money(SWGlobalWarAccount.Industry, SWGlobalWarLedgerAction.Debit, industryCredit);
                var accept = Guid.NewGuid();
                var revision = Revision();
                var callbacks = 0;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    Assert.That(receiving.ContainedEntities, Is.EquivalentTo(guns[..2]));
                    Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.EqualTo(2));
                    Assert.That(purchase.Obligation.Accepted, Is.EqualTo(2));
                    Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(2));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(warBefore - 3000));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore + 3000));
                    Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation, Revision(), guns[2..]), Is.False);
                    Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.False);
                };
                Assert.That(markets.TryAcceptSupplyPurchase(generation, accept, request.Operation, revision, guns[..2]), Is.True);
                probe.LedgerChanged = null;
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(markets.TryAcceptSupplyPurchase(generation, accept, request.Operation, revision, guns[..2]), Is.True);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, accept, request.Operation, revision + 1, guns[..2]), Is.False);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation, Revision(), guns[..2]), Is.False);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(3000));
                Assert.That(program.RemainingQuota(kind), Is.Zero);
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(guns[2..]));
                Assert.That(receiving.ContainedEntities, Is.EquivalentTo(guns[..2]));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(warBefore - 3000));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore + 3000));
                Assert.That(purchase.Obligation.Outstanding, Is.Zero);
                Assert.That(purchase.Obligation.Cancelled, Is.EqualTo(2));
                Assert.That(program.RemainingQuota(kind), Is.EqualTo(2));

                // A paid unit returned to a factory is still the same paid unit, even under a new purchase ID.
                foreach (var gun in guns[..2])
                    Assert.That(containers.Insert(gun, source), Is.True);
                var second = request with { Operation = Guid.NewGuid(), ObservedRevision = Revision(), Quantity = 2 };
                Assert.That(markets.TryReserveSupplyPurchase(second, out var secondPurchase), Is.True);
                Assert.That(secondPurchase.Goods, Is.EquivalentTo(guns[2..]));
                Assert.That(markets.TryCancelSupplyPurchase(generation, second.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(guns));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore + 3000));

                var captured = second with { Operation = Guid.NewGuid(), ObservedRevision = Revision() };
                Assert.That(markets.TryReserveSupplyPurchase(captured, out var capturedPurchase), Is.True);
                transform.SetCoordinates(carrier, sourcePosition);
                Assert.That(markets.TryCollectSupplyPurchase(generation, captured.Operation, carrier, [guns[2]]), Is.True);
                server.EntMan.DeleteEntity(factory);
                ChangeOwner(SWGlobalWarFaction.Empire);
                Assert.That(capturedPurchase.ReturnPending, Is.True);
                Assert.That(cargo.Contains(guns[2]), Is.True, "Territorial capture must not steal cargo already collected.");
                Assert.That(server.EntMan.EntityExists(guns[3]), Is.True);
                yield return true;
                factory = server.EntMan.SpawnEntity(null, sourcePosition);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out source), Is.True);
                Assert.That(markets.TryCancelSupplyPurchase(generation, captured.Operation, factory), Is.True);
                Assert.That(source!.ContainedEntities, Is.EqualTo(new[] { guns[3] }));
                ChangeOwner(faction);
                yield return true;
                var trophyRequest = captured with
                {
                    Operation = Guid.NewGuid(), ObservedRevision = Revision(), Source = factory, Quantity = 1,
                };
                Assert.That(markets.TryReserveSupplyPurchase(trophyRequest, out _), Is.False,
                    "Escrow captured while its source was deleted remains Trophy after recovery and recapture.");
                Assert.That(containers.Insert(guns[2], source), Is.True);
                Assert.That(markets.TryReserveSupplyPurchase(trophyRequest, out var escaped), Is.True,
                    "The actually collected unit was outside escrow and did not inherit its source's capture.");
                Assert.That(escaped.Goods, Is.EqualTo(new[] { guns[2] }));
                Assert.That(markets.TryCancelSupplyPurchase(generation, trophyRequest.Operation), Is.True);
                Assert.That(source.ContainedEntities, Is.EquivalentTo(guns[2..]));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore + 3000));

                void ChangeOwner(SWGlobalWarFaction owner)
                {
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet.Value);
                    var ownershipRevision = state.OwnershipRevision;
                    foreach (var location in new[] { SWGlobalWarPlanetLocation.Surface, SWGlobalWarPlanetLocation.Orbit })
                        Assert.That(steps.TryQueueOwnership(planet.Value, location, owner, ownershipRevision++, generation,
                            Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
                long Revision()
                {
                    Assert.That(ledger.TryGetLedger(out var state), Is.True);
                    return state.Comp.Revision;
                }
                void Money(SWGlobalWarAccount account, SWGlobalWarLedgerAction action, long amount)
                {
                    Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), Revision(), action, faction,
                        account, amount, Purpose: "supply-fixture"), out _), Is.True);
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
                probe.LedgerChanged = null;
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        }
    }
}
