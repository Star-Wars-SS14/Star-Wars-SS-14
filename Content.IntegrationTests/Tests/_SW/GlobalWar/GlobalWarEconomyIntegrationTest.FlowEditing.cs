using System;
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
    private static int Q(decimal amount) => SWGlobalWarMarketUnits.FromUnits(amount);

    [TestPrototypes]
    private const string EditorPrototypes = """
- type: entity
  parent: SWSpawnTestEconomyViewRule
  id: SWSpawnTestEconomyEditorRule
  components:
  - type: SWGlobalWarCampaign
    peaceDuration: 0
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWPoliticsTestHead: { faction: Republic, role: Head }
      SWPoliticsTestLogistics: { faction: Republic, role: LogisticsMinister }
      SWPoliticsTestWorker: { faction: Republic, role: LogisticsWorker }
      SWPoliticsTestCivilian: { faction: Republic, role: Civilian }
      SWDiplomacyTestEHead: { faction: Empire, role: Head }
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWPoliticsTestHead: [1, 1]
            SWPoliticsTestLogistics: [1, 1]
            SWPoliticsTestWorker: [3, 3]
            SWPoliticsTestCivilian: [-1, -1]
      Empire:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWDiplomacyTestEHead: [1, 1]

- type: gamePreset
  id: SWSpawnTestEconomyEditorPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestEconomyEditorRule]
""";

    [Test]
    public async Task FlowEditingUsesCurrentWorkAuthorityOneCreateGrantAndRealTradePolicy()
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
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
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
        string[] jobs = ["SWPoliticsTestHead", "SWPoliticsTestLogistics", "SWPoliticsTestWorker", "SWDiplomacyTestEHead"];
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            ProtoId<JobPrototype> job = jobs[i];
            await pair.SetJobPriorities(sessions[i].UserId,
                profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        }
        await server.WaitAssertion(() =>
        {
            var savedTime = timing.TimeBase;
            var savedTick = timing.CurTick;
            try
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestEconomyEditorPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(4));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var generation = campaign.Comp.Generation;
                var head = Assignment(0);
                var minister = Assignment(1);
                var worker = Assignment(2);
                var imperial = Assignment(3);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var model = markets.Simulation!;
                const string farm = "SWSpawnTestEconomyViewRFarm";
                const string capital = "SWSpawnTestEconomyViewR";
                const string enemy = "SWSpawnTestEconomyViewE";
                const string food = "SWSpawnTestEconomyViewFoodR";
                var settings = new SWGlobalWarFlowSettings(farm, capital, "SWGlobalWarF", Q(0.2m));
                SWGlobalWarFlowCommand Command(int user, string? id = food, long revision = 0) =>
                    new(generation, Guid.NewGuid(), Assignment(user).Revision, id, revision, settings);
                foreach (var invalid in new[] { Command(1) with { Settings = null! },
                             Command(1) with { Settings = settings with { Source = default } },
                             Command(1) with { Settings = settings with { Destination = default } },
                             Command(1) with { Settings = settings with { Commodity = default } },
                             Command(1) with { Settings = settings with { Commodity = "missing" } } })
                    Assert.That(markets.TryEditFlow(sessions[1], invalid, out _), Is.False);
                Assert.That(markets.TryEditFlow(sessions[0], Command(0), out _), Is.False,
                    "An occupied ministry requires an explicit head decision.");
                var decision = Command(0) with { HeadDecision = true };
                Assert.That(markets.TryEditFlow(sessions[0], decision, out _), Is.True);
                Assert.That(markets.FlowJournal.Last().HeadDecision, Is.True);
                Assert.That(markets.FlowJournal.Last().Before!.Revision, Is.Zero);
                Assert.That(markets.FlowJournal.Last().After.Revision, Is.EqualTo(1));

                var money = new SWGlobalWarLedgerCommand(generation, Guid.NewGuid(), 0,
                    SWGlobalWarLedgerAction.Reserve, SWGlobalWarFaction.Republic, SWGlobalWarAccount.Logistics,
                    100, Employee: worker.User, Purpose: "flow-work-test");
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, money, out var monetaryGrant), Is.True);
                var growthSet = new SWGlobalWarGrowthCommand(generation, Guid.NewGuid(), worker.Revision,
                    SWGlobalWarGrowthAction.Set, farm, 0, 15, monetaryGrant);
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet, out _), Is.False,
                    "A money reservation grants no authority over growth food.");
                var growthGrantCommand = new SWGlobalWarGrowthCommand(generation, Guid.NewGuid(), minister.Revision,
                    SWGlobalWarGrowthAction.Grant, farm, Employee: worker.User, EmployeeRevision: worker.Revision,
                    Minimum: 5, Maximum: 20);
                Assert.That(markets.TryConfigureGrowth(sessions[1], growthGrantCommand, out _), Is.True);
                Assert.That(markets.TryConfigureGrowth(sessions[1], growthGrantCommand, out _), Is.True);
                var growthGrant = markets.GetGrowthGrants(worker).Single().Id;
                growthSet = growthSet with { Grant = growthGrant };
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet with { Request = Guid.NewGuid(), Percent = 25 }, out _), Is.False);
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet, out var growthResult), Is.True);
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet, out var growthReplay), Is.True);
                Assert.That(growthReplay, Is.EqualTo(growthResult));
                Assert.That(model.Markets[farm].GrowthRevision, Is.EqualTo(1));
                Assert.That(markets.TryConfigureGrowth(sessions[0], growthSet with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = head.Revision, ExpectedRevision = 1, Percent = 0,
                }, out _), Is.False);
                Assert.That(markets.TryConfigureGrowth(sessions[0], growthSet with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = head.Revision, ExpectedRevision = 1, Percent = 0, HeadDecision = true,
                }, out _), Is.True);
                Assert.That(markets.GrowthJournal.Last().Before, Is.EqualTo(15));
                Assert.That(markets.GrowthJournal.Last().After, Is.Zero);
                Assert.That(markets.TryConfigureGrowth(sessions[1], growthSet with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = minister.Revision, ExpectedRevision = 2, Percent = 10,
                }, out _), Is.True);
                Assert.That(markets.TryConfigureGrowth(sessions[1], growthGrantCommand with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarGrowthAction.Revoke, Grant = growthGrant,
                }, out _), Is.True);
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet, out _), Is.False,
                    "Revoked work rights cannot replay a previous setting command.");
                Assert.That(markets.TryEditFlow(sessions[2], Command(2, food, 1) with { WorkGrant = monetaryGrant }, out _), Is.False);
                var expiredClaim = Guid.NewGuid();
                Assert.That(ledger.TryAllocateReservation(generation, expiredClaim, monetaryGrant, 100, "hub-upgrade"), Is.True);
                var beforeExpiration = timing.TimeBase;
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromMinutes(15), timing.TimeBase.Item2);
                Assert.That(ledger.IsAllocationActive(generation, expiredClaim, monetaryGrant, 100), Is.False);
                Assert.That(ledger.TryExecuteSystem(money with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarLedgerAction.Commit,
                    Reservation = monetaryGrant, Allocation = expiredClaim,
                }, out _), Is.False);
                Assert.That(ledger.TryGetReservation(monetaryGrant, out _), Is.False);
                Assert.That(ledger.TryGetLedger(out var allocationLedger), Is.True);
                Assert.That(allocationLedger.Comp.Allocations[expiredClaim].Status,
                    Is.EqualTo(SWGlobalWarMoneyAllocationStatus.Released));
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.Logistics),
                    Is.EqualTo(new SWGlobalWarAccountBalance(150000, 0)));
                timing.TimeBase = beforeExpiration;
                var growthBeforeCatchUp = model.Markets[farm].Goods["SWGlobalWarF"].GrowthConsumed;
                var growthCallbacks = 0;
                probe.LedgerChanged = () =>
                {
                    growthCallbacks++;
                    Assert.That(model.Markets[farm].GrowthFoodFraction, Is.EqualTo(0.1m),
                        "All elapsed seconds settle under the previous fraction.");
                    Assert.That(markets.TryConfigureGrowth(sessions[1], growthSet with
                    {
                        Request = Guid.NewGuid(), AssignmentRevision = minister.Revision, Percent = 25,
                        ExpectedRevision = model.Markets[farm].GrowthRevision,
                    }, out _), Is.False, "Ledger notification cannot reenter growth mutation.");
                };
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(5) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                var changeGrowth = growthSet with
                {
                    Request = Guid.NewGuid(), AssignmentRevision = minister.Revision, Percent = 25,
                    ExpectedRevision = model.Markets[farm].GrowthRevision,
                };
                Assert.That(markets.TryConfigureGrowth(sessions[1], changeGrowth, out _), Is.True);
                probe.LedgerChanged = null;
                Assert.That(growthCallbacks, Is.GreaterThan(0));
                Assert.That(model.Markets[farm].Goods["SWGlobalWarF"].GrowthConsumed - growthBeforeCatchUp, Is.EqualTo(1000));
                Assert.That(markets.TryConfigureGrowth(sessions[1], changeGrowth with
                {
                    Request = Guid.NewGuid(), ExpectedRevision = model.Markets[farm].GrowthRevision, Percent = 10,
                }, out _), Is.True);
                var createGrant = new SWGlobalWarFlowGrantCommand(generation, Guid.NewGuid(), minister.Revision,
                    worker.User, worker.Revision, null, farm, capital, "SWGlobalWarF", 0, Q(1), -2, 2, TimeSpan.FromMinutes(10));
                Assert.That(markets.TryGrantFlowWork(sessions[1], createGrant, out var grant), Is.True);
                Assert.That(markets.TryGrantFlowWork(sessions[1], createGrant, out var sameGrant), Is.True);
                Assert.That(sameGrant, Is.EqualTo(grant));
                var creation = Command(2, null, -1) with { WorkGrant = grant };
                var failed = creation with { Settings = settings with { Rate = Q(2) } };
                Assert.That(markets.TryEditFlow(sessions[2], failed, out _), Is.False);
                Assert.That(markets.TryEditFlow(sessions[2], creation, out var created), Is.True);
                var count = model.Flows.Count;
                Assert.That(markets.TryEditFlow(sessions[2], creation, out var replay), Is.True);
                Assert.That(replay, Is.EqualTo(created));
                Assert.That(model.Flows.Count, Is.EqualTo(count));
                Assert.That(markets.TryEditFlow(sessions[2], creation with { Request = Guid.NewGuid() }, out _), Is.False);
                Assert.That(markets.TryEditFlow(sessions[2], creation with { Request = Guid.NewGuid(),
                    Flow = created.Flow!.Id, ExpectedRevision = 1, Settings = settings with { Rate = Q(0.3m) } }, out _), Is.True);
                var revoke = new SWGlobalWarFlowRevokeCommand(generation, Guid.NewGuid(), minister.Revision, grant, false);
                Assert.That(markets.TryRevokeFlowWork(sessions[1], revoke), Is.True);
                Assert.That(markets.TryRevokeFlowWork(sessions[1], revoke), Is.True);
                Assert.That(markets.TryRevokeFlowWork(sessions[1], revoke with { Grant = Guid.NewGuid() }), Is.False);
                Assert.That(markets.TryEditFlow(sessions[2], creation, out _), Is.False,
                    "Even an exact replay requires the current work authorization.");

                var crossBorder = Command(1, null, -1) with { Settings = settings with { Destination = enemy, Rate = Q(12) } };
                Assert.That(markets.TryEditFlow(sessions[1], crossBorder, out var crossResult), Is.True);
                var cross = model.Flows[crossResult.Flow!.Id];
                Advance(30);
                var diagnosticFood = model.Markets[farm].Goods["SWGlobalWarF"];
                Assert.That(cross.Dispatched, Is.GreaterThan(0),
                    $"Peace export: wait={cross.WaitReason}; rate={cross.PerMinute}; stock={diagnosticFood.Stock}; " +
                    $"target={diagnosticFood.TargetStock}; produced={diagnosticFood.Produced}; consumed={diagnosticFood.Consumed}; " +
                    $"growth={diagnosticFood.GrowthConsumed}; plan={model.Markets[farm].ProductionOrders["SWGlobalWarF"]}; " +
                    string.Join(';', model.Flows.Values.Where(f => f.Source == farm).Select(f =>
                        $"{f.Id}/{f.PerMinute}/{f.Dispatched}/{f.WaitReason}")));
                Assert.That(markets.GetMarketAvailability(enemy, minister.Body), Is.EqualTo(SWGlobalWarMarketAvailability.Unknown));
                var sent = cross.Dispatched;
                Assert.That(politics.Execute(sessions[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarPoliticalAction.SetSanction, Commodity: "SWGlobalWarF", Enabled: true)).Success, Is.True);
                Advance(95);
                Assert.That(cross.Dispatched, Is.EqualTo(sent));
                Assert.That(cross.Received, Is.Zero);
                Assert.That(model.Shipments.Any(s => s.Flow == cross.Id && s.ArrivesAt == null &&
                    s.Route[s.Leg] == capital), Is.True, "The actual sanction must hold the next leg at the capital.");
                Assert.That(politics.Execute(sessions[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarPoliticalAction.SetSanction, Commodity: "SWGlobalWarF", Enabled: false)).Success, Is.True);
                Advance(97);
                Assert.That(cross.Dispatched, Is.GreaterThan(sent));
                Assert.That(model.Shipments.Any(s => s.Flow == cross.Id && s.ArrivesAt != null && s.Leg == 1), Is.True);
                Assert.That(politics.Execute(sessions[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarPoliticalAction.SetSanction, Commodity: "SWGlobalWarF", Enabled: true)).Success, Is.True);
                Advance(165);
                Assert.That(cross.Received, Is.Zero);
                Assert.That(model.Shipments.Any(s => s.Flow == cross.Id && s.ArrivesAt == null &&
                    s.Route[s.Leg] == enemy), Is.True, "The actual sanction must also hold final receipt.");
                Assert.That(politics.Execute(sessions[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarPoliticalAction.SetSanction, Commodity: "SWGlobalWarF", Enabled: false)).Success, Is.True);
                Advance(167);
                Assert.That(cross.Received, Is.GreaterThan(0));
                Assert.That(politics.TryGetPolitics(out var politicalState), Is.True);
                Assert.That(politics.Execute(sessions[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarPoliticalAction.DeclareWar, RelationsRevision: politicalState.Comp.RelationsRevision)).Success, Is.True);
                sent = cross.Dispatched;
                var received = cross.Received;
                Advance(230);
                Assert.That(cross.Dispatched, Is.EqualTo(sent));
                Assert.That(cross.Received, Is.EqualTo(received));

                const string neutral = "SWSpawnTestEconomyViewNeutralImport";
                Assert.That(markets.TryConfigureGrowth(sessions[1], growthGrantCommand with { Request = Guid.NewGuid() }, out _), Is.True);
                var capturedGrowthGrant = markets.GetGrowthGrants(worker).Single(g => !g.Revoked).Id;
                Capture(farm, SWGlobalWarFaction.Empire);
                Assert.That(markets.TryConfigureGrowth(sessions[3], new(generation, Guid.NewGuid(), imperial.Revision,
                    SWGlobalWarGrowthAction.Revoke, farm, Grant: capturedGrowthGrant), out _), Is.False,
                    "Capturing a world does not authorize revoking the previous faction's work grant.");
                Assert.That(markets.GetGrowthGrants(imperial), Is.Empty);
                Assert.That(markets.GetFlowViews(imperial.Body).Any(f => f.Id == neutral || f.Id == food), Is.False);
                Assert.That(markets.GetFlowViews(head.Body).Any(f => f.Id == neutral), Is.True);
                Assert.That(markets.TryEditFlow(sessions[3], Command(3, neutral) with
                {
                    Settings = new("SWSpawnTestEconomyViewN", farm, "SWGlobalWarE", Q(0.1m)),
                }, out _), Is.False);
                var oldNeutralSent = model.Flows[neutral].Dispatched;
                Advance(231);
                Assert.That(model.Flows[neutral].Dispatched, Is.EqualTo(oldNeutralSent));
                Capture(farm, SWGlobalWarFaction.Republic);

                var freshGrant = createGrant with { Request = Guid.NewGuid(), Flow = food };
                Assert.That(ledger.TryExecute(sessions[1], minister.Revision, money with
                {
                    Request = Guid.NewGuid(), ObservedRevision = allocationLedger.Comp.Revision,
                }, out var resignationOrder), Is.True);
                var resignationClaim = Guid.NewGuid();
                Assert.That(ledger.TryAllocateReservation(generation, resignationClaim, resignationOrder, 100, "hub-upgrade"), Is.True);
                Assert.That(markets.TryGrantFlowWork(sessions[1], freshGrant, out var catchUpGrant), Is.True);
                var delayed = Command(2, food, model.Flows[food].Revision) with { WorkGrant = catchUpGrant };
                var previousRevision = model.Flows[food].Revision;
                var callback = false;
                probe.LedgerChanged = () =>
                {
                    probe.LedgerChanged = null;
                    callback = true;
                    Assert.That(markets.TryEditFlow(sessions[2], delayed, out _), Is.False,
                        "A settlement callback cannot reenter flow mutation.");
                    Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet with
                    {
                        Request = Guid.NewGuid(), Grant = capturedGrowthGrant,
                        ExpectedRevision = model.Markets[farm].GrowthRevision,
                    }, out _), Is.False);
                    Assert.That(markets.TryRevokeFlowWork(sessions[1], generation, minister.Revision, catchUpGrant, false),
                        Is.False, "External economy mutations are closed during settlement.");
                    Assert.That(registry.TryExecute(sessions[2], new(generation, Guid.NewGuid(),
                        SWGlobalWarAppointmentAction.Resign, worker.Revision, worker.User, worker.Revision), out _), Is.True);
                };
                timing.CurTick += 1;
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(235) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                Assert.That(markets.TryEditFlow(sessions[2], delayed, out _), Is.False);
                Assert.That(callback, Is.True);
                Assert.That(markets.TryConfigureGrowth(sessions[2], growthSet with
                {
                    Request = Guid.NewGuid(), Grant = capturedGrowthGrant,
                    ExpectedRevision = model.Markets[farm].GrowthRevision,
                }, out _), Is.False, "A resigned assignment cannot use its growth grant.");
                Assert.That(model.Flows[food].Revision, Is.EqualTo(previousRevision));
                Assert.That(ledger.IsAllocationActive(generation, resignationClaim, resignationOrder, 100), Is.False);
                Assert.That(ledger.TryGetReservation(resignationOrder, out _), Is.False);
                Assert.That(allocationLedger.Comp.Allocations[resignationClaim].Status,
                    Is.EqualTo(SWGlobalWarMoneyAllocationStatus.Released));
                Assert.That(ledger.TryExecuteSystem(money with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarLedgerAction.Commit,
                    Reservation = resignationOrder, Allocation = resignationClaim,
                }, out _), Is.False);
                Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                Assert.That(markets.TryEditFlow(sessions[0], decision, out _), Is.False);

                SWGlobalWarAssignment Assignment(int user)
                {
                    Assert.That(registry.TryGetAssignment(sessions[user].UserId, out var assignment), Is.True);
                    return assignment;
                }

                void Advance(int seconds)
                {
                    timing.CurTick += 1;
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                }

                void Capture(string planetId, SWGlobalWarFaction owner)
                {
                    timing.CurTick += 1;
                    Assert.That(server.System<SWGlobalWarPlanetSystem>().TryGetPlanet(planetId, out var planet), Is.True);
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                    var steps = server.System<SWGlobalWarCampaignStepSystem>();
                    Assert.That(steps.TryQueueOwnership(planet.Value, SWGlobalWarPlanetLocation.Surface, owner,
                        state.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
            }
            finally
            {
                probe.LedgerChanged = null;
                timing.TimeBase = savedTime;
                timing.CurTick = savedTick;
                ticker.SetGamePreset(originalPreset);
            }
        });
        await pair.CleanReturnAsync();
    }
}
