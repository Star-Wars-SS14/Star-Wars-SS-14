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
    private const string LanthanumPrototypes = """
- type: swGlobalWarEconomyProfile
  id: SWSpawnTestLanthanumEmpty
  mandatory: { SWGlobalWarLn: 0 }
  hubCapacity: 60
  maximumHubCapacity: 60

- type: swGlobalWarEconomyProfile
  id: SWSpawnTestLanthanumIndustry
  mandatory: { SWGlobalWarLn: 0 }
  exportReserves: { SWGlobalWarLn: 600 }
  hubCapacity: 60
  maximumHubCapacity: 60

- type: swGlobalWarEconomyProfile
  id: SWSpawnTestLanthanumEnclave
  mandatory: { SWGlobalWarLn: 0 }
  recipes:
  - output: SWGlobalWarLn
    perMinute: 1
    inputs: { SWGlobalWarF: 1 }
  hubCapacity: 60
  maximumHubCapacity: 60

- type: swGlobalWarPlanet
  id: SWSpawnTestLnRHome
  name: Ln RHome
  kind: Capital
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty

- type: swGlobalWarPlanet
  id: SWSpawnTestLnRIndustry
  name: Ln RIndustry
  kind: Industry
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumIndustry

- type: swGlobalWarPlanet
  id: SWSpawnTestLnRIndustry2
  name: Ln RIndustry2
  kind: Industry
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumIndustry

- type: swGlobalWarPlanet
  id: SWSpawnTestLnREnclave
  name: Ln REnclave
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEnclave
  enclave: true
  tradeConnections:
    SWSpawnTestLnShort: { travelTime: 1, capacity: 64 }
    SWSpawnTestLnBypass: { travelTime: 10, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestLnShort
  name: Ln Short
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty
  tradeConnections:
    SWSpawnTestLnRIndustry: { travelTime: 1, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestLnBypass
  name: Ln Bypass
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty
  tradeConnections:
    SWSpawnTestLnRIndustry: { travelTime: 10, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestLnRDeposit
  name: Ln RDeposit
  kind: Agriculture
  initialOwner: Republic
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty
  lanthanumReserve: true
  tradeConnections:
    SWSpawnTestLnRIndustry: { travelTime: 20, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestLnEHome
  name: Ln EHome
  kind: Capital
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty

- type: swGlobalWarPlanet
  id: SWSpawnTestLnEIndustry
  name: Ln EIndustry
  kind: Industry
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumIndustry

- type: swGlobalWarPlanet
  id: SWSpawnTestLnEEnclave
  name: Ln EEnclave
  kind: Agriculture
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEnclave
  enclave: true
  tradeConnections:
    SWSpawnTestLnEIndustry: { travelTime: 1, capacity: 64 }

- type: swGlobalWarPlanet
  id: SWSpawnTestLnEDeposit
  name: Ln EDeposit
  kind: Agriculture
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestLanthanumEmpty
  lanthanumReserve: true
  tradeConnections:
    SWSpawnTestLnRIndustry: { travelTime: 20, capacity: 64 }
    SWSpawnTestLnEIndustry: { travelTime: 20, capacity: 64 }

- type: entity
  parent: SWSpawnTestEconomyEditorRule
  id: SWSpawnTestLanthanumRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestLnRHome, SWSpawnTestLnRIndustry, SWSpawnTestLnRIndustry2, SWSpawnTestLnREnclave, SWSpawnTestLnShort, SWSpawnTestLnBypass, SWSpawnTestLnRDeposit, SWSpawnTestLnEHome, SWSpawnTestLnEIndustry, SWSpawnTestLnEEnclave, SWSpawnTestLnEDeposit]

- type: gamePreset
  id: SWSpawnTestLanthanumPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestLanthanumRule]
""";

    [TestCase(false)]
    [TestCase(true)]
    public async Task LanthanumReserveUsesRealTreasuryOwnershipAndEnding(bool endFromCallback)
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
            ticker.SetGamePreset("SWSpawnTestLanthanumPreset");
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
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(4));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var generation = campaign.Comp.Generation;
                var head = Assignment(0);
                var minister = Assignment(1);
                var worker = Assignment(2);
                var imperialHead = Assignment(3);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var model = markets.Simulation!;
                const SWGlobalWarFaction republic = SWGlobalWarFaction.Republic;
                const SWGlobalWarFaction empire = SWGlobalWarFaction.Empire;
                const string ln = "SWGlobalWarLn";
                const string depot = "SWSpawnTestLnRDeposit";
                var deposit = model.LanthanumDeposits[depot];
                var good = model.Markets[depot].Goods[ln];
                var doctrine = model.LanthanumDoctrines[republic];
                var disable = new SWGlobalWarLanthanumCommand(generation, Guid.NewGuid(), head.Revision, 0, false);
                Advance(1);
                Assert.That(markets.GetLanthanumView(head)!.Deposits.Select(d => d.Planet),
                    Is.EquivalentTo(new[] { depot }));
                Assert.That(markets.GetLanthanumView(imperialHead)!.Deposits.Select(d => d.Planet),
                    Is.EquivalentTo(new[] { "SWSpawnTestLnEDeposit" }));
                Assert.That(doctrine.WaitingReason, Is.EqualTo(SWGlobalWarLanthanumWaitingReason.EnclaveAvailable));
                Change(("SWSpawnTestLnShort", SWGlobalWarPlanetLocation.Orbit, empire));
                yield return true;
                Advance(2);
                Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Waiting), "The longer permitted path still reaches industry.");
                Assert.That(model.HasLanthanumPath("SWSpawnTestLnREnclave", "SWSpawnTestLnRIndustry", republic), Is.True);
                Change(("SWSpawnTestLnBypass", SWGlobalWarPlanetLocation.Orbit, empire));
                yield return true;
                Assert.That(doctrine.StartedAt, Is.Not.Null);
                var ready = (int) doctrine.StartedAt!.Value.TotalSeconds + 120;
                Money(republic, SWGlobalWarLedgerAction.Debit, ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Available - 399);
                Advance(ready - 1);
                Assert.That(deposit.Released, Is.Zero);
                Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Starting));
                Advance(ready + 10);
                Assert.That(deposit.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.WaitingFunds));
                Assert.That(deposit.Released, Is.Zero);
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(399));
                good.Capacity = Q(1) - 1;
                Money(republic, SWGlobalWarLedgerAction.Credit, 100000);
                Advance(ready + 20);
                Assert.That(deposit.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.WaitingSpace));
                Assert.That(deposit.Released, Is.Zero);
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.Zero);
                good.Capacity = SWGlobalWarMarketUnits.StockCapacity;
                var callbacks = 0;
                var balance = ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Balance;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    Assert.That(deposit.Released, Is.EqualTo(1));
                    Assert.That(deposit.Remaining, Is.EqualTo(1199));
                    Assert.That(good.Produced, Is.EqualTo(Q(1)));
                    Assert.That(markets.LanthanumJournal.Single(j => j.Units == 1).Cost, Is.EqualTo(400));
                    Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(balance - 400));
                    Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.EqualTo(4400));
                    Assert.That(markets.TryConfigureLanthanum(sessions[0], disable, out _), Is.False);
                    if (endFromCallback)
                        Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                };
                Advance(ready + (endFromCallback ? 100 : 25));
                probe.LedgerChanged = null;
                Assert.That(callbacks, Is.GreaterThan(0));
                Assert.That(deposit.Released, Is.EqualTo(1));
                if (endFromCallback)
                {
                    Assert.That(model.Time, Is.EqualTo(TimeSpan.FromSeconds(ready + 25)));
                    markets.Update(600);
                    Assert.That(deposit.Released, Is.EqualTo(1));
                    Assert.That(ledger.TryGetLedger(out _), Is.False);
                    var accounts = server.EntMan.GetComponent<SWGlobalWarLedgerComponent>(campaign.Owner);
                    Assert.That(accounts.Reservations, Is.Not.Empty, "Ending closes the ledger API; Stopped owns final cleanup.");
                    ticker.EndGameRule(campaign.Owner);
                    Assert.That(accounts.Reservations, Is.Empty);
                    yield break;
                }
                var flow = model.Flows.Values.Single(f => f.Source == depot);
                Assert.That(markets.TryEditFlow(sessions[1], new(generation, Guid.NewGuid(), minister.Revision,
                    flow.Id, flow.Revision, new(depot, flow.Destination, ln, Q(3), Enabled: false)), out _), Is.True);
                var departed = model.Shipments.Single(s => s.Source == depot);
                Assert.That(departed.Batch!.Owner, Is.EqualTo(republic));
                var oldStock = good.Stock;
                Change((depot, SWGlobalWarPlanetLocation.Surface, empire), (depot, SWGlobalWarPlanetLocation.Orbit, empire));
                yield return true;
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.Zero);
                Assert.That(good.Stock, Is.EqualTo(oldStock));
                Assert.That(good.Batches.All(b => b.Owner == empire), Is.True);
                Assert.That(departed.Batch.Owner, Is.EqualTo(republic));
                Assert.That(markets.GetLanthanumView(head)!.Deposits, Is.Empty);
                Assert.That(markets.GetLanthanumView(imperialHead)!.Deposits.Select(d => d.Planet),
                    Is.EquivalentTo(new[] { depot, "SWSpawnTestLnEDeposit" }));
                Advance(ready + 30);
                Assert.That(deposit.Released, Is.EqualTo(1), "An owner without a prepared episode cannot inherit readiness.");
                Change((depot, SWGlobalWarPlanetLocation.Surface, republic), (depot, SWGlobalWarPlanetLocation.Orbit, republic));
                yield return true;
                Advance(ready + 35);
                Assert.That(deposit.Released, Is.EqualTo(2));
                Assert.That(flow.Enabled, Is.False, "Default flow discovery must preserve the user's decision.");
                Assert.That(flow.ExplicitRate, Is.EqualTo(3));
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.GreaterThan(0));
                Change(("SWSpawnTestLnBypass", SWGlobalWarPlanetLocation.Orbit, republic));
                yield return true;
                Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.Waiting));
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.Zero);
                Change(("SWSpawnTestLnBypass", SWGlobalWarPlanetLocation.Orbit, empire));
                yield return true;
                var secondReady = (int) doctrine.StartedAt!.Value.TotalSeconds + 120;
                Assert.That(secondReady, Is.GreaterThan(ready));
                Advance(secondReady + 5);
                Assert.That(deposit.Released, Is.EqualTo(3));
                Assert.That(markets.TryConfigureLanthanum(sessions[1], disable with { AssignmentRevision = minister.Revision }, out _), Is.False);
                Assert.That(markets.TryConfigureLanthanum(sessions[2], disable with { AssignmentRevision = worker.Revision }, out _), Is.False);
                Assert.That(markets.GetLanthanumView(worker), Is.Not.Null, "Logistics workers retain their own economic overview.");
                Assert.That(markets.TryConfigureLanthanum(sessions[0], disable, out var accepted), Is.True);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], disable, out var replay), Is.True);
                Assert.That(replay, Is.EqualTo(accepted));
                Assert.That(doctrine.Revision, Is.EqualTo(1));
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.Zero);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], disable with { Automatic = true }, out _), Is.False);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], disable with { Request = Guid.NewGuid(), Automatic = true }, out var conflict), Is.False);
                Assert.That(conflict.Conflict, Is.True);
                Advance(secondReady + 200);
                Assert.That(deposit.Released, Is.EqualTo(3));
                Assert.That(registry.TryExecute(sessions[0], new(generation, Guid.NewGuid(),
                    SWGlobalWarAppointmentAction.Resign, head.Revision, head.User, head.Revision), out _), Is.True);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], disable, out _), Is.False);
                Assert.That(registry.TryGetSuccession(republic, minister.User,
                    out var vacancy, out var stage, out _, out var eligible), Is.True);
                Assert.That(eligible, Is.True);
                Assert.That(registry.TryExecute(sessions[1], new(generation, Guid.NewGuid(),
                    SWGlobalWarAppointmentAction.ClaimHead, minister.Revision, minister.User, minister.Revision,
                    Vacancy: vacancy, Stage: stage), out _), Is.True);
                var successor = Assignment(1);
                Assert.That(successor.Role, Is.EqualTo(SWGlobalWarRole.Head));
                Assert.That(doctrine.Automatic, Is.False, "A successor must inherit the explicit prohibition.");
                Assert.That(deposit.Remaining, Is.EqualTo(1197));
                var enable = disable with { Request = Guid.NewGuid(), AssignmentRevision = successor.Revision,
                    ExpectedRevision = 1, Automatic = true };
                Assert.That(markets.TryConfigureLanthanum(sessions[1], enable, out _), Is.True);
                Assert.That(doctrine.StartedAt, Is.EqualTo(model.Time));
                var thirdReady = (int) model.Time.TotalSeconds + 120;
                Advance(thirdReady + 5);
                Assert.That(deposit.Released, Is.EqualTo(4));
                Change(("SWSpawnTestLnRIndustry", SWGlobalWarPlanetLocation.Surface, empire),
                    ("SWSpawnTestLnRIndustry2", SWGlobalWarPlanetLocation.Surface, empire));
                yield return true;
                Assert.That(doctrine.Phase, Is.EqualTo(SWGlobalWarLanthanumPhase.NoRecipient));
                Assert.That(doctrine.Demand, Is.GreaterThan(0));
                Assert.That(ledger.GetBalance(republic, SWGlobalWarAccount.Treasury).Reserved, Is.Zero);
                // The other faction prepares its own episode before taking the already depleted physical deposit.
                Change(("SWSpawnTestLnEEnclave", SWGlobalWarPlanetLocation.Surface, republic));
                yield return true;
                var imperialDoctrine = model.LanthanumDoctrines[empire];
                var imperialReady = (int) imperialDoctrine.StartedAt!.Value.TotalSeconds + 120;
                var imperialOpening = ledger.GetBalance(empire, SWGlobalWarAccount.Treasury).Balance;
                Advance(imperialReady + 5);
                Assert.That(model.LanthanumDeposits["SWSpawnTestLnEDeposit"].Released, Is.EqualTo(1));
                Change((depot, SWGlobalWarPlanetLocation.Surface, empire), (depot, SWGlobalWarPlanetLocation.Orbit, empire));
                yield return true;
                Advance(imperialReady + 10);
                Assert.That(deposit.Released, Is.EqualTo(5));
                Assert.That(deposit.Remaining, Is.EqualTo(1195));
                Assert.That(model.LanthanumDeposits["SWSpawnTestLnEDeposit"].Released, Is.EqualTo(2));
                Assert.That(ledger.GetBalance(empire, SWGlobalWarAccount.Treasury).Balance, Is.EqualTo(imperialOpening - 1200));
                Assert.That(markets.LanthanumJournal.Where(j => j.Faction == empire).Sum(j => j.Units), Is.EqualTo(3));
                Assert.That(clock.TryAdvancePhase(generation, SWGlobalWarCampaignPhase.Ending), Is.True);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], enable, out _), Is.False);
                ticker.EndGameRule(campaign.Owner);
                yield return true;
                Assert.That(ticker.StartGameRule("SWSpawnTestLanthanumRule"), Is.True);
                Assert.That(markets.Simulation!.Generation, Is.Not.EqualTo(generation));
                Assert.That(markets.Simulation.LanthanumDeposits.Values.Sum(d => d.Remaining), Is.EqualTo(2400));
                Assert.That(markets.Simulation.LanthanumDoctrines[republic].Automatic, Is.True);
                Assert.That(markets.TryConfigureLanthanum(sessions[0], enable, out _), Is.False);
                Assert.That(clock.TryGetCampaign(out var restarted), Is.True);
                ticker.EndGameRule(restarted.Owner);

                SWGlobalWarAssignment Assignment(int user)
                {
                    Assert.That(registry.TryGetAssignment(sessions[user].UserId, out var assignment), Is.True);
                    return assignment;
                }

                void Money(SWGlobalWarFaction faction, SWGlobalWarLedgerAction action, long amount)
                {
                    Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                    Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                        action, faction, SWGlobalWarAccount.Treasury, amount, Purpose: "ln-fixture"), out _), Is.True);
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
                probe.LedgerChanged = null;
                timing.TimeBase = savedTime;
                ticker.SetGamePreset(originalPreset);
            }
        }
    }
}
