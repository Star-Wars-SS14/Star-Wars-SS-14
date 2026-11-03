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
    [TestPrototypes]
    private const string SupplyTransportPrototypes = """
- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTransportFarm
  name: Supply transport farm
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  tradeConnections:
    SWSpawnTestSupplyTransportTransit: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTransportIndustry
  name: Supply transport industry
  kind: Industry
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  connections: [SWSpawnTestSupplyTransportTransit]
  travelTimes: { SWSpawnTestSupplyTransportTransit: 2 }
  tradeConnections:
    SWSpawnTestSupplyTransportTransit: { travelTime: 2, capacity: 64 }

# Hyperspace goes through the transit world while the economic lane is direct, as on R7 to R0.
- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTransportDirect
  name: Supply transport direct lane
  kind: Industry
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  connections: [SWSpawnTestSupplyTransportTransit]
  travelTimes: { SWSpawnTestSupplyTransportTransit: 2 }
  tradeConnections:
    SWSpawnTestSupplyTransportHome: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTransportTransit
  name: Supply transport transit
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  connections: [SWSpawnTestSupplyTransportHome]
  travelTimes: { SWSpawnTestSupplyTransportHome: 2 }
  tradeConnections:
    SWSpawnTestSupplyTransportHome: { travelTime: 2, capacity: 64 }
    SWSpawnTestSupplyTransportIndustry: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTransportHome
  name: Supply transport home
  kind: Capital
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs

- type: entity
  parent: SWGlobalWarCampaign
  id: SWSpawnTestSupplyTransportRule
  components:
  - type: SWGlobalWarCampaign
    participantAbsenceDuration: 3600
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestSupplyTransportHome, SWSpawnTestSupplyTransportFarm, SWSpawnTestSupplyTransportIndustry, SWSpawnTestSupplyTransportDirect, SWSpawnTestSupplyTransportTransit, SWSpawnTestLnEHome]
""";

    [TestCase("SWSpawnTestSupplyTransportFarm")]
    [TestCase("SWSpawnTestSupplyTransportIndustry")]
    public async Task SupplyPhysicalManifestTravelsRealGraphAndPaysOnlySurvivingCargo(string sourceWorld)
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
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyTransportRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string destination = "SWSpawnTestSupplyTransportHome";
                const string transit = "SWSpawnTestSupplyTransportTransit";
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store(sourceWorld, false);
                var arsenal = Store(destination, true);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                Assert.That(containers.TryGetContainer(arsenal, "sw-program-supply", out var receiving), Is.True);
                var rawFood = model.Markets[sourceWorld].Goods["SWGlobalWarF"].Stock;
                var rawSteel = model.Markets[sourceWorld].Goods["SWGlobalWarS"].Stock;
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                yield return true;
                Advance(1200);
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Assert.That(model.Markets[sourceWorld].Goods["SWGlobalWarF"].Stock,
                    Is.EqualTo(rawFood - SWGlobalWarMarketUnits.FromUnits(10)));
                Assert.That(model.Markets[sourceWorld].Goods["SWGlobalWarS"].Stock,
                    Is.EqualTo(rawSteel - SWGlobalWarMarketUnits.FromUnits(1)));
                Change(destination, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), Revision(), faction,
                    factory, arsenal, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out _, out var failure), Is.False);
                Assert.That(failure, Is.EqualTo(SWGlobalWarSupplyFailure.TransportRoute));
                Assert.That(model.SupplyPrograms[faction].Orders, Is.Empty);
                Assert.That(source!.ContainedEntities, Is.EquivalentTo(goods));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                yield return true;
                Change(destination, SWGlobalWarPlanetLocation.Orbit, faction);
                yield return true;
                request = request with { ObservedRevision = Revision() };
                Assert.That(markets.TryReserveSupplyPurchase(request, out var purchase), Is.True);
                yield return true;
                Change(destination, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire);
                var carrier = server.EntMan.SpawnEntity("ClothingBackpack", server.EntMan.GetComponent<TransformComponent>(factory).Coordinates);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, purchase.Goods), Is.False,
                    "The local collector cannot take a remote order before its transport stage, even after late blockade.");
                foreach (var item in purchase.Goods)
                {
                    Assert.That(server.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>()
                        .Insert(carrier, item, out _, playSound: false, stackAutomatically: false), Is.True);
                    // Simulate an external physical carrier integration with no verified transport stage.
                    purchase.Carriers[item] = carrier;
                }
                server.System<SharedTransformSystem>().SetCoordinates(carrier,
                    server.EntMan.GetComponent<TransformComponent>(arsenal).Coordinates);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation, Revision(), purchase.Goods), Is.False,
                    "Being physically beside the remote arsenal is not a transport receipt.");
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                Assert.That(containers.TryGetContainer(purchase.Escrow, "sw-program-supply", out var escrow), Is.True);
                foreach (var item in purchase.Goods)
                {
                    Assert.That(containers.Insert(item, escrow!), Is.True);
                    purchase.Carriers.Remove(item);
                }
                yield return true;
                Change(destination, SWGlobalWarPlanetLocation.Orbit, faction);
                yield return true;
                var units = purchase.Goods.ToDictionary(e => e, e =>
                {
                    Assert.That(markets.TryGetSupplyItem(e, out var item), Is.True);
                    return item.Unit;
                });
                Assert.That(purchase.AllowedArsenals.Count, Is.EqualTo(1));
                model.Markets[sourceWorld].SendBudget = 0;
                Advance(1201);
                var departed = model.Shipments.Single(s => s.SupplyManifest != null && s.ArrivesAt != null);
                var waiting = model.Shipments.Single(s => s.SupplyManifest != null && s.ArrivesAt == null);
                Assert.That(departed.SupplyManifest!.Items.Count, Is.EqualTo(1));
                Assert.That(waiting.SupplyManifest!.Items.Count, Is.EqualTo(3));
                Assert.That(model.Shipments.SelectMany(s => s.SupplyManifest!.Items).Select(i => i.Entity),
                    Is.EquivalentTo(purchase.Goods));
                Assert.That(model.Markets[destination].Goods.ContainsKey("SWGlobalWarSupplyKolto"), Is.False);
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                yield return true;
                Change(transit, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire);
                yield return true;
                Change(sourceWorld, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire);
                yield return true;
                Change(sourceWorld, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire);
                yield return true;
                Advance(1206);
                Assert.That(departed.Route[departed.Leg], Is.EqualTo(transit));
                Assert.That(departed.ArrivesAt, Is.Null);
                Assert.That(departed.HoldReason, Is.EqualTo("sw-global-war-trade-blockade"));
                Assert.That(departed.SupplyManifest.Owner, Is.EqualTo(faction));
                Assert.That(waiting.ReturnCaptured, Is.True);
                Assert.That(waiting.SupplyManifest.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
                Assert.That(waiting.SupplyManifest.Capture!.Quantity, Is.EqualTo(3));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(4));
                Assert.That(purchase.Obligation.Cancelled, Is.Zero);
                Assert.That(receiving!.ContainedEntities, Is.Empty);
                var warBefore = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                var industryBefore = ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance;
                Change(transit, SWGlobalWarPlanetLocation.Orbit, faction);
                yield return true;
                var payments = 0;
                probe.LedgerChanged = () =>
                {
                    payments++;
                    Assert.That(purchase.Obligation.Accepted, Is.EqualTo(1));
                    Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(3));
                    Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.False);
                };
                Advance(1212);
                probe.LedgerChanged = null;
                Assert.That(payments, Is.EqualTo(1));
                Assert.That(receiving.ContainedEntities, Is.EquivalentTo(departed.SupplyManifest.Items.Select(i => i.Entity)));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(warBefore - 150));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBefore + 150));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(450));
                Assert.That(model.ReceiveSupplyManifest!(departed), Is.False);
                foreach (var (entity, unit) in units)
                {
                    Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                    Assert.That(item.Unit, Is.EqualTo(unit));
                    Assert.That(item.OriginOwner, Is.EqualTo(faction));
                }
                Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                Assert.That(purchase.Obligation.Cancelled, Is.EqualTo(3));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                Assert.That(waiting.SupplyManifest.Items.All(i => server.EntMan.EntityExists(i.Entity)), Is.True);
                Assert.That(source.ContainedEntities.Count, Is.EqualTo(1));

                EntityUid Store(string world, bool arsenalStore)
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                        out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, arsenalStore), Is.True);
                    return store;
                }
                void Change(string world, SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner)
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                    Assert.That(steps.TryQueueOwnership(planet.Value, location, owner, state.OwnershipRevision,
                        generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
                long Revision()
                {
                    Assert.That(ledger.TryGetLedger(out var state), Is.True);
                    return state.Comp.Revision;
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
