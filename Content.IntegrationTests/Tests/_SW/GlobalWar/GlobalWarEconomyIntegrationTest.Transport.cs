using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string TransportPrototypes = """
- type: swGlobalWarEconomyProfile
  id: SWDEconomyTestTransit
  mandatory: { SWGlobalWarF: 0 }
  hubCapacity: 25

- type: swGlobalWarPlanet
  id: SWDEconomyTransportA
  name: Test farm
  kind: Agriculture
  initialOwner: Republic
  economyProfile: SWGlobalWarEconomyAgriculture
  tradeConnections:
    SWDEconomyTransportB: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWDEconomyTransportB
  name: Test transit
  kind: Agriculture
  initialOwner: Republic
  economyProfile: SWDEconomyTestTransit
  tradeConnections:
    SWDEconomyTransportA: { travelTime: 2, capacity: 64 }
    SWDEconomyTransportC: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWDEconomyTransportC
  name: Test capital
  kind: Capital
  initialOwner: Republic
  economyProfile: SWDEconomyTestEmptyCapital
  tradeConnections:
    SWDEconomyTransportB: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWDEconomyTransportE
  name: Test empire
  kind: Capital
  initialOwner: Empire
  economyProfile: SWDEconomyTestEmptyCapital

- type: entity
  parent: SWGlobalWarCampaign
  id: SWDEconomyTransportRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWDEconomyTransportA, SWDEconomyTransportB, SWDEconomyTransportC, SWDEconomyTransportE]
""";

    [TestCase("next-leg")]
    [TestCase("receipt")]
    [TestCase("capture-held")]
    public async Task RealOwnershipPolicyHoldsAndReturnsExistingCargo(string stage)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                Assert.That(ticker.StartGameRule("SWDEconomyTransportRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var model = markets.Simulation!;
                var original = model.Markets.Values.SelectMany(m => m.Goods).GroupBy(g => g.Key)
                    .ToDictionary(g => g.Key, g => g.Sum(p => (long) p.Value.Stock));
                var second = 0;
                void Advance()
                {
                    second++;
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
                Guid Change(params (string Planet, SWGlobalWarPlanetLocation Location, SWGlobalWarFaction Owner)[] changes)
                {
                    var lastOperation = Guid.Empty;
                    var revisions = new Dictionary<string, long>();
                    foreach (var (planet, location, owner) in changes)
                    {
                        Assert.That(planets.TryGetPlanet(planet, out var entity), Is.True);
                        var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(entity!.Value);
                        lastOperation = Guid.NewGuid();
                        var revision = revisions.GetValueOrDefault(planet, state.OwnershipRevision);
                        revisions[planet] = revision + 1;
                        Assert.That(steps.TryQueueOwnership(entity.Value, location, owner, revision,
                            campaign.Comp.Generation, lastOperation, completed => Assert.That(completed, Is.True)), Is.True);
                    }
                    Assert.That(steps.CommitCampaignStep(campaign.Comp.Generation, timing.CurTick), Is.True);
                    return lastOperation;
                }
                const string source = "SWDEconomyTransportA";
                const string transit = "SWDEconomyTransportB";
                const string destination = "SWDEconomyTransportC";
                const string commodity = "SWGlobalWarF";
                var republic = SWGlobalWarFaction.Republic;
                var empire = SWGlobalWarFaction.Empire;
                var surface = SWGlobalWarPlanetLocation.Surface;
                var orbit = SWGlobalWarPlanetLocation.Orbit;
                while (!model.Shipments.Any(s => s.Source == source && s.Destination == destination) && second < 20)
                    Advance();
                var shipment = model.Shipments.First(s => s.Source == source && s.Destination == destination);
                Assert.That(shipment.ArrivesAt, Is.Not.Null);
                var quantity = shipment.Quantity;
                var heldNode = stage == "receipt" ? destination : transit;
                if (stage == "receipt")
                {
                    Change((source, surface, empire), (source, orbit, empire));
                    while (shipment.Leg == 0 && second < 20)
                        Advance();
                    Assert.That(shipment.ArrivesAt, Is.Not.Null, "The last leg must depart before receipt is blocked.");
                    Change((heldNode, orbit, empire));
                }
                else
                    Change((source, surface, empire), (source, orbit, empire), (heldNode, orbit, empire));
                Assert.That(model.Markets[source].Goods[commodity].Batches.All(b => b.Owner == empire), Is.True);
                Assert.That(shipment.Batch!.Owner, Is.EqualTo(republic), "The departing port cannot capture in-transit goods.");
                Assert.That(shipment.Batch.Capture, Is.Null);
                while ((shipment.ArrivesAt != null || shipment.Route[shipment.Leg] != heldNode) && second < 30)
                    Advance();
                Assert.That(shipment.ArrivesAt, Is.Null);
                Assert.That(shipment.Route[shipment.Leg], Is.EqualTo(heldNode));
                Assert.That(shipment.HoldReason, Is.EqualTo("sw-global-war-trade-blockade"));
                Assert.That(shipment.Quantity, Is.EqualTo(quantity));

                var owner = republic;
                if (stage == "capture-held")
                {
                    // Advance the tick without advancing the market so this is a distinct ownership batch.
                    timing.CurTick += 1;
                    var capture = Change((transit, surface, empire));
                    owner = empire;
                    Assert.That(shipment.Batch.Owner, Is.EqualTo(empire));
                    Assert.That(shipment.Batch.Origin, Is.EqualTo(republic));
                    Assert.That(shipment.Batch.Capture!.CaptureOperation, Is.EqualTo(capture));
                    Assert.That(shipment.Batch.Capture.Quantity, Is.EqualTo(quantity));
                }
                else
                {
                    timing.CurTick += 1;
                    Change((source, surface, republic), (source, orbit, republic));
                }
                var request = new SWGlobalWarMarketReturnRequest(campaign.Comp.Generation, Guid.NewGuid(), shipment.Id,
                    owner, source, quantity);
                var oldIncoming = model.Markets[destination].Goods[commodity].Incoming;
                var newIncoming = model.Markets[source].Goods[commodity].Incoming;
                Assert.That(markets.TryReturnShipment(request with { Owner = owner == republic ? empire : republic }, out _),
                    Is.False);
                Assert.That(markets.TryReturnShipment(request, out var receipt), Is.True);
                Assert.That(markets.TryReturnShipment(request, out var replay), Is.True);
                Assert.That(replay, Is.SameAs(receipt));
                Assert.That(model.Markets[destination].Goods[commodity].Incoming, Is.EqualTo(oldIncoming - quantity));
                Assert.That(model.Markets[source].Goods[commodity].Incoming, Is.EqualTo(newIncoming + quantity));
                if (stage != "capture-held")
                {
                    Advance();
                    Assert.That(shipment.ArrivesAt, Is.Null);
                    Assert.That(shipment.HoldReason, Is.EqualTo("sw-global-war-trade-blockade"));
                    Change((heldNode, orbit, republic));
                }
                var returnStart = second;
                do
                {
                    Advance();
                    Assert.That(markets.TryReturnShipment(request, out replay), Is.True);
                } while (replay.ReceivedQuantity < quantity && second < returnStart + 30);
                Assert.That(replay.ReceivedQuantity, Is.EqualTo(quantity));
                Assert.That(second - returnStart, Is.GreaterThanOrEqualTo(stage == "receipt" ? 4 : 2));
                Assert.That(model.Shipments.Any(s => s.ReturnOperation == request.Operation), Is.False);
                foreach (var (good, opening) in original)
                {
                    var values = model.Markets.Values.Select(m => m.Goods[good]).ToArray();
                    Assert.That(values.Sum(g => (long) g.Stock + g.Consumed + g.GrowthConsumed + g.RecipeConsumed - g.Produced)
                        + model.Shipments.Where(s => s.Commodity == good).Sum(s => (long) s.Quantity), Is.EqualTo(opening));
                }
                Assert.That(clock.TryAdvancePhase(campaign.Comp.Generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                Assert.That(markets.TryReturnShipment(request, out _), Is.False);
                ticker.EndGameRule(campaign.Owner);
            }
            finally
            {
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
            }
        });
        await pair.CleanReturnAsync();
    }
}
