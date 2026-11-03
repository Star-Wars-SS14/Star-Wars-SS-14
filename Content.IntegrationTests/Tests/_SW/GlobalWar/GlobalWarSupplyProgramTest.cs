using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarSupplyProgramTest
{
    [TestCase(10, 7550)]
    [TestCase(20, 15100)]
    [TestCase(30, 22650)]
    public void QuotasRetainApprovedWholeItemPrices(int participants, int total)
    {
        var cost = Enum.GetValues<SWGlobalWarSupplyKind>()
            .Where(k => SWGlobalWarSupplyProgram.Quota(k, participants) > 0)
            .Sum(k => SWGlobalWarSupplyProgram.Quota(k, participants) * SWGlobalWarSupplyProgram.UnitPrice(k));
        Assert.That(cost, Is.EqualTo(total));
        Assert.That(SWGlobalWarSupplyProgram.Target(SWGlobalWarSupplyKind.T2, 11), Is.EqualTo(17));
        Assert.That(SWGlobalWarSupplyProgram.Quota(SWGlobalWarSupplyKind.T2, 11), Is.EqualTo(3));
        Assert.That(SWGlobalWarSupplyProgram.Quota(SWGlobalWarSupplyKind.T1, participants), Is.Zero);
        Assert.That(SWGlobalWarSupplyProgram.Quota(SWGlobalWarSupplyKind.AdvancedElectronics, participants), Is.Zero);
        Assert.That(SWGlobalWarSupplyProgram.UnitPrice(SWGlobalWarSupplyKind.Medicine, true), Is.EqualTo(500));
    }

    [Test]
    public void WindowDecisionSurvivesIssueAndManufacturingWithoutDoubleCounting()
    {
        var model = new SWGlobalWarMarketSimulation([], [], [], Guid.NewGuid());
        var n = 10;
        var stock = 14;
        model.SupplyParticipants = _ => n;
        model.AcceptedSupplyStock = (_, kind) => kind == SWGlobalWarSupplyKind.T2 ? stock : 0;
        var program = model.SupplyPrograms[SWGlobalWarFaction.Republic];
        model.CaptureStartingSupply();
        Assert.That(program.StartArsenalLimit, Is.EqualTo(13));
        model.Advance(TimeSpan.FromSeconds(1199));
        Assert.That(program.Window, Is.Zero);
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.T2), Is.Zero);
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(program.Windows[1].Budgets[SWGlobalWarSupplyKind.T2], Is.EqualTo(1));
        var order = new SWGlobalWarSupplyObligation(Guid.NewGuid(), 1, SWGlobalWarSupplyKind.T2, 1);
        Assert.That(model.AddSupplyObligation(SWGlobalWarFaction.Republic, order), Is.True);
        Assert.That(model.AddSupplyObligation(SWGlobalWarFaction.Republic, order), Is.False);
        order.Stage = SWGlobalWarSupplyStage.Producing;
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.T2), Is.Zero);
        order.Stage = SWGlobalWarSupplyStage.Ready;
        order.Stage = SWGlobalWarSupplyStage.InTransit;
        Assert.That(program.Orders.Values.Sum(o => o.Outstanding), Is.EqualTo(1));
        order.Accepted = 1;
        stock = 15;
        order.Stage = SWGlobalWarSupplyStage.Complete;
        stock = 0; // Issued equipment is no longer accepted warehouse stock.
        model.Advance(TimeSpan.FromSeconds(1));
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.T2), Is.Zero);
        model.Advance(TimeSpan.FromSeconds(1199));
        Assert.That(program.Windows[2].Budgets[SWGlobalWarSupplyKind.T2], Is.EqualTo(2));
        var unfinished = new SWGlobalWarSupplyObligation(Guid.NewGuid(), 2, SWGlobalWarSupplyKind.T2, 2);
        Assert.That(model.AddSupplyObligation(SWGlobalWarFaction.Republic, unfinished), Is.True);
        n = 1;
        model.CaptureStartingSupply();
        model.Advance(TimeSpan.FromMinutes(20));
        Assert.That(program.Participants, Is.EqualTo(1));
        Assert.That(program.StartParticipants, Is.EqualTo(10));
        Assert.That(program.StartArsenalLimit, Is.EqualTo(13));
        Assert.That(program.Windows[3].Budgets[SWGlobalWarSupplyKind.T2], Is.Zero,
            "The previous window's funded order already covers the smaller target.");
        Assert.That(unfinished.Outstanding, Is.EqualTo(2));
    }

    [Test]
    public void CancelReleasesOnlyCurrentDecisionAndCurrentNHasNoStartingClamp()
    {
        var model = new SWGlobalWarMarketSimulation([], [], [], Guid.NewGuid());
        var participants = 31;
        model.SupplyParticipants = _ => participants;
        model.CaptureStartingSupply();
        model.Advance(TimeSpan.FromMinutes(20));
        var program = model.SupplyPrograms[SWGlobalWarFaction.Republic];
        Assert.That(program.Participants, Is.EqualTo(31));
        Assert.That(program.StartParticipants, Is.EqualTo(30));
        Assert.That(program.StartArsenalLimit, Is.EqualTo(38));
        var order = new SWGlobalWarSupplyObligation(Guid.NewGuid(), 1, SWGlobalWarSupplyKind.Medicine, 13);
        Assert.That(model.AddSupplyObligation(SWGlobalWarFaction.Republic, order), Is.True);
        order.Accepted = 5;
        order.Cancelled = 8;
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.Medicine), Is.EqualTo(8));
        model.Advance(TimeSpan.FromMinutes(20));
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.Medicine), Is.EqualTo(13),
            "Unused previous-window quota is not added to the current decision.");
        Assert.That(model.AddSupplyObligation(SWGlobalWarFaction.Republic,
            new(Guid.NewGuid(), 1, SWGlobalWarSupplyKind.Medicine, 1)), Is.False);
        participants = 0;
        model.Advance(TimeSpan.FromMinutes(20));
        Assert.That(program.Participants, Is.Zero);
        Assert.That(program.RemainingQuota(SWGlobalWarSupplyKind.Medicine), Is.Zero);
        Assert.That(program.Lines.Values.All(l => l.LocalPercent == 25), Is.True);
    }
}
