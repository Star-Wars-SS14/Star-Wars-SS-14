using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    // These are transport-model identities, not a substitute for the real-entity consumer integration scenario.
    private static SWGlobalWarSupplyManifest Manifest(Guid generation, int count)
    {
        return new(generation, Guid.NewGuid(), SWGlobalWarFaction.Republic, SWGlobalWarFaction.Republic,
            Enumerable.Range(1, count).Select(i => new SWGlobalWarSupplyManifestItem(new EntityUid(i), Guid.NewGuid())).ToArray());
    }

    [TestCase(0.75, 0)]
    [TestCase(2.6, 2)]
    public void SupplyManifestUsesWholeItemsAndConservesEveryIdentity(double budget, int firstDeparture)
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("medical", 1)],
            [new("A", [], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("B", [], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64)], generation);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.Markets["A"].SendBudget = budget - 1; // The next one-second refill supplies exactly the tested budget.
        var manifest = Manifest(generation, 8);
        var originals = manifest.Items.ToArray();
        Assert.That(model.TryQueueSupplyManifest("medical", "A", "B", manifest, out _), Is.True);
        Assert.That(model.TryQueueSupplyManifest("medical", "A", "B", manifest, out _), Is.False);
        var received = new List<SWGlobalWarSupplyManifestItem>();
        model.ReceiveSupplyManifest = shipment =>
        {
            received.AddRange(shipment.SupplyManifest!.Items);
            return true;
        };
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments.Where(s => s.ArrivesAt != null).Sum(s => s.SupplyManifest!.Items.Count),
            Is.EqualTo(firstDeparture));
        Assert.That(model.Markets["A"].SendBudget, Is.EqualTo(budget - firstDeparture).Within(1e-8));
        Assert.That(model.Shipments.SelectMany(s => s.SupplyManifest!.Items), Is.EquivalentTo(originals));
        model.Advance(TimeSpan.FromSeconds(30));
        Assert.That(received, Is.EquivalentTo(originals));
        Assert.That(received.Distinct().Count(), Is.EqualTo(8));
        Assert.That(model.Shipments, Is.Empty);
        Assert.That(model.Markets.Values.All(m => m.Goods.Count == 0), Is.True);
    }

    [Test]
    public void SupplyManifestSharesPriorityBudgetBetweenFoodCivilianAndGrowth()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1), new("E", 1), new("L", 1), new("medical", 1)],
            [new("A", [new("F", Q(100), 0), new("E", Q(100), 0), new("L", Q(100), 0)],
                    SWGlobalWarFaction.Republic, new() { HubCapacity = 180 }),
                new("B", [new("F", 0, 0), new("E", 0, 0), new("L", 0, 0)],
                    SWGlobalWarFaction.Republic, new() { HubCapacity = 180 })],
            [new("A", "B", TimeSpan.FromSeconds(60), 64)], generation);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        model.ConfigureFlows([
            Flow("food", "A", "B", "F", 60, priority: SWGlobalWarFlowPriority.MandatoryFood),
            Flow("civilian", "A", "B", "E", 60, priority: SWGlobalWarFlowPriority.DomesticCivilian),
            Flow("growth", "A", "B", "L", 60, priority: SWGlobalWarFlowPriority.GrowthFood),
        ]);
        model.Markets["A"].SendBudget = 0;
        foreach (var good in model.Markets["B"].Goods.Values)
            good.MinimumBuffer = Q(10);
        Assert.That(model.TryQueueSupplyManifest("medical", "A", "B", Manifest(generation, 2), out _), Is.True);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Flows["food"].Dispatched, Is.EqualTo(Q(1)));
        Assert.That(model.Flows["civilian"].Dispatched, Is.EqualTo(Q(1)));
        Assert.That(model.Shipments.Where(s => s.SupplyManifest != null && s.ArrivesAt != null)
            .Sum(s => s.SupplyManifest!.Items.Count), Is.EqualTo(1));
        Assert.That(model.Flows["growth"].Dispatched, Is.Zero);
        Assert.That(model.Markets["A"].SendBudget, Is.Zero.Within(1e-8));
    }

    [Test]
    public void SupplyManifestCaptureAppliesOnlyAtCurrentNodeAndReceiptWaitsForPolicy()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("medical", 1)],
            [new("A", [], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 }),
                new("B", [], SWGlobalWarFaction.Republic, new() { HubCapacity = 60 })],
            [new("A", "B", TimeSpan.FromSeconds(2), 64)], generation);
        var blockReceipt = false;
        model.TradePolicy = request => blockReceipt && request.Stage == SWGlobalWarTradeStage.Receipt
            ? new(false, "receipt-blocked", 0, 0, 0, 0, 0) : Allow(request);
        model.Markets["A"].SendBudget = 0;
        var manifest = Manifest(generation, 3);
        Assert.That(model.TryQueueSupplyManifest("medical", "A", "B", manifest, out _), Is.True);
        model.Advance(TimeSpan.FromSeconds(1));
        var departed = model.Shipments.Single(s => s.ArrivesAt != null);
        var waiting = model.Shipments.Single(s => s.ArrivesAt == null);
        var capture = Guid.NewGuid();
        model.CommitOwnershipBatch(generation, [new("A", SWGlobalWarFaction.Empire, capture)]);
        Assert.That(departed.SupplyManifest!.Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(departed.SupplyManifest.Capture, Is.Null);
        Assert.That(waiting.SupplyManifest!.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
        Assert.That(waiting.SupplyManifest.Capture!.CaptureOperation, Is.EqualTo(capture));
        Assert.That(waiting.SupplyManifest.Capture.Quantity, Is.EqualTo(2));
        Assert.That(waiting.SupplyManifest.Capture.Batch, Is.EqualTo(waiting.SupplyManifest.Id));
        blockReceipt = true;
        var receipts = 0;
        model.ReceiveSupplyManifest = _ => { receipts++; return true; };
        model.Advance(TimeSpan.FromSeconds(3));
        Assert.That(receipts, Is.Zero);
        Assert.That(model.Shipments.All(s => s.SupplyManifest!.Origin == SWGlobalWarFaction.Republic), Is.True);
        blockReceipt = false;
        model.Advance(TimeSpan.FromSeconds(10));
        Assert.That(model.Shipments.Count, Is.EqualTo(1));
        Assert.That(model.Shipments[0], Is.SameAs(waiting));
        Assert.That(waiting.ReturnCaptured, Is.True);
        Assert.That(receipts, Is.EqualTo(1));
    }
}
