using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarMarketInventoryTest
{
    private static int Q(decimal value) => SWGlobalWarMarketUnits.FromUnits(value);
    private static SWGlobalWarTradeDecision Allow(SWGlobalWarTradeRequest request) =>
        new(true, string.Empty, 0, 0, 0, 0, 0);

    private static SWGlobalWarMarketSimulation Create(Guid generation, decimal consumption = 0)
    {
        return new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), Q(consumption))], SWGlobalWarFaction.Republic)], [], generation);
    }

    [Test]
    public void ReservationsAreNotStockAndCannotBeCommittedOrCancelledTwice()
    {
        var generation = Guid.NewGuid();
        var model = Create(generation);
        var good = model.Markets["A"].Goods["F"];
        var request = new SWGlobalWarStockRequest(generation, Guid.NewGuid(), "A", "F", Q(30),
            SWGlobalWarFaction.Republic, "physical-export");
        Assert.That(model.TryReserveStock(request, out var pending), Is.True);
        Assert.That(good.Stock, Is.EqualTo(Q(100)));
        Assert.That(good.Available, Is.EqualTo(Q(70)));
        Assert.That(model.TryReserveStock(request, out var repeated), Is.True);
        Assert.That(repeated, Is.SameAs(pending));
        Assert.That(model.TryReserveStock(request with { Quantity = Q(31) }, out _), Is.False);
        Assert.That(model.TryReserveStock(request with { Operation = Guid.NewGuid(), Quantity = Q(71) }, out _), Is.False);
        Assert.That(model.TryAddStock("A", "F", 1), Is.False);
        Assert.That(model.TryTakeStock("A", "F", 1), Is.False);
        Assert.That(model.TrySetConsumption("A", "F", 1), Is.False);
        Assert.That(model.TryCommitStock(Guid.NewGuid(), request.Operation, out _), Is.False);
        Assert.That(model.TryCommitStock(generation, request.Operation, out var receipt), Is.True);
        Assert.That(model.TryCommitStock(generation, request.Operation, out repeated), Is.True);
        Assert.That(repeated, Is.SameAs(receipt));
        Assert.That(model.TryCancelStock(generation, request.Operation), Is.False);
        Assert.That(good.Reserved, Is.Zero);
        Assert.That(good.Stock + receipt.Withdrawn.Sum(b => b.Quantity), Is.EqualTo(Q(100)));
        Assert.That(receipt.Withdrawn.All(b => b.Origin == SWGlobalWarFaction.Republic && b.Reserved == 0), Is.True);
        var cancelled = request with { Operation = Guid.NewGuid() };
        Assert.That(model.TryReserveStock(cancelled, out _), Is.True);
        Assert.That(model.TryCancelStock(generation, cancelled.Operation), Is.True);
        Assert.That(model.TryCancelStock(generation, cancelled.Operation), Is.True);
        Assert.That(model.TryCommitStock(generation, cancelled.Operation, out _), Is.False);
        Assert.That(model.TryReserveStock(cancelled, out _), Is.False);
        Assert.That(good.Stock, Is.EqualTo(Q(70)));
        Assert.That(good.Available, Is.EqualTo(good.Stock));
    }

    [Test]
    public void CaptureTransfersOnlyGoodsAtTheNodeAndSplitsConserveEvidence()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic),
                new("B", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic)],
            [new("A", "B", TimeSpan.FromSeconds(2), 64)], generation);
        model.TradePolicy = request => request.Stage == SWGlobalWarTradeStage.Receipt
            ? new(false, "hold", 0, 0, 0, 0, 0) : Allow(request);
        model.Advance(TimeSpan.FromSeconds(1));
        var cargo = model.Shipments.Single();
        var departing = cargo.Quantity;
        Assert.That(departing, Is.GreaterThan(0));
        Assert.That(departing, Is.LessThanOrEqualTo(Q(64)), "Cargo volume must be scaled once, not 60000 times.");
        Assert.That(cargo.DepartureReceipt, Is.Not.EqualTo(Guid.Empty));
        var operation = Guid.NewGuid();
        var changes = new[] { new SWGlobalWarMarketOwnershipChange("A", SWGlobalWarFaction.Empire, operation) };
        model.CommitOwnershipBatch(generation, changes);
        var stock = model.Markets["A"].Goods["F"];
        var capture = stock.Batches.Single();
        Assert.That(capture.Origin, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(capture.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
        Assert.That(capture.Capture!.Quantity, Is.EqualTo(Q(100) - departing));
        Assert.That(cargo.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(cargo.Batch.Capture, Is.Null);
        model.CommitOwnershipBatch(generation, changes);
        Assert.That(stock.Batches.Single(), Is.EqualTo(capture));
        var request = new SWGlobalWarStockRequest(generation, Guid.NewGuid(), "A", "F", Q(2),
            SWGlobalWarFaction.Empire, "captured-stock-use");
        Assert.That(model.TryReserveStock(request, out _), Is.True);
        Assert.That(model.TryCommitStock(generation, request.Operation, out var receipt), Is.True);
        var split = receipt.Withdrawn.Single();
        Assert.That(split.Id, Is.Not.EqualTo(capture.Id));
        Assert.That(split.Capture!.Batch, Is.EqualTo(split.Id));
        Assert.That(split.Capture.Quantity + stock.Batches.Single().Capture!.Quantity, Is.EqualTo(capture.Quantity));
        model.Advance(TimeSpan.FromSeconds(2));
        Assert.That(cargo.ArrivesAt, Is.Null);
        Assert.That(cargo.HoldReason, Is.EqualTo("hold"));
        Assert.That(model.Markets["B"].Goods["F"].Stock, Is.Zero);
        model.CommitOwnershipBatch(generation,
            [new("B", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        Assert.That(cargo.Batch.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
        Assert.That(cargo.Batch.Capture!.Quantity, Is.EqualTo(departing));
        Assert.That(stock.Stock + split.Quantity + cargo.Quantity, Is.EqualTo(Q(100)));
    }

    [Test]
    public void DeniedFastSupplierDoesNotPreventAnAllowedAlternative()
    {
        var generation = Guid.NewGuid();
        var model = new SWGlobalWarMarketSimulation([new("F", 1)],
            [new("A", [new("F", Q(100), 0)], SWGlobalWarFaction.Empire),
                new("B", [new("F", Q(100), 0)], SWGlobalWarFaction.Republic),
                new("C", [new("F", 0, Q(1))], SWGlobalWarFaction.Republic)],
            [new("A", "C", TimeSpan.FromSeconds(1), 64),
                new("B", "C", TimeSpan.FromSeconds(2), 64)], generation);
        model.TradePolicy = request => request.Origin == SWGlobalWarFaction.Empire
            ? new(false, "war", 0, 0, 0, 0, 0) : Allow(request);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(model.Shipments, Has.Count.EqualTo(1));
        Assert.That(model.Shipments.Single().Source, Is.EqualTo("B"));
        Assert.That(model.Markets["A"].Goods["F"].Stock, Is.EqualTo(Q(100)));
        Assert.That(model.Shipments.Single().ArrivesAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void CapturingStockInvalidatesOldReservationsBeforeCallbacks()
    {
        var generation = Guid.NewGuid();
        var model = Create(generation);
        var request = new SWGlobalWarStockRequest(generation, Guid.NewGuid(), "A", "F", Q(10),
            SWGlobalWarFaction.Republic, "export");
        Assert.That(model.TryReserveStock(request, out _), Is.True);
        model.CommitOwnershipBatch(generation, [new("A", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        Assert.That(model.TryCommitStock(generation, request.Operation, out _), Is.False);
        Assert.That(model.Markets["A"].Goods["F"].Available, Is.EqualTo(Q(100)));
        Assert.That(model.TryReserveStock(request with { Operation = Guid.NewGuid() }, out _), Is.False);
    }

    [TestCase(0.1)]
    [TestCase(0.2)]
    [TestCase(0.4)]
    public void FractionalConsumptionIsExactAcross180MinutesAndTimePartitions(decimal rate)
    {
        var generation = Guid.NewGuid();
        var whole = Create(generation, rate);
        var split = Create(generation, rate);
        whole.SettleConsumption = split.SettleConsumption = (_, commit) => { commit(true); return true; };
        whole.Advance(TimeSpan.FromMinutes(180));
        for (var i = 0; i < 180 * 60 * 4; i++)
            split.Advance(TimeSpan.FromSeconds(0.25));
        var a = whole.Markets["A"].Goods["F"];
        var b = split.Markets["A"].Goods["F"];
        Assert.That(a.Consumed, Is.EqualTo(Q(rate * 180)));
        Assert.That(b.Consumed, Is.EqualTo(a.Consumed));
        Assert.That(a.Stock + a.Consumed, Is.EqualTo(Q(100)));
        Assert.That(b.Stock, Is.EqualTo(a.Stock));
        Assert.That(a.Reserved, Is.Zero);
    }

    [Test]
    public void DemographyUsesActualMandatoryAndGrowthFoodSeparately()
    {
        SWGlobalWarMarketSimulation PopulationModel(int stock, decimal growth)
        {
            var model = new SWGlobalWarMarketSimulation([new("SWGlobalWarF", 1)],
                [new("A", [new("SWGlobalWarF", Q(stock), Q(10))], SWGlobalWarFaction.Republic,
                    new SWGlobalWarEconomyProfilePrototype())], [], Guid.NewGuid());
            model.Markets["A"].GrowthFoodFraction = growth;
            model.SettleConsumption = (_, commit) => { commit(true); return true; };
            return model;
        }
        var growing = PopulationModel(100, 0.1m);
        var stable = PopulationModel(100, 0);
        var hungry = PopulationModel(0, 0.1m);
        foreach (var model in new[] { growing, stable, hungry })
            model.Advance(TimeSpan.FromMinutes(1));
        Assert.That(growing.Markets["A"].Goods["SWGlobalWarF"].Consumed, Is.EqualTo(Q(10)));
        Assert.That(growing.Markets["A"].Goods["SWGlobalWarF"].GrowthConsumed, Is.EqualTo(Q(1)));
        Assert.That(growing.Markets["A"].Population, Is.EqualTo(1m + 0.02m * 0.4m * (1m - 1m / 1.5m)));
        Assert.That(stable.Markets["A"].Population, Is.EqualTo(1));
        Assert.That(hungry.Markets["A"].Population, Is.EqualTo(0.98m));
        Assert.That(hungry.Markets["A"].Goods["SWGlobalWarF"].GrowthConsumed, Is.Zero);
    }

    [Test]
    public void EndingDuringSettlementStopsTheRemainingCatchUp()
    {
        var model = Create(Guid.NewGuid(), 1);
        var active = true;
        model.IsActive = () => active;
        model.SettleConsumption = (_, commit) => { commit(true); active = false; return true; };
        model.Advance(TimeSpan.FromMinutes(10));
        Assert.That(model.Time, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(model.Markets["A"].Goods["F"].Consumed, Is.EqualTo(Q(1) / 60));
    }

    [Test]
    public void FailedSettlementPreservesGoodsAndReentryCannotStartAnotherStep()
    {
        var model = Create(Guid.NewGuid(), 1);
        model.SettleConsumption = (_, complete) =>
        {
            Assert.That(model.InternalReservationCount, Is.GreaterThan(0));
            complete(false);
            Assert.That(model.InternalReservationCount, Is.Zero);
            var receipts = model.ConsumptionReceiptCount;
            complete(false);
            Assert.That(model.ConsumptionReceiptCount, Is.EqualTo(receipts));
            return false;
        };
        model.Advance(TimeSpan.FromMinutes(1));
        var good = model.Markets["A"].Goods["F"];
        Assert.That(good.Stock, Is.EqualTo(Q(100)));
        Assert.That(good.Consumed, Is.Zero);
        Assert.That(good.Reserved, Is.Zero);
        model.SettleConsumption = (_, commit) =>
        {
            Assert.Throws<InvalidOperationException>(() => model.Advance(TimeSpan.FromSeconds(1)));
            commit(true);
            commit(true);
            return true;
        };
        model.Advance(TimeSpan.FromMinutes(1));
        Assert.That(good.Stock, Is.EqualTo(Q(99)));
        Assert.That(good.Consumed, Is.EqualTo(Q(1)));
        Assert.That(model.Time, Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    [Test]
    public void ConsumptionLifetimeDoesNotChangeExternalReplayOrCaptureCancellation()
    {
        var generation = Guid.NewGuid();
        var model = Create(generation, 0.1m);
        var external = new SWGlobalWarStockRequest(generation, Guid.NewGuid(), "A", "F", Q(5),
            SWGlobalWarFaction.Republic, "mandatory-consumption");
        Assert.That(model.TryReserveStock(external, out _), Is.True);
        Assert.That(model.TryCommitStock(generation, external.Operation, out var committed), Is.True);
        var cancelled = external with { Operation = Guid.NewGuid(), Quantity = Q(3) };
        Assert.That(model.TryReserveStock(cancelled, out _), Is.True);
        Assert.That(model.TryCancelStock(generation, cancelled.Operation), Is.True);
        var pending = external with { Operation = Guid.NewGuid(), Quantity = Q(2) };
        Assert.That(model.TryReserveStock(pending, out _), Is.True);
        model.SettleConsumption = (_, complete) =>
        {
            Assert.That(model.InternalPendingReservationCount, Is.GreaterThan(0));
            complete(true);
            Assert.That(model.InternalReservationCount, Is.Zero);
            var receipts = model.ConsumptionReceiptCount;
            var stock = model.Markets["A"].Goods["F"].Stock;
            complete(true);
            Assert.That(model.Markets["A"].Goods["F"].Stock, Is.EqualTo(stock));
            Assert.That(model.ConsumptionReceiptCount, Is.EqualTo(receipts));
            return true;
        };
        model.Advance(TimeSpan.FromMinutes(10));
        Assert.That(model.InternalReservationCount, Is.Zero);
        Assert.That(model.ExternalReservationCount, Is.EqualTo(3));
        Assert.That(model.ExternalPendingReservationCount, Is.EqualTo(1));
        Assert.That(model.TryReserveStock(external, out var replay), Is.True);
        Assert.That(replay, Is.SameAs(committed));
        Assert.That(model.TryCommitStock(generation, external.Operation, out replay), Is.True);
        Assert.That(replay.Withdrawn.Sum(b => b.Quantity), Is.EqualTo(Q(5)));
        Assert.That(replay.Withdrawn.Single().Origin, Is.EqualTo(SWGlobalWarFaction.Republic));
        Assert.That(model.TryReserveStock(external with { Quantity = Q(6) }, out _), Is.False);
        Assert.That(model.TryCancelStock(generation, cancelled.Operation), Is.True);
        Assert.That(model.TryCommitStock(generation, cancelled.Operation, out _), Is.False);
        model.CommitOwnershipBatch(generation, [new("A", SWGlobalWarFaction.Empire, Guid.NewGuid())]);
        Assert.That(model.InternalReservationCount, Is.Zero);
        Assert.That(model.ExternalPendingReservationCount, Is.Zero);
        Assert.That(model.TryCommitStock(generation, pending.Operation, out _), Is.False);
        Assert.That(model.TryCommitStock(generation, external.Operation, out replay), Is.True);
        Assert.That(replay, Is.SameAs(committed));
        Assert.That(model.Markets["A"].Goods["F"].Stock, Is.EqualTo(Q(94)));
        Assert.That(model.ConsumptionReceiptCount, Is.EqualTo(600));
    }
}
