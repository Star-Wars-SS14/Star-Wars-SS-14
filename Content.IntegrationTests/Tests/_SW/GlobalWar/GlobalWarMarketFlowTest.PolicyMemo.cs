using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    private static SWGlobalWarMarketSimulation ConvoyModel(Guid generation)
    {
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(500), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 150,
                    Recipes = [new() { Output = "F", PerMinute = 30 }] }),
                new("B", [new("F", 0, 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("C", [new("F", 0, Q(6))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150,
                    Recipes = [new() { Output = "F", PerMinute = 30 }] })],
            [new("A", "B", TimeSpan.FromSeconds(60), 64), new("B", "C", TimeSpan.FromSeconds(60), 64)], generation);
        model.ConfigureFlows([Flow("convoy", "A", "C", "F", 6)]);
        model.DemographyEnabled = false;
        return model;
    }

    [Test]
    public void PlanningAsksOncePerRouteLegButEverySecondAgain()
    {
        var generation = Guid.NewGuid();
        var model = ConvoyModel(generation);
        var receiptOpen = true;
        var calls = 0;
        model.TradePolicy = request =>
        {
            calls++;
            return new(request.Stage != SWGlobalWarTradeStage.Receipt || receiptOpen, string.Empty, 0, 0, 0, 0, 0);
        };
        model.Advance(TimeSpan.FromSeconds(40));
        var cargo = model.Shipments.Count;
        Assert.That(cargo, Is.GreaterThan(20), "A convoy of separate shipments on one route.");
        Assert.That(model.Shipments.All(s => s.ArrivesAt != null), Is.True);

        calls = 0;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(calls, Is.LessThan(cargo), "Cargo on one route and leg shares the planner's question.");

        // The answer lasts one pass only: the next second's plan sees a changed policy at once.
        var open = model.Markets["C"].ProductionOrders["F"];
        receiptOpen = false;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Markets["C"].ProductionOrders["F"], Is.GreaterThan(open),
            "Undeliverable cargo no longer reduces the destination's own production order.");
        receiptOpen = true;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Markets["C"].ProductionOrders["F"], Is.LessThanOrEqualTo(open));
    }

    [Test]
    public void CapturedCargoIsAskedBatchByBatchWithItsOwnEvidence()
    {
        var generation = Guid.NewGuid();
        var model = ConvoyModel(generation);
        var nextLegOpen = false;
        var asked = new HashSet<Guid>();
        var capturedNow = 0;
        model.TradePolicy = request =>
        {
            if (request.Capture != null)
            {
                capturedNow++;
                asked.Add(request.Batch);
            }
            return new(request.Stage != SWGlobalWarTradeStage.NextLeg || nextLegOpen, "held", 0, 0, 0, 0, 0);
        };
        model.Advance(TimeSpan.FromSeconds(90));
        var held = model.Shipments.Where(s => s.ArrivesAt == null && s.Leg == 1).ToArray();
        Assert.That(held, Has.Length.GreaterThan(5), "Cargo is stopped together at the transit world.");
        Assert.That(held.All(s => s.HoldReason == "held"), Is.True, "Shared answers still reach every shipment.");

        model.CommitOwnershipBatch(generation, [new("B", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        var captured = model.Shipments.Where(s => s.Batch?.Capture != null).Select(s => s.Batch!.Id).ToArray();
        Assert.That(captured, Has.Length.EqualTo(held.Length));
        asked.Clear();
        capturedNow = 0;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(asked, Is.SupersetOf(captured), "Evidence belongs to one batch and cannot be shared with another.");
        Assert.That(capturedNow, Is.GreaterThanOrEqualTo(captured.Length));
    }

    [Test]
    public void EditedContractIsFoundUnderItsNewEndpoints()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(500), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("B", [new("F", 0, Q(3))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("C", [new("F", 0, Q(3))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64), new("A", "C", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("one", "A", "B", "F", 3), Flow("two", "A", "C", "F", 3)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromSeconds(10));
        var sentToC = model.Flows["two"].Dispatched;
        Assert.That(sentToC, Is.GreaterThan(0));

        Assert.That(model.TryEditFlow(new(generation, Guid.NewGuid(), "one", 0, new("A", "C", "F", Q(3)),
            SWGlobalWarFlowPriority.DomesticCivilian), out _), Is.True);
        var before = model.Flows["one"].Dispatched;
        model.Advance(TimeSpan.FromSeconds(10));
        Assert.That(model.Flows["one"].Dispatched, Is.GreaterThan(before), "The moved contract keeps shipping to C.");
        Assert.That(model.Shipments.Where(s => s.Flow == "one" && s.DepartedAt > TimeSpan.FromSeconds(10))
            .All(s => s.Destination == "C"), Is.True);
        Assert.That(model.Flows["one"].PerMinute + model.Flows["two"].PerMinute, Is.EqualTo(6m));
        Assert.That(model.Flows["two"].Dispatched, Is.GreaterThan(sentToC),
            "Both rows now share C's requirement, which means each sees the other in the total rate.");
    }
}
