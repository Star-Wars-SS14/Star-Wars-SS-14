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
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string ReturnPrototypes = """
- type: swGlobalWarEconomyProfile
  id: SWSpawnTestReturnSource
  mandatory: { SWGlobalWarF: 0 }
  openingStock: { SWGlobalWarF: 100 }
  hubCapacity: 180
  maximumHubCapacity: 180

- type: swGlobalWarEconomyProfile
  id: SWSpawnTestReturnTransit
  mandatory: { SWGlobalWarF: 0 }
  hubCapacity: 60
  maximumHubCapacity: 60

- type: swGlobalWarPlanet
  id: SWSpawnTestReturnA
  name: Return A
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestReturnSource
  tradeConnections:
    SWSpawnTestReturnB: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestReturnB
  name: Return B
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestReturnTransit
  tradeConnections:
    SWSpawnTestReturnA: { travelTime: 2, capacity: 64 }
    SWSpawnTestReturnC: { travelTime: 2, capacity: 64 }
    SWSpawnTestReturnE: { travelTime: 2, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestReturnC
  name: Return C
  kind: Capital
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWDEconomyTestEmptyCapital

- type: swGlobalWarPlanet
  id: SWSpawnTestReturnE
  name: Return E
  kind: Capital
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWDEconomyTestEmptyCapital

- type: swGlobalWarEconomyFlow
  id: SWSpawnTestReturnFood
  source: SWSpawnTestReturnA
  destination: SWSpawnTestReturnC
  commodity: SWGlobalWarF
  rateMode: Fixed
  share: 180
  priority: DomesticCivilian

- type: entity
  parent: SWSpawnTestEconomyEditorRule
  id: SWSpawnTestReturnRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestReturnA, SWSpawnTestReturnB, SWSpawnTestReturnC, SWSpawnTestReturnE]

- type: gamePreset
  id: SWSpawnTestReturnPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestReturnRule]
""";

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task ReturnConsumerKeepsExactAuthorityHistoryAndPhysicalPart(bool captureRemainder, bool collisionOnly)
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
        var timing = server.ResolveDependency<IGameTiming>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var steps = server.System<SWGlobalWarCampaignStepSystem>();
        await server.AddDummySessions(collisionOnly ? 4 : 3);
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
        string[] jobs = ["SWPoliticsTestHead", "SWPoliticsTestLogistics", "SWPoliticsTestWorker", "SWDiplomacyTestEHead", "SWPoliticsTestWorker"];
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
            ticker.SetGamePreset("SWSpawnTestReturnPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        await pair.RunTicksSync(30);
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
            try
            {
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(collisionOnly ? 5 : 4));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var generation = campaign.Comp.Generation;
                var head = Assignment(0);
                var minister = Assignment(1);
                var worker = Assignment(2);
                var imperial = Assignment(3);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var model = markets.Simulation!;
                const string source = "SWSpawnTestReturnA";
                const string transit = "SWSpawnTestReturnB";
                const string destination = "SWSpawnTestReturnC";
                const string food = "SWGlobalWarF";
                Advance(1);
                var shipment = model.Shipments.Single(s => s.Commodity == food);
                var request = new SWGlobalWarReturnCommand(generation, Guid.NewGuid(), minister.Revision,
                    SWGlobalWarReturnAction.Redirect, SWGlobalWarMarketSystem.ReturnSelection(shipment), source);
                Assert.That(shipment.Quantity, Is.EqualTo(Q(3)));
                Assert.That(markets.TryConfigureReturn(sessions[1], request, out var result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.InTransit));
                var flow = model.Flows["SWSpawnTestReturnFood"];
                Assert.That(markets.TryEditFlow(sessions[1], new(generation, Guid.NewGuid(), minister.Revision,
                    flow.Id, flow.Revision, new(source, destination, food, Q(180), Enabled: false)), out _), Is.True);
                Change((transit, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire));
                yield return true;
                Advance(3);
                Assert.That(shipment.ArrivesAt, Is.Null);
                Assert.That(shipment.Route[shipment.Leg], Is.EqualTo(transit));
                request = request with { Request = Guid.NewGuid(), Part = SWGlobalWarMarketSystem.ReturnSelection(shipment) };
                var oldIncoming = model.Markets[destination].Goods[food].Incoming;
                var stock = model.Markets[source].Goods[food];
                var opening = stock.Stock;
                var money = new SWGlobalWarLedgerCommand(generation, Guid.NewGuid(), 0,
                    SWGlobalWarLedgerAction.Reserve, SWGlobalWarFaction.Republic, SWGlobalWarAccount.Logistics,
                    100, Employee: worker.User, Purpose: "return-work-test");
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, money, out var monetaryGrant), Is.True);
                var workerRequest = request with { Request = Guid.NewGuid(), AssignmentRevision = worker.Revision, Grant = monetaryGrant };
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.Denied));
                Assert.That(markets.TryConfigureReturn(sessions[0], request with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = head.Revision,
                }, out result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.HeadDecisionRequired));
                stock.Capacity = stock.Stock;
                Assert.That(markets.TryConfigureReturn(sessions[1], request, out result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.NoSpace));
                Assert.That(model.Markets[destination].Goods[food].Incoming, Is.EqualTo(oldIncoming));
                Assert.That(stock.Incoming, Is.Zero);
                stock.Capacity = SWGlobalWarMarketUnits.StockCapacity;
                var grantRequest = request with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarReturnAction.Grant, Employee = worker.User,
                    EmployeeRevision = worker.Revision, Minutes = 1,
                };
                Assert.That(markets.TryConfigureReturn(sessions[1], grantRequest, out var grant), Is.True);
                Assert.That(markets.TryConfigureReturn(sessions[1], grantRequest, out var grantReplay), Is.True);
                Assert.That(grantReplay, Is.EqualTo(grant));
                workerRequest = workerRequest with { Grant = grant.Grant };
                foreach (var invalid in new[]
                {
                    workerRequest with { Destination = destination },
                    workerRequest with { Part = workerRequest.Part with { Quantity = Q(2) } },
                    workerRequest with { Part = workerRequest.Part with { Batch = Guid.NewGuid() } },
                    workerRequest with { Part = workerRequest.Part with { Node = source } },
                    workerRequest with { Part = workerRequest.Part with { Shipment = Guid.NewGuid() } },
                })
                    Assert.That(markets.TryConfigureReturn(sessions[2], invalid with { Request = Guid.NewGuid() }, out _), Is.False);
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out var accepted), Is.True);
                Assert.That(markets.GetReturnGrants(worker).Single().Failure, Is.EqualTo(SWGlobalWarReturnFailure.GrantUsed));
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out var replay), Is.True);
                Assert.That(replay, Is.EqualTo(accepted));
                Assert.That(stock.Stock, Is.EqualTo(opening));
                Assert.That(stock.Incoming, Is.EqualTo(Q(3)));
                Assert.That(model.Markets[destination].Goods[food].Incoming, Is.EqualTo(oldIncoming - Q(3)));
                Assert.That(flow.Received, Is.Zero);
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest with { Request = Guid.NewGuid() }, out result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.GrantUsed));
                if (collisionOnly)
                {
                    Change((transit, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire));
                    yield return true;
                    var enemyReturn = new SWGlobalWarReturnCommand(generation, Guid.NewGuid(), imperial.Revision,
                        SWGlobalWarReturnAction.Redirect, SWGlobalWarMarketSystem.ReturnSelection(shipment), transit);
                    Assert.That(markets.TryConfigureReturn(sessions[3], enemyReturn, out _), Is.True);
                    Change((transit, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Republic),
                        (transit, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Republic));
                    yield return true;
                    var secondWorker = Assignment(4);
                    var collisionGrant = grantRequest with
                    {
                        Request = Guid.NewGuid(), Part = SWGlobalWarMarketSystem.ReturnSelection(shipment),
                        Employee = secondWorker.User, EmployeeRevision = secondWorker.Revision,
                    };
                    Assert.That(markets.TryConfigureReturn(sessions[1], collisionGrant, out var secondGrant), Is.True);
                    var collision = collisionGrant with
                    {
                        Request = workerRequest.Request, Action = SWGlobalWarReturnAction.Redirect,
                        AssignmentRevision = secondWorker.Revision, Grant = secondGrant.Grant,
                    };
                    var beforeCollision = stock.Incoming;
                    Assert.That(markets.TryConfigureReturn(sessions[4], collision, out result), Is.False);
                    Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.InvalidRequest));
                    Assert.That(stock.Incoming, Is.EqualTo(beforeCollision));
                    Assert.That(shipment.ReturnOperation, Is.EqualTo(enemyReturn.Request));
                    Assert.That(markets.GetReturnGrants(secondWorker).Single().Failure, Is.EqualTo(SWGlobalWarReturnFailure.None));
                    Assert.That(markets.TryConfigureReturn(sessions[4], collision with { Request = Guid.NewGuid() }, out _), Is.True);
                    Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out replay), Is.True);
                    Assert.That(replay.Receipt!.Received, Is.Zero);
                    Assert.That(replay.Receipt.Captured, Is.EqualTo(Q(3)));
                    ticker.EndGameRule(campaign.Owner);
                    yield break;
                }
                yield return true;
                Advance(70);
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out replay), Is.True,
                    "Expiry after acceptance preserves historical replay and does not cancel the transport.");
                Assert.That(replay.Receipt!.Received, Is.Zero);
                Change((transit, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Republic));
                model.Markets[transit].SendBudget = 0;
                yield return true;
                Advance(71);
                var moving = model.Shipments.Single(s => s.ArrivesAt != null);
                Assert.That(moving.Quantity, Is.EqualTo(Q(1)));
                Assert.That(shipment.Quantity, Is.EqualTo(Q(2)));
                var originalRoute = moving.Route.ToArray();
                var inTransit = request with { Request = Guid.NewGuid(), Part = SWGlobalWarMarketSystem.ReturnSelection(moving), Destination = transit };
                Assert.That(markets.TryConfigureReturn(sessions[1], inTransit, out result), Is.False);
                Assert.That(result.Failure, Is.EqualTo(SWGlobalWarReturnFailure.InTransit));
                SWGlobalWarReturnCommand? capturedRequest = null;
                if (captureRemainder)
                {
                    Change((transit, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire),
                        (transit, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire),
                        (source, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Empire),
                        (source, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Empire));
                    Assert.That(shipment.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Empire));
                    Assert.That(moving.Batch!.Owner, Is.EqualTo(SWGlobalWarFaction.Republic));
                    Assert.That(moving.Route, Is.EqualTo(originalRoute));
                    Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out replay), Is.True);
                    Assert.That(replay.Receipt!.Part, Is.EqualTo(workerRequest.Part));
                    Assert.That(replay.Receipt.Captured, Is.EqualTo(Q(2)));
                    Assert.That(markets.GetReturnCandidates(worker, [shipment.Id]), Is.Empty);
                    Assert.That(markets.GetReturnGrants(worker), Is.Empty);
                    Assert.That(markets.GetReturnResults(imperial), Is.Empty);
                    yield return true;
                    Advance(72);
                    Assert.That(shipment.ArrivesAt, Is.Null, "Captured return cargo remains held without a new order.");
                    Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out replay), Is.True);
                    Assert.That(replay.Receipt!.Received, Is.Zero, "Capture of both node and destination cannot credit foreign receipt.");
                    Assert.That(replay.Receipt.Captured, Is.EqualTo(Q(2)));
                    Change((source, SWGlobalWarPlanetLocation.Surface, SWGlobalWarFaction.Republic),
                        (source, SWGlobalWarPlanetLocation.Orbit, SWGlobalWarFaction.Republic));
                    Assert.That(markets.TryConfigureReturn(sessions[1], request with
                    {
                        Request = Guid.NewGuid(), Part = SWGlobalWarMarketSystem.ReturnSelection(shipment),
                    }, out _), Is.False);
                    capturedRequest = new(generation, Guid.NewGuid(), imperial.Revision, SWGlobalWarReturnAction.Redirect,
                        SWGlobalWarMarketSystem.ReturnSelection(shipment), transit);
                    Assert.That(markets.TryConfigureReturn(sessions[3], capturedRequest, out result), Is.True,
                        "The captured-return exception permits the new owner's current node.");
                    Assert.That(markets.TryConfigureReturn(sessions[3], capturedRequest, out _), Is.True);
                    Assert.That(stock.Incoming, Is.EqualTo(Q(1)));
                }
                yield return true;
                Advance(76);
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out replay), Is.True);
                Assert.That(replay.Receipt!.Received, Is.EqualTo(captureRemainder ? Q(1) : Q(3)));
                Assert.That(stock.Incoming, Is.Zero);
                Assert.That(stock.Stock, Is.EqualTo(opening + (captureRemainder ? Q(1) : Q(3))));
                Assert.That(flow.Received, Is.Zero, "The original delivery remains unsatisfied.");
                if (capturedRequest != null)
                {
                    Assert.That(markets.TryConfigureReturn(sessions[3], capturedRequest, out result), Is.True);
                    Assert.That(result.Receipt!.Received, Is.EqualTo(Q(2)));
                    Assert.That(replay.Receipt.Captured, Is.EqualTo(Q(2)));
                    Assert.That(replay.Receipt.Redirected, Is.EqualTo(Q(2)));
                    for (var i = 0; i < 16; i++)
                        Assert.That(markets.TryConfigureReturn(sessions[3], capturedRequest with
                        {
                            Request = Guid.NewGuid(), Action = SWGlobalWarReturnAction.Revoke, Grant = Guid.NewGuid(),
                        }, out _), Is.False);
                    var history = markets.GetReturnResults(imperial);
                    Assert.That(history, Has.Count.EqualTo(17));
                    Assert.That(history.Single(r => r.Request == capturedRequest.Request).Receipt!.Received, Is.EqualTo(Q(2)),
                        "The bounded service-result tail cannot evict the latest accepted return.");
                }
                Assert.That(markets.ReturnJournal.Count(j => j.Command.Request == workerRequest.Request), Is.EqualTo(1));
                Assert.That(registry.TryExecute(sessions[2], new(generation, Guid.NewGuid(),
                    SWGlobalWarAppointmentAction.Resign, worker.Revision, worker.User, worker.Revision), out _), Is.True);
                Assert.That(markets.TryConfigureReturn(sessions[2], workerRequest, out _), Is.False);
                Assert.That(registry.TryGetAssignment(sessions[2].UserId, out _), Is.False);
                Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                Assert.That(markets.TryConfigureReturn(sessions[1], grantRequest, out _), Is.False);
                ticker.EndGameRule(campaign.Owner);

                SWGlobalWarAssignment Assignment(int user)
                {
                    Assert.That(registry.TryGetAssignment(sessions[user].UserId, out var assignment), Is.True);
                    return assignment;
                }

                void Advance(int second)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(second) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                }

                void Change(params (string Planet, SWGlobalWarPlanetLocation Location, SWGlobalWarFaction Owner)[] changes)
                {
                    var revisions = new Dictionary<string, long>();
                    foreach (var change in changes)
                    {
                        Assert.That(planets.TryGetPlanet(change.Planet, out var entity), Is.True);
                        var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(entity!.Value);
                        var revision = revisions.GetValueOrDefault(change.Planet, state.OwnershipRevision);
                        revisions[change.Planet] = revision + 1;
                        Assert.That(steps.TryQueueOwnership(entity.Value, change.Location, change.Owner, revision,
                            generation, Guid.NewGuid()), Is.True);
                    }
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
            }
            finally
            {
                timing.TimeBase = savedTime;
                ticker.SetGamePreset(originalPreset);
            }
        }
    }
}
