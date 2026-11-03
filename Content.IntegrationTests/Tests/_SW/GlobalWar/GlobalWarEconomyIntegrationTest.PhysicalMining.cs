using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string PhysicalMiningPrototypes = """
- type: swGlobalWarEconomyProfile
  id: SWEPhysicalMineProfile
  openingStock: { SWGlobalWarFe: 20 }
  recipes:
  - output: SWGlobalWarFe
    perMinute: 1

- type: swGlobalWarPlanet
  id: SWEPhysicalMine
  name: Physical mine fixture
  kind: Mining
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWEPhysicalMineProfile

- type: entity
  parent: SWGlobalWarCampaign
  id: SWEPhysicalMiningRule
  components:
  - type: SWGlobalWarCampaign
    participantAbsenceDuration: 3600
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestLnRHome, SWEPhysicalMine, SWSpawnTestLnEHome]
""";

    [Test]
    public async Task PhysicalMiningSharesFiniteDepositAcrossPowerOutageCaptureAndExtraDrills()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var markets = server.System<SWGlobalWarMarketSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var containers = server.System<SharedContainerSystem>();
        var drills = new List<EntityUid>();
        EntityUid rule = default;
        EntityUid planet = default;
        Guid generation = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<GameTicker>().StartGameRule("SWEPhysicalMiningRule"), Is.True);
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            rule = campaign.Owner;
            generation = campaign.Comp.Generation;
            Assert.That(planets.TryGetPlanet("SWEPhysicalMine", out var world), Is.True);
            planet = world!.Value;
            Assert.That(planets.TryEnsureLocation(planet, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(
                server.EntMan.GetComponent<MapComponent>(map!.Value).MapId);
            var maps = server.System<SharedMapSystem>();
            for (var x = -2; x <= 5; x++)
            for (var y = -2; y <= 5; y++)
                maps.SetTile(grid, new Vector2i(x, y), new Tile(1));
            var apc = server.EntMan.SpawnEntity("APCBasic", new EntityCoordinates(grid, .5f, .5f));
            for (var x = 0; x <= 4; x++)
                server.EntMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, x + .5f, .5f));
            server.System<SharedBatterySystem>().SetMaxCharge(apc, 1_000_000);
            server.System<SharedBatterySystem>().SetCharge(apc, 1_000_000);
            for (var i = 0; i < 3; i++)
            {
                var drill = server.EntMan.SpawnEntity("SWGlobalWarMiningDrill", new EntityCoordinates(grid, i + 1.5f, .5f));
                server.EntMan.GetComponent<SWGlobalWarFactoryComponent>(drill).AutoStart = false;
                drills.Add(drill);
            }
        });
        try
        {
            await pair.RunSeconds(20);
            await server.WaitAssertion(() =>
            {
                foreach (var drill in drills)
                {
                    Assert.That(server.EntMan.GetComponent<ApcPowerReceiverComponent>(drill).Powered, Is.True);
                    server.EntMan.GetComponent<SWGlobalWarFactoryComponent>(drill).Enabled = true;
                }
            });
            await pair.RunSeconds(61);
            await server.WaitAssertion(() =>
            {
                Assert.That(drills.Select(Count).Order().ToArray(), Is.EqualTo(new[] { 0, 6, 6 }));
                Assert.That(Deposits()["SWEPhysicalMine"], Is.EqualTo(2400 - 12));
                foreach (var drill in drills)
                    server.EntMan.GetComponent<ApcPowerReceiverComponent>(drill).PowerDisabled = true;
            });
            await pair.RunSeconds(61);
            await server.WaitAssertion(() =>
            {
                Assert.That(drills.Sum(Count), Is.EqualTo(12));
                Assert.That(Deposits()["SWEPhysicalMine"], Is.EqualTo(2388));
                var steps = server.System<SWGlobalWarCampaignStepSystem>();
                Assert.That(steps.TryQueueOwnership(planet, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire,
                    server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).OwnershipRevision,
                    generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, server.ResolveDependency<IGameTiming>().CurTick), Is.True);
                Assert.That(Deposits()["SWEPhysicalMine"], Is.EqualTo(2388), "Capture cannot replenish a physical deposit.");
                foreach (var drill in drills)
                    server.EntMan.GetComponent<ApcPowerReceiverComponent>(drill).PowerDisabled = false;
            });
            await pair.RunSeconds(61);
            await server.WaitAssertion(() =>
            {
                Assert.That(drills.Sum(Count), Is.EqualTo(24));
                Assert.That(Deposits()["SWEPhysicalMine"], Is.EqualTo(2376));
                var virtualIron = markets.Simulation!.Markets["SWEPhysicalMine"].Goods["SWGlobalWarFe"];
                Assert.That(virtualIron.Stock, Is.EqualTo(SWGlobalWarMarketUnits.FromUnits(20) + virtualIron.Produced),
                    "Only the independent virtual recipe credits the market; physical ore does not.");
                // Place the finite-deposit fixture at its final two units; do not spend 200 campaign minutes testing this boundary.
                Deposits()["SWEPhysicalMine"] = 2;
            });
            await pair.RunSeconds(61);
            await server.WaitAssertion(() =>
            {
                Assert.That(drills.Sum(Count), Is.EqualTo(26));
                Assert.That(Deposits()["SWEPhysicalMine"], Is.Zero);
                foreach (var drill in drills)
                    Assert.That(server.EntMan.GetComponent<SWGlobalWarFactoryComponent>(drill).WorkLease, Is.Null);
            });
        }
        finally
        {
            await server.WaitPost(() => server.EntMan.DeleteEntity(rule));
        }
        await pair.CleanReturnAsync();

        int Count(EntityUid drill)
        {
            Assert.That(containers.TryGetContainer(drill, "sw-program-supply", out var inventory), Is.True);
            foreach (var item in inventory.ContainedEntities)
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID, Is.EqualTo("SteelOre1"));
            return inventory.ContainedEntities.Count;
        }
        Dictionary<string, int> Deposits() => (Dictionary<string, int>) typeof(SWGlobalWarMarketSystem)
            .GetField("_physicalMiningDeposits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(markets)!;
    }
}
