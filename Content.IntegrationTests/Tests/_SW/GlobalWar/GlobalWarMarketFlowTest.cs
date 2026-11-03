using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed partial class GlobalWarMarketFlowTest
{
    private static int Q(decimal value) => SWGlobalWarMarketUnits.FromUnits(value);
    private static SWGlobalWarTradeDecision Allow(SWGlobalWarTradeRequest request) =>
        new(true, string.Empty, 0, 0, 0, 0, 0);

    private static SWGlobalWarEconomyFlowPrototype Flow(string id, string source, string destination,
        string commodity, decimal rate, SWGlobalWarFlowRateMode mode = SWGlobalWarFlowRateMode.Fixed,
        SWGlobalWarFlowPriority priority = SWGlobalWarFlowPriority.DomesticCivilian)
    {
        var definition = new SWGlobalWarEconomyFlowPrototype
        {
            Source = source, Destination = destination, Commodity = commodity, Share = rate,
            RateMode = mode, Priority = priority,
        };
        typeof(SWGlobalWarEconomyFlowPrototype).GetProperty(nameof(SWGlobalWarEconomyFlowPrototype.ID))!
            .SetValue(definition, id);
        return definition;
    }

    private static SWGlobalWarEconomyProfilePrototype Industry(bool neutral = false) => new()
    {
        HubCapacity = 50, HalfProductionBeforeThirtyMinutes = neutral,
        Recipes = [new() { Output = "S", PerMinute = 4, Inputs = { ["Fe"] = 2, ["C"] = 1 } }],
    };

    private static void Withdraw(SWGlobalWarMarketSimulation model, string planet, string commodity, decimal amount)
    {
        var request = new SWGlobalWarStockRequest(model.Generation!.Value, Guid.NewGuid(), planet, commodity,
            Q(amount), model.Markets[planet].Owner, "flow-test-use");
        Assert.That(model.TryReserveStock(request, out _), Is.True);
        Assert.That(model.TryCommitStock(request.Generation, request.Operation, out _), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UnavailableNeutralImportCannotSuppressLocalSteel(bool blocked)
    {
        var model = new SWGlobalWarMarketSimulation([new("S", 1), new("Fe", 1), new("C", 1)],
            [new("H", [new("S", Q(40), 0), new("Fe", Q(80), 0), new("C", Q(40), 0)],
                    SWGlobalWarFaction.Republic, Industry()),
                new("N", [new("S", 0, 0), new("Fe", Q(80), 0), new("C", blocked ? Q(40) : 0, 0)],
                    SWGlobalWarFaction.Neutral, Industry(true))],
            [new("N", "H", TimeSpan.FromMinutes(1), 64)], Guid.NewGuid());
        model.ConfigureFlows([Flow("import", "N", "H", "S", 0.25m, SWGlobalWarFlowRateMode.ProducerSurplus)]);
        model.TradePolicy = request => blocked && request.Sender.Id == "N"
            ? new(false, "blocked", 0, 0, 0, 0, 0) : Allow(request);
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromMinutes(30));
        Withdraw(model, "H", "S", 1);
        model.Advance(TimeSpan.FromMinutes(1));
        var home = model.Markets["H"].Goods;
        Assert.That(home["S"].Stock, Is.GreaterThan(Q(39)));
        Assert.That(home["S"].Produced, Is.GreaterThan(0));
        Assert.That(home["Fe"].RecipeConsumed, Is.EqualTo(home["S"].Produced * 2));
        Assert.That(home["C"].RecipeConsumed, Is.EqualTo(home["S"].Produced));
        Assert.That(model.Flows["import"].PerMinute, Is.EqualTo(1));
        Assert.That(model.Flows["import"].Dispatched, Is.Zero);
        if (!blocked)
        {
            Assert.That(model.Markets["N"].Goods["S"].Produced, Is.Zero);
            Assert.That(model.Markets["N"].Goods["Fe"].RecipeConsumed, Is.Zero);
            Assert.That(model.Markets["N"].ProductionOrders["S"], Is.GreaterThan(0));
        }
    }

    [TestCase(600)]
    [TestCase(40)]
    public void FinishedSurplusDoesNotOrderUnusedInputs(int stock)
    {
        var model = new SWGlobalWarMarketSimulation([new("S", 1), new("Fe", 1), new("C", 1)],
            [new("H", [new("S", Q(stock), Q(0.1m)), new("Fe", 0, 0), new("C", 0, 0)],
                    SWGlobalWarFaction.Republic, Industry()),
                new("R", [new("Fe", Q(100), 0), new("C", Q(100), 0)],
                    SWGlobalWarFaction.Republic, new()),
                new("D", [new("S", 0, Q(1))], SWGlobalWarFaction.Republic, new())],
            [new("R", "H", TimeSpan.FromMinutes(1), 64), new("H", "D", TimeSpan.FromMinutes(1), 64)],
            Guid.NewGuid());
        model.Markets["H"].Goods["S"].MinimumBuffer = Q(20);
        model.ConfigureFlows([Flow("export", "H", "D", "S", 1),
            Flow("iron", "R", "H", "Fe", 1, SWGlobalWarFlowRateMode.ProductionInput),
            Flow("coal", "R", "H", "C", 1, SWGlobalWarFlowRateMode.ProductionInput)]);
        model.TradePolicy = Allow;
        model.SettleConsumption = (_, complete) => { complete(true); return true; };
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromMinutes(1));
        Assert.That(model.Flows["export"].Dispatched, Is.GreaterThan(0));
        Assert.That(model.Markets["H"].Goods["S"].Produced, Is.Zero);
        Assert.That(model.Markets["H"].ProductionOrders["S"], Is.Zero);
        foreach (var id in new[] { "iron", "coal" })
        {
            Assert.That(model.Flows[id].PerMinute, Is.Zero);
            Assert.That(model.Flows[id].Dispatched, Is.Zero);
        }
    }

    [Test]
    public void ActualNeutralShipmentsReplaceLocalOutputWithoutErasingImportDemand()
    {
        SWGlobalWarMarketSimulation Run(bool blocked)
        {
            var model = new SWGlobalWarMarketSimulation([new("S", 1), new("Fe", 1), new("C", 1)],
                [new("H", [new("S", Q(20), Q(0.1m)), new("Fe", Q(80), 0), new("C", Q(40), 0)],
                        SWGlobalWarFaction.Republic, Industry()),
                    new("N", [new("S", Q(1), Q(0.1m)), new("Fe", Q(120), 0), new("C", Q(60), 0)],
                        SWGlobalWarFaction.Neutral, Industry()),
                    new("D", [new("S", Q(10.5m), Q(1.05m))], SWGlobalWarFaction.Republic, new())],
                [new("N", "H", TimeSpan.FromMinutes(1), 64), new("H", "D", TimeSpan.FromMinutes(1), 64)],
                Guid.NewGuid());
            model.ConfigureFlows([Flow("neutral", "N", "H", "S", 0.25m, SWGlobalWarFlowRateMode.ProducerSurplus),
                Flow("domestic", "H", "D", "S", 1.05m)]);
            model.TradePolicy = request => blocked && request.Sender.Id == "N"
                ? new(false, "blocked", 0, 0, 0, 0, 0) : Allow(request);
            model.SettleConsumption = (_, complete) => { complete(true); return true; };
            model.DemographyEnabled = false;
            model.Advance(TimeSpan.FromMinutes(10));
            return model;
        }
        var supplied = Run(false);
        var blocked = Run(true);
        Assert.That(supplied.Flows["neutral"].Dispatched, Is.GreaterThan(Q(8)));
        Assert.That(supplied.Flows["neutral"].Received, Is.GreaterThan(Q(7)));
        Assert.That(blocked.Flows["neutral"].Dispatched, Is.Zero);
        Assert.That(supplied.Markets["H"].Goods["S"].Produced,
            Is.LessThan(blocked.Markets["H"].Goods["S"].Produced / 2));
        foreach (var model in new[] { supplied, blocked })
        {
            Assert.That(model.Markets["H"].Goods["S"].Stock, Is.GreaterThan(Q(19)));
            Assert.That(model.Markets["D"].Goods["S"].UnmetConsumption, Is.Zero);
            var neutral = model.Markets["N"].Goods;
            Assert.That(neutral["Fe"].RecipeConsumed, Is.EqualTo(2 * neutral["S"].Produced));
            Assert.That(neutral["C"].RecipeConsumed, Is.EqualTo(neutral["S"].Produced));
        }
    }

    [Test]
    public void LocalReplacementCannotEraseExportProductionDependingOnPlanetNames()
    {
        foreach (var source in new[] { "ANeutral", "ZNeutral" })
        {
            var producer = new SWGlobalWarEconomyProfilePrototype
            {
                HubCapacity = 50, Recipes = [new() { Output = "E", PerMinute = 1 }],
            };
            var home = new SWGlobalWarEconomyProfilePrototype
            {
                HubCapacity = 50, Recipes = [new() { Output = "E", PerMinute = 2 }],
            };
            var worlds = new List<SWGlobalWarMarketDefinition>
            {
                new(source, [new("E", Q(1), Q(0.1m))], SWGlobalWarFaction.Neutral, producer),
                new("Demand", [new("E", Q(42), Q(4.2m))], SWGlobalWarFaction.Republic, new()),
            };
            var links = new List<SWGlobalWarTradeConnection>();
            var flows = new List<SWGlobalWarEconomyFlowPrototype>();
            for (var i = 0; i < 4; i++)
            {
                var name = "Home" + i;
                worlds.Add(new(name, [new("E", Q(20), Q(0.1m))], SWGlobalWarFaction.Republic, home));
                links.Add(new(source, name, TimeSpan.FromMinutes(1), 64));
                links.Add(new(name, "Demand", TimeSpan.FromMinutes(1), 64));
                flows.Add(Flow("import" + i, source, name, "E", 0.25m, SWGlobalWarFlowRateMode.ProducerSurplus));
                flows.Add(Flow("use" + i, name, "Demand", "E", 1.05m));
            }
            var model = new SWGlobalWarMarketSimulation([new("E", 1)], worlds, links, Guid.NewGuid());
            model.Markets[source].Goods["E"].MinimumBuffer = Q(1);
            foreach (var world in model.Markets.Values.Where(w => w.Planet.StartsWith("Home")))
                world.Goods["E"].MinimumBuffer = Q(20);
            model.ConfigureFlows(flows);
            model.TradePolicy = Allow;
            model.SettleConsumption = (_, complete) => { complete(true); return true; };
            model.DemographyEnabled = false;
            model.Advance(TimeSpan.FromMinutes(20));
            var before = model.Markets[source].Goods["E"].Produced;
            var received = Enumerable.Range(0, 4).Select(i => model.Flows["import" + i].Received).ToArray();
            model.Advance(TimeSpan.FromMinutes(10));
            Assert.That((model.Markets[source].Goods["E"].Produced - before) / (decimal) Q(10),
                Is.EqualTo(1m).Within(0.001m), source);
            for (var i = 0; i < 4; i++)
                Assert.That((model.Flows["import" + i].Received - received[i]) / (decimal) Q(10),
                    Is.EqualTo(0.225m).Within(0.001m), source + "/" + i);
        }
    }

    [Test]
    public void CoveredPipelineKeepsRecipeAndMineOrdersInTheSamePlan()
    {
        var model = new SWGlobalWarMarketSimulation([new("E", 1), new("Au", 1)],
            [new("Mine", [new("Au", 0, 0)], SWGlobalWarFaction.Republic,
                    new() { HubCapacity = 50, Recipes = [new() { Output = "Au", PerMinute = 42 }] }),
                new("Industry", [new("E", Q(1), Q(0.1m)), new("Au", Q(66), 0)], SWGlobalWarFaction.Republic,
                    new() { HubCapacity = 50, Recipes = [new() { Output = "E", PerMinute = 2, Inputs = { ["Au"] = 6 } }] }),
                new("Consumer", [new("E", Q(10), Q(1))], SWGlobalWarFaction.Republic, new())],
            [new("Mine", "Industry", TimeSpan.FromMinutes(1), 64),
                new("Industry", "Consumer", TimeSpan.FromMinutes(1), 64)], Guid.NewGuid());
        model.ConfigureFlows([Flow("gold", "Mine", "Industry", "Au", 1, SWGlobalWarFlowRateMode.ProductionInput),
            Flow("electronics", "Industry", "Consumer", "E", 1)]);
        model.TradePolicy = Allow;
        model.SettleConsumption = (_, complete) => { complete(true); return true; };
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Markets["Industry"].ProductionOrders["E"], Is.EqualTo(1.1m));
        Assert.That(model.Markets["Mine"].ProductionOrders["Au"], Is.EqualTo(6.6m));
        Assert.That(model.Flows["gold"].PerMinute, Is.EqualTo(6.6m));
        model.Advance(TimeSpan.FromMinutes(20) - TimeSpan.FromSeconds(1));
        var before = model.Flows["gold"].Received;
        var produced = model.Markets["Industry"].Goods["E"].Produced;
        for (var second = 0; second < 600; second++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            Assert.That(model.Markets["Industry"].ProductionOrders["E"], Is.GreaterThan(1));
            Assert.That(model.Flows["gold"].PerMinute, Is.GreaterThan(6));
        }
        Assert.That((model.Flows["gold"].Received - before) / (decimal) Q(10), Is.EqualTo(6.6m).Within(0.01m));
        Assert.That((model.Markets["Industry"].Goods["E"].Produced - produced) / (decimal) Q(10),
            Is.EqualTo(1.1m).Within(0.001m));
        Assert.That(model.Markets["Consumer"].Goods["E"].UnmetConsumption, Is.Zero);
    }

    [Test]
    public void MissingInputKeepsItsOrderWithoutSpendingOtherInputs()
    {
        var model = new SWGlobalWarMarketSimulation([new("S", 1), new("Fe", 1), new("C", 1)],
            [new("H", [new("S", Q(40), 0), new("Fe", Q(80), 0), new("C", 0, 0)],
                    SWGlobalWarFaction.Republic, Industry()),
                new("R", [new("C", 0, 0)], SWGlobalWarFaction.Republic, new())],
            [new("R", "H", TimeSpan.FromMinutes(1), 64)], Guid.NewGuid());
        model.ConfigureFlows([Flow("coal", "R", "H", "C", 1, SWGlobalWarFlowRateMode.ProductionInput)]);
        model.TradePolicy = Allow;
        Withdraw(model, "H", "S", 1);
        model.Advance(TimeSpan.FromMinutes(1));
        Assert.That(model.Flows["coal"].PerMinute, Is.GreaterThan(0));
        Assert.That(model.Markets["H"].Goods["C"].ProductionDemandPerMinute, Is.GreaterThan(0));
        Assert.That(model.Markets["H"].Goods["S"].Stock, Is.EqualTo(Q(39)));
        Assert.That(model.Markets["H"].Goods["S"].Produced, Is.Zero);
        Assert.That(model.Markets["H"].Goods["Fe"].Stock, Is.EqualTo(Q(80)));
        Assert.That(model.Markets["H"].Goods["Fe"].RecipeConsumed, Is.Zero);
    }

    [Test]
    public void NeutralRestorationChangesCapacityWithoutIssuingOpeningStockAgain()
    {
        var model = new SWGlobalWarMarketSimulation([new("S", 1), new("Fe", 1), new("C", 1)],
            [new("N", [new("S", 0, 0), new("Fe", Q(120), 0), new("C", Q(60), 0)],
                SWGlobalWarFaction.Neutral, Industry(true))], [], Guid.NewGuid());
        var goods = model.Markets["N"].Goods;
        goods["S"].MinimumBuffer = Q(40);
        model.Advance(TimeSpan.FromSeconds(1799));
        Withdraw(model, "N", "S", 10);
        var before = goods["S"].Produced;
        model.Advance(TimeSpan.FromSeconds(1));
        var boundary = goods["S"].Produced;
        model.Advance(TimeSpan.FromSeconds(1));
        var after = goods["S"].Produced;
        Assert.That(boundary - before, Is.EqualTo(Q(2) / 60));
        Assert.That(after - boundary, Is.EqualTo(Q(4) / 60));
        Assert.That(goods["Fe"].Stock + goods["Fe"].RecipeConsumed, Is.EqualTo(Q(120)));
        Assert.That(goods["C"].Stock + goods["C"].RecipeConsumed, Is.EqualTo(Q(60)));
        Assert.That(goods["S"].Stock + Q(10), Is.EqualTo(goods["S"].Produced));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ScarceHubUsesBufferThenRotatesTiesIndependentOfFlowNames(bool reversed)
    {
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("Farm", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 25 }),
                new("A", [new("F", Q(15), Q(10))], SWGlobalWarFaction.Republic, new()),
                new("B", [new("F", Q(15), Q(10))], SWGlobalWarFaction.Empire, new())],
            [new("Farm", "A", TimeSpan.FromMinutes(1), 64), new("Farm", "B", TimeSpan.FromMinutes(1), 64)],
            Guid.NewGuid());
        model.Markets["A"].Population = model.Markets["B"].Population = 1.5m;
        model.DemographyEnabled = false;
        model.TradePolicy = Allow;
        var flows = new[] { Flow(reversed ? "zzz" : "aaa", "Farm", "A", "F", 1,
                SWGlobalWarFlowRateMode.RecipientMandatory),
            Flow(reversed ? "aaa" : "zzz", "Farm", "B", "F", 1, SWGlobalWarFlowRateMode.RecipientMandatory) };
        model.ConfigureFlows(reversed ? flows.Reverse() : flows);
        // Equal, unchanged available buffers isolate the tie rule from later population feedback.
        model.Advance(TimeSpan.FromMinutes(1));
        var a = model.Flows.Values.Single(f => f.Destination == "A").Dispatched;
        var b = model.Flows.Values.Single(f => f.Destination == "B").Dispatched;
        Assert.That(a, Is.GreaterThan(Q(12)));
        Assert.That(b, Is.GreaterThan(Q(12)));
        Assert.That(Math.Abs(a - b), Is.LessThanOrEqualTo(Q(25) / 60));
        Assert.That(a + b, Is.InRange(Q(25) - 120, Q(25)));
        Withdraw(model, "B", "F", 14);
        var prior = model.Flows.Values.Single(f => f.Destination == "B").Dispatched;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows.Values.Single(f => f.Destination == "B").Dispatched - prior,
            Is.GreaterThanOrEqualTo(Q(25) / 60 - 2), "The shorter available buffer wins regardless of its identifier.");
        Assert.That(model.Markets.Values.Sum(m => m.Goods["F"].Stock) + model.Shipments.Sum(s => s.Quantity),
            Is.EqualTo(Q(116)));
    }

    [Test]
    public void ReturnMovesSpaceOnceAndUsesRealTimeAndSplitHubCapacity()
    {
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 180 }),
                new("B", [new("F", 0, 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("C", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic, new())],
            [new("A", "B", TimeSpan.FromSeconds(2), 64), new("B", "C", TimeSpan.FromSeconds(2), 64),
                new("B", "A", TimeSpan.FromSeconds(2), 64)], Guid.NewGuid());
        var returnAllowed = false;
        model.TradePolicy = request => request.Current.Id == "B" &&
            (request.Next.Id == "C" || !returnAllowed)
                ? new(false, "blocked", 0, 0, 0, 0, 0) : Allow(request);
        model.ConfigureFlows([Flow("outbound", "A", "C", "F", 180)]);
        model.Advance(TimeSpan.FromSeconds(1));
        model.Flows["outbound"].Enabled = false;
        var shipment = model.Shipments.Single();
        var source = model.Markets["A"].Goods["F"];
        var recipient = model.Markets["C"].Goods["F"];
        var request = new SWGlobalWarMarketReturnRequest(model.Generation!.Value, Guid.NewGuid(), shipment.Id,
            SWGlobalWarFaction.Republic, "A", shipment.Quantity);
        Assert.That(shipment.Quantity, Is.EqualTo(Q(3)));
        Assert.That(model.TryReturnShipment(request, out _), Is.False, "In-transit cargo cannot change its current leg.");
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(shipment.ArrivesAt, Is.Null);
        Assert.That(shipment.Route[shipment.Leg], Is.EqualTo("B"));
        Assert.That(recipient.Incoming, Is.EqualTo(Q(3)));
        source.Capacity = source.Stock;
        Assert.That(model.TryReturnShipment(request, out _), Is.False);
        Assert.That(recipient.Incoming, Is.EqualTo(Q(3)));
        Assert.That(source.Incoming, Is.Zero);
        source.Capacity = SWGlobalWarMarketUnits.StockCapacity;
        Assert.That(model.TryReturnShipment(request with { Generation = Guid.NewGuid() }, out _), Is.False);
        Assert.That(model.TryReturnShipment(request with { Owner = SWGlobalWarFaction.Empire }, out _), Is.False);
        Assert.That(model.TryReturnShipment(request with { Quantity = Q(2) }, out _), Is.False);
        Assert.That(model.TryReturnShipment(request, out var receipt), Is.True);
        Assert.That(receipt.OriginalSource, Is.EqualTo("A"));
        Assert.That(receipt.OriginalDestination, Is.EqualTo("C"));
        Assert.That(receipt.DepartureReceipt, Is.EqualTo(shipment.DepartureReceipt));
        Assert.That(model.TryReturnShipment(request, out var replay), Is.True);
        Assert.That(replay, Is.SameAs(receipt));
        Assert.That(recipient.Incoming, Is.Zero);
        Assert.That(source.Incoming, Is.EqualTo(Q(3)));
        Assert.That(source.Stock, Is.EqualTo(Q(97)));
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(model.Shipments, Has.Count.EqualTo(1));
        Assert.That(shipment.ArrivesAt, Is.Null);
        Assert.That(shipment.HoldReason, Is.EqualTo("blocked"));
        Assert.That(source.Stock, Is.EqualTo(Q(97)));
        model.Markets["B"].SendBudget = 0;
        returnAllowed = true;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments, Has.Count.EqualTo(2));
        Assert.That(model.Shipments.Single(s => s.ArrivesAt != null).Quantity, Is.EqualTo(Q(1)));
        Assert.That(model.Shipments.Single(s => s.ArrivesAt != null).ArrivesAt, Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(source.Stock, Is.EqualTo(Q(97)));
        Assert.That(model.Shipments.Sum(s => s.Quantity), Is.EqualTo(Q(3)));
        Assert.That(model.Shipments.Select(s => s.Id).Distinct().Count(), Is.EqualTo(2));
        Assert.That(model.Shipments.All(s => s.ReturnOperation == request.Operation &&
            s.Batch!.Origin == SWGlobalWarFaction.Republic && s.DeliveryParticipant == request.Owner), Is.True);
        model.Advance(TimeSpan.FromSeconds(4));
        Assert.That(model.Shipments, Is.Empty);
        Assert.That(source.Stock, Is.EqualTo(Q(100)));
        Assert.That(source.Incoming, Is.Zero);
        Assert.That(recipient.Stock, Is.Zero);
        Assert.That(recipient.Incoming, Is.Zero);
        Assert.That(model.Flows["outbound"].Received, Is.Zero);
        Assert.That(model.Flows["outbound"].Returned, Is.EqualTo(Q(3)));
        Assert.That(model.TryReturnShipment(request, out replay), Is.True);
        Assert.That(replay.ReceivedQuantity, Is.EqualTo(Q(3)));
        Assert.That(model.TryReturnShipment(request with { Quantity = Q(2) }, out _), Is.False);
        Assert.That(source.Stock, Is.EqualTo(Q(100)));
    }

    [Test]
    public void CapturedReturnRemainderGetsANewOrderWithoutRewritingThePartAlreadyInFlight()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 180 }),
                new("B", [new("F", 0, 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("C", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic, new()),
                new("D", [new("F", 0, 0)], SWGlobalWarFaction.Empire, new())],
            [new("A", "B", TimeSpan.FromSeconds(2), 64), new("B", "C", TimeSpan.FromSeconds(2), 64),
                new("B", "A", TimeSpan.FromSeconds(2), 64), new("B", "D", TimeSpan.FromSeconds(2), 64)], generation);
        model.TradePolicy = request => request.Next.Id == "C"
            ? new(false, "blocked", 0, 0, 0, 0, 0) : Allow(request);
        model.ConfigureFlows([Flow("outbound", "A", "C", "F", 180)]);
        model.Advance(TimeSpan.FromSeconds(1));
        model.Flows["outbound"].Enabled = false;
        model.Advance(TimeSpan.FromSeconds(2));
        var remainder = model.Shipments.Single();
        var oldRequest = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), remainder.Id,
            SWGlobalWarFaction.Republic, "A", Q(3));
        Assert.That(model.TryReturnShipment(oldRequest, out _), Is.True);
        model.Markets["B"].SendBudget = 0;
        model.Advance(TimeSpan.FromSeconds(1));
        var departed = model.Shipments.Single(s => s.ArrivesAt != null);
        Assert.That(departed.Quantity, Is.EqualTo(Q(1)));
        Assert.That(remainder.Quantity, Is.EqualTo(Q(2)));
        var originalRoute = departed.Route.ToArray();
        var capture = new SWGlobalWarMarketOwnershipChange("B", SWGlobalWarFaction.Empire, Guid.NewGuid());
        model.CommitOwnershipBatch(generation, [capture]);
        model.CommitOwnershipBatch(generation, [capture]);
        Assert.That(departed.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(departed.Batch.Capture, Is.Null);
        Assert.That(departed.Route, Is.EqualTo(originalRoute));
        Assert.That(remainder.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
        Assert.That(remainder.Batch.Origin, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(remainder.Batch.Capture!.Quantity, Is.EqualTo(Q(2)));
        Assert.That(remainder.ReturnCaptured, Is.True);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(remainder.ArrivesAt, Is.Null, "A captured return needs the new owner's instruction even in a truce.");
        Assert.That(model.TryReturnShipment(oldRequest, out var oldReceipt), Is.True);
        Assert.That(oldReceipt.CapturedQuantity, Is.EqualTo(Q(2)));
        Assert.That(oldReceipt.RedirectedQuantity, Is.Zero);
        var newRequest = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), remainder.Id,
            SWGlobalWarFaction.Empire, "D", Q(2));
        Assert.That(model.TryReturnShipment(newRequest with { Owner = SWGlobalWarFaction.Republic }, out _), Is.False);
        Assert.That(model.TryReturnShipment(newRequest, out var newReceipt), Is.True);
        Assert.That(newReceipt.PreviousOperation, Is.EqualTo(oldRequest.Operation));
        Assert.That(model.TryReturnShipment(newRequest, out var replay), Is.True);
        Assert.That(replay, Is.SameAs(newReceipt));
        Assert.That(model.Markets["A"].Goods["F"].Incoming, Is.EqualTo(Q(1)));
        Assert.That(model.Markets["D"].Goods["F"].Incoming, Is.EqualTo(Q(2)));
        Assert.That(model.TryReturnShipment(oldRequest, out oldReceipt), Is.True);
        Assert.That(oldReceipt.CapturedQuantity, Is.EqualTo(Q(2)));
        Assert.That(oldReceipt.RedirectedQuantity, Is.EqualTo(Q(2)));
        Assert.That(oldReceipt.ReceivedQuantity, Is.Zero);
        Assert.That(model.TryReturnShipment(oldRequest with { Destination = "D" }, out _), Is.False);
        Assert.That(model.TryReturnShipment(newRequest with { Quantity = Q(1) }, out _), Is.False);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Markets["A"].Goods["F"].Stock, Is.EqualTo(Q(98)));
        Assert.That(model.Markets["D"].Goods["F"].Stock, Is.Zero);
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(model.Shipments, Is.Empty);
        Assert.That(model.Markets["D"].Goods["F"].Stock, Is.EqualTo(Q(2)));
        var captured = model.Markets["D"].Goods["F"].Batches.Single();
        Assert.That(captured.Origin, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(captured.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
        Assert.That(captured.Capture!.Quantity, Is.EqualTo(Q(2)));
        Assert.That(model.TryReturnShipment(oldRequest, out oldReceipt), Is.True);
        Assert.That(model.TryReturnShipment(newRequest, out newReceipt), Is.True);
        Assert.That(oldReceipt.ReceivedQuantity, Is.EqualTo(Q(1)));
        Assert.That(oldReceipt.CapturedQuantity, Is.EqualTo(Q(2)));
        Assert.That(oldReceipt.RedirectedQuantity, Is.EqualTo(Q(2)), "Redirected is a disposition of captured cargo, not extra material.");
        Assert.That(newReceipt.ReceivedQuantity, Is.EqualTo(Q(2)));
        Assert.That(newReceipt.CapturedQuantity, Is.Zero);
        var flow = model.Flows["outbound"];
        Assert.That(flow.Returned, Is.EqualTo(Q(1)));
        Assert.That(flow.ReturnCaptured, Is.EqualTo(Q(2)));
        Assert.That(flow.ReturnRedirected, Is.EqualTo(Q(2)));
        Assert.That(flow.RedirectedReturnReceived, Is.EqualTo(Q(2)));
        Assert.That(model.Markets.Values.Sum(m => m.Goods["F"].Stock), Is.EqualTo(Q(100)));
        Assert.That(model.Markets.Values.Sum(m => m.Goods["F"].Incoming), Is.Zero);
    }
}
