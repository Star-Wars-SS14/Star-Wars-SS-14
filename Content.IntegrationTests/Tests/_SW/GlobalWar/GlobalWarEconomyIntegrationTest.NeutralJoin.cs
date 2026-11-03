using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    /// <summary>The real manifest and ownership commit: nobody edits a contract, and each joined world stays supplied.</summary>
    [Test]
    public async Task JoinedNeutralsStaySuppliedByTheirOwnersWithoutAnyEdit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        const string food = "SWGlobalWarF";
        const string n0 = "SWGlobalWarN0";
        const string n1 = "SWGlobalWarN1";
        const string waiting = "sw-global-war-economy-wait-ownership";

        await server.WaitPost(() => Assert.That(ticker.StartGameRule("SWGlobalWarCampaign"), Is.True));
        foreach (var location in new[] { SWGlobalWarPlanetLocation.Orbit, SWGlobalWarPlanetLocation.Surface })
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                foreach (var (world, owner) in new[] { (n0, SWGlobalWarFaction.Empire), (n1, SWGlobalWarFaction.Republic) })
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                    Assert.That(steps.TryQueueOwnership(planet.Value, location, owner, state.OwnershipRevision,
                        campaign.Comp.Generation, Guid.NewGuid()), Is.True);
                }
            });
            await pair.RunTicksSync(2);
        }
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var model = markets.Simulation!;
            Assert.That(model.Markets[n0].Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
            Assert.That(model.Markets[n1].Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
            foreach (var (world, owner) in new[] { (n0, SWGlobalWarFaction.Empire), (n1, SWGlobalWarFaction.Republic) })
            {
                var market = model.Markets[world];
                var mandatory = market.Goods[food].BaseConsumptionPerMinute * market.Population / model.UnitsPerItem;
                var contracts = model.Flows.Values.Where(f => f.Destination == world).ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(contracts.Where(f => f.Commodity == food && f.Priority == SWGlobalWarFlowPriority.MandatoryFood &&
                        f.ContractFaction == owner).Sum(f => f.PerMinute), Is.EqualTo(mandatory).Within(0.000001m),
                        "The owner's two farms carry the whole mandatory food.");
                    Assert.That(contracts.Where(f => f.ContractFaction != owner).All(f => f.PerMinute == 0 && f.WaitReason == waiting),
                        Is.True, "The other side's contracts give no right to deliver.");
                    Assert.That(contracts.Where(f => f.ContractFaction == owner).All(f => f.WaitReason != waiting), Is.True);
                });
            }
            var industry = model.Markets[n1];
            foreach (var recipe in industry.Profile!.Recipes)
            foreach (var (input, perOutput) in recipe.Inputs)
            {
                var need = industry.ProductionOrders.GetValueOrDefault(recipe.Output.Id) * perOutput;
                var supplied = model.Flows.Values.Where(f => f.Destination == n1 && f.Commodity == input.Id &&
                    f.ContractFaction == SWGlobalWarFaction.Republic).Sum(f => f.PerMinute);
                Assert.That(supplied, Is.EqualTo(need).Within(0.000001m), input.Id);
            }
            Assert.That(model.Flows.Values.Where(f => f.Source == n0 && f.ContractFaction == SWGlobalWarFaction.Empire)
                .All(f => f.PerMinute > 0), Is.True, "Luxury goes on to the owner.");
            Assert.That(model.Flows.Values.Where(f => f.Source == n0 && f.ContractFaction == SWGlobalWarFaction.Republic)
                .All(f => f.PerMinute == 0), Is.True, "The other power loses its neutral offer.");
            Assert.That(model.Flows.Values.Where(f => f.Source == n1 && f.ContractFaction == SWGlobalWarFaction.Republic)
                .All(f => f.PerMinute > 0), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
