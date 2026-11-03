using System;
using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyAutomaticStopsBeforeNextRecipeWhenReadyPurchaseCallbackEndsCampaign(bool reset)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var markets = server.System<SWGlobalWarMarketSystem>();
            var ledger = server.System<SWGlobalWarLedgerSystem>();
            var clock = server.System<SWGlobalWarCampaignSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var probe = server.System<SWPoliticsNotificationProbeSystem>();
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string world = "SWSpawnTestSupplySource";
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(markets.TryReserveSupplyProduction(new(generation, Guid.NewGuid(), factory, faction, kind, 1),
                    out var batch), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, batch.Request.Operation), Is.True);
                Advance(451);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                Assert.That(batch.MarketQuantity, Is.EqualTo(3));
                Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Debit, faction, SWGlobalWarAccount.War,
                    ledger.GetBalance(faction, SWGlobalWarAccount.War).Available - 450, Purpose: "test-ready-only-budget"),
                    out _), Is.True);
                Advance(1199);
                Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.True);
                var receipts = model.ExternalReservationCount;
                var callbacks = 0;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    probe.LedgerChanged = null;
                    Assert.That(model.SupplyPrograms[faction].Orders.Values.Single().Quantity, Is.EqualTo(3));
                    Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty,
                        "This purchase uses only ready goods; LineBusy must not mask the lifecycle boundary.");
                    if (reset)
                        ticker.EndGameRule(campaign.Owner);
                    else
                        Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                };
                Advance(1200);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(model.ExternalReservationCount, Is.EqualTo(receipts));
                Assert.That(model.ExternalPendingReservationCount, Is.Zero);
                Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Stock, Is.EqualTo(Q(90)));
                Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty);
                if (reset)
                    Assert.That(markets.Simulation, Is.Null);
                else
                    Assert.That(campaign.Comp.Phase, Is.EqualTo(SWGlobalWarCampaignPhase.Ending));

                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
            }
            finally
            {
                probe.LedgerChanged = null;
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyAutomaticShortageRetriesDoNotMultiplyReserves(bool moneyShortage)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var markets = server.System<SWGlobalWarMarketSystem>();
            var ledger = server.System<SWGlobalWarLedgerSystem>();
            var clock = server.System<SWGlobalWarCampaignSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string world = "SWSpawnTestSupplySource";
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                var otherReservation = Guid.NewGuid();
                if (moneyShortage)
                {
                    Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                    Assert.That(ledger.TryExecuteSystem(new(generation, otherReservation, accounts.Comp.Revision,
                        SWGlobalWarLedgerAction.Reserve, faction, SWGlobalWarAccount.War,
                        ledger.GetBalance(faction, SWGlobalWarAccount.War).Available, Purpose: "test-other-war-order"), out _),
                        Is.True);
                }
                else
                    Assert.That(model.TryReserveStock(new(generation, otherReservation, world, "SWGlobalWarS", Q(100),
                        faction, "test-other-input-consumer"), out _), Is.True);
                Advance(1200);
                Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.True);
                Advance(1201);
                var intent = markets.SupplyIntents.Single(i => i.Faction == faction && i.Kind == kind);
                Assert.That(intent.Failure, Is.EqualTo(moneyShortage ? SWGlobalWarSupplyFailure.Funds :
                    SWGlobalWarSupplyFailure.Materials));
                Assert.That(intent.Orders, Is.Empty);
                var receipts = model.ExternalReservationCount;
                Advance(1250);
                Assert.That(model.ExternalReservationCount, Is.EqualTo(receipts),
                    "Repeated shortages must not leave a fresh cancelled raw reservation on each second.");
                Assert.That(intent.Orders, Is.Empty);
                Assert.That(model.SupplyPrograms[faction].RemainingQuota(kind), Is.EqualTo(8));
                if (moneyShortage)
                {
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Available, Is.Zero);
                    Assert.That(markets.GetSupplyProductionWork(generation, factory).Single().Quantity, Is.EqualTo(5),
                        "The base program still reserves real materials independently of procurement money.");
                }
                else
                {
                    Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty);
                    Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Available, Is.EqualTo(Q(100)));
                    Assert.That(model.TryCancelStock(generation, otherReservation), Is.True);
                    Advance(1251);
                    Assert.That(intent.Orders.Count, Is.EqualTo(1));
                    Assert.That(model.SupplyPrograms[faction].Orders[intent.Orders.Single()].Quantity, Is.EqualTo(8));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
                    Assert.That(markets.GetSupplyProductionWork(generation, factory).Single().Quantity, Is.EqualTo(15));
                }

                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
            }
            finally
            {
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task SupplyAutomaticProgramsReserveActualInputsAndKeepOneWindowIntent(bool localOnly, bool switchToBacta)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var ticker = server.System<GameTicker>();
            var markets = server.System<SWGlobalWarMarketSystem>();
            var ledger = server.System<SWGlobalWarLedgerSystem>();
            var clock = server.System<SWGlobalWarCampaignSystem>();
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var containers = server.System<SharedContainerSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var savedTime = timing.TimeBase;
            EntityUid? rule = null;
            try
            {
                Assert.That(ticker.StartGameRule("SWSpawnTestSupplyRule"), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                rule = campaign.Owner;
                var generation = campaign.Comp.Generation;
                var model = markets.Simulation!;
                var participants = localOnly ? 1 : 20;
                model.SupplyParticipants = _ => participants;
                const SWGlobalWarFaction faction = SWGlobalWarFaction.Republic;
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                const string world = "SWSpawnTestSupplySource";
                var program = model.SupplyPrograms[faction];
                if (localOnly)
                    program.Lines[kind].LocalPercent = 100;
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var source), Is.True);
                Advance(1);
                Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty);
                Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.True);
                Advance(2);
                var first = markets.GetSupplyProductionWork(generation, factory).Single();
                Assert.That(first.Quantity, Is.EqualTo(5));
                Assert.That(first.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Reserved));
                Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Available, Is.EqualTo(Q(90)));
                Assert.That(model.Markets[world].Goods["SWGlobalWarF"].Stock, Is.EqualTo(Q(100)));
                Assert.That(source!.ContainedEntities, Is.Empty, "The program does not manufacture free output.");
                Assert.That(program.Orders, Is.Empty);
                Assert.That(markets.SupplyIntents, Is.Empty, "There is no procurement window at round start.");
                Assert.That(markets.TryBeginSupplyProduction(generation, first.Request.Operation), Is.True);
                Advance(453);
                Complete(first);
                Advance(454);

                SWGlobalWarSupplyProduction? second = null;
                if (localOnly)
                    Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty,
                        "The complete local batch covers the total target of two.");
                else
                {
                    second = markets.GetSupplyProductionWork(generation, factory).Single();
                    Assert.That(markets.TryBeginSupplyProduction(generation, second.Request.Operation), Is.True);
                }
                Advance(1199);
                Assert.That(program.Orders, Is.Empty);
                if (switchToBacta)
                    program.Lines[kind].Bacta = true;
                Advance(1200);
                var intent = markets.SupplyIntents.Single(i => i.Faction == faction && i.Kind == kind);
                Assert.That(intent.Window, Is.EqualTo(1));
                Assert.That(intent.Budget, Is.EqualTo(localOnly ? 1 : 8));
                if (localOnly)
                {
                    Assert.That(program.Orders, Is.Empty);
                    Assert.That(intent.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.ExistingSupply));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                    Assert.That(source.ContainedEntities.Count, Is.EqualTo(5));
                    Assert.That(markets.GetSupplyProductionWork(generation, factory), Is.Empty);
                }
                else
                {
                    Assert.That(intent.Orders.Count, Is.EqualTo(1));
                    Assert.That(program.Orders[intent.Orders[0]].Quantity, Is.EqualTo(3),
                        "The existing market allocation can be reserved while the second batch is busy.");
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(450));
                    var reservations = model.ExternalReservationCount;
                    Advance(1201);
                    Assert.That(intent.Orders.Count, Is.EqualTo(1));
                    Assert.That(model.ExternalReservationCount, Is.EqualTo(reservations));
                    Complete(second!);
                    Advance(1202);
                    Assert.That(intent.Orders.Count, Is.EqualTo(2));
                    var ordered = switchToBacta ? 7 : 8;
                    Assert.That(intent.Orders.Sum(id => program.Orders[id].Outstanding), Is.EqualTo(ordered));
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(ordered * 150),
                        "A factory switching to bacta cannot change the automatic kolto composition or price.");
                    Assert.That(program.RemainingQuota(kind), Is.EqualTo(8 - ordered));
                    participants = 1;
                    Advance(1250);
                    Assert.That(program.Windows[1].Budgets[kind], Is.EqualTo(8));
                    Assert.That(intent.Orders.Count, Is.EqualTo(2));
                    Assert.That(markets.TryCancelSupplyPurchase(generation, intent.Orders[0]), Is.True);
                    Assert.That(intent.Suspended, Is.True);
                    Advance(1251);
                    Assert.That(intent.Orders.Count, Is.EqualTo(2));
                }
                Advance(2400);
                Assert.That(markets.SupplyIntents.Count(i => i.Faction == faction && i.Kind == kind && i.Window == 1),
                    Is.EqualTo(1));
                if (!localOnly)
                {
                    Assert.That(program.Windows[2].Budgets[kind], Is.Zero,
                        "The five outstanding old-window units cover the lower current target.");
                    Assert.That(ledger.GetBalance(faction, SWGlobalWarAccount.War).Reserved,
                        Is.EqualTo(switchToBacta ? 600 : 750));
                }
                Assert.That(model.AcceptedSupplyStock!(faction, kind), Is.Zero);
                ticker.EndGameRule(rule.Value);
                rule = null;
                Assert.That(markets.TryRegisterSupplyConsumer(generation, factory, kind), Is.False);
                Assert.That(markets.TryBeginSupplyProduction(generation, first.Request.Operation), Is.False);

                void Complete(SWGlobalWarSupplyProduction batch)
                {
                    var goods = Enumerable.Range(0, batch.Quantity).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                        markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                    Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                }
                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
            }
            finally
            {
                if (rule is { } entity)
                    ticker.EndGameRule(entity);
                timing.TimeBase = savedTime;
            }
        });
        await pair.CleanReturnAsync();
    }
}
