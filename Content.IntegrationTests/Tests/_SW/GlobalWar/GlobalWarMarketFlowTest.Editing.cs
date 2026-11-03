using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    [Test]
    public void GrowthFoodDoesNotMultiplyIndustrialFoodInputs()
    {
        var model = new SWGlobalWarMarketSimulation([new("SWGlobalWarF", 1), new("Bio", 1)],
            [new("A", [new("SWGlobalWarF", Q(60), Q(2)), new("Bio", 0, Q(2))], SWGlobalWarFaction.Republic,
                new() { HubCapacity = 150, Recipes = [new() { Output = "SWGlobalWarF", PerMinute = 30 },
                    new() { Output = "Bio", PerMinute = 2, Inputs = { ["SWGlobalWarF"] = 2 } }] }),
                new("B", [new("SWGlobalWarF", 0, 0), new("Bio", 0, 0)], SWGlobalWarFaction.Republic, new())],
            [new("A", "B", TimeSpan.FromSeconds(60), 64)], Guid.NewGuid());
        model.ConfigureFlows([Flow("idle", "A", "B", "SWGlobalWarF", 0)]);
        model.Markets["A"].GrowthFoodFraction = 0.25m;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Markets["A"].ProductionOrders["Bio"], Is.EqualTo(2));
        Assert.That(model.Markets["A"].ProductionOrders["SWGlobalWarF"], Is.EqualTo(6.5m),
            "Two mandatory food plus0.5 growth plus four industrial input; the industrial input has no growth surcharge.");
    }

    [Test]
    public void StoppedDefaultNeutralContractRetainsItsExactOrderedQuotaInTheProjection()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("E", 1)],
            [new("N", [new("E", Q(100), Q(0.1m))], SWGlobalWarFaction.Neutral,
                new() { HubCapacity = 150, Recipes = [new() { Output = "E", PerMinute = 2 }] }),
                new("A", [new("E", 0, Q(100))], SWGlobalWarFaction.Republic, new()),
                new("B", [new("E", 0, Q(100))], SWGlobalWarFaction.Republic, new())],
            [new("N", "A", TimeSpan.FromSeconds(60), 64), new("N", "B", TimeSpan.FromSeconds(60), 64)], generation);
        model.ConfigureFlows([Flow("default", "N", "A", "E", 0.3333333m, SWGlobalWarFlowRateMode.ProducerSurplus)]);
        model.DemographyEnabled = false;
        model.TradePolicy = Allow;
        var ordered = model.NeutralOrderedRate("N", "E", SWGlobalWarFaction.Republic);
        Assert.That(ordered, Is.GreaterThan(0));
        model.CommitOwnershipBatch(generation, [new("A", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        model.Advance(TimeSpan.FromSeconds(1));
        var view = model.FlowView(model.Flows["default"]);
        Assert.That(view.PlannedRate, Is.Zero);
        Assert.That(view.DesiredRate, Is.EqualTo(ordered));
        Assert.That(view.Settings.Rate, Is.EqualTo(decimal.Floor(ordered * Q(1))));
        Assert.That(model.NeutralOrderedRate("N", "E", SWGlobalWarFaction.Republic), Is.EqualTo(ordered));
        Assert.That(model.TryEditFlow(new(generation, Guid.NewGuid(), "extra", -1, new("N", "B", "E", 1),
            SWGlobalWarFlowPriority.NeutralIndustry), out var refused), Is.False);
        Assert.That(refused.Failure, Is.EqualTo(SWGlobalWarEconomyFailure.OfferExceeded));
        Assert.That(ordered - view.DesiredRate!.Value, Is.Zero,
            "Editing subtracts the exact declared rate, not the quantized input field or stopped executable plan.");
    }

    [Test]
    public void LowerNeutralOfferCapsProductionInputsWithoutErasingTheSavedOrder()
    {
        var generation = Guid.NewGuid();
        var profile = new SWGlobalWarEconomyProfilePrototype
        {
            HubCapacity = 150, HalfProductionBeforeThirtyMinutes = true,
            Recipes = [new() { Output = "E", PerMinute = 2, Inputs = { ["Au"] = 6 } }],
        };
        var model = new SWGlobalWarMarketSimulation([new("E", 1), new("Au", 1)],
            [new("N", [new("E", 0, Q(0.1m)), new("Au", Q(100), 0)], SWGlobalWarFaction.Neutral, profile),
                new("A", [new("E", 0, Q(100)), new("Au", 0, 0)], SWGlobalWarFaction.Republic, new())],
            [new("N", "A", TimeSpan.FromSeconds(60), 64)], generation);
        model.ConfigureFlows([Flow("import", "N", "A", "E", 0.5m, SWGlobalWarFlowRateMode.ProducerSurplus)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        Assert.That(model.TryEditFlow(new(generation, Guid.NewGuid(), "import", 0,
            new("N", "A", "E", Q(0.45m)), SWGlobalWarFlowPriority.NeutralIndustry), out _), Is.True);
        model.Markets["N"].Population = 0.5m;
        var offer = model.NeutralOffer("N", "E", SWGlobalWarFaction.Republic);
        var openingTarget = model.Markets["N"].Goods["E"].TargetStock;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["import"].ExplicitRate, Is.EqualTo(0.45m));
        Assert.That(model.Markets["N"].ProductionOrders["E"],
            Is.LessThanOrEqualTo(offer + 0.05m + openingTarget / (decimal) Q(10)));
        Assert.That(model.Markets["N"].Goods["Au"].ProductionDemandPerMinute,
            Is.EqualTo((int) decimal.Floor(model.Markets["N"].ProductionOrders["E"] * Q(6))));
        Assert.That(model.Markets["N"].ProductionOrders["E"], Is.GreaterThan(offer),
            "The neutral's own mandatory consumption remains in the production plan.");
    }

    [Test]
    public void FlowEditingRejectsDefaultIdentifiersBeforeDictionaryAccess()
    {
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(10), 0)], SWGlobalWarFaction.Republic, new()),
                new("B", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic, new())],
            [new("A", "B", TimeSpan.FromSeconds(60), 64)], Guid.NewGuid());
        var request = new SWGlobalWarFlowEdit(model.Generation!.Value, Guid.NewGuid(), "new", -1,
            new("A", "B", "F", Q(1)), SWGlobalWarFlowPriority.MandatoryFood);
        foreach (var invalid in new[] { request with { Flow = null! }, request with { Settings = null! },
                     request with { Settings = request.Settings with { Source = default } },
                     request with { Settings = request.Settings with { Destination = default } },
                     request with { Settings = request.Settings with { Commodity = default } },
                     request with { Settings = request.Settings with { Commodity = "missing" } } })
            Assert.That(model.TryEditFlow(invalid, out _), Is.False);
        Assert.That(model.Flows, Is.Empty);
        Assert.That(model.Markets["A"].Goods["F"].Stock, Is.EqualTo(Q(10)));
    }

    [Test]
    public void NeutralOfferIsSharedAcrossRowsAndSurvivesEditsWithDynamicPopulationLimits()
    {
        var generation = Guid.NewGuid();
        var profile = new SWGlobalWarEconomyProfilePrototype
        {
            HubCapacity = 150, HalfProductionBeforeThirtyMinutes = true,
            Recipes = [new() { Output = "E", PerMinute = 2 }],
        };
        var model = new SWGlobalWarMarketSimulation([new("E", 1)],
            [new("N", [new("E", Q(100), Q(0.1m))], SWGlobalWarFaction.Neutral, profile),
                new("A", [new("E", 0, Q(100))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("B", [new("E", 0, Q(100))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("C", [new("E", 0, Q(100))], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 })],
            [new("N", "A", TimeSpan.FromSeconds(2), 64), new("N", "B", TimeSpan.FromSeconds(2), 64),
                new("N", "C", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("a", "N", "A", "E", 0.25m, SWGlobalWarFlowRateMode.ProducerSurplus),
            Flow("b", "N", "B", "E", 0.25m, SWGlobalWarFlowRateMode.ProducerSurplus)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        Assert.That(model.NeutralOffer("N", "E", SWGlobalWarFaction.Republic), Is.EqualTo(0.45m));
        var third = new SWGlobalWarFlowEdit(generation, Guid.NewGuid(), "third", -1,
            new("N", "C", "E", Q(0.225m)), SWGlobalWarFlowPriority.NeutralIndustry);
        Assert.That(model.TryEditFlow(third, out var refused), Is.False);
        Assert.That(refused.Failure, Is.EqualTo(SWGlobalWarEconomyFailure.OfferExceeded));
        model.Advance(TimeSpan.FromSeconds(1));
        var originalCargo = model.Shipments.First();
        var oldDestination = originalCargo.Destination;
        Assert.That(model.TryEditFlow(third with { Flow = "a", ExpectedRevision = 0,
            Operation = Guid.NewGuid(), Settings = new("N", "A", "E", Q(0.225m), Enabled: false) }, out _), Is.True);
        Assert.That(model.TryEditFlow(third with { Operation = Guid.NewGuid() }, out _), Is.True);
        Assert.That(originalCargo.Destination, Is.EqualTo(oldDestination));
        model.Advance(TimeSpan.FromSeconds(59));
        var sent = model.Flows.Values.Sum(f => f.Dispatched);
        Assert.That(sent, Is.LessThanOrEqualTo(Q(0.45m)));
        Assert.That(sent, Is.GreaterThan(Q(0.44m)));
        Assert.That(model.TryEditFlow(third with { Flow = "b", ExpectedRevision = 0, Operation = Guid.NewGuid(),
            Settings = new("N", "B", "E", Q(0.225m)) }, out _), Is.True);
        model.Markets["N"].Population = 0.5m;
        Assert.That(model.NeutralOffer("N", "E", SWGlobalWarFaction.Republic), Is.LessThan(0.45m));
        Assert.That(model.TryEditFlow(third with { Flow = "b", ExpectedRevision = 1, Operation = Guid.NewGuid(),
            Settings = new("N", "B", "E", Q(0.2m)) }, out _), Is.True, "Allow incremental reduction while still over quota.");
        Assert.That(model.TryEditFlow(third with { Flow = "fourth", Operation = Guid.NewGuid(),
            Settings = new("N", "C", "E", Q(0.01m)) }, out _), Is.False);
        var before = model.Flows.Values.Sum(f => f.Dispatched);
        var allowance = model.NeutralOffer("N", "E", SWGlobalWarFaction.Republic);
        model.Advance(TimeSpan.FromMinutes(1));
        Assert.That(model.Flows.Values.Sum(f => f.Dispatched) - before,
            Is.LessThanOrEqualTo((long) decimal.Ceiling(allowance * Q(1)) + 2));
        var pending = model.Shipments.Select(s => s.Id).ToArray();
        model.CommitOwnershipBatch(generation, [new("C", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        var previousThird = model.Flows["third"].Dispatched;
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["third"].Dispatched, Is.EqualTo(previousThird));
        Assert.That(model.Flows["third"].ContractFaction, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(model.Shipments.Where(s => pending.Contains(s.Id)).All(s => s.Batch!.Owner == SWGlobalWarFaction.Neutral), Is.True);
        model.CommitOwnershipBatch(generation, [new("N", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        Assert.That(model.NeutralOffer("N", "E", SWGlobalWarFaction.Republic), Is.Zero);
        before = model.Flows.Values.Sum(f => f.Dispatched);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows.Values.Sum(f => f.Dispatched), Is.EqualTo(before));
    }

    [Test]
    public void EditKeepsOldCargoObligationsAndCommodityStatisticsWithIndependentReplay()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1), new("E", 1)],
            [new("A", [new("F", Q(100), 0), new("E", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("B", [new("F", 0, Q(1)), new("E", 0, Q(1))], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("C", [new("F", 0, Q(1)), new("E", 0, Q(1))], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("D", [new("F", Q(100), 0), new("E", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64), new("A", "C", TimeSpan.FromSeconds(2), 64),
                new("D", "C", TimeSpan.FromSeconds(2), 64)], generation);
        model.ConfigureFlows([Flow("contract", "A", "B", "F", 60), Flow("unrelated", "A", "C", "E", 0)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.Advance(TimeSpan.FromSeconds(1));
        var original = model.Shipments.Single();
        var batch = original.Batch!.Id;
        var departure = original.DepartureReceipt;
        var request = new SWGlobalWarFlowEdit(generation, Guid.NewGuid(), "contract", 0,
            new("D", "C", "E", Q(60)), SWGlobalWarFlowPriority.DomesticCivilian);
        Assert.That(model.TryEditFlow(request, out var receipt), Is.True);
        Assert.That(receipt.Before!.Settings.Source.Id, Is.EqualTo("A"));
        Assert.That(receipt.After!.Revision, Is.EqualTo(1));
        Assert.That(original.Route, Is.EqualTo(new[] { "A", "B" }));
        Assert.That(original.Commodity, Is.EqualTo("F"));
        Assert.That(original.Batch.Id, Is.EqualTo(batch));
        Assert.That(original.DepartureReceipt, Is.EqualTo(departure));
        Assert.That(model.Markets["B"].Goods["F"].Incoming, Is.EqualTo(Q(1)));
        Assert.That(model.Markets["C"].Goods["E"].Incoming, Is.Zero);
        Assert.That(model.Flows["contract"].Dispatched, Is.Zero, "The new commodity must not inherit F statistics.");
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments.Single(s => s.Commodity == "E").Source, Is.EqualTo("D"));
        Assert.That(model.Markets["C"].Goods["E"].Incoming, Is.EqualTo(Q(1)));
        Assert.That(model.TryEditFlow(request, out var replay), Is.True);
        Assert.That(replay, Is.SameAs(receipt));
        Assert.That(model.Flows["contract"].Revision, Is.EqualTo(1));
        Assert.That(model.TryEditFlow(request with { Settings = request.Settings with { Rate = Q(1) } }, out _), Is.False);

        var stale = request with { Operation = Guid.NewGuid() };
        Assert.That(model.TryEditFlow(stale, out var conflict), Is.False);
        Assert.That(conflict.Failure, Is.EqualTo(SWGlobalWarEconomyFailure.Conflict));
        Assert.That(model.TryEditFlow(stale, out replay), Is.False);
        Assert.That(replay, Is.SameAs(conflict));
        Assert.That(model.TryEditFlow(request with { Operation = Guid.NewGuid(), ExpectedRevision = 1,
            Settings = request.Settings with { Enabled = false } }, out _), Is.True);
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(model.Shipments, Is.Empty);
        Assert.That(model.Markets["B"].Goods["F"].Incoming, Is.Zero);
        Assert.That(model.Markets["C"].Goods["E"].Incoming, Is.Zero);
        Assert.That(model.Flows["contract"].Statistics["F"].Received, Is.EqualTo(Q(1)));
        Assert.That(model.Flows["contract"].Statistics["F"].Dispatched, Is.EqualTo(Q(1)));
        Assert.That(model.Flows["contract"].Statistics["E"].Received, Is.EqualTo(Q(1)));
        Assert.That(model.Flows["contract"].Statistics["E"].Dispatched, Is.EqualTo(Q(1)));
        Assert.That(model.Flows["unrelated"].Revision, Is.Zero);
        Assert.That(model.Markets.Values.Sum(m => m.Goods["F"].Stock), Is.EqualTo(Q(200)));
        Assert.That(model.Markets.Values.Sum(m => m.Goods["E"].Stock), Is.EqualTo(Q(200)));
    }

    [Test]
    public void CreateRejectsInvalidRoutesAndCannotImportOldDispatchCreditOrCrossEnding()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("B", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64)], generation);
        model.TradePolicy = Allow;
        model.ConfigureFlows([Flow("idle", "A", "B", "F", 0)]);
        model.Advance(TimeSpan.FromMinutes(1));
        var request = new SWGlobalWarFlowEdit(generation, Guid.NewGuid(), "new", -1,
            new("A", "B", "F", Q(0.1m)), SWGlobalWarFlowPriority.MandatoryFood);
        Assert.That(model.TryEditFlow(request, out _), Is.True);
        Assert.That(model.Flows.Count, Is.EqualTo(2));
        Assert.That(model.TryEditFlow(request, out _), Is.True);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["new"].Dispatched, Is.EqualTo(Q(0.1m) / 60));
        Assert.That(model.TryEditFlow(request with { Operation = Guid.NewGuid(), Flow = "invalid",
            Settings = request.Settings with { Source = "B", Destination = "A" } }, out var refused), Is.False);
        Assert.That(refused.Failure, Is.EqualTo(SWGlobalWarEconomyFailure.NoRoute));
        Assert.That(model.Flows.Count, Is.EqualTo(2));
        model.IsActive = () => false;
        Assert.That(model.TryEditFlow(request with { Operation = Guid.NewGuid(), ExpectedRevision = 1 }, out _), Is.False);
        Assert.That(model.Flows["new"].Revision, Is.EqualTo(1));
    }
}
