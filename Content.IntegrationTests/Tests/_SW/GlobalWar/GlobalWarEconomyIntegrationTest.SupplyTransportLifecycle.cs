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
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyTransportStopsAfterPaymentCallbackAndCannotEnterNextGeneration(bool stop)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
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
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string sourceWorld = "SWSpawnTestSupplyTransportFarm";
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store(sourceWorld, false);
                var arsenal = Store("SWSpawnTestSupplyTransportHome", true);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                yield return true;
                Advance(1200);
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Assert.That(ledger.TryGetLedger(out var state), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), state.Comp.Revision,
                    faction, factory, arsenal, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                model.Markets[sourceWorld].SendBudget = 0;
                Advance(1201);
                var departed = model.Shipments.Single(s => s.ArrivesAt != null);
                var callbacks = 0;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    probe.LedgerChanged = null;
                    Assert.That(purchase.Obligation.Accepted, Is.EqualTo(1));
                    if (stop)
                        ticker.EndGameRule(campaign.Owner);
                    else
                        Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                };
                Advance(1210);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(1));
                var stoppedTime = model.Time;
                model.Advance(TimeSpan.FromMinutes(10));
                Assert.That(model.Time, Is.EqualTo(stoppedTime));
                Assert.That(model.ReceiveSupplyManifest!(departed), Is.False);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                    state.Comp.Revision, purchase.Goods), Is.False);
                if (!stop)
                {
                    Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Ending));
                    Assert.That(ledger.TryGetLedger(out _), Is.False, "Ending hides the active ledger API.");
                    Assert.That(state.Comp.Reservations[purchase.Reservation].Remaining, Is.EqualTo(450));
                    ticker.EndGameRule(campaign.Owner);
                }
                Assert.That(markets.Simulation, Is.Null);
                yield return true;
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyTransportRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var next), Is.True);
                rule = next.Owner;
                Assert.That(next.Comp.Generation, Is.Not.EqualTo(generation));
                var fresh = markets.Simulation!;
                Assert.That(fresh, Is.Not.SameAs(model));
                Assert.That(fresh.Shipments, Is.Empty);
                Assert.That(fresh.SupplyPrograms[faction].Orders, Is.Empty);
                Assert.That(markets.SupplyIntents, Is.Empty);
                Assert.That(model.ReceiveSupplyManifest!(departed), Is.False);
                Assert.That(markets.TryReserveSupplyPurchase(request, out _), Is.False);
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.False);
                model.Advance(TimeSpan.FromMinutes(10));
                Assert.That(fresh.Shipments, Is.Empty);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);

                EntityUid Store(string world, bool arsenalStore)
                {
                    Assert.That(planets.TryGetPlanet(world, out var entity), Is.True);
                    Assert.That(planets.TryEnsureLocation(entity!.Value, SWGlobalWarPlanetLocation.Surface,
                        out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, arsenalStore), Is.True);
                    return store;
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
