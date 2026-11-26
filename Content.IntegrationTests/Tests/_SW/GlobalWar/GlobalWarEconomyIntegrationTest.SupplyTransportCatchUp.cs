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
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyManifestReceiptBeforeCaptureIsIndependentOfUpdateOrdering()
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
        var timing = server.ResolveDependency<IGameTiming>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        var outcomes = new List<(int Accepted, int Outstanding, long War, long Industry, long Held, int Trophy, int Paid)>();
        foreach (var updateFirst in new[] { true, false })
        {
            using var scenario = RunScenario(updateFirst).GetEnumerator();
            var more = true;
            while (more)
            {
                await server.WaitAssertion(() => more = scenario.MoveNext());
                if (more)
                    await pair.RunTicksSync(1);
            }
            await pair.RunTicksSync(1);
        }
        Assert.That(outcomes.Count, Is.EqualTo(2));
        Assert.That(outcomes[1], Is.EqualTo(outcomes[0]));
        Assert.That(outcomes[0], Is.EqualTo((4, 0, -600L, 600L, 0L, 4, 4)));
        await pair.CleanReturnAsync();

        IEnumerable<bool> RunScenario(bool updateFirst)
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
                const string targetWorld = "SWSpawnTestSupplyTransportHome";
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store(sourceWorld, false);
                var arsenal = Store(targetWorld, true);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                yield return true;
                SetTime(1200);
                markets.Update(0);
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Assert.That(ledger.TryGetLedger(out var state), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), state.Comp.Revision,
                    faction, factory, arsenal, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industry = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                SetTime(1201);
                markets.Update(0);
                Assert.That(model.Shipments.Single(s => s.SupplyManifest != null).ArrivesAt, Is.Not.Null);
                yield return true;
                var callbacks = 0;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, factory, purchase.Goods), Is.False);
                    Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                        state.Comp.Revision, purchase.Goods), Is.False);
                };
                SetTime(1210);
                if (updateFirst)
                    markets.Update(0);
                Assert.That(planets.TryGetPlanet(targetWorld, out var target), Is.True);
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(target!.Value);
                var revision = planet.OwnershipRevision;
                foreach (var location in new[] { SWGlobalWarPlanetLocation.Surface, SWGlobalWarPlanetLocation.Orbit })
                    Assert.That(steps.TryQueueOwnership(target.Value, location, SWGlobalWarFaction.Empire,
                        revision++, generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                probe.LedgerChanged = null;
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(containers.TryGetContainer(arsenal, "sw-program-supply", out var receiving), Is.True);
                Assert.That(receiving!.ContainedEntities, Is.EquivalentTo(purchase.Goods));
                var items = purchase.Goods.Select(e =>
                {
                    Assert.That(markets.TryGetSupplyItem(e, out var item), Is.True);
                    return item;
                }).ToArray();
                outcomes.Add((purchase.Obligation.Accepted, purchase.Obligation.Outstanding,
                    ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance - war,
                    ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance - industry,
                    ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved,
                    items.Count(i => i.Owner == SWGlobalWarFaction.Empire && i.Origin == SWGlobalWarSupplyOrigin.Trophy),
                    items.Count(i => i.Payment != null && i.Accepted)));

                EntityUid Store(string world, bool arsenalStore)
                {
                    Assert.That(planets.TryGetPlanet(world, out var entity), Is.True);
                    Assert.That(planets.TryEnsureLocation(entity!.Value, SWGlobalWarPlanetLocation.Surface,
                        out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, arsenalStore), Is.True);
                    return store;
                }
                void SetTime(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
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
