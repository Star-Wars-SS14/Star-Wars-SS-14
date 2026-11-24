using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Production;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string PhysicalFreightPrototypes = """
- type: entity
  parent: SWGlobalWarTravelConsole
  id: SWEFreightTestConsole
  components:
  - type: ApcPowerReceiver
    needsPower: false
""";

    [TestCase(false, "ClothingBackpack", "SWSpawnTestSupplyTransportIndustry")]
    [TestCase(true, "ClothingBackpack", "SWSpawnTestSupplyTransportIndustry")]
    [TestCase(false, "SWCrateWood", "SWSpawnTestSupplyTransportIndustry")]
    [TestCase(false, "SWCrateWood", "SWSpawnTestSupplyTransportDirect")]
    public async Task PhysicalFreightRequiresRealFlightsAndPaysOnlyAtUnloading(bool redirect, string carrierPrototype, string sourceWorld)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var containers = server.System<SharedContainerSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid rule = default;
        EntityUid carrier = default;
        EntityUid arsenal = default;
        EntityUid ship = default;
        EntityUid console = default;
        EntityUid pilot = default;
        Guid generation = default;
        SWGlobalWarSupplyPurchase purchase = default!;
        var savedTime = timing.TimeBase;
        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<GameTicker>().StartGameRule("SWSpawnTestSupplyTransportRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string targetWorld = "SWSpawnTestSupplyTransportHome";
                model.SupplyParticipants = _ => 20;
                model.SupplyPrograms[faction].Lines[kind].LocalPercent = 0;
                var factory = Store(sourceWorld, false);
                arsenal = Store(targetWorld, true);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, faction, kind, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                var pieces = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                Advance(1200);
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, pieces), Is.True);
                // Mark this source as a physical E endpoint; fabrication itself is covered by the powered-line test.
                server.EntMan.AddComponent<SWGlobalWarFactoryComponent>(factory).AutoStart = false;
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), Revision(), faction,
                    factory, arsenal, kind, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out purchase), Is.True);
                Assert.That(purchase.AwaitingPhysical, Is.True);
                carrier = server.EntMan.SpawnEntity(carrierPrototype,
                    server.EntMan.GetComponent<TransformComponent>(factory).Coordinates);
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, purchase.Goods), Is.True);
                var manifests = model.Shipments.Where(s => s.SupplyManifest?.Purchase == request.Operation).ToArray();
                Assert.That(manifests, Has.Length.EqualTo(1));
                Assert.That(manifests[0].SupplyManifest!.AwaitingPhysical, Is.True);
                Assert.That(manifests[0].SupplyManifest!.Items.Select(i => i.Entity), Is.EquivalentTo(purchase.Goods));
                var units = manifests[0].SupplyManifest!.Items.Select(i => i.Unit).ToArray();
                Assert.That(markets.TryCollectSupplyPurchase(generation, request.Operation, carrier, purchase.Goods), Is.True);
                Assert.That(model.Shipments.Count(s => s.SupplyManifest?.Purchase == request.Operation), Is.EqualTo(1));
                var balance = ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance;
                Advance(1230);
                Assert.That(manifests[0].Leg, Is.EqualTo(manifests[0].Route.Length - 1));
                Assert.That(manifests[0].ArrivesAt, Is.Null);
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(balance));
                Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                Assert.That(containers.TryGetContainer(arsenal, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory!.ContainedEntities, Is.Empty);
                Assert.That(Cargo(), Is.EquivalentTo(purchase.Goods));
                server.System<SharedTransformSystem>().SetCoordinates(carrier,
                    server.EntMan.GetComponent<TransformComponent>(arsenal).Coordinates);
                Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), request.Operation,
                    Revision(), purchase.Goods), Is.False, "Teleport plus elapsed scheduler time is not a flight receipt.");
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(manifests[0].SupplyManifest.Items.Select(i => i.Unit), Is.EqualTo(units));
                Assert.That(model.Markets[targetWorld].Goods.ContainsKey("SWGlobalWarSupplyKolto"), Is.False);

                EntityUid Store(string world, bool isArsenal)
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                        out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, isArsenal), Is.True);
                    return store;
                }
                long Revision()
                {
                    Assert.That(ledger.TryGetLedger(out var state), Is.True);
                    return state.Comp.Revision;
                }
                void Advance(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
            });
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.FTLStartupTime, .1f);
                server.CfgMan.SetCVar(CCVars.FTLArrivalTime, .2f);
                server.CfgMan.SetCVar(CCVars.FTLCooldown, .1f);
                var origin = Orbit(sourceWorld);
                Orbit("SWSpawnTestSupplyTransportTransit");
                Orbit("SWSpawnTestSupplyTransportHome");
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(server.Transform(origin).MapID);
                ship = grid.Owner;
                var maps = server.System<SharedMapSystem>();
                for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                    maps.SetTile(grid, new Vector2i(x, y), new Tile(1));
                server.System<SharedTransformSystem>().SetLocalPosition(ship, new Vector2(1000, 1000));
                console = server.EntMan.SpawnEntity("SWEFreightTestConsole", new EntityCoordinates(ship, .5f, .5f));
                pilot = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(ship, .5f, 1.5f));
                var session = server.PlayerMan.Sessions.Single();
                server.PlayerMan.SetAttachedEntity(session, pilot);
                var mind = server.System<SharedMindSystem>().CreateMind(session.UserId);
                server.System<SharedMindSystem>().TransferTo(mind, pilot);
                Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic,
                    generation), Is.True);
                Assert.That(server.System<SWGlobalWarShipOwnershipSystem>().RegisterShip(ship, generation,
                    SWGlobalWarFaction.Republic), Is.True);
                // Helm access requires a current office; appointing one is covered by the personnel tests.
                Assert.That(server.System<SWGlobalWarAssignmentsSystem>().TryGetRegistry(out var registry), Is.True);
#pragma warning disable RA0002
                registry.Comp.Assignments[session.UserId] = new SWGlobalWarAssignment(Guid.NewGuid(), generation,
                    session.UserId, mind, pilot, SWGlobalWarFaction.Republic, SWGlobalWarRole.Pilot, "Passenger", 1, false);
#pragma warning restore RA0002
                // Loading onto orbit and final surface unloading are fixture moves; both interplanetary legs use real FTL.
                server.System<SharedTransformSystem>().SetCoordinates(carrier, new EntityCoordinates(ship, 1.5f, 1.5f));

                EntityUid Orbit(string world)
                {
                    Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                    Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Orbit,
                        out var map, out _), Is.True);
                    if (!planets.TryGetOrbitStation(planet.Value, out _))
                    {
                        var stationGrid = server.ResolveDependency<IMapManager>().CreateGridEntity(server.Transform(map!.Value).MapID);
                        server.System<SharedMapSystem>().SetTile(stationGrid, Vector2i.Zero, new Tile(1));
                        var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                        server.EntMan.AddComponent<StationDataComponent>(station);
                        server.System<StationSystem>().AddGridToStation(station, stationGrid);
#pragma warning disable RA0002
                        server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet.Value).StationGrids.Add(stationGrid);
#pragma warning restore RA0002
                    }
                    var destination = server.EntMan.EnsureComponent<FTLDestinationComponent>(map!.Value);
                    destination.Enabled = true;
                    destination.RequireCoordinateDisk = false;
                    return map.Value;
                }
            });
            await pair.RunSeconds(2);
            foreach (var target in new[] { "SWSpawnTestSupplyTransportTransit", "SWSpawnTestSupplyTransportHome" })
            {
                await server.WaitAssertion(() => Assert.That(server.System<SWGlobalWarTravelSystem>()
                    .TryStartTravel(console, pilot, target, out var reason), Is.True, reason));
                await pair.RunSeconds(8);
                await server.WaitAssertion(() =>
                {
                    Assert.That(planets.TryGetPhysicalLocation(ship, out var planet, out _), Is.True);
                    Assert.That(server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet).Prototype.Id, Is.EqualTo(target));
                    Assert.That(purchase.Obligation.Accepted, Is.Zero, "Arrival alone never pays for cargo.");
                });
            }
            await server.WaitAssertion(() =>
            {
                server.System<SharedTransformSystem>().SetCoordinates(carrier,
                    server.EntMan.GetComponent<TransformComponent>(arsenal).Coordinates);
                if (redirect)
                {
                    var coordinates = server.EntMan.GetComponent<TransformComponent>(arsenal).Coordinates;
                    var replacement = server.EntMan.SpawnEntity(null,
                        new EntityCoordinates(coordinates.EntityId, coordinates.Position + new Vector2(8, 0)));
                    Assert.That(markets.TryRegisterSupplyStore(generation, replacement, true), Is.True);
                    Assert.That(markets.TryApproveSupplyArsenal(generation, purchase.Request.Operation,
                        SWGlobalWarFaction.Republic, replacement, 4, purchase.ArsenalRevision, out _), Is.True);
                    var shipment = markets.Simulation!.Shipments.Single(s => s.SupplyManifest?.Purchase == purchase.Request.Operation);
                    var request = new SWGlobalWarMarketReturnRequest(generation, Guid.NewGuid(), shipment.Id,
                        SWGlobalWarFaction.Republic, "SWSpawnTestSupplyTransportHome", shipment.Quantity, replacement);
                    Assert.That(markets.Simulation.TryReturnShipment(request, out _), Is.True);
                    Assert.That(markets.Simulation.TryReturnShipment(request, out _), Is.True);
                    Assert.That(ledger.TryGetLedger(out var beforeMove), Is.True);
                    Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), purchase.Request.Operation,
                        beforeMove.Comp.Revision, purchase.Goods), Is.False, "The old receiving point is no longer authorized.");
                    server.System<SharedTransformSystem>().SetCoordinates(carrier,
                        server.EntMan.GetComponent<TransformComponent>(replacement).Coordinates);
                    arsenal = replacement;
                }
                if (server.EntMan.HasComponent<EntityStorageComponent>(carrier))
                {
                    // An opened crate has spilled the cargo: it is on the floor, not presented, until it is shut again.
                    Assert.That(ledger.TryGetLedger(out var spilled), Is.True);
                    var crates = server.System<SharedEntityStorageSystem>();
                    crates.OpenStorage(carrier);
                    Assert.That(markets.TryAcceptSupplyPurchase(generation, Guid.NewGuid(), purchase.Request.Operation,
                        spilled.Comp.Revision, purchase.Goods), Is.False);
                    crates.CloseStorage(carrier);
                    Assert.That(Cargo(), Is.EquivalentTo(purchase.Goods));
                }
                Assert.That(ledger.TryGetLedger(out var state), Is.True);
                var revision = state.Comp.Revision;
                var operation = Guid.NewGuid();
                var before = ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Balance;
                Assert.That(markets.TryAcceptSupplyPurchase(generation, operation, purchase.Request.Operation,
                    revision, purchase.Goods), Is.True);
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(4));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Balance,
                    Is.EqualTo(before - 600));
                Assert.That(markets.TryAcceptSupplyPurchase(generation, operation, purchase.Request.Operation,
                    revision, purchase.Goods), Is.True);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Balance,
                    Is.EqualTo(before - 600));
                Assert.That(markets.Simulation!.Shipments.Any(s => s.SupplyManifest?.Purchase == purchase.Request.Operation), Is.False);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.EntMan.DeleteEntity(rule);
                timing.TimeBase = savedTime;
            });
        }
        await pair.CleanReturnAsync();

        IEnumerable<EntityUid> Cargo()
        {
            return server.EntMan.TryGetComponent<EntityStorageComponent>(carrier, out var crate)
                ? crate.Contents.ContainedEntities
                : server.EntMan.GetComponent<StorageComponent>(carrier).Container.ContainedEntities;
        }
    }
}
