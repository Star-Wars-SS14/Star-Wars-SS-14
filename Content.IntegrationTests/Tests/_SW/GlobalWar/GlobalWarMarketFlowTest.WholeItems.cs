using System;
using System.Linq;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMarketFlowTest
{
    /// <summary>A flow of 1.5 items a minute never carries half an item; ore at the same rate still may.</summary>
    [Test]
    public void WholeItemFlowShipsOnlyWholeItems()
    {
        var model = new SWGlobalWarMarketSimulation([new("AE", 1, WholeItems: true), new("Fe", 1)],
            [new("H", [new("AE", Q(40), 0), new("Fe", Q(40), 0)], SWGlobalWarFaction.Republic, new() { HubCapacity = 150 }),
                new("D", [new("AE", 0, Q(1.5m)), new("Fe", 0, Q(1.5m))], SWGlobalWarFaction.Republic,
                    new() { HubCapacity = 150 })],
            [new("H", "D", TimeSpan.FromMinutes(1), 64)], Guid.NewGuid());
        model.ConfigureFlows([Flow("items", "H", "D", "AE", 1.5m), Flow("ore", "H", "D", "Fe", 1.5m)]);
        model.TradePolicy = Allow;
        model.DemographyEnabled = false;
        var sawFractionalOre = false;
        for (var step = 0; step < 240; step++)
        {
            model.Advance(TimeSpan.FromSeconds(1));
            foreach (var shipment in model.Shipments)
            {
                if (shipment.Commodity == "AE")
                    Assert.That(shipment.Quantity % Q(1), Is.Zero, $"AE shipment of {shipment.Quantity} quanta");
                else if (shipment.Quantity % Q(1) != 0)
                    sawFractionalOre = true;
            }
        }
        Assert.That(model.Flows["items"].Dispatched, Is.GreaterThanOrEqualTo(Q(4)));
        Assert.That(model.Flows["items"].Dispatched % Q(1), Is.Zero);
        Assert.That(sawFractionalOre, Is.True, "Resources keep moving in fractions.");
    }
}
