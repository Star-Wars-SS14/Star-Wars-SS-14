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

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarEconomyProfile
  id: SWDEconomyTestEmptyCapital
  mandatory: { SWGlobalWarF: 12 }
  openingStock: { SWGlobalWarF: 0 }
  hubCapacity: 150
  maximumHubCapacity: 250

- type: swGlobalWarPlanet
  id: SWDEconomyTestCapital
  name: Test capital
  kind: Capital
  initialOwner: Republic
  economyProfile: SWDEconomyTestEmptyCapital
  tradeConnections:
    SWDEconomyTestFarm: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWDEconomyTestFarm
  name: Test farm
  kind: Agriculture
  initialOwner: Republic
  economyProfile: SWGlobalWarEconomyAgriculture
  tradeConnections:
    SWDEconomyTestCapital: { travelTime: 60, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWDEconomyTestEmpire
  name: Test empire
  kind: Capital
  initialOwner: Empire
  economyProfile: SWDEconomyTestEmptyCapital

- type: entity
  parent: SWGlobalWarCampaign
  id: SWDEconomyTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWDEconomyTestCapital, SWDEconomyTestFarm, SWDEconomyTestEmpire]
""";

    [Test]
    public async Task DelayedMarketUpdateCannotApplyCaptureToEarlierEconomicSeconds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var market = server.System<SWGlobalWarMarketSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                (long Republic, long Empire, string Goods, string Cargo) Run(bool regular)
                {
                    Assert.That(ticker.StartGameRule("SWDEconomyTestRule"), Is.True);
                    Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                    var simulation = market.Simulation!;
                    void At(int second, bool update)
                    {
                        timing.CurTick += 1;
                        timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                            timing.TimeBase.Item2);
                        if (update)
                            market.Update(0);
                    }
                    if (regular)
                        for (var second = 1; second <= 10; second++)
                            At(second, true);
                    else
                        At(10, false);
                    Assert.That(planets.TryGetPlanet("SWDEconomyTestFarm", out var farm), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(farm!.Value);
                    Assert.That(steps.TryQueueOwnership(farm.Value, SWGlobalWarPlanetLocation.Surface,
                        SWGlobalWarFaction.Empire, state.OwnershipRevision, campaign.Comp.Generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(campaign.Comp.Generation, timing.CurTick), Is.True);
                    Assert.That(simulation.Time, Is.EqualTo(TimeSpan.FromSeconds(10)));
                    Assert.That(simulation.Markets["SWDEconomyTestFarm"].CapturedAt, Is.EqualTo(TimeSpan.FromSeconds(10)));
                    At(20, true);
                    var result = (ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Treasury).Balance,
                        ledger.GetBalance(SWGlobalWarFaction.Empire, SWGlobalWarAccount.Treasury).Balance,
                        string.Join(';', simulation.Markets.SelectMany(m => m.Value.Goods.Select(g =>
                            $"{m.Key}/{g.Key}:{g.Value.Stock}/{g.Value.Consumed}/{g.Value.GrowthConsumed}/{g.Value.Produced}"))),
                        string.Join(';', simulation.Shipments.Select(s =>
                            $"{s.Commodity}/{s.Source}/{s.Destination}:{s.Quantity}/{s.Leg}/{s.ArrivesAt}/{s.Batch!.Owner}")));
                    Assert.That(result.Item1, Is.GreaterThan(450000), "Pre-capture consumption pays the previous owner.");
                    Assert.That(result.Item2, Is.GreaterThan(450000), "Post-capture consumption pays the new owner.");
                    ticker.EndGameRule(campaign.Owner);
                    // No game-loop tick ran during this clock injection. Restore it before allocating the next
                    // rule so PVS and GameTiming still agree on their current dirty-buffer index.
                    timing.TimeBase = savedTime;
                    timing.CurTick = savedTick;
                    return result;
                }
                var regular = Run(true);
                var delayed = Run(false);
                Assert.That(delayed, Is.EqualTo(regular));
            }
            finally
            {
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EndingFromTaxCallbackFreezesArrivingCargoAndRemainingCatchUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var market = server.System<SWGlobalWarMarketSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                Assert.That(ticker.StartGameRule("SWDEconomyTestRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var simulation = market.Simulation!;
                void At(int second)
                {
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    market.Update(0);
                }
                At(60);
                var arrival = simulation.Shipments.Where(s => s.ArrivesAt != null).Min(s => s.ArrivesAt!.Value);
                At((int) arrival.TotalSeconds - 1);
                Assert.That(simulation.Shipments.Any(s => s.ArrivesAt == simulation.Time + TimeSpan.FromSeconds(1)), Is.True);
                string Snapshot() => simulation.Time + ":" + string.Join(';', simulation.Markets.SelectMany(m =>
                    m.Value.Goods.Select(g => $"{m.Key}/{g.Key}:{g.Value.Stock}/{g.Value.Consumed}/" +
                        $"{g.Value.GrowthConsumed}/{g.Value.Produced}/{m.Value.Population}"))) + ":" +
                    string.Join(';', simulation.Shipments.Select(s => $"{s.Quantity}/{s.Leg}/{s.ArrivesAt}"));
                string? stopped = null;
                probe.LedgerChanged = () =>
                {
                    probe.LedgerChanged = null;
                    Assert.That(clock.TryAdvancePhase(campaign.Comp.Generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                    stopped = Snapshot();
                };
                At((int) arrival.TotalSeconds + 5);
                Assert.That(stopped, Is.Not.Null);
                Assert.That(Snapshot(), Is.EqualTo(stopped));
                Assert.That(simulation.Time, Is.EqualTo(arrival));
                market.Update(600);
                Assert.That(Snapshot(), Is.EqualTo(stopped));
                ticker.EndGameRule(campaign.Owner);
            }
            finally
            {
                probe.LedgerChanged = null;
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllCampaignProfilesUseTheirOwnGraphAndKeepStrategicOpeningReserves()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWGlobalWarCampaign"), Is.True);
            var model = markets.Simulation!;
            Assert.That(model.Generation, Is.Not.Null);
            Assert.That(model.Markets, Has.Count.EqualTo(22));
            Assert.That(model.Flows, Has.Count.EqualTo(163));
            Assert.That(model.Connections, Has.Count.EqualTo(174));
            foreach (var side in new[] { "R", "E" })
            {
                var capital = model.Markets[$"SWGlobalWar{side}0"].Goods["SWGlobalWarF"];
                Assert.That(capital.Stock, Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(240)));
                Assert.That(capital.Supply, Is.Zero);
                foreach (var sector in new[] { 7, 8 })
                {
                    var lanthanide = model.Markets[$"SWGlobalWar{side}{sector}"].Goods["SWGlobalWarLn"];
                    Assert.That(lanthanide.Stock, Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(180)));
                    Assert.That(lanthanide.Supply, Is.Zero);
                }
                var links = model.Connections.Values.Where(c => c.From == $"SWGlobalWar{side}6").ToArray();
                Assert.That(links, Has.Length.EqualTo(1));
                Assert.That(links.Single().To, Is.EqualTo($"SWGlobalWar{side}0"));
                Assert.That(links.Single().TravelTime, Is.EqualTo(TimeSpan.FromSeconds(120)));
                Assert.That(links.Single().Capacity, Is.EqualTo(64));
            }
            Assert.That(model.Markets["SWGlobalWarN1"].Goods["SWGlobalWarE"].TargetStock,
                Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(1)));
            Assert.That(model.Flows.Values.Any(f => f.Destination == "SWGlobalWarN1" && f.Commodity == "SWGlobalWarE"),
                Is.False);
        });
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            var model = markets.Simulation!;
            Assert.That(model.Time, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(9)));
            foreach (var market in model.Markets.Values)
            foreach (var (commodity, good) in market.Goods)
            {
                Assert.That(good.Stock + good.Incoming + good.ReservedIncoming, Is.LessThanOrEqualTo(good.Capacity));
                Assert.That(good.Incoming, Is.EqualTo(model.Shipments.Where(s => s.Destination == market.Planet &&
                    s.Commodity == commodity).Sum(s => s.Quantity)));
            }
            Assert.That(model.Shipments, Is.Not.Empty);
            Assert.That(model.Flows.Values.All(f => f.Dispatched >= 0), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RefusedTaxCompletesGoodsBeforeExpirationCallbackCanEndCampaign()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var market = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                Assert.That(ticker.StartGameRule("SWDEconomyTestRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var simulation = market.Simulation!;
                void At(int second)
                {
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    market.Update(0);
                }
                At(59);
                var faction = SWGlobalWarFaction.Republic;
                var overflow = new SWGlobalWarLedgerCommand(campaign.Comp.Generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Credit, faction, SWGlobalWarAccount.Treasury,
                    long.MaxValue - ledger.GetBalance(faction, SWGlobalWarAccount.Treasury).Balance,
                    Purpose: "overflow-boundary-fixture");
                Assert.That(ledger.TryExecuteSystem(overflow, out _), Is.True);
                var reserve = new SWGlobalWarLedgerCommand(campaign.Comp.Generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Reserve, faction, SWGlobalWarAccount.War, 1, Purpose: "expiry-boundary-fixture");
                Assert.That(ledger.TryExecuteSystem(reserve, out var order), Is.True);
                Assert.That(ledger.TryGetReservation(order, out var reservation), Is.True);
                // Inject only a deadline on a real server-created reservation. Employee authorization is covered by C;
                // this boundary test exercises the actual ledger Expire, failed batch and synchronous notification.
                var reservations = (Dictionary<Guid, SWGlobalWarMoneyReservation>) typeof(SWGlobalWarLedgerComponent)
                    .GetField(nameof(SWGlobalWarLedgerComponent.Reservations))!.GetValue(accounts.Comp)!;
                reservations[order] = reservation with { Expires = TimeSpan.FromSeconds(60) };
                var previousReceipts = simulation.ConsumptionReceiptCount;
                var previousFailures = simulation.FailedConsumptionReceiptCount;
                var farm = simulation.Markets["SWDEconomyTestFarm"];
                var food = farm.Goods["SWGlobalWarF"];
                var consumed = food.Consumed;
                var unmet = food.UnmetConsumption;
                var population = farm.Population;
                string Snapshot() => simulation.Time + ":" + simulation.ConsumptionReceiptCount + ":" +
                    simulation.FailedConsumptionReceiptCount + ":" + string.Join(';', simulation.Markets.SelectMany(m =>
                        m.Value.Goods.Select(g => $"{m.Key}/{g.Key}:{g.Value.Stock}/{g.Value.Reserved}/" +
                            $"{g.Value.Consumed}/{g.Value.UnmetConsumption}/{g.Value.GrowthConsumed}/{g.Value.Produced}/{m.Value.Population}"))) +
                    ":" + string.Join(';', simulation.Shipments.Select(s => $"{s.Quantity}/{s.Leg}/{s.ArrivesAt}"));
                string? stopped = null;
                probe.LedgerChanged = () =>
                {
                    probe.LedgerChanged = null;
                    Assert.That(ledger.TryGetReservation(order, out _), Is.False);
                    Assert.That(simulation.ConsumptionReceiptCount, Is.EqualTo(previousReceipts + 1));
                    Assert.That(simulation.FailedConsumptionReceiptCount, Is.EqualTo(previousFailures + 1));
                    Assert.That(food.Reserved, Is.Zero);
                    Assert.That(simulation.InternalReservationCount, Is.Zero);
                    Assert.That(food.Consumed, Is.EqualTo(consumed));
                    Assert.That(food.UnmetConsumption, Is.GreaterThan(unmet));
                    Assert.That(farm.Population, Is.LessThan(population));
                    Assert.That(clock.TryAdvancePhase(campaign.Comp.Generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                    stopped = Snapshot();
                };
                At(65);
                Assert.That(stopped, Is.Not.Null);
                Assert.That(Snapshot(), Is.EqualTo(stopped));
                Assert.That(simulation.Time, Is.EqualTo(TimeSpan.FromMinutes(1)));
                Assert.That(simulation.InternalReservationCount, Is.Zero);
                market.Update(600);
                Assert.That(Snapshot(), Is.EqualTo(stopped));
                ticker.EndGameRule(campaign.Owner);
            }
            finally
            {
                probe.LedgerChanged = null;
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealCampaignClockDeliversFarmFoodBeforeCapitalConsumptionAndTax()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var market = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        SWGlobalWarMarketSimulation simulation = default!;
        var callbacks = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWDEconomyTestRule"), Is.True);
            simulation = market.Simulation!;
            Assert.That(simulation.Generation, Is.Not.Null);
            Assert.That(simulation.Markets["SWDEconomyTestCapital"].Goods["SWGlobalWarF"].Stock, Is.Zero);
            probe.LedgerChanged = () =>
            {
                callbacks++;
                var consumed = simulation.Markets.Values.Where(m => m.Owner == SWGlobalWarFaction.Republic)
                    .Sum(m => m.Goods["SWGlobalWarF"].Consumed * 100L +
                              m.Goods["SWGlobalWarE"].Consumed * 2000L + m.Goods["SWGlobalWarL"].Consumed * 1000L);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Treasury).Balance,
                    Is.EqualTo(450000 + consumed / SWGlobalWarMarketUnits.PerUnit),
                    "A ledger callback must observe completed goods accounting, including carried fractional tax.");
                market.Update(600);
            };
        });
        try
        {
            await pair.RunSeconds(30);
            await server.WaitAssertion(() =>
            {
                var capital = simulation.Markets["SWDEconomyTestCapital"].Goods["SWGlobalWarF"];
                Assert.That(capital.Stock, Is.Zero);
                Assert.That(capital.Consumed, Is.Zero);
                Assert.That(capital.Incoming, Is.GreaterThan(0));
                Assert.That(simulation.Shipments.All(s => s.ArrivesAt > simulation.Time), Is.True);
            });
            await pair.RunSeconds(95);
            await server.WaitAssertion(() =>
            {
                var capital = simulation.Markets["SWDEconomyTestCapital"].Goods["SWGlobalWarF"];
                Assert.That(capital.Consumed, Is.GreaterThan(0));
                Assert.That(callbacks, Is.GreaterThan(0));
                var initial = SWGlobalWarMarketUnits.FromUnits(20);
                var goods = simulation.Markets.Values.Select(m => m.Goods["SWGlobalWarF"]).ToArray();
                Assert.That(goods.Sum(g => (long) g.Stock + g.Consumed + g.GrowthConsumed) +
                            simulation.Shipments.Where(s => s.Commodity == "SWGlobalWarF").Sum(s => (long) s.Quantity),
                    Is.EqualTo(initial + goods.Sum(g => g.Produced)));
                Assert.That(simulation.Time, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(124)));
                Assert.That(simulation.Time, Is.LessThan(TimeSpan.FromSeconds(127)),
                    "Reentrant Update(600) must not add an economic frame-time delta.");
            });
        }
        finally
        {
            await server.WaitPost(() => probe.LedgerChanged = null);
        }
        await pair.CleanReturnAsync();
    }
}
