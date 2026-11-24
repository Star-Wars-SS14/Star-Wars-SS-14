using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string SupplyAuthorityPrototypes = """
- type: swGlobalWarPlanet
  id: SWSpawnTestSupplyTaxHome
  name: Supply tax home
  kind: Capital
  initialOwner: Empire
  surface: SWSpawnTestMap
  orbit: SWSpawnTestMap
  economyProfile: SWSpawnTestSupplyInputs

- type: entity
  parent: SWSpawnTestEconomyEditorRule
  id: SWSpawnTestSupplyAuthorityRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestLnRHome, SWSpawnTestSupplySource, SWSpawnTestSupplyTaxHome]
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWPoliticsTestHead: { faction: Republic, role: Head }
      SWPoliticsTestWar: { faction: Republic, role: WarMinister }
      SWPoliticsTestIndustry: { faction: Republic, role: IndustryMinister }
      SWPoliticsTestWorker: { faction: Republic, role: IndustryWorker }
      SWDiplomacyTestEHead: { faction: Empire, role: Head }
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWPoliticsTestHead: [1, 1]
            SWPoliticsTestWar: [1, 1]
            SWPoliticsTestIndustry: [1, 1]
            SWPoliticsTestWorker: [1, 1]
      Empire:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWDiplomacyTestEHead: [1, 1]

- type: gamePreset
  id: SWSpawnTestSupplyAuthorityPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestSupplyAuthorityRule]
""";

    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyAuthoritySeparatesWarIndustryHeadAndExactReceipts(bool capture)
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
        await server.AddDummySessions(4);
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
        string[] jobs = ["SWPoliticsTestHead", "SWPoliticsTestWar", "SWPoliticsTestIndustry", "SWPoliticsTestWorker", "SWDiplomacyTestEHead"];
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
            ticker.SetGamePreset("SWSpawnTestSupplyAuthorityPreset");
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
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(5));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var generation = campaign.Comp.Generation;
                var head = Assignment(0);
                var war = Assignment(1);
                var industry = Assignment(2);
                var worker = Assignment(3);
                var enemy = Assignment(4);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                const SWGlobalWarSupplyKind kind = SWGlobalWarSupplyKind.Medicine;
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                var command = new SWGlobalWarSupplyCommand(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyAction.ConfigureLine, kind, LocalPercent: 50);
                Assert.That(markets.TryConfigureSupply(sessions[0], command, out _), Is.False,
                    "An occupied industry ministry requires an explicit head decision.");
                Assert.That(markets.TryConfigureSupply(sessions[1], command with { AssignmentRevision = war.Revision }, out _), Is.False);
                Assert.That(markets.TryConfigureSupply(sessions[3], command with { AssignmentRevision = worker.Revision }, out _), Is.False);
                command = command with { HeadDecision = true };
                Assert.That(markets.TryConfigureSupply(sessions[0], command, out var changed), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[0], command, out var replay), Is.True);
                Assert.That(replay, Is.EqualTo(changed));
                Assert.That(markets.TryConfigureSupply(sessions[0], command with { LocalPercent = 75 }, out _), Is.False);
                var configure = command with { Request = Guid.NewGuid(), AssignmentRevision = industry.Revision,
                    ExpectedRevision = 1, LocalPercent = 0, HeadDecision = false };
                Assert.That(markets.TryConfigureSupply(sessions[2], configure, out _), Is.True);
                Assert.That(markets.SupplyJournal.Count, Is.EqualTo(2));
                Assert.That(markets.SupplyJournal[0].Before.LocalPercent, Is.EqualTo(25));
                Assert.That(markets.SupplyJournal[0].After.LocalPercent, Is.EqualTo(50));
                Assert.That(markets.SupplyJournal[0].HeadDecision, Is.True);
                Assert.That(markets.GetSupplyView(war)!.WarAvailable, Is.Not.Null);
                Assert.That(markets.GetSupplyView(industry)!.WarAvailable, Is.Null);
                Assert.That(markets.GetSupplyView(industry)!.LedgerRevision, Is.Null);
                Assert.That(markets.GetSupplyView(worker)!.WarAvailable, Is.Null);
                Assert.That(markets.GetSupplyView(worker)!.CanProduce, Is.False);
                Assert.That(markets.GetSupplyView(worker)!.CanPurchase, Is.False);
                Assert.That(markets.GetSupplyResults(worker), Is.Empty);
                Assert.That(markets.GetSupplyView(enemy)!.Lines.Single(l => l.Kind == kind).LocalPercent, Is.EqualTo(25));

                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var position = new EntityCoordinates(map!.Value, Vector2.Zero);
                var factory = server.EntMan.SpawnEntity(null, position);
                var arsenal = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Advance(1200);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var purchase = new SWGlobalWarSupplyCommand(generation, Guid.NewGuid(), war.Revision,
                    SWGlobalWarSupplyAction.Purchase, kind, accounts.Comp.Revision,
                    Source: server.EntMan.GetNetEntity(factory), Recipient: server.EntMan.GetNetEntity(arsenal), Quantity: 2,
                    HeadDecision: true);
                purchase = Quoted(1, purchase);
                DeniedQuote(0, purchase with { AssignmentRevision = head.Revision });
                DeniedQuote(1, purchase with { Quantity = 3 });
                DeniedQuote(1, purchase with { Kind = SWGlobalWarSupplyKind.Ration });
                DeniedQuote(1, purchase with { Bacta = true });
                DeniedQuote(1, purchase with { HeadDecision = false });
                DeniedQuote(1, purchase with { Source = purchase.Recipient });
                DeniedQuote(1, purchase with { Recipient = purchase.Source });
                DeniedQuote(1, purchase with { ExpectedRevision = purchase.ExpectedRevision + 1 });
                Assert.That(markets.TryConfigureSupply(sessions[2], configure with { Request = Guid.NewGuid(),
                    ExpectedRevision = 2 }, out _), Is.True);
                DeniedQuote(1, purchase);
                purchase = Quoted(1, purchase);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Reserve, war.Faction, SWGlobalWarAccount.War,
                    ledger.GetBalance(war.Faction, SWGlobalWarAccount.War).Available, Purpose: "test-quote-war-shortage"),
                    out var heldWar), Is.True);
                DeniedQuote(1, purchase);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Release, war.Faction, SWGlobalWarAccount.War, Reservation: heldWar), out _), Is.True);
                var taxFood = model.Markets["SWSpawnTestSupplyTaxHome"].Goods["SWGlobalWarF"];
                taxFood.BaseConsumptionPerMinute = Q(2);
                var treasury = ledger.GetBalance(SWGlobalWarFaction.Empire, SWGlobalWarAccount.Treasury).Balance;
                var consumed = taxFood.Consumed;
                Advance(1205);
                Assert.That(taxFood.Consumed, Is.GreaterThan(consumed));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Empire, SWGlobalWarAccount.Treasury).Balance,
                    Is.GreaterThan(treasury));
                Assert.That(accounts.Comp.Revision, Is.GreaterThan(purchase.ExpectedRevision));
                taxFood.BaseConsumptionPerMinute = 0;
                Assert.That(markets.TryConfigureSupply(sessions[2], purchase with { AssignmentRevision = industry.Revision }, out _), Is.False);
                Assert.That(markets.TryConfigureSupply(sessions[4], purchase with { AssignmentRevision = enemy.Revision }, out _), Is.False);
                var callbacks = 0;
                probe.LedgerChanged = () =>
                {
                    callbacks++;
                    Assert.That(markets.GetSupplyResults(war).Single(r => r.Request == purchase.Request).Success, Is.True);
                    Assert.That(markets.SupplyJournal.Count, Is.EqualTo(4));
                    Assert.That(markets.TryConfigureSupply(sessions[1], purchase, out _), Is.False,
                        "A monetary observer cannot reenter the transaction before notification closure.");
                };
                Assert.That(markets.TryConfigureSupply(sessions[1], purchase, out var bought), Is.True);
                probe.LedgerChanged = null;
                Assert.That(callbacks, Is.GreaterThan(0));
                Assert.That(markets.TryConfigureSupply(sessions[1], purchase, out replay), Is.True);
                Assert.That(replay, Is.EqualTo(bought));
                Assert.That(ledger.GetBalance(war.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(300));
                Assert.That(model.SupplyPrograms[war.Faction].Orders.Count, Is.EqualTo(1));
                Assert.That(markets.SupplyJournal[3].Before.Exists, Is.False);
                Assert.That(markets.SupplyJournal[3].After.Outstanding, Is.EqualTo(2));
                Assert.That(markets.SupplyJournal[3].After.ReservedPayment, Is.EqualTo(300));
                var batch = markets.GetSupplyProductionWork(generation, factory).Single();
                Assert.That(markets.TryBeginSupplyProduction(generation, batch.Request.Operation), Is.True);
                Advance(1656);
                var goods = Enumerable.Range(0, batch.Quantity).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                var carrier = server.EntMan.SpawnEntity("SWBoxWood", position);
                Assert.That(markets.TryCollectSupplyPurchase(generation, bought.Target, carrier, [goods[0]]), Is.True);
                var cancel = purchase with { Request = Guid.NewGuid(), Action = SWGlobalWarSupplyAction.CancelPurchase,
                    Target = bought.Target };
                Assert.That(markets.TryConfigureSupply(sessions[2], cancel with { AssignmentRevision = industry.Revision }, out _), Is.False);
                server.EntMan.DeleteEntity(factory);
                if (capture)
                {
                    ChangeOwner(SWGlobalWarFaction.Empire);
                    yield return true;
                }
                Assert.That(markets.TryConfigureSupply(sessions[1], cancel, out var pending), Is.False);
                Assert.That(pending.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.ReturnPending));
                Assert.That(ledger.GetBalance(war.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(300));
                var wrong = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Value, new Vector2(3, 0)));
                Assert.That(markets.TryRegisterSupplyStore(generation, wrong), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[1], cancel, out _), Is.False);
                var restored = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, restored), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[1], cancel, out _), Is.True,
                    "An exact retry resolves a rebuilt store on the original point without changing its payload.");
                var containers = server.System<SharedContainerSystem>();
                Assert.That(containers.TryGetContainer(restored, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory!.ContainedEntities.Count, Is.EqualTo(1));
                Assert.That(containers.TryGetContainingContainer(goods[0], out var carried), Is.True);
                Assert.That(carried.Owner, Is.EqualTo(carrier), "Cancellation never teleports the item already in a carrier.");
                Assert.That(markets.TryGetSupplyItem(goods[1], out var returned), Is.True);
                Assert.That(returned.Origin, Is.EqualTo(capture ? SWGlobalWarSupplyOrigin.Trophy : SWGlobalWarSupplyOrigin.Industrial));
                Assert.That(returned.Owner, Is.EqualTo(capture ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Republic));
                Assert.That(ledger.GetBalance(war.Faction, SWGlobalWarAccount.War).Reserved, Is.Zero);
                var cancellation = markets.SupplyJournal.Last();
                Assert.That(cancellation.Before.ReturnPending, Is.True);
                Assert.That(cancellation.Before.Outstanding, Is.EqualTo(2));
                Assert.That(cancellation.After.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Cancelled));
                Assert.That(cancellation.After.Outstanding, Is.Zero);
                Assert.That(cancellation.After.ReservedPayment, Is.Zero);
                if (capture)
                {
                    ChangeOwner(SWGlobalWarFaction.Republic);
                    yield return true;
                    Assert.That(markets.TryGetSupplyItem(goods[1], out returned), Is.True);
                    Assert.That(returned.Origin, Is.EqualTo(SWGlobalWarSupplyOrigin.Trophy));
                    Assert.That(returned.Payment, Is.Null);
                }

                var cables = server.EntMan.SpawnEntity("SWAdvancedCables", position);
                Assert.That(containers.Insert(cables, inventory), Is.True);
                Assert.That(markets.TryReserveSupplyProduction(new(generation, Guid.NewGuid(), restored, war.Faction,
                    SWGlobalWarSupplyKind.Magazine, 1), out var magazine), Is.True);
                var stop = new SWGlobalWarSupplyCommand(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyAction.CancelProduction, SWGlobalWarSupplyKind.Magazine,
                    Target: magazine.Request.Operation, HeadDecision: true);
                server.EntMan.DeleteEntity(restored);
                Assert.That(markets.TryConfigureSupply(sessions[0], stop, out pending), Is.False);
                Assert.That(pending.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.ReturnPending));
                restored = server.EntMan.SpawnEntity(null, position);
                Assert.That(markets.TryRegisterSupplyStore(generation, restored), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[0], stop, out _), Is.True);
                Assert.That(containers.TryGetContainer(restored, "sw-program-supply", out inventory), Is.True);
                Assert.That(inventory!.Contains(cables), Is.True);
                var stopped = markets.SupplyJournal.Last();
                Assert.That(stopped.Before.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Reserved));
                Assert.That(stopped.Before.InputReservations, Is.EqualTo(1));
                Assert.That(stopped.After.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Cancelled));
                Assert.That(stopped.After.InputReservations, Is.Zero);

                Assert.That(markets.TryRegisterSupplyConsumer(generation, restored, kind), Is.True);
                Advance(1657);
                var intent = markets.SupplyIntents.Single(i => i.Faction == war.Faction && i.Kind == kind);
                var automatic = intent.Orders.Single();
                var cancelAutomatic = cancel with { Request = Guid.NewGuid(), AssignmentRevision = head.Revision,
                    Target = automatic, HeadDecision = true };
                Assert.That(markets.TryConfigureSupply(sessions[0], cancelAutomatic, out _), Is.True);
                Assert.That(intent.Suspended, Is.True);
                var resume = cancelAutomatic with { Request = Guid.NewGuid(), Action = SWGlobalWarSupplyAction.ResumeIntent,
                    Target = intent.Id };
                Assert.That(markets.TryConfigureSupply(sessions[0], resume, out _), Is.True);
                var resumed = markets.SupplyJournal.Last();
                Assert.That(resumed.Before.Suspended, Is.True);
                Assert.That(resumed.After.Suspended, Is.False);
                var oldOfficeQuote = Quoted(1, purchase with { Request = Guid.NewGuid(),
                    Source = server.EntMan.GetNetEntity(restored), Quantity = 1 });
                Assert.That(registry.TryExecute(sessions[1], new(generation, Guid.NewGuid(),
                    SWGlobalWarAppointmentAction.Resign, war.Revision, war.User, war.Revision), out _), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[1], purchase, out _), Is.False);
                Assert.That(markets.TryConfigureSupply(sessions[1], cancel, out _), Is.False);
                Assert.That(markets.GetSupplyView(war), Is.Null);
                Assert.That(markets.GetSupplyResults(war), Is.Empty);
                Assert.That(markets.GetSupplyView(head)!.WarHeadDecisionRequired, Is.False);
                purchase = purchase with { Request = Guid.NewGuid(), AssignmentRevision = head.Revision,
                    ExpectedRevision = accounts.Comp.Revision, Quantity = 1, Source = server.EntMan.GetNetEntity(restored),
                    HeadDecision = false };
                purchase = Quoted(0, purchase);
                Assert.That(markets.TryConfigureSupply(sessions[0], purchase, out _), Is.True);
                Assert.That(markets.TryConfigureSupply(sessions[0], purchase with { Request = Guid.NewGuid(),
                    Action = SWGlobalWarSupplyAction.CancelPurchase, Target = purchase.Request }, out _), Is.True);
                Assert.That(registry.TryGetAssignment(war.User, out _), Is.False);
                Assert.That(registry.TryExecute(sessions[0], new(generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Offer,
                    head.Revision, war.User, 0, "SWPoliticsTestWar"), out var offer), Is.True);
                Assert.That(registry.TryExecute(sessions[1], new(generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Accept,
                    0, head.User, head.Revision, Offer: offer), out _), Is.True);
                var reappointed = Assignment(1);
                Assert.That(reappointed.Role, Is.EqualTo(SWGlobalWarRole.WarMinister));
                Assert.That(reappointed.Id, Is.Not.EqualTo(war.Id));
                DeniedQuote(1, oldOfficeQuote with { AssignmentRevision = reappointed.Revision });
                ticker.EndGameRule(campaign.Owner);
                Assert.That(markets.TryConfigureSupply(sessions[0], purchase, out _), Is.False);

                SWGlobalWarAssignment Assignment(int index)
                {
                    Assert.That(registry.TryGetAssignment(sessions[index].UserId, out var assignment), Is.True);
                    return assignment;
                }
                SWGlobalWarSupplyCommand Quoted(int index, SWGlobalWarSupplyCommand request)
                {
                    var preview = request with { Request = Guid.NewGuid(), Action = SWGlobalWarSupplyAction.PreviewPurchase };
                    Assert.That(markets.TryConfigureSupply(sessions[index], preview, out var quote), Is.True);
                    Assert.That(quote.Quote!.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.None));
                    return request with { Quote = preview.Request, ExpectedRevision = quote.Quote.LedgerRevision };
                }
                void DeniedQuote(int index, SWGlobalWarSupplyCommand request)
                {
                    var receipts = model.ExternalReservationCount;
                    var pendingInputs = model.ExternalPendingReservationCount;
                    var balance = ledger.GetBalance(war.Faction, SWGlobalWarAccount.War);
                    var orders = model.SupplyPrograms[war.Faction].Orders.Count;
                    var journal = markets.SupplyJournal.Count;
                    Assert.That(markets.TryConfigureSupply(sessions[index], request with { Request = Guid.NewGuid() }, out var denied), Is.False);
                    Assert.That(denied.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.Revision));
                    Assert.That(model.ExternalReservationCount, Is.EqualTo(receipts));
                    Assert.That(model.ExternalPendingReservationCount, Is.EqualTo(pendingInputs));
                    Assert.That(ledger.GetBalance(war.Faction, SWGlobalWarAccount.War), Is.EqualTo(balance));
                    Assert.That(model.SupplyPrograms[war.Faction].Orders.Count, Is.EqualTo(orders));
                    Assert.That(markets.SupplyJournal.Count, Is.EqualTo(journal));
                }
                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign),
                        timing.TimeBase.Item2);
                    markets.Update(0);
                }
                void ChangeOwner(SWGlobalWarFaction owner)
                {
                    var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet.Value);
                    var revision = state.OwnershipRevision;
                    foreach (var location in new[] { SWGlobalWarPlanetLocation.Surface, SWGlobalWarPlanetLocation.Orbit })
                        Assert.That(steps.TryQueueOwnership(planet.Value, location, owner, revision++, generation,
                            Guid.NewGuid()), Is.True);
                    Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                }
            }
            finally
            {
                probe.LedgerChanged = null;
                if (clock.TryGetCampaign(out var active))
                    ticker.EndGameRule(active.Owner);
                timing.TimeBase = savedTime;
                ticker.SetGamePreset(originalPreset);
            }
        }
    }
}
