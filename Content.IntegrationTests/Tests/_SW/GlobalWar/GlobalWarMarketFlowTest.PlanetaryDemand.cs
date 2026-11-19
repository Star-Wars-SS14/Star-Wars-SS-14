using System;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    [TestCase(true)]
    [TestCase(false)]
    public void PlayerDemandShipsSurplusAndCountsIncoming(bool once)
    {
        var model = new SWGlobalWarMarketSimulation([new("kit", 1, WholeItems: true)],
            [new("A", [new("kit", Q(14), 0)], SWGlobalWarFaction.Republic),
             new("B", [new("kit", Q(2), 0)], SWGlobalWarFaction.Republic)],
            [new("A", "B", TimeSpan.FromSeconds(4), 20)], Guid.NewGuid());
        model.TradePolicy = Allow;
        model.SetMarketDemand("A", "kit", 10, false);
        Assert.That(model.SetMarketDemand("B", "kit", once ? 3 : 5, once), Is.True);

        model.Advance(TimeSpan.FromSeconds(3));
        var destination = model.Markets["B"].Goods["kit"];
        Assert.That(destination.Incoming, Is.EqualTo(Q(3)));
        Assert.That(model.Shipments, Has.Count.EqualTo(1));
        model.Advance(TimeSpan.FromSeconds(4));
        Assert.That(destination.Stock, Is.EqualTo(Q(5)));
        Assert.That(destination.DeliveryRemaining, Is.Zero);
        Withdraw(model, "B", "kit", 3);

        model.Advance(TimeSpan.FromSeconds(6));
        Assert.That(destination.Stock, Is.EqualTo(Q(once ? 2 : 3)));
        Assert.That(model.Markets["A"].Goods["kit"].Stock, Is.EqualTo(Q(once ? 11 : 10)));
    }
}
