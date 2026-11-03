using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Database;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string HubPrototypes = """
- type: entity
  parent: SWGlobalWarOrbitalHub
  id: SWSpawnTestOrbitalHub
  components:
  - type: SWGlobalWarHub
  - type: SWGlobalWarMapAnchor
    anchorId: logisticsHub
    kind: Logistics
  - type: ApcPowerReceiver
    needsPower: false
""";

    [Test]
    public async Task OrbitalHubWorkCommitsOnceWithItsReceiptAndCancelsWithoutSpending()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        await server.AddDummySessions(3);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var sessions = server.PlayerMan.Sessions.ToArray();
        string[] jobs = ["SWPoliticsTestHead", "SWPoliticsTestLogistics", "SWPoliticsTestWorker", "SWPoliticsTestWorker"];
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            ProtoId<JobPrototype> job = jobs[i];
            await pair.SetJobPriorities(sessions[i].UserId,
                profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        }
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestEconomyEditorPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        // Deliver round-start network variables before advancing the deterministic server clock.
        await pair.RunTicksSync(30);
        using var scenario = RunScenario().GetEnumerator();
        var more = true;
        while (more)
        {
            await server.WaitAssertion(() => more = scenario.MoveNext());
            if (more)
                await pair.RunTicksSync(1);
        }
        // Disconnect unloads preferences. Reload only that fixture cache for the pool's restoration;
        // a dummy has no authenticated database player row for GameTicker's real reconnect path.
        await server.WaitPost(() => server.ResolveDependency<UserDbDataManager>().ClientConnected(sessions[3]));
        loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.ResolveDependency<UserDbDataManager>().IsLoadComplete(sessions[3]) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(sessions[3].UserId, out _));
        }
        Assert.That(loaded, Is.True);

        IEnumerable<bool> RunScenario()
        {
            var savedTime = timing.TimeBase;
            try
            {
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(4));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var scenarioOrigin = clock.Elapsed(campaign);
                Assert.That(registry.TryGetAssignment(sessions[0].UserId, out var head), Is.True);
                Assert.That(registry.TryGetAssignment(sessions[1].UserId, out var minister), Is.True);
                Assert.That(registry.TryGetAssignment(sessions[2].UserId, out var worker), Is.True);
                var generation = campaign.Comp.Generation;
                const string planetId = "SWSpawnTestEconomyViewRFarm";
                Assert.That(planets.TryGetPlanet(planetId, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
                Assert.That(planets.TryGetOrbitStation(planet.Value, out var grid), Is.True);
                var gridData = server.EntMan.GetComponent<MapGridComponent>(grid!.Value);
                server.System<SharedPhysicsSystem>().SetBodyType(grid.Value, BodyType.Static);
                var tiles = new List<(Vector2i, Tile)>();
                for (var x = -2; x <= 3; x++)
                for (var y = -2; y <= 3; y++)
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                maps.SetTiles(grid.Value, gridData, tiles);
                maps.SetPaused(map!.Value, false);
                foreach (var session in sessions)
                {
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                    transform.SetCoordinates(session.AttachedEntity.Value, new EntityCoordinates(grid.Value, .5f, 1.5f));
                }
                var hub = server.EntMan.SpawnEntity("SWSpawnTestOrbitalHub", new EntityCoordinates(grid.Value, .5f, .5f));
                server.System<PowerNetSystem>().Update(0);
                Assert.That(planets.TryGetPlanet("SWSpawnTestEconomyViewR", out var capital), Is.True);
                Assert.That(planets.TryEnsureLocation(capital!.Value, SWGlobalWarPlanetLocation.Orbit, out var capitalMap, out _), Is.True);
                Assert.That(planets.TryGetOrbitStation(capital.Value, out var capitalGrid), Is.True);
                server.System<SharedPhysicsSystem>().SetBodyType(capitalGrid!.Value, BodyType.Static);
                maps.SetTiles(capitalGrid.Value, server.EntMan.GetComponent<MapGridComponent>(capitalGrid.Value), tiles);
                maps.SetPaused(capitalMap!.Value, false);
                var capitalPosition = new EntityCoordinates(capitalGrid.Value, .5f, .5f);
                var capitalHub = server.EntMan.SpawnEntity("SWSpawnTestOrbitalHub", capitalPosition);
                server.System<PowerNetSystem>().Update(0);

                Assert.That(server.EntMan.GetComponent<SWGlobalWarHubComponent>(hub).Binding, Is.Not.Null);
                Assert.That(server.System<PowerReceiverSystem>().IsPowered(hub), Is.True, "Prepared fixture hub must be powered.");
                var market = markets.Simulation!.Markets[planetId];
                var faction = SWGlobalWarFaction.Republic;
                var account = SWGlobalWarAccount.Logistics;
                SWGlobalWarLedgerCommand Money(SWGlobalWarLedgerAction action, long amount, Guid parent = default) =>
                    new(generation, Guid.NewGuid(), 0, action, faction, account, amount,
                        Reservation: parent, Purpose: "hub-integration-fixture");
                // Fixture funding is explicit; production keeps its original 150000 starting budget.
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Credit, 3000000), out _), Is.True);
                Assert.That(ledger.TryGetLedger(out var initialLedger), Is.True);
                var limitedReserve = Money(SWGlobalWarLedgerAction.Reserve, 600000) with
                {
                    ObservedRevision = initialLedger.Comp.Revision, Employee = worker.User,
                };
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, limitedReserve, out var limitedOrder), Is.True);
                var limitCommand = new SWGlobalWarHubGrantCommand(generation, Guid.NewGuid(), minister.Revision,
                    worker.User, worker.Revision, limitedOrder, 60, TimeSpan.FromMinutes(10));
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, limitCommand, out var limitGrant), Is.True);
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, limitCommand with
                {
                    Request = Guid.NewGuid(), MaximumCapacity = 100,
                }, out var unrelatedGrant), Is.True);
                var start = new SWGlobalWarHubCommand(generation, Guid.NewGuid(), minister.Revision, 25);
                Assert.That(markets.TryStartHubUpgrade(sessions[0], hub, start with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = head.Revision,
                }, out _, out _), Is.False, "An occupied ministry requires the head's explicit decision.");
                Assert.That(markets.TryStartHubUpgrade(sessions[1], hub, start, out var begun, out var failure), Is.True, failure.ToString());
                Assert.That(begun!.Started, Is.EqualTo(scenarioOrigin));
                Assert.That(begun.Direct, Is.True);
                Assert.That(begun.MoneyOrder, Is.Not.EqualTo(Guid.Empty));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(3150000, 900000)));
                Assert.That(markets.TryStartHubUpgrade(sessions[1], hub, start, out var repeated, out _), Is.True);
                Assert.That(repeated, Is.EqualTo(begun));
                Assert.That(markets.TryStartHubUpgrade(sessions[0], hub, start with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = head.Revision, HeadDecision = true,
                }, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(SWGlobalWarHubFailure.Busy));
                yield return true;
                Advance(119);
                Assert.That(market.HubCapacity, Is.EqualTo(25));
                var oldSendBudget = market.SendBudget;
                var oldReceiveBudget = market.ReceiveBudget;
                var oldMarketTime = markets.Simulation.Time;
                timing.TimeBase = (timing.TimeBase.Item1 + scenarioOrigin + TimeSpan.FromSeconds(120) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                Assert.That(markets.TryRevokeHubWork(sessions[1], generation, minister.Revision, unrelatedGrant, false), Is.True);
                Assert.That(markets.Simulation.Time, Is.EqualTo(oldMarketTime));
                Assert.That(market.HubCapacity, Is.EqualTo(25), "Revoking B cannot finish due A before the market catches up.");
                Assert.That(market.SendBudget, Is.EqualTo(oldSendBudget));
                Assert.That(market.ReceiveBudget, Is.EqualTo(oldReceiveBudget));
                var observedCommit = false;
                probe.LedgerChanged = () =>
                {
                    if (ledger.GetBalance(faction, account).Balance != 2850000)
                        return;
                    observedCommit = true;
                    Assert.That(market.HubCapacity, Is.EqualTo(50));
                    Assert.That(markets.GetHubWorkResult(sessions[1], hub, generation, minister.Revision, start.Request)!.Status,
                        Is.EqualTo(SWGlobalWarHubStatus.Completed));
                    Assert.That(markets.TryStartHubUpgrade(sessions[1], hub, start, out _, out _), Is.False,
                        "A ledger callback cannot reenter the economy transaction.");
                };
                yield return true;
                Advance(120);
                probe.LedgerChanged = null;
                Assert.That(observedCommit, Is.True);
                Assert.That(market.HubCapacity, Is.EqualTo(50));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(2850000, 600000)));
                Assert.That(market.ReceiveBudget, Is.LessThanOrEqualTo(oldReceiveBudget + 25d / 60 + 0.000001),
                    "The elapsed second receives only the old capacity, and completion adds no immediate tokens.");
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, new(generation, Guid.NewGuid(), worker.Revision,
                    50, limitedOrder, limitGrant), out _, out _), Is.False,
                    "An active max60 grant issued at25 cannot buy50→75 even with600000 available.");
                Assert.That(ledger.TryGetReservation(limitedOrder, out var limitedFunds), Is.True);
                Assert.That(limitedFunds.Remaining, Is.EqualTo(600000));
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release, 0, limitedOrder), out _), Is.True);
                Assert.That(markets.TryStartHubUpgrade(sessions[1], hub, start, out repeated, out _), Is.True);
                Assert.That(repeated!.Status, Is.EqualTo(SWGlobalWarHubStatus.Completed));
                Assert.That(markets.TryCancelHubUpgrade(sessions[1], hub, generation, minister.Revision, start.Request, out var cancelled), Is.True);
                Assert.That(cancelled, Is.EqualTo(repeated));
                Assert.That(market.HubCapacity, Is.EqualTo(50));

                var leave = start with { Request = Guid.NewGuid(), ExpectedCapacity = 50 };
                Assert.That(markets.TryStartHubUpgrade(sessions[1], hub, leave, out _, out failure), Is.True, failure.ToString());
                transform.SetCoordinates(minister.Body, new EntityCoordinates(grid.Value, .5f, 2.5f));
                yield return true;
                Advance(121);
                Assert.That(markets.GetHubWorkResult(sessions[1], hub, generation, minister.Revision, leave.Request)!.Status,
                    Is.EqualTo(SWGlobalWarHubStatus.Working), "Movement inside the work radius must not cancel.");
                transform.SetCoordinates(minister.Body, new EntityCoordinates(grid.Value, .5f, 3.5f));
                yield return true;
                Advance(122);
                cancelled = markets.GetHubWorkResult(sessions[1], hub, generation, minister.Revision, leave.Request);
                Assert.That(cancelled!.Failure, Is.EqualTo(SWGlobalWarHubFailure.OutOfRange));
                Assert.That(cancelled.FundingReleased, Is.True);
                Assert.That(market.HubCapacity, Is.EqualTo(50));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(2850000, 0)));
                transform.SetCoordinates(minister.Body, new EntityCoordinates(grid.Value, .5f, 1.5f));

                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var reserve = Money(SWGlobalWarLedgerAction.Reserve, 1800000) with
                {
                    ObservedRevision = accounts.Comp.Revision, Employee = worker.User,
                };
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, reserve, out var order), Is.True);
                var grantCommand = new SWGlobalWarHubGrantCommand(generation, Guid.NewGuid(), minister.Revision,
                    worker.User, worker.Revision, order, 60, TimeSpan.FromMinutes(10));
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand, out _), Is.False,
                    "At capacity50 a grant capped at60 cannot authorize the next actual step75.");
                grantCommand = grantCommand with { Request = Guid.NewGuid(), MaximumCapacity = 75 };
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand, out var grant), Is.True);
                var work = new SWGlobalWarHubCommand(generation, Guid.NewGuid(), worker.Revision, 50, order, grant);
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work with { WorkGrant = order }, out _, out _), Is.False,
                    "A money order does not confer work authority.");
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out _, out failure), Is.True, failure.ToString());
                var ownView = markets.GetHubView(sessions[2], hub);
                Assert.That(ownView.Active!.MoneyOrder, Is.EqualTo(order));
                Assert.That(ownView.Active.Direct, Is.False);
                Assert.That(ownView.Active.Purpose, Is.EqualTo(reserve.Purpose));
                Assert.That(ownView.Grants.Single(g => g.Id == grant).MoneyExpires, Is.Not.Null);
                var otherWorkerView = markets.GetHubView(sessions[3], hub);
                Assert.That(otherWorkerView.Active, Is.Null);
                Assert.That(otherWorkerView.Executor, Is.Not.Empty);
                Assert.That(otherWorkerView.OwnResults, Is.Empty);
                Assert.That(otherWorkerView.Orders, Is.Empty);
                Assert.That(ledger.GetBalance(faction, account).Reserved, Is.EqualTo(1800000), "No second reservation.");
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit, 1200001, order), out _), Is.False);
                yield return true;
                Capture(SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire);
                yield return true;
                Advance(241);
                Assert.That(market.HubCapacity, Is.EqualTo(50));
                yield return true;
                Advance(242);
                Assert.That(market.HubCapacity, Is.EqualTo(75));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(2250000, 1200000)));
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out repeated, out _), Is.True);
                Assert.That(repeated!.Status, Is.EqualTo(SWGlobalWarHubStatus.Completed));
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work with
                {
                    Request = Guid.NewGuid(), ExpectedCapacity = 75,
                }, out _, out _), Is.False, "The next actual capacity100 exceeds the work grant's75 limit.");

                grantCommand = grantCommand with { Request = Guid.NewGuid(), MaximumCapacity = 100 };
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand, out grant), Is.True);
                work = work with { Request = Guid.NewGuid(), WorkGrant = grant, ExpectedCapacity = 75 };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out _, out failure), Is.True, failure.ToString());
                yield return true;
                Capture(SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire);
                var lostOrbit = markets.GetHubView(sessions[2], hub);
                Assert.That(lostOrbit.Ready, Is.False);
                Assert.That(lostOrbit.Capacity, Is.Zero, "Current foreign hub data is not exposed with the own receipt.");
                Assert.That(lostOrbit.Orders, Is.Empty);
                Assert.That(lostOrbit.Grants, Is.Empty);
                Assert.That(lostOrbit.OwnResults.Single(r => r.Request == work.Request).Failure,
                    Is.EqualTo(SWGlobalWarHubFailure.OrbitChanged));
                yield return true;
                Capture(SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Republic);
                cancelled = markets.GetHubWorkResult(sessions[2], hub, generation, worker.Revision, work.Request);
                Assert.That(cancelled!.Failure, Is.EqualTo(SWGlobalWarHubFailure.OrbitChanged));
                Assert.That(cancelled.FundingReleased, Is.True);
                Assert.That(cancelled.FundingState, Is.EqualTo(SWGlobalWarHubFundingState.OrderAvailable));
                Assert.That(market.HubCapacity, Is.EqualTo(75));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(2250000, 1200000)));
                Assert.That(ledger.CanSpendOrder(sessions[2], generation, worker.Revision, order, 1200000, reserve.Purpose), Is.True);
                Assert.That(SWGlobalWarMarketSystem.TryGetHubUpgrade(market, out var nextCapacity, out var nextPrice), Is.True);
                Assert.That((nextCapacity, nextPrice), Is.EqualTo((100, 1200000L)));
                work = work with { Request = Guid.NewGuid() };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out _, out failure), Is.True, failure.ToString());
                Assert.That(markets.TryRevokeHubWork(sessions[1], generation, minister.Revision, grant, false), Is.True);
                Assert.That(markets.GetHubWorkResult(sessions[2], hub, generation, worker.Revision, work.Request)!.Failure,
                    Is.EqualTo(SWGlobalWarHubFailure.AuthorityChanged));
                Assert.That(ledger.CanSpendOrder(sessions[2], generation, worker.Revision, order, 1200000, reserve.Purpose), Is.True);
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand with { Request = Guid.NewGuid() }, out grant), Is.True);
                work = work with { Request = Guid.NewGuid(), WorkGrant = grant };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out _, out failure), Is.True, failure.ToString());
                server.System<PowerReceiverSystem>().SetPowerDisabled(hub, true);
                server.System<PowerNetSystem>().Update(0);
                markets.Update(0);
                Assert.That(markets.GetHubWorkResult(sessions[2], hub, generation, worker.Revision, work.Request)!.Failure,
                    Is.EqualTo(SWGlobalWarHubFailure.Unavailable));
                server.System<PowerReceiverSystem>().SetPowerDisabled(hub, false);
                server.System<PowerNetSystem>().Update(0);
                work = work with { Request = Guid.NewGuid() };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out _, out failure), Is.True, failure.ToString());
                transform.Unanchor(hub);
                markets.Update(0);
                Assert.That(markets.GetHubWorkResult(sessions[2], hub, generation, worker.Revision, work.Request)!.Failure,
                    Is.EqualTo(SWGlobalWarHubFailure.Unavailable));
                Assert.That(transform.AnchorEntity(hub), Is.True);
                work = work with { Request = Guid.NewGuid() };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work, out var race, out failure), Is.True, failure.ToString());
                timing.TimeBase = (timing.TimeBase.Item1 + race!.Finishes - clock.Elapsed(campaign), timing.TimeBase.Item2);
                Assert.That(markets.TryCancelHubUpgrade(sessions[2], hub, generation, worker.Revision, work.Request, out cancelled), Is.True);
                Assert.That(cancelled!.Status, Is.EqualTo(SWGlobalWarHubStatus.Cancelled));
                Assert.That(cancelled.Failure, Is.EqualTo(SWGlobalWarHubFailure.CancelledByUser));
                markets.Update(0);
                Assert.That(market.HubCapacity, Is.EqualTo(75), "An explicit cancel wins before the completion commit, even at its deadline.");
                var shortCommand = grantCommand with { Request = Guid.NewGuid(), Duration = TimeSpan.FromSeconds(30) };
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, shortCommand, out var shortGrant), Is.True);
                var beforeClaims = accounts.Comp.Allocations.Count;
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work with
                {
                    Request = Guid.NewGuid(), WorkGrant = shortGrant,
                }, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(SWGlobalWarHubFailure.InsufficientDuration));
                Assert.That(accounts.Comp.Allocations.Count, Is.EqualTo(beforeClaims));

                Assert.That(ledger.TryGetReservation(order, out var expiringOrder), Is.True);
                var nearExpiry = expiringOrder.Expires!.Value - TimeSpan.FromSeconds(30);
                timing.TimeBase = (timing.TimeBase.Item1 + nearExpiry - clock.Elapsed(campaign), timing.TimeBase.Item2);
                markets.Update(0);
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand with
                {
                    Request = Guid.NewGuid(), Duration = TimeSpan.FromMinutes(10),
                }, out var fundedTooBriefly), Is.True);
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, work with
                {
                    Request = Guid.NewGuid(), WorkGrant = fundedTooBriefly,
                }, out _, out failure), Is.False);
                Assert.That(failure, Is.EqualTo(SWGlobalWarHubFailure.InsufficientDuration));
                Assert.That(accounts.Comp.Allocations.Count, Is.EqualTo(beforeClaims));

                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release, 0, order), out _), Is.True);
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, reserve with
                {
                    Request = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision, Amount = 1200000,
                }, out var revokedOrder), Is.True);
                Assert.That(markets.TryGrantHubWork(sessions[1], hub, grantCommand with
                {
                    Request = Guid.NewGuid(), MoneyOrder = revokedOrder,
                }, out var revokedMoneyGrant), Is.True);
                var moneyWork = work with { Request = Guid.NewGuid(), MoneyOrder = revokedOrder, WorkGrant = revokedMoneyGrant };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], hub, moneyWork, out _, out failure), Is.True, failure.ToString());
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release, 0, revokedOrder), out _), Is.True);
                markets.Update(0);
                var moneyCancelled = markets.GetHubWorkResult(sessions[2], hub, generation, worker.Revision, moneyWork.Request)!;
                Assert.That(moneyCancelled.Failure, Is.EqualTo(SWGlobalWarHubFailure.Funding));
                Assert.That(moneyCancelled.FundingState, Is.EqualTo(SWGlobalWarHubFundingState.ParentClosed));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(2250000, 0)));

                foreach (var session in sessions)
                    transform.SetCoordinates(session.AttachedEntity!.Value, new EntityCoordinates(capitalGrid.Value, .5f, 1.5f));
                var capitalMarket = markets.Simulation.Markets["SWSpawnTestEconomyViewR"];
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, reserve with
                {
                    Request = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision,
                }, out var capitalOrder), Is.True);
                var capitalGrantCommand = new SWGlobalWarHubGrantCommand(generation, Guid.NewGuid(), minister.Revision,
                    worker.User, worker.Revision, capitalOrder, 225, TimeSpan.FromMinutes(10));
                Assert.That(markets.TryGrantHubWork(sessions[1], capitalHub, capitalGrantCommand, out var capitalGrant), Is.True);
                var capitalWork = new SWGlobalWarHubCommand(generation, Guid.NewGuid(), worker.Revision, 150, capitalOrder, capitalGrant);
                Assert.That(markets.TryStartHubUpgrade(sessions[2], capitalHub, capitalWork, out var capitalStarted, out failure), Is.True, failure.ToString());
                timing.TimeBase = (timing.TimeBase.Item1 + capitalStarted!.Finishes - clock.Elapsed(campaign), timing.TimeBase.Item2);
                server.System<SharedDoAfterSystem>().Update(0);
                markets.Update(0);
                Assert.That(capitalMarket.HubCapacity, Is.EqualTo(200));
                Assert.That(ledger.TryGetReservation(capitalOrder, out var capitalFunds), Is.True);
                Assert.That(capitalFunds.Remaining, Is.EqualTo(1200000));
                Assert.That(markets.TryStartHubUpgrade(sessions[2], capitalHub, capitalWork with
                {
                    Request = Guid.NewGuid(), ExpectedCapacity = 200,
                }, out _, out _), Is.False, "The active max225 grant cannot buy200→250 with1200000 available.");
                yield return true;
                server.EntMan.DeleteEntity(capitalHub);
                yield return true;
                capitalHub = server.EntMan.SpawnEntity("SWSpawnTestOrbitalHub", capitalPosition);
                server.System<PowerNetSystem>().Update(0);
                Assert.That(capitalMarket.HubCapacity, Is.EqualTo(200));
                Assert.That(SWGlobalWarMarketSystem.TryGetHubUpgrade(capitalMarket, out nextCapacity, out nextPrice), Is.True);
                Assert.That((nextCapacity, nextPrice), Is.EqualTo((250, 1200000L)), "A rebuilt object retains the next absolute price.");
                capitalGrantCommand = capitalGrantCommand with { Request = Guid.NewGuid(), MaximumCapacity = 250 };
                Assert.That(markets.TryGrantHubWork(sessions[1], capitalHub, capitalGrantCommand, out capitalGrant), Is.True);
                capitalWork = capitalWork with { Request = Guid.NewGuid(), ExpectedCapacity = 200, WorkGrant = capitalGrant };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], capitalHub, capitalWork, out _, out failure), Is.True, failure.ToString());
                server.System<MobStateSystem>().ChangeMobState(worker.Body, MobState.Critical);
                markets.Update(0);
                Assert.That(markets.HubJournal.Last().Result.Status, Is.EqualTo(SWGlobalWarHubStatus.Cancelled));
                Assert.That(capitalMarket.HubCapacity, Is.EqualTo(200));
                server.System<MobStateSystem>().ChangeMobState(worker.Body, MobState.Alive);
                capitalWork = capitalWork with { Request = Guid.NewGuid() };
                Assert.That(markets.TryStartHubUpgrade(sessions[2], capitalHub, capitalWork, out _, out failure), Is.True, failure.ToString());
                server.System<MobStateSystem>().ChangeMobState(worker.Body, MobState.Dead);
                markets.Update(0);
                Assert.That(markets.HubJournal.Last().Result.Status, Is.EqualTo(SWGlobalWarHubStatus.Cancelled));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(new SWGlobalWarAccountBalance(1650000, 0)));
                Assert.That(registry.TryGetAssignment(sessions[3].UserId, out var secondWorker), Is.True);
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, reserve with
                {
                    Request = Guid.NewGuid(), ObservedRevision = accounts.Comp.Revision, Amount = 1200000, Employee = secondWorker.User,
                }, out var disconnectedOrder), Is.True);
                Assert.That(markets.TryGrantHubWork(sessions[1], capitalHub, capitalGrantCommand with
                {
                    Request = Guid.NewGuid(), Employee = secondWorker.User, EmployeeRevision = secondWorker.Revision,
                    MoneyOrder = disconnectedOrder,
                }, out var disconnectedGrant), Is.True);
                Assert.That(markets.TryStartHubUpgrade(sessions[3], capitalHub, new(generation, Guid.NewGuid(), secondWorker.Revision,
                    200, disconnectedOrder, disconnectedGrant), out _, out failure), Is.True, failure.ToString());
                server.PlayerMan.SetStatus(sessions[3], SessionStatus.Disconnected);
                markets.Update(0);
                Assert.That(markets.HubJournal.Last().Result.Status, Is.EqualTo(SWGlobalWarHubStatus.Cancelled));
                Assert.That(registry.TryGetAssignment(secondWorker.User, out var incumbent), Is.True);
                Assert.That(incumbent.Id, Is.EqualTo(secondWorker.Id), "Disconnect cancels work, not the appointment.");
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release, 0, disconnectedOrder), out _), Is.True);
                Assert.That(markets.TryStartHubUpgrade(sessions[1], capitalHub,
                    new(generation, Guid.NewGuid(), minister.Revision, 200), out _, out failure), Is.True, failure.ToString());
                Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                markets.Update(0);
                Assert.That(markets.HubJournal.Last().Result.Failure, Is.EqualTo(SWGlobalWarHubFailure.CampaignEnded));
                Assert.That(markets.HubJournal.Last().Result.FundingState, Is.EqualTo(SWGlobalWarHubFundingState.CleanupPending));
                Assert.That(capitalMarket.HubCapacity, Is.EqualTo(200));
                ticker.EndGameRule(campaign.Owner);
                Assert.That(accounts.Comp.Reservations, Is.Empty);
                Assert.That(accounts.Comp.Balances[(faction, account)], Is.EqualTo(1650000));


                void Advance(int seconds)
                {
                        timing.TimeBase = (timing.TimeBase.Item1 + scenarioOrigin + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    server.System<SharedDoAfterSystem>().Update(0);
                    markets.Update(0);
                }

                void Capture(SWGlobalWarPlanetLocation location, SWGlobalWarFaction owner)
                {
                        var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet.Value);
                    var steps = server.System<SWGlobalWarCampaignStepSystem>();
                    Assert.That(steps.TryQueueOwnership(planet.Value, location, owner, state.OwnershipRevision,
                        generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
            }
            finally
            {
                probe.LedgerChanged = null;
                timing.TimeBase = savedTime;
                // DoAfter and physical map changes dirty networked entities at these ticks.
                // Keep the sequence monotonic when cleanup sends their states to the connected client.
                ticker.SetGamePreset(originalPreset);
            }
        }
        try
        {
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}
