using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string PhysicalFactoryPrototypes = """
- type: swGlobalWarPlanet
  id: SWEPhysicalEmpireSource
  name: Physical Empire factory test
  kind: Industry
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  enclave: true

- type: entity
  parent: SWGlobalWarCampaign
  id: SWEPhysicalRule
  components:
  - type: SWGlobalWarCampaign
    participantAbsenceDuration: 3600
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestLnRHome, SWSpawnTestSupplySource, SWSpawnTestLnEHome, SWEPhysicalEmpireSource]
""";

    [Test]
    public async Task PhysicalFactoriesUseFiniteApcPowerAndKeepOneOutputAcrossAnOutage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var markets = server.System<SWGlobalWarMarketSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var maps = server.System<SharedMapSystem>();
        var containers = server.System<SharedContainerSystem>();
        var batteries = server.System<SharedBatterySystem>();
        var records = new List<(EntityUid Store, SWGlobalWarFactoryComponent Factory,
            ApcPowerReceiverComponent Receiver, SWGlobalWarSupplyProduction Work)>();
        var sources = new List<EntityUid>();
        EntityUid rule = default;
        Guid generation = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<GameTicker>().StartGameRule("SWEPhysicalRule"), Is.True);
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            rule = campaign.Owner;
            generation = campaign.Comp.Generation;
            markets.Simulation!.SupplyParticipants = _ => 0;
            foreach (var (world, faction) in new[]
                     {
                         ("SWSpawnTestSupplySource", SWGlobalWarFaction.Republic),
                         ("SWEPhysicalEmpireSource", SWGlobalWarFaction.Empire),
                     })
            {
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var mapId = server.EntMan.GetComponent<MapComponent>(map!.Value).MapId;
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = -2; x <= 5; x++)
                for (var y = -2; y <= 5; y++)
                    maps.SetTile(grid, new Vector2i(x, y), new Tile(1));
                var apc = server.EntMan.SpawnEntity("APCBasic", new EntityCoordinates(grid, 0.5f, 0.5f));
                for (var x = 0; x <= 4; x++)
                for (var y = 0; y <= 4; y++)
                    server.EntMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
                batteries.SetMaxCharge(apc, 10_000_000);
                batteries.SetCharge(apc, 10_000_000);
                sources.Add(apc);
                foreach (var kind in Enum.GetValues<SWGlobalWarSupplyKind>())
                {
                    var position = new EntityCoordinates(grid, 1.5f + (int) kind % 3, 1.5f + (int) kind / 3);
                    var store = server.EntMan.SpawnEntity("SWGlobalWarFactoryStorage", position);
                    var factory = server.EntMan.GetComponent<SWGlobalWarFactoryComponent>(store);
                    factory.Kind = kind;
                    Assert.That(markets.TryRegisterSupplyStore(generation, store), Is.True);
                    Assert.That(containers.TryGetContainer(store, "sw-program-supply", out var stock), Is.True);
                    var physical = kind switch
                    {
                        SWGlobalWarSupplyKind.T2 => new[] { "SWBlasterFrame", "SWBlasterBarrel" },
                        SWGlobalWarSupplyKind.Armor => Enumerable.Repeat("SWReinforcedSteel", 5).ToArray(),
                        SWGlobalWarSupplyKind.Magazine => ["SWAdvancedCables1"],
                        SWGlobalWarSupplyKind.AdvancedElectronics => ["SWAdvancedCables1", "SWAdvancedCables1", "SWSheetLantanidAlloy1"],
                        _ => Array.Empty<string>(),
                    };
                    foreach (var prototype in physical)
                        Assert.That(containers.Insert(server.EntMan.SpawnEntity(prototype, position), stock), Is.True);
                    Assert.That(markets.TryReserveSupplyProduction(new(generation, Guid.NewGuid(), store, faction,
                        kind, 1), out var work), Is.True);
                    records.Add((store, factory, server.EntMan.GetComponent<ApcPowerReceiverComponent>(store), work));
                }
            }
        });
        try
        {
            // The ordinary APC ramps its finite supply at 500 W/s; seven 1 kW receivers need time to settle.
            await pair.RunSeconds(20);
            await server.WaitAssertion(() =>
            {
                foreach (var row in records)
                {
                    Assert.That(row.Receiver.NeedsPower, Is.True);
                    Assert.That(row.Receiver.Powered, Is.True,
                        $"A finite real APC must supply {row.Work.Request.Faction}/{row.Work.Request.Kind}; " +
                        $"anchored={server.EntMan.GetComponent<TransformComponent>(row.Store).Anchored}, " +
                        $"provider={server.EntMan.GetComponent<ExtensionCableReceiverComponent>(row.Store).Provider}, " +
                        $"supply={string.Join(',', sources.Select(e => server.EntMan.GetComponent<PowerNetworkBatteryComponent>(e).CurrentSupply))}.");
                    row.Factory.Enabled = true;
                }
            });
            await pair.RunSeconds(5);
            Dictionary<Guid, TimeSpan> progress = new();
            await server.WaitAssertion(() =>
            {
                foreach (var row in records)
                {
                    Assert.That(row.Work.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Producing));
                    row.Receiver.PowerDisabled = true;
                }
            });
            await pair.RunSeconds(1);
            await server.WaitAssertion(() => progress = records.ToDictionary(r => r.Work.Request.Operation,
                r => r.Factory.Work[r.Work.Request.Operation].ProductiveTime));
            await pair.RunSeconds(20);
            await server.WaitAssertion(() =>
            {
                foreach (var row in records)
                {
                    Assert.That(row.Receiver.Powered, Is.False);
                    Assert.That(row.Factory.Work[row.Work.Request.Operation].ProductiveTime,
                        Is.EqualTo(progress[row.Work.Request.Operation]));
                    Assert.That(row.Work.Output, Is.Empty);
                    row.Receiver.PowerDisabled = false;
                }
            });
            await pair.RunSeconds(65);
            await server.WaitAssertion(() =>
            {
                foreach (var row in records.Where(r => r.Work.Request.Kind == SWGlobalWarSupplyKind.T2))
                {
                    Assert.That(row.Factory.Work[row.Work.Request.Operation].Output, Has.Count.EqualTo(row.Work.Quantity),
                        "T2 finishes after 60 powered seconds, independently of the base program's slower planned rate.");
                    Assert.That(row.Work.Output, Is.Empty, "The plan still holds the physical pieces in escrow.");
                }
            });
            await pair.RunSeconds((float) records.Max(r => r.Work.Duration.TotalSeconds) + 2);
            await server.WaitAssertion(() =>
            {
                foreach (var row in records)
                {
                    Assert.That(row.Work.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Complete));
                    Assert.That(row.Work.Output.Count, Is.EqualTo(row.Work.Quantity));
                    var units = new HashSet<Guid>();
                    foreach (var entity in row.Work.Output)
                    {
                        Assert.That(markets.TryGetSupplyItem(entity, out var item), Is.True);
                        Assert.That(item.OriginOwner, Is.EqualTo(row.Work.Request.Faction));
                        Assert.That(item.Payment, Is.Null);
                        Assert.That(units.Add(item.Unit), Is.True);
                        Assert.That(markets.IsSuitableSupplyItem(entity, item.Kind, item.Owner, item.Bacta), Is.True);
                        if (item.Kind == SWGlobalWarSupplyKind.Magazine)
                            Assert.That(server.EntMan.GetComponent<BatteryComponent>(entity).CurrentCharge, Is.Zero);
                        Assert.That(server.EntMan.GetComponent<TransformComponent>(entity).ParentUid, Is.EqualTo(row.Store));
                    }
                    Assert.That(markets.TryCompleteSupplyProduction(generation, row.Work.Request.Operation,
                        row.Work.Output), Is.True, "Replay uses the same output and cannot manufacture another batch.");
                }
                foreach (var source in sources)
                    Assert.That(server.EntMan.GetComponent<BatteryComponent>(source).CurrentCharge,
                        Is.InRange(1f, 9_999_999f), "Production draws down finite charge.");
            });
        }
        finally
        {
            await server.WaitPost(() => server.EntMan.DeleteEntity(rule));
        }
        await pair.CleanReturnAsync();
    }
}
