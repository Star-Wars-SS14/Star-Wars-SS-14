using System;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarCampaignClockTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: SWClockTestEmpire
  name: Empire
  initialOwner: Empire

- type: entity
  parent: WorldTestRule
  id: SWCampaignClockTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [WorldTestHome, SWClockTestEmpire]
  - type: SWGlobalWarCampaign
""";

    [Test]
    public async Task MapSleepKeepsCampaignClocksRunningAndOldRuleCannotStopTheNextGeneration()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<MapSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        SWGlobalWarMarketSimulation firstMarket = default!;
        Entity<SWGlobalWarCampaignComponent> first = default;
        EntityUid surface = default;
        TimeSpan before = default;
        TimeSpan stopped = default;
        TimeSpan marketStopped = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(clock.TryGetCampaign(out first), Is.True);
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var home), Is.True);
            Assert.That(planets.TryEnsureLocation(home!.Value, SWGlobalWarPlanetLocation.Surface,
                out var map, out _), Is.True);
            surface = map!.Value;
            maps.SetPaused(surface, true);
            before = clock.Elapsed(first);
            firstMarket = markets.Simulation!;
            Assert.That(firstMarket, Is.Not.Null);
            markets.Update(600);
            Assert.That(firstMarket.Time, Is.EqualTo(TimeSpan.Zero), "A campaign must ignore an arbitrary frame-time delta.");
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(maps.IsPaused(surface), Is.True);
            Assert.That(clock.Elapsed(first) - before, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9)));
            Assert.That(firstMarket.Time, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(firstMarket.Time, Is.LessThanOrEqualTo(clock.Elapsed(first)));
            Assert.That(clock.TryAdvancePhase(first.Comp.Generation, SWGlobalWarCampaignPhase.Ending), Is.True);
            stopped = clock.Elapsed(first);
            marketStopped = firstMarket.Time;
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(clock.Elapsed(first), Is.EqualTo(stopped));
            Assert.That(firstMarket.Time, Is.EqualTo(marketStopped), "Ending cannot run economic catch-up.");
            ticker.EndGameRule(first.Owner);
            Assert.That(clock.TryGetCampaign(out _), Is.False);
            Assert.That(markets.Simulation, Is.Null);
            Assert.That(ticker.StartGameRule("SWCampaignClockTestRule"), Is.True);
            Assert.That(clock.TryGetCampaign(out var second), Is.True);
            Assert.That(second.Comp.Generation, Is.Not.EqualTo(first.Comp.Generation));
            Assert.That(markets.Simulation, Is.Not.SameAs(firstMarket));
            Assert.That(markets.Simulation!.Time, Is.EqualTo(TimeSpan.Zero));
            server.EntMan.DeleteEntity(first.Owner);
            Assert.That(clock.TryGetCampaign(out var current), Is.True);
            Assert.That(current.Owner, Is.EqualTo(second.Owner));
            Assert.That(clock.TryAdvancePhase(first.Comp.Generation, SWGlobalWarCampaignPhase.Ending), Is.False);
            Assert.That(current.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Running));
            Assert.That(markets.Simulation, Is.Not.Null);
        });
        await pair.CleanReturnAsync();
    }
}
