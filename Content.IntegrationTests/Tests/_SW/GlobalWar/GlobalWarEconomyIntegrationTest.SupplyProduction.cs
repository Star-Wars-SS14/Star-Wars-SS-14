using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string SupplyProductionPrototypes = """
- type: swGlobalWarEconomyProfile
  id: SWSpawnTestSupplyInputs
  mandatory: { SWGlobalWarF: 0, SWGlobalWarS: 0, SWGlobalWarE: 0 }
  openingStock: { SWGlobalWarF: 100, SWGlobalWarS: 100, SWGlobalWarE: 100 }
  hubCapacity: 60
  maximumHubCapacity: 60

- type: swGlobalWarPlanet
  id: SWSpawnTestSupplySource
  name: Supply source
  kind: Industry
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs
  enclave: true

- type: entity
  parent: SWGlobalWarCampaign
  id: SWSpawnTestSupplyRule
  components:
  - type: SWGlobalWarCampaign
    participantAbsenceDuration: 3600
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestLnRHome, SWSpawnTestSupplySource, SWSpawnTestLnEHome]
""";

    [Test]
    public async Task SupplyProductionConsumesRealInputsOnceAndRecoversDeletedStoreCancellation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var containers = server.System<SharedContainerSystem>();
        var stacks = server.System<SharedStackSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        using var scenario = RunScenario().GetEnumerator();
        var more = true;
        while (more)
        {
            await server.WaitAssertion(() => more = scenario.MoveNext());
            if (more)
                await pair.RunTicksSync(1);
        }
        await pair.CleanReturnAsync();

        IEnumerable<bool> RunScenario()
        {
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const string world = "SWSpawnTestSupplySource";
                const string containerId = "sw-program-supply";
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var store = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, store), Is.True);
                Assert.That(containers.TryGetContainer(store, containerId, out var stock), Is.True);
                var food = model.Markets[world].Goods["SWGlobalWarF"];
                var steel = model.Markets[world].Goods["SWGlobalWarS"];
                var electronics = model.Markets[world].Goods["SWGlobalWarE"];
                var request = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), store,
                    faction, SWGlobalWarSupplyKind.Medicine, 1);
                Assert.That(markets.TryReserveSupplyProduction(request, out var medicine), Is.True);
                Assert.That(medicine.Quantity, Is.EqualTo(5));
                Assert.That(medicine.Duration, Is.EqualTo(TimeSpan.FromSeconds(450)));
                Assert.That(food.Stock, Is.EqualTo(Q(100)));
                Assert.That(model.ExternalPendingReservationCount, Is.EqualTo(2));
                Assert.That(markets.TryReserveSupplyProduction(request, out var replay), Is.True);
                Assert.That(replay, Is.SameAs(medicine));
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(food.Stock, Is.EqualTo(Q(90)));
                Assert.That(steel.Stock, Is.EqualTo(Q(99)));
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(food.Stock, Is.EqualTo(Q(90)));
                Assert.That(model.ExternalPendingReservationCount, Is.Zero);
                var pens = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, pens), Is.False);
                yield return true;
                Advance(451);
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, pens[..4]), Is.False);
                var solutions = server.System<SharedSolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(pens[0], "pen", out var pen), Is.True);
                solutions.RemoveAllSolution(pen!.Value);
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, pens), Is.False);
                Assert.That(solutions.TryAddReagent(pen.Value, "SWKolto", 15), Is.True);
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, pens), Is.True);
                Assert.That(stock!.ContainedEntities, Is.EquivalentTo(pens));
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, pens), Is.True);
                var secondPens = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, secondPens), Is.False);
                Assert.That(stock.ContainedEntities, Has.Count.EqualTo(5));
                Assert.That(food.Stock, Is.EqualTo(Q(90)));
                Assert.That(model.AcceptedSupplyStock!(faction, SWGlobalWarSupplyKind.Medicine), Is.Zero,
                    "Factory production does not claim army acceptance or payment.");

                var wire = server.EntMan.SpawnEntity("SWAdvancedCables10", position);
                Assert.That(containers.Insert(wire, stock), Is.True);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(10));
                request = new(generation, Guid.NewGuid(), store, faction, SWGlobalWarSupplyKind.Magazine, 1);
                Assert.That(markets.TryReserveSupplyProduction(request, out var magazine), Is.True);
                Assert.That(stock.Contains(wire), Is.False);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(10));
                Assert.That(markets.TryReserveSupplyProduction(request with { Operation = Guid.NewGuid() }, out _), Is.False);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(stock.Contains(wire), Is.True);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(10));
                Assert.That(model.ExternalPendingReservationCount, Is.Zero);

                request = request with { Operation = Guid.NewGuid() };
                Assert.That(markets.TryReserveSupplyProduction(request, out magazine), Is.True);
                server.EntMan.DeleteEntity(store);
                Assert.That(server.EntMan.EntityExists(wire), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.False);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.False);
                Assert.That(magazine.ReturnPending, Is.True);
                Assert.That(magazine.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Reserved));
                Assert.That(stacks.GetCount(wire), Is.EqualTo(10));
                Assert.That(model.ExternalPendingReservationCount, Is.EqualTo(1));
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.False);
                var elsewhere = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Value, new Vector2(3, 0)));
                Assert.That(markets.TryRegisterSupplyStore(generation, elsewhere), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation, elsewhere), Is.False,
                    "A return does not teleport the reserved stack to a different location.");
                store = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, store), Is.True);
                Assert.That(containers.TryGetContainer(store, containerId, out stock), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation, store), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation, store), Is.True);
                Assert.That(magazine.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Cancelled));
                Assert.That(magazine.ReturnPending, Is.False);
                Assert.That(stock!.ContainedEntities, Is.EqualTo(new[] { wire }));
                Assert.That(stacks.GetCount(wire), Is.EqualTo(10));
                Assert.That(model.ExternalPendingReservationCount, Is.Zero);

                request = request with { Operation = Guid.NewGuid(), Store = store };
                Assert.That(markets.TryReserveSupplyProduction(request, out magazine), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(9));
                Assert.That(stock.Contains(wire), Is.True);
                Assert.That(steel.Stock, Is.EqualTo(Q(98)));
                var result = server.EntMan.SpawnEntity("SWH206Magazine",
                    markets.SupplyProductionOutputCoordinates(request.Operation));
                yield return true;
                Advance(512);
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, [result]), Is.False,
                    "Factory output cannot inherit free charge from its prototype.");
                server.System<SharedBatterySystem>().SetCharge(result, 0);
                Assert.That(markets.TryCompleteSupplyProduction(generation, request.Operation, [result]), Is.True);

                var alloy = server.EntMan.SpawnEntity("SWSheetLantanidAlloy", position);
                Assert.That(containers.Insert(alloy, stock), Is.True);
                var alloyCount = stacks.GetCount(alloy);
                Assert.That(alloyCount, Is.GreaterThan(1));
                request = new(generation, Guid.NewGuid(), store, faction, SWGlobalWarSupplyKind.AdvancedElectronics, 1);
                Assert.That(markets.TryReserveSupplyProduction(request, out var advanced), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(7));
                Assert.That(stacks.GetCount(alloy), Is.EqualTo(alloyCount - 1));
                Assert.That(stock.Contains(wire) && stock.Contains(alloy), Is.True);
                Assert.That(electronics.Stock, Is.EqualTo(Q(99)));
                Assert.That(markets.TryBeginSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(stacks.GetCount(wire), Is.EqualTo(7));
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(markets.TryCancelSupplyProduction(generation, request.Operation), Is.True);
                Assert.That(stacks.GetCount(alloy), Is.EqualTo(alloyCount - 1),
                    "Cancelling WIP must not recreate consumed physical materials.");
                Assert.That(electronics.Stock, Is.EqualTo(Q(99)));

                void Advance(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
                long Q(int units) => SWGlobalWarMarketUnits.FromUnits(units);
            }
            finally
            {
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        }
    }
}
