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
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public async Task SupplyReturnStoresSameItemsWithoutPaymentUntilApprovedArsenal(bool approveNewPoint,
        bool repurchase = false, bool captureFullStore = false)
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
                const string farm = "SWSpawnTestSupplyTransportFarm";
                const string home = "SWSpawnTestSupplyTransportHome";
                const string transit = "SWSpawnTestSupplyTransportTransit";
                const string industry = "SWSpawnTestSupplyTransportIndustry";
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store(farm, false);
                var primary = Store(home, true);
                var backup = Store(industry, true);
                var warehouse = Store(transit, false);
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
                    faction, factory, primary, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                var reservation = purchase.Reservation;
                Assert.That(purchase.AllowedArsenals.Count, Is.EqualTo(2));
                var newArsenal = Store(transit, true, 10);
                Assert.That(purchase.AllowedArsenals.Count, Is.EqualTo(2), "Future points are not automatically approved.");
                Advance(1201);
                yield return true;
                Assert.That(planets.TryGetPlanet(home, out var target), Is.True);
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(target!.Value);
                Assert.That(steps.TryQueueOwnership(target.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, planet.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                yield return true;
                Advance(1204);
                var shipment = model.Shipments.Single(s => s.SupplyManifest != null);
                Assert.That(shipment.Route[shipment.Leg], Is.EqualTo(transit));
                Assert.That(shipment.ArrivesAt, Is.Null);
                var originals = shipment.SupplyManifest!.Items.ToArray();
                var war = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var income = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                var returning = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), shipment.Id,
                    faction, transit, shipment.Quantity, warehouse);
                Assert.That(model.TryReturnShipment(returning with { DestinationStore = null }, out _), Is.False);
                Assert.That(model.TryReturnShipment(returning with { Owner = SWGlobalWarFaction.Empire }, out _), Is.False);
                Assert.That(model.TryReturnShipment(returning, out var receipt), Is.True);
                Assert.That(model.TryReturnShipment(returning, out var replay), Is.True);
                Assert.That(replay, Is.EqualTo(receipt));
                Assert.That(purchase.Reservation, Is.EqualTo(reservation));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Advance(1205);
                Assert.That(shipment.SupplyManifest.Stored, Is.True);
                Assert.That(shipment.SupplyManifest.SpaceReserved, Is.False);
                Assert.That(shipment.HoldReason, Is.EqualTo("sw-global-war-supply-transport-stored"));
                Assert.That(containers.TryGetContainer(warehouse, "sw-program-supply", out var stored), Is.True);
                Assert.That(stored!.ContainedEntities, Is.EquivalentTo(purchase.Goods));
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(income));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                Assert.That(model.GetReturnReceipt(returning.Operation)!.ReceivedQuantity,
                    Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(4)));
                if (repurchase)
                {
                    Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                    Assert.That(model.Shipments, Is.Empty);
                    Assert.That(model.ClosedSupplyManifests.Single().Units, Is.EquivalentTo(originals.Select(i => i.Unit)));
                    Assert.That(stored.ContainedEntities, Is.EquivalentTo(purchase.Goods));
                    Assert.That(ledger.TryGetLedger(out var nextLedger), Is.True);
                    var nextRequest = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), nextLedger.Comp.Revision,
                        faction, warehouse, backup, kind, 4);
                    Assert.That(markets.TryReserveSupplyPurchase(nextRequest, out var nextPurchase), Is.True);
                    Assert.That(nextPurchase.Goods, Is.EquivalentTo(purchase.Goods));
                    Advance(1206);
                    Assert.That(model.Shipments.Count, Is.EqualTo(1));
                    Assert.That(model.Shipments.Single().SupplyManifest!.Items, Is.EquivalentTo(originals));
                    Advance(1210);
                    Assert.That(model.Shipments, Is.Empty);
                    Assert.That(purchase.Obligation.Accepted, Is.Zero);
                    Assert.That(nextPurchase.Obligation.Accepted, Is.EqualTo(4));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war - 600));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(income + 600));
                    Assert.That(model.GetReturnReceipt(returning.Operation)!.ReceivedQuantity,
                        Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(4)), "Retiring active custody preserves transport history.");
                    yield break;
                }
                if (captureFullStore)
                {
                    // Occupancy fixture: real pre-existing items with explicit starting provenance. This is not
                    // evidence of producing 596 medicines; the four transported items above use the real consumer.
                    var records = (Dictionary<EntityUid, SWGlobalWarSupplyItem>) typeof(SWGlobalWarMarketSystem)
                        .GetField("_supplyItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(markets)!;
                    for (var i = 0; i < 596; i++)
                    {
                        var entity = server.EntMan.SpawnEntity("SWKoltoMedipen",
                            server.EntMan.GetComponent<TransformComponent>(warehouse).Coordinates);
                        records.Add(entity, new(generation, kind, false, faction, faction, SWGlobalWarSupplyOrigin.Starting,
                            Guid.NewGuid(), Guid.NewGuid()));
                        Assert.That(containers.Insert(entity, stored), Is.True);
                    }
                    var occupied = stored.ContainedEntities.ToArray();
                    Assert.That(occupied.Length, Is.EqualTo(600));
                    yield return true;
                    Assert.That(planets.TryGetPlanet(transit, out var captured), Is.True);
                    var capturedState = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(captured!.Value);
                    var revision = capturedState.OwnershipRevision;
                    foreach (var location in new[] { SWGlobalWarPlanetLocation.Surface, SWGlobalWarPlanetLocation.Orbit })
                        Assert.That(steps.TryQueueOwnership(captured.Value, location, SWGlobalWarFaction.Empire,
                            revision++, generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                    Assert.That(shipment.ReturnCaptured, Is.True);
                    Assert.That(shipment.SupplyManifest.Capture!.Quantity, Is.EqualTo(4));
                    model.Markets[transit].ReceiveBudget = 0;
                    var samePoint = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), shipment.Id,
                        SWGlobalWarFaction.Empire, transit, shipment.Quantity, warehouse);
                    Assert.That(model.CheckReturn(samePoint), Is.EqualTo(SWGlobalWarReturnFailure.None));
                    Assert.That(model.TryReturnShipment(samePoint, out _), Is.True);
                    Assert.That(shipment.SupplyManifest.Custody, Is.EqualTo(warehouse));
                    Assert.That(shipment.SupplyManifest.SpaceReserved, Is.False);
                    Assert.That(model.Markets[transit].ReceiveBudget, Is.Zero);
                    Advance(1206);
                    Assert.That(model.Markets[transit].ReceiveBudget, Is.EqualTo(1).Within(1e-8),
                        "One second refills one unit; an already stored item must not unload for a second time.");
                    Assert.That(stored.ContainedEntities, Is.EquivalentTo(occupied));
                    Assert.That(shipment.SupplyManifest.Items, Is.EquivalentTo(originals));
                    Assert.That(purchase.Obligation.Accepted, Is.Zero);
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(income));
                    yield break;
                }
                var onward = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), shipment.Id,
                    faction, approveNewPoint ? transit : industry, shipment.Quantity, approveNewPoint ? newArsenal : backup);
                Assert.That(model.TryReturnShipment(onward, out _), Is.True);
                Assert.That(stored.ContainedEntities, Is.Empty);
                Advance(1210);
                if (approveNewPoint)
                {
                    Assert.That(shipment.SupplyManifest.Stored, Is.True);
                    Assert.That(purchase.Obligation.Accepted, Is.Zero);
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                    Assert.That(markets.TryApproveSupplyArsenal(generation, request.Operation, faction, newArsenal,
                        3, 0, out _), Is.False);
                    Assert.That(markets.TryApproveSupplyArsenal(generation, request.Operation, faction, newArsenal,
                        4, 1, out _), Is.False);
                    Assert.That(markets.TryApproveSupplyArsenal(generation, request.Operation, faction, newArsenal,
                        4, 0, out _), Is.True);
                    Assert.That(purchase.Obligation.Accepted, Is.Zero, "Approval itself does not accept or move cargo.");
                    Assert.That(purchase.AllowedArsenals.Count, Is.EqualTo(3));
                    Advance(1211);
                }
                Assert.That(model.Shipments, Is.Empty);
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(4));
                Assert.That(purchase.Obligation.Outstanding, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(war - 600));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(income + 600));
                foreach (var item in originals)
                {
                    Assert.That(markets.TryGetSupplyItem(item.Entity, out var actual), Is.True);
                    Assert.That(actual.Unit, Is.EqualTo(item.Unit));
                    Assert.That(actual.Accepted && actual.Payment != null, Is.True);
                }

                EntityUid Store(string world, bool arsenalStore, float x = 0)
                {
                    Assert.That(planets.TryGetPlanet(world, out var entity), Is.True);
                    Assert.That(planets.TryEnsureLocation(entity!.Value, SWGlobalWarPlanetLocation.Surface,
                        out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, new Vector2(x, 0)));
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
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        }
    }
}
