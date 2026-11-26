using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    private const string Waiting = "sw-global-war-economy-wait-ownership";

    private static SWGlobalWarMarketDefinition Farm(string id, SWGlobalWarFaction owner) =>
        new(id, [new("F", Q(500), 0)], owner, new() { HubCapacity = 150, Recipes = [new() { Output = "F", PerMinute = 30 }] });

    private static SWGlobalWarMarketSimulation FoodModel(Guid generation)
    {
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [Farm("R1", SWGlobalWarFaction.Republic), Farm("R2", SWGlobalWarFaction.Republic),
                Farm("E1", SWGlobalWarFaction.Empire), Farm("E2", SWGlobalWarFaction.Empire),
                new("N", [new("F", Q(5), Q(3))], SWGlobalWarFaction.Neutral, new() { HubCapacity = 150 })],
            new[] { "R1", "R2", "E1", "E2" }.Select(s => new SWGlobalWarTradeConnection(s, "N", TimeSpan.FromSeconds(2), 64)), generation);
        model.ConfigureFlows(new[] { "R1", "R2", "E1", "E2" }.Select(s =>
            Flow(s, s, "N", "F", 0.25m, SWGlobalWarFlowRateMode.RecipientMandatory, SWGlobalWarFlowPriority.MandatoryFood)));
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        return model;
    }

    [Test]
    public void JoinedNeutralIsSuppliedWholeByItsOwnerAndOnlyByItsOwner()
    {
        var generation = Guid.NewGuid();
        var model = FoodModel(generation);
        model.Advance(TimeSpan.FromSeconds(1));
        foreach (var flow in model.Flows.Values)
            Assert.That(flow.PerMinute, Is.EqualTo(0.75m), "Before joining each of four contracts carries a quarter.");

        model.CommitOwnershipBatch(generation, [new("N", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        var republicSent = model.Flows["R1"].Dispatched + model.Flows["R2"].Dispatched;
        var empireSent = model.Flows["E1"].Dispatched + model.Flows["E2"].Dispatched;
        model.Advance(TimeSpan.FromSeconds(120));
        Assert.Multiple(() =>
        {
            Assert.That(model.Flows["E1"].PerMinute + model.Flows["E2"].PerMinute, Is.EqualTo(3m),
                "The owner's own contracts carry the whole requirement.");
            Assert.That(model.Flows["E1"].ExpectedRecipient, Is.EqualTo(SWGlobalWarFaction.Neutral),
                "The stored recipient is not rewritten; only the authorization understands the join.");
            Assert.That(model.Flows["E1"].ContractFaction, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(model.Flows["R1"].PerMinute, Is.Zero);
            Assert.That(model.Flows["R2"].WaitReason, Is.EqualTo(Waiting));
            Assert.That(model.Flows["R1"].Dispatched + model.Flows["R2"].Dispatched, Is.EqualTo(republicSent),
                "The other side's contract gives no right to deliver into the joined world.");
            Assert.That(model.Flows["E1"].Dispatched + model.Flows["E2"].Dispatched, Is.GreaterThan(empireSent));
        });

        // A later conquest hands the world over the same way, and the previous owner's rows wait again.
        model.CommitOwnershipBatch(generation, [new("N", SWGlobalWarFaction.Republic, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(model.Flows["R1"].PerMinute + model.Flows["R2"].PerMinute, Is.EqualTo(3m));
            Assert.That(model.Flows["E1"].PerMinute, Is.Zero);
            Assert.That(model.Flows["E1"].WaitReason, Is.EqualTo(Waiting));
        });
    }

    [Test]
    public void JoinedNeutralKeepsAMinistersExplicitRateAndOwnersLostSourceStopsItsPart()
    {
        var generation = Guid.NewGuid();
        var model = FoodModel(generation);
        Assert.That(model.TryEditFlow(new(generation, Guid.NewGuid(), "E1", 0, new("E1", "N", "F", Q(0.2m)),
            SWGlobalWarFlowPriority.MandatoryFood, SWGlobalWarFaction.Empire), out _), Is.True);
        model.CommitOwnershipBatch(generation, [new("N", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["E1"].PerMinute, Is.EqualTo(0.2m));
        Assert.That(model.Flows["E2"].PerMinute, Is.EqualTo(1.5m), "An edited row keeps its share of the requirement.");

        model.CommitOwnershipBatch(generation, [new("E2", SWGlobalWarFaction.Republic, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["E2"].PerMinute, Is.Zero, "A captured source stops, as on a home world.");
        Assert.That(model.Flows["E2"].WaitReason, Is.EqualTo(Waiting));
    }

    [Test]
    public void JoinedIndustryGetsAllItsInputsFromTheOwnerAndSendsOutputOnlyToTheOwner()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("E", 1), new("Au", 1)],
            [new("N", [new("E", 0, 0), new("Au", Q(10), 0)], SWGlobalWarFaction.Neutral, new()
                { HubCapacity = 150, Recipes = [new() { Output = "E", PerMinute = 2, Inputs = { ["Au"] = 6 } }] }),
                new("RA", [new("Au", Q(500), 0), new("E", 0, Q(1))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("EA", [new("Au", Q(500), 0), new("E", 0, Q(1))], SWGlobalWarFaction.Empire, new() { HubCapacity = 150 })],
            [new("RA", "N", TimeSpan.FromSeconds(2), 64), new("EA", "N", TimeSpan.FromSeconds(2), 64),
                new("N", "RA", TimeSpan.FromSeconds(2), 64), new("N", "EA", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("rin", "RA", "N", "Au", 0.5m, SWGlobalWarFlowRateMode.ProductionInput),
            Flow("ein", "EA", "N", "Au", 0.5m, SWGlobalWarFlowRateMode.ProductionInput),
            Flow("rout", "N", "RA", "E", 0.5m, SWGlobalWarFlowRateMode.ProducerSurplus),
            Flow("eout", "N", "EA", "E", 0.5m, SWGlobalWarFlowRateMode.ProducerSurplus)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.CommitOwnershipBatch(generation, [new("N", SWGlobalWarFaction.Republic, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(60));
        var order = model.Markets["N"].ProductionOrders["E"];
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.GreaterThan(0));
            Assert.That(model.Flows["rin"].PerMinute, Is.EqualTo(order * 6), "The owner covers the full input of the order.");
            Assert.That(model.Flows["ein"].PerMinute, Is.Zero);
            Assert.That(model.Flows["rout"].PerMinute, Is.GreaterThan(0));
            Assert.That(model.Flows["rout"].Dispatched, Is.GreaterThan(0));
            Assert.That(model.Flows["eout"].PerMinute, Is.Zero);
            Assert.That(model.Flows["eout"].WaitReason, Is.EqualTo(Waiting));
        });
    }

    [Test]
    public void NeutralUpkeepBetweenNeutralsRunsOnlyWhileBothShareAnOwner()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("L", 1)],
            [new("A", [new("L", Q(100), 0)], SWGlobalWarFaction.Neutral, new() { HubCapacity = 150 }),
                new("B", [new("L", 0, Q(0.2m))], SWGlobalWarFaction.Neutral, new() { HubCapacity = 150 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("upkeep", "A", "B", "L", 1, SWGlobalWarFlowRateMode.RecipientMandatory,
            SWGlobalWarFlowPriority.LuxuryAndExpansion)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["upkeep"].PerMinute, Is.EqualTo(0.2m));

        model.CommitOwnershipBatch(generation, [new("A", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["upkeep"].PerMinute, Is.Zero, "One neutral has joined, the other has not.");

        model.CommitOwnershipBatch(generation, [new("B", SWGlobalWarFaction.Republic, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["upkeep"].PerMinute, Is.Zero, "Opposing owners do not inherit the neutrals' arrangement.");

        model.CommitOwnershipBatch(generation, [new("B", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["upkeep"].PerMinute, Is.EqualTo(0.2m), "Under one owner it continues as an internal delivery.");
    }

    [Test]
    public void CapturedHomeWorldStillStopsItsContractsAndDoublesNothing()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [Farm("R1", SWGlobalWarFaction.Republic), Farm("R2", SWGlobalWarFaction.Republic),
                new("H", [new("F", Q(5), Q(3))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 })],
            [new("R1", "H", TimeSpan.FromSeconds(2), 64), new("R2", "H", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("a", "R1", "H", "F", 0.5m, SWGlobalWarFlowRateMode.RecipientMandatory),
            Flow("b", "R2", "H", "F", 0.5m, SWGlobalWarFlowRateMode.RecipientMandatory)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.CommitOwnershipBatch(generation, [new("H", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["a"].PerMinute, Is.Zero);
        Assert.That(model.Flows["a"].WaitReason, Is.EqualTo(Waiting));
        model.CommitOwnershipBatch(generation, [new("R2", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.CommitOwnershipBatch(generation, [new("H", SWGlobalWarFaction.Republic, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["a"].PerMinute, Is.EqualTo(1.5m), "A home world's split is not rescaled by this rule.");
        Assert.That(model.Flows["b"].PerMinute, Is.Zero);
    }
}
