#nullable enable
using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Pure economic checks; no server, maps, clock sleeps or physical shuttles are required.</summary>
[TestFixture]
public sealed class GlobalWarMarketTest
{
    private static SWGlobalWarCommodityDefinition Ore => new("ore", 1);
    private static SWGlobalWarMarketDefinition Market(string id, int stock, int consumption) =>
        new(id, new[] { new SWGlobalWarMarketGoodDefinition("ore", stock, consumption) });

    [Test]
    public void ConsumptionUsesExistingStockAndRecordsShortages()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, new[] { Market("A", 20, 60) },
            Array.Empty<SWGlobalWarTradeConnection>());
        model.Advance(TimeSpan.FromSeconds(30));
        var good = model.Markets["A"].Goods["ore"];
        Assert.Multiple(() =>
        {
            Assert.That(good.Stock, Is.Zero);
            Assert.That(good.TargetStock, Is.EqualTo(600));
            Assert.That(good.Demand, Is.EqualTo(600));
            Assert.That(good.Supply, Is.Zero);
            Assert.That(good.Consumed, Is.EqualTo(20));
            Assert.That(good.UnmetConsumption, Is.EqualTo(10));
        });
    }

    [Test]
    public void ConsumptionWaitsUntilAWholeItemIsDue()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, new[] { Market("A", 3, 1) },
            Array.Empty<SWGlobalWarTradeConnection>());
        var good = model.Markets["A"].Goods["ore"];
        model.Advance(TimeSpan.FromSeconds(59));
        Assert.That(good.Stock, Is.EqualTo(3));
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(good.Stock, Is.EqualTo(2));
        model.TrySetConsumption("A", "ore", 2);
        model.Advance(TimeSpan.FromSeconds(30));
        Assert.Multiple(() =>
        {
            Assert.That(good.Stock, Is.EqualTo(1));
            Assert.That(good.Consumed, Is.EqualTo(2));
            Assert.That(good.UnmetConsumption, Is.Zero);
        });
    }

    [Test]
    public void CargoLargerThanCapacityNeverDeparts()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { new SWGlobalWarCommodityDefinition("ore", 2) },
            new[] { Market("A", 10, 0), Market("B", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(3), 1) });
        model.Advance(TimeSpan.FromMinutes(20));
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments, Is.Empty);
            Assert.That(model.Markets["A"].Goods["ore"].Stock, Is.EqualTo(10));
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Markets["B"].Goods["ore"].Incoming, Is.Zero);
        });
    }

    [Test]
    public void CapacityRemainsOccupiedUntilArrival()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 100, 0), Market("B", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(5), 3) });
        model.Advance(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(model.Markets["A"].Goods["ore"].Stock, Is.EqualTo(97));
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Markets["B"].Goods["ore"].Incoming, Is.EqualTo(3));
            Assert.That(model.Shipments.Single().Quantity, Is.EqualTo(3));
        });
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(model.Markets["A"].Goods["ore"].Stock, Is.EqualTo(94));
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.EqualTo(3));
            Assert.That(model.Markets["B"].Goods["ore"].Incoming, Is.EqualTo(3));
            Assert.That(model.Shipments.Single().Quantity, Is.EqualTo(3));
            Assert.That(model.Shipments.Single().DepartedAt, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(model.Shipments.Single().ArrivesAt, Is.EqualTo(TimeSpan.FromSeconds(11)));
        });
    }

    [Test]
    public void ReplacementCargoIsNotDispatchedTwice()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 1000, 0), Market("B", 80, 8) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(30), 120) });
        model.Advance(TimeSpan.FromSeconds(7));
        Assert.Multiple(() =>
        {
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.EqualTo(80));
            Assert.That(model.Markets["B"].Goods["ore"].Incoming, Is.EqualTo(4));
            Assert.That(model.Shipments.Single().Quantity, Is.EqualTo(4));
            Assert.That(model.Markets["A"].Goods["ore"].Stock, Is.EqualTo(996));
        });
    }

    [TestCase(8, 30, 1, 4)]
    [TestCase(3, 30, 2, 3)]
    [TestCase(60, 0.1, 2, 2)]
    public void FullStocksReceiveReplacementForConsumptionDuringDelivery(int consumption, double legSeconds, int legs, int expected)
    {
        var target = consumption * 10;
        var markets = new[] { Market("A", 10000, 0), Market("M", 0, 0), Market("Z", target + consumption / 60, consumption) };
        var duration = TimeSpan.FromSeconds(legSeconds);
        var links = legs == 1
            ? new[] { new SWGlobalWarTradeConnection("A", "Z", duration, 1000) }
            : new[] { new SWGlobalWarTradeConnection("A", "M", duration, 1000), new SWGlobalWarTradeConnection("M", "Z", duration, 1000) };
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, markets, links);
        model.Advance(TimeSpan.FromSeconds(1));
        var recipient = model.Markets["Z"].Goods["ore"];
        Assert.Multiple(() =>
        {
            Assert.That(recipient.Stock, Is.EqualTo(target));
            Assert.That(recipient.Demand, Is.Zero, "Physical demand must retain its existing definition.");
            Assert.That(recipient.Incoming, Is.EqualTo(expected));
            Assert.That(model.Shipments.Single().Quantity, Is.EqualTo(expected));
        });
    }

    [Test]
    public void DeliveryCompensationMaintainsPhysicalBuffersAcrossThePlanetChain()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 10000, 0), Market("B", 20, 8), Market("C", 10, 3) },
            new[]
            {
                new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(30), 120),
                new SWGlobalWarTradeConnection("B", "A", TimeSpan.FromSeconds(30), 120),
                new SWGlobalWarTradeConnection("B", "C", TimeSpan.FromSeconds(30), 120),
                new SWGlobalWarTradeConnection("C", "B", TimeSpan.FromSeconds(30), 120),
            });
        model.Advance(TimeSpan.FromMinutes(3));
        for (var second = 0; second < 180; second++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            Assert.Multiple(() =>
            {
                Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.GreaterThanOrEqualTo(80), "The 30-second trip consumes four units.");
                Assert.That(model.Markets["C"].Goods["ore"].Stock, Is.GreaterThanOrEqualTo(30), "The two-leg trip consumes three units.");
            });
        }
    }

    [Test]
    public void ShipmentIsDebitedImmediatelyAndOnlyCreditedOnArrival()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, new[] { Market("A", 100, 0), Market("B", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(5), 600) });
        model.Advance(TimeSpan.FromSeconds(1));
        var source = model.Markets["A"].Goods["ore"];
        var target = model.Markets["B"].Goods["ore"];
        Assert.Multiple(() =>
        {
            Assert.That(source.Stock, Is.EqualTo(89));
            Assert.That(target.Stock, Is.Zero);
            Assert.That(target.Incoming, Is.EqualTo(11));
            Assert.That(target.ImportNeed, Is.Zero);
        });

        model.Advance(TimeSpan.FromSeconds(4));
        Assert.That(model.Shipments.Count, Is.EqualTo(1), "Already travelling goods must prevent duplicate dispatches.");
        Assert.That(target.Stock, Is.Zero);
        // Reducing consumption does not cancel an actual shipment that has already left its source.
        model.TrySetConsumption("B", "ore", 0);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(target.Stock, Is.EqualTo(11));
            Assert.That(target.Incoming, Is.Zero);
            Assert.That(model.Shipments, Is.Empty);
        });
    }

    [Test]
    public void ExportsPreserveTheSuppliersTenMinuteBuffer()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, new[] { Market("A", 20, 1), Market("B", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(5), 6000) });
        model.Advance(TimeSpan.FromSeconds(1));
        var source = model.Markets["A"].Goods["ore"];
        Assert.That(source.Stock, Is.EqualTo(source.TargetStock).Within(1e-9));
        Assert.That(model.Shipments.Sum(shipment => shipment.Quantity), Is.EqualTo(10));
    }

    [Test]
    public void ReverseTravelRequiresItsOwnDirectedConnection()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore }, new[] { Market("A", 0, 1), Market("B", 100, 0) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(5), 600) });
        model.Advance(TimeSpan.FromSeconds(20));
        Assert.That(model.Shipments, Is.Empty);
        Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.EqualTo(100));
    }

    [Test]
    public void FastestAvailableSupplierIsChosen()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 100, 0), Market("B", 100, 0), Market("C", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "C", TimeSpan.FromSeconds(10), 600), new SWGlobalWarTradeConnection("B", "C", TimeSpan.FromSeconds(3), 600) });
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments.Single().Source, Is.EqualTo("B"));
        Assert.That(model.Markets["A"].Goods["ore"].Stock, Is.EqualTo(100));
    }

    [Test]
    public void TransitUsesEveryLegWithoutBecomingIntermediateMarketStock()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 100, 0), Market("B", 0, 0), Market("C", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(3), 600), new SWGlobalWarTradeConnection("B", "C", TimeSpan.FromSeconds(3), 600) });
        model.Advance(TimeSpan.FromSeconds(4));
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments.Single().Route, Is.EqualTo(new[] { "A", "B", "C" }));
            Assert.That(model.Shipments.Single().Leg, Is.EqualTo(1));
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Markets["C"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Markets["C"].Goods["ore"].Incoming, Is.EqualTo(11));
        });
        model.Advance(TimeSpan.FromSeconds(3));
        Assert.That(model.Markets["C"].Goods["ore"].Stock, Is.EqualTo(11));
        Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
    }

    [Test]
    public void RoutesExcludeLinksTooSmallForAWholeItem()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { new SWGlobalWarCommodityDefinition("ore", 2) },
            new[] { Market("A", 20, 0), Market("B", 0, 0), Market("C", 0, 0), Market("D", 0, 1) },
            new[]
            {
                new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(1), 4),
                new SWGlobalWarTradeConnection("B", "D", TimeSpan.FromSeconds(1), 1),
                new SWGlobalWarTradeConnection("A", "C", TimeSpan.FromSeconds(2), 4),
                new SWGlobalWarTradeConnection("C", "D", TimeSpan.FromSeconds(2), 4),
            });
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments.Single().Route, Is.EqualTo(new[] { "A", "C", "D" }));
        Assert.That(model.Shipments.Single().Quantity, Is.EqualTo(2));
    }

    [Test]
    public void WaitingTransitReleasesItsPreviousLane()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 10, 0), Market("B", 0, 0), Market("C", 0, 1) },
            new[]
            {
                new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(1), 10),
                new SWGlobalWarTradeConnection("B", "C", TimeSpan.FromSeconds(5), 4),
            });
        model.Advance(TimeSpan.FromSeconds(1));
        model.TryAddStock("A", "ore", 10);
        model.TrySetConsumption("B", "ore", 1);
        model.TrySetConsumption("C", "ore", 0);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments.Where(shipment => shipment.Destination == "C" && shipment.ArrivesAt == null)
                .Sum(shipment => shipment.Quantity), Is.EqualTo(6));
            Assert.That(model.Shipments.Where(shipment => shipment.Destination == "C" && shipment.ArrivesAt != null)
                .Sum(shipment => shipment.Quantity), Is.EqualTo(4));
            Assert.That(model.Shipments.Single(shipment => shipment.Destination == "B").Quantity, Is.EqualTo(10),
                "Waiting on B must not hold the A-B capacity needed by the next shipment.");
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Markets["B"].Goods["ore"].Incoming, Is.EqualTo(10));
        });
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments.Where(shipment => shipment.Destination == "C" && shipment.ArrivesAt == null)
            .Sum(shipment => shipment.Quantity), Is.EqualTo(6), "The occupied B-C lane must remain full until arrival.");
    }

    [Test]
    public void OppositeFlowsAndDifferentCommoditiesShareConcurrentCapacity()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore, new SWGlobalWarCommodityDefinition("fuel", 2) },
            new[]
            {
                new SWGlobalWarMarketDefinition("A", new[]
                {
                    new SWGlobalWarMarketGoodDefinition("ore", 100, 0), new SWGlobalWarMarketGoodDefinition("fuel", 0, 1),
                }),
                new SWGlobalWarMarketDefinition("B", new[]
                {
                    new SWGlobalWarMarketGoodDefinition("ore", 0, 1), new SWGlobalWarMarketGoodDefinition("fuel", 100, 0),
                }),
            },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(10), 3), new SWGlobalWarTradeConnection("B", "A", TimeSpan.FromSeconds(10), 3) });
        model.Advance(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments.Sum(shipment => shipment.Quantity * (shipment.Commodity == "fuel" ? 2 : 1)),
                Is.EqualTo(3).Within(1e-9));
            Assert.That(model.Shipments.Select(shipment => shipment.Commodity), Is.EquivalentTo(new[] { "ore", "fuel" }));
        });
        for (var step = 0; step < 30; step++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            Assert.That(model.Shipments.Sum(shipment => shipment.Quantity * (shipment.Commodity == "fuel" ? 2 : 1)),
                Is.LessThanOrEqualTo(3), "Arrivals must release the shared capacity without exceeding it on reuse.");
        }
    }

    [TestCase(10, 0, 4, 1)]
    [TestCase(3, 5, 6, 1)]
    [TestCase(3, 5, 6, 2)]
    public void TransitSplitsCargoToUseAvailableCapacity(int sourceA, int sourceC, int capacity, double cargoVolume)
    {
        var total = sourceA + sourceC;
        var model = new SWGlobalWarMarketSimulation(new[] { new SWGlobalWarCommodityDefinition("ore", cargoVolume) },
            new[] { Market("A", sourceA, 0), Market("B", 0, 0), Market("C", sourceC, 0), Market("D", 0, 1) },
            new[]
            {
                new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(1), 10 * cargoVolume),
                new SWGlobalWarTradeConnection("C", "B", TimeSpan.FromSeconds(1), 10 * cargoVolume),
                new SWGlobalWarTradeConnection("B", "D", TimeSpan.FromSeconds(1), capacity * cargoVolume),
            });
        model.Advance(TimeSpan.FromSeconds(1));
        var target = model.Markets["D"].Goods["ore"];
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments.Count, Is.EqualTo(sourceC > 0 ? 2 : 1),
                "The first leg must not limit batches to the narrowest downstream link.");
            Assert.That(target.Incoming, Is.EqualTo(total));
        });
        model.TrySetConsumption("D", "ore", 0);

        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments.Where(shipment => shipment.ArrivesAt == TimeSpan.FromSeconds(3)).Sum(shipment => shipment.Quantity),
                Is.EqualTo(capacity), "Transit must use capacity even when only part of the next shipment fits.");
            Assert.That(model.Shipments.Where(shipment => shipment.ArrivesAt == null).Sum(shipment => shipment.Quantity),
                Is.EqualTo(total - capacity));
            Assert.That(target.Stock, Is.Zero, "Split cargo must still spend time travelling on its next leg.");
            Assert.That(target.Incoming, Is.EqualTo(total));
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
            Assert.That(model.Shipments.All(shipment => shipment.Leg == 1 && shipment.DepartedAt == TimeSpan.FromSeconds(1)), Is.True);
        });

        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(target.Stock, Is.EqualTo(capacity));
            Assert.That(model.Shipments.Where(shipment => shipment.ArrivesAt == TimeSpan.FromSeconds(4))
                .Sum(shipment => shipment.Quantity), Is.EqualTo(Math.Min(capacity, total - capacity)),
                "All arrivals must release capacity before any waiting cargo is forwarded this step.");
        });

        for (var step = 0; step < 10 && model.Shipments.Count > 0; step++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            Assert.That(model.Markets.Values.Sum(market => market.Goods["ore"].Stock)
                + model.Shipments.Sum(shipment => shipment.Quantity), Is.EqualTo(total).Within(1e-9));
            Assert.That(target.Incoming, Is.EqualTo(model.Shipments.Sum(shipment => shipment.Quantity)).Within(1e-9));
        }
        Assert.Multiple(() =>
        {
            Assert.That(model.Shipments, Is.Empty);
            Assert.That(target.Stock, Is.EqualTo(total));
            Assert.That(target.Incoming, Is.Zero);
            Assert.That(model.Markets["B"].Goods["ore"].Stock, Is.Zero);
        });
    }

    [Test]
    public void StockPlusConsumptionPlusTransitConservesEveryCommodity()
    {
        var model = new SWGlobalWarMarketSimulation(new[] { Ore },
            new[] { Market("A", 1000, 1), Market("B", 5, 10), Market("C", 5, 10) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(4), 120), new SWGlobalWarTradeConnection("B", "C", TimeSpan.FromSeconds(4), 120) });
        for (var second = 0; second < 120; second++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            var accounted = model.Markets.Values.Sum(market => market.Goods["ore"].Stock + market.Goods["ore"].Consumed)
                + model.Shipments.Sum(shipment => shipment.Quantity);
            Assert.That(accounted, Is.EqualTo(1010).Within(1e-7));
            foreach (var market in model.Markets.Values)
            {
                Assert.That(market.Goods["ore"].Incoming, Is.EqualTo(model.Shipments
                    .Where(shipment => shipment.Destination == market.Planet).Sum(shipment => shipment.Quantity)).Within(1e-7));
            }
        }
    }

    [Test]
    public void SplittingElapsedTimeIntoFramesDoesNotChangeTheResult()
    {
        SWGlobalWarMarketSimulation Create() => new(new[] { Ore }, new[] { Market("A", 100, 0), Market("B", 0, 1) },
            new[] { new SWGlobalWarTradeConnection("A", "B", TimeSpan.FromSeconds(5), 60) });
        var whole = Create();
        var frames = Create();
        whole.Advance(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 120; i++)
            frames.Advance(TimeSpan.FromSeconds(.25));
        Assert.That(frames.Time, Is.EqualTo(whole.Time));
        foreach (var planet in new[] { "A", "B" })
        {
            Assert.That(frames.Markets[planet].Goods["ore"].Stock, Is.EqualTo(whole.Markets[planet].Goods["ore"].Stock));
            Assert.That(frames.Markets[planet].Goods["ore"].Incoming, Is.EqualTo(whole.Markets[planet].Goods["ore"].Incoming));
        }
    }
}
