using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Server.Administration.Managers;
using Content.Shared._SW;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Console;
using Content.Shared.GameTicking;
using Content.Server.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Administration.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared._SW.TaskSystem;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarPoliticsTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: job
  id: SWPoliticsTestHead
  name: job-name-captain
  playTimeTracker: SWPoliticsTestHeadTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestCivilian
  name: job-name-captain
  playTimeTracker: SWPoliticsTestCivilianTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestWar
  name: job-name-captain
  playTimeTracker: SWPoliticsTestWarTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestLogistics
  requirements:
  - !type:SWSpeciesJobRequirement
    allowedSpecies: [Human]
  name: job-name-captain
  playTimeTracker: SWPoliticsTestLogisticsTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestIndustry
  name: job-name-captain
  playTimeTracker: SWPoliticsTestIndustryTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestWorker
  name: job-name-captain
  playTimeTracker: SWPoliticsTestWorkerTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestForce
  name: job-name-captain
  playTimeTracker: SWPoliticsTestForceTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestSoldier
  name: job-name-captain
  playTimeTracker: SWPoliticsTestSoldierTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: job
  id: SWPoliticsTestSenior
  name: job-name-captain
  playTimeTracker: SWPoliticsTestSeniorTracker
  setPreference: true
  startingGear: CaptainGear
  icon: JobIconCaptain

- type: playTimeTracker
  id: SWPoliticsTestHeadTracker

- type: playTimeTracker
  id: SWPoliticsTestCivilianTracker

- type: playTimeTracker
  id: SWPoliticsTestWarTracker

- type: playTimeTracker
  id: SWPoliticsTestLogisticsTracker

- type: playTimeTracker
  id: SWPoliticsTestIndustryTracker

- type: playTimeTracker
  id: SWPoliticsTestWorkerTracker

- type: playTimeTracker
  id: SWPoliticsTestForceTracker

- type: playTimeTracker
  id: SWPoliticsTestSoldierTracker

- type: playTimeTracker
  id: SWPoliticsTestSeniorTracker

- type: entity
  parent: SWSpawnTestRule
  id: SWPoliticsTestRule
  components:
  - type: SWGlobalWarCampaign
    participantAbsenceDuration: 14400
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWPoliticsTestHead: {faction: Republic, role: Head}
      SWPoliticsTestCivilian: {faction: Republic, role: Civilian}
      SWPoliticsTestWar: {faction: Republic, role: WarMinister}
      SWPoliticsTestLogistics: {faction: Republic, role: LogisticsMinister}
      SWPoliticsTestIndustry: {faction: Republic, role: IndustryMinister}
      SWPoliticsTestWorker: {faction: Republic, role: IndustryWorker}
      SWPoliticsTestForce: {faction: Republic, role: ForceUser, force: true}
      SWPoliticsTestSoldier: {faction: Republic, role: Soldier}
      SWPoliticsTestSenior: {faction: Republic, role: SeniorOfficer}
    stations:
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWPoliticsTestHead: [20, 20]
            SWPoliticsTestCivilian: [20, 20]
            SWPoliticsTestWar: [20, 20]
            SWPoliticsTestLogistics: [20, 20]
            SWPoliticsTestIndustry: [20, 20]
            SWPoliticsTestWorker: [20, 20]
            SWPoliticsTestForce: [20, 20]
            SWPoliticsTestSoldier: [20, 20]
            SWPoliticsTestSenior: [20, 20]
    civilianRoles:
      Republic: SWPoliticsTestCivilian

- type: gamePreset
  id: SWPoliticsTestPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWPoliticsTestRule]
""";

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedSpawnReleasesReservationAndOldGenerationCannotReleaseSuccessor(bool throwDuringSpawn)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var preset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var player = server.PlayerMan.Sessions.Single();
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        var sawReservation = false;
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWPoliticsTestPreset");
            ticker.ToggleReadyAll(false);
            ticker.StartRound();
            Assert.That(spawn.TryGetRoleStation(SWGlobalWarFaction.Republic, out var station), Is.True);
            probe.BeforeValidation = (ref SWGlobalWarValidateSpawnEvent args) =>
            {
                Assert.That(registry.TryGetRegistry(out var state), Is.True);
                Assert.That(state.Comp.Reservations.Count, Is.EqualTo(1));
                sawReservation = true;
                if (!throwDuringSpawn)
                    args.Cancelled = true;
            };
            probe.ThrowDuringSpawn = throwDuringSpawn;
            if (throwDuringSpawn)
                Assert.Throws<InvalidOperationException>(() => ticker.MakeJoinGame(player, station, "SWPoliticsTestHead"));
            else
                ticker.MakeJoinGame(player, station, "SWPoliticsTestHead");
            Assert.That(sawReservation, Is.True);
            Assert.That(registry.TryGetRegistry(out var current), Is.True);
            Assert.That(current.Comp.Reservations, Is.Empty);
            Assert.That(current.Comp.Assignments, Is.Empty);
            Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.Head), Is.EqualTo(1));
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.Zero);
            var oldGeneration = current.Comp.Generation;
            Assert.That(registry.TryReserveEntry(oldGeneration, new NetUserId(Guid.NewGuid()),
                "SWPoliticsTestWorker", out var abandoned), Is.True);
            ticker.EndGameRule(current.Owner);
            Assert.That(current.Comp.Reservations, Is.Empty);
            probe.BeforeValidation = null;
            probe.ThrowDuringSpawn = false;
            Assert.That(ticker.StartGameRule("SWPoliticsTestRule"), Is.True);
            Assert.That(registry.TryGetRegistry(out var next), Is.True);
            Assert.That(next.Comp.Generation, Is.Not.EqualTo(oldGeneration));
            Assert.That(registry.TryReserveEntry(next.Comp.Generation, new NetUserId(Guid.NewGuid()),
                "SWPoliticsTestWorker", out var valid), Is.True);
            registry.ReleaseEntry(oldGeneration, valid.Id);
            registry.ReleaseEntry(oldGeneration, abandoned.Id);
            Assert.That(next.Comp.Reservations.Count, Is.EqualTo(1));
            registry.ReleaseEntry(next.Comp.Generation, valid.Id);
            Assert.That(next.Comp.Reservations, Is.Empty);
            Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
            Assert.That(accounts.Comp.Journal.Count, Is.EqualTo(8));
            Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Treasury).Balance,
                Is.EqualTo(450_000));
            ticker.RestartRound();
            ticker.SetGamePreset(preset);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("appointments")]
    [TestCase("personnel-ui")]
    [TestCase("succession")]
    [TestCase("identity-and-force")]
    [TestCase("ledger")]
    [TestCase("ledger-ui")]
    [TestCase("ledger-reasons")]
    [TestCase("access")]
    [TestCase("timers")]
    [TestCase("disconnect")]
    [TestCase("disconnect-minister")]
    [TestCase("disconnect-worker")]
    [TestCase("disconnect-force")]
    [TestCase("force-death")]
    [TestCase("offer-authority")]
    [TestCase("offer-recipient-dead")]
    [TestCase("offer-recipient-visit")]
    public async Task RealRoundAssignmentsAndAccounting(string scenario)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var jobs = server.System<StationJobsSystem>();
        var spawn = server.System<SWGlobalWarSpawnSystem>();
        var entities = server.EntMan;
        var networkUser = server.PlayerMan.Sessions.Single().UserId;
        await server.AddDummySessions(5);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var orderedSessions = server.PlayerMan.Sessions.OrderBy(s => s.UserId == networkUser ? 0 : 1).ToList();
        var disconnectIndex = scenario switch
        {
            "disconnect-minister" => 4,
            "disconnect-worker" => 5,
            "disconnect-force" => 3,
            _ => 0,
        };
        if (disconnectIndex != 0)
        {
            var networkSession = orderedSessions[0];
            orderedSessions.RemoveAt(0);
            orderedSessions.Insert(disconnectIndex, networkSession);
        }
        var sessions = orderedSessions.ToArray();
        string[] initial = ["Head", "Civilian", "Civilian", "Force", "War", "Worker"];
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile)server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(sessions[i].UserId).SelectedCharacter;
            await pair.SetJobPriorities(sessions[i].UserId, profile.JobPriorities.Keys
                .Select(j => (j, JobPriority.Never))
                .Append(((ProtoId<JobPrototype>)("SWPoliticsTest" + initial[i]), JobPriority.High)).ToArray());
        }
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWPoliticsTestPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(6));
            Assert.That(registry.TryGetRegistry(out var state), Is.True);
            Assert.That(state.Comp.Assignments.Count, Is.EqualTo(6));
            Assert.That(state.Comp.Reservations, Is.Empty);
            Assert.That(spawn.TryGetRoleStation(SWGlobalWarFaction.Republic, out var station), Is.True);
            foreach (var session in sessions)
            {
                Assert.That(registry.TryGetAssignment(session.UserId, out var assignment), Is.True);
                Assert.That(assignment.Body, Is.EqualTo(session.AttachedEntity));
                Assert.That(registry.TryGetActor(session, state.Comp.Generation, assignment.Revision, out _), Is.True);
                Assert.That(jobs.TryGetPlayerJobs(station, session.UserId, out var history), Is.True);
                Assert.That(history, Has.Count.EqualTo(1));
            }
            Assert.That(jobs.TryGetJobSlot(station, "SWPoliticsTestHead", out var headSlots), Is.True);
            Assert.That(headSlots, Is.Zero, "One registry appointment, no second decrement.");
            Assert.That(jobs.GetRoundStartJobs(station)["SWPoliticsTestHead"], Is.Zero,
                "Roundstart must not return the original twenty fixture slots.");
            Assert.That(jobs.GetRoundStartJobs(station)["SWPoliticsTestCivilian"], Is.Null);
            Assert.That(registry.TryGetAssignment(sessions[0].UserId, out var head), Is.True);
            Assert.That(registry.GetAuthority(head).HasFlag(SWGlobalWarAuthority.IndustryBudget), Is.True);
            Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.IndustryMinister), Is.EqualTo(1),
                "Combining duties does not occupy an office.");

            switch (scenario)
            {
                case "appointments":
                    Appointments();
                    break;
                case "personnel-ui":
                    PersonnelPresentation();
                    break;
                case "offer-authority":
                case "offer-recipient-dead":
                case "offer-recipient-visit":
                    InvalidatedOffer();
                    break;
                case "succession":
                    Succession();
                    break;
                case "identity-and-force":
                    IdentityAndForce();
                    break;
                case "force-death":
                    var dyingForce = Assignment(3);
                    Assert.That(registry.TryGetForceCarrier(dyingForce.User, out var originalCarrier), Is.True);
                    server.System<MobStateSystem>().ChangeMobState(dyingForce.Body, MobState.Dead);
                    Assert.That(registry.TryGetAssignment(dyingForce.User, out _), Is.False);
                    Assert.That(registry.TryGetForceCarrier(dyingForce.User, out var deadCarrier), Is.True);
                    Assert.That(deadCarrier, Is.EqualTo(originalCarrier));
                    Assert.That(registry.Available(dyingForce.Faction, SWGlobalWarRole.ForceUser), Is.Zero);
                    server.System<MobStateSystem>().ChangeMobState(dyingForce.Body, MobState.Alive);
                    Assert.That(registry.TryGetAssignment(dyingForce.User, out _), Is.False);
                    Assert.That(registry.TryGetForceCarrier(dyingForce.User, out _), Is.True);
                    break;
                case "ledger":
                    Accounting();
                    break;
                case "ledger-ui":
                    AccountingPresentation();
                    break;
                case "ledger-reasons":
                    AccountingReasons();
                    break;
                case "access":
                    Access();
                    break;
                case "timers":
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer,
                        1, "Senior"), out var seniorOffer), Is.True);
                    Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.Accept,
                        offer: seniorOffer), out _), Is.True);
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                    break;
            }

            if (scenario == "timers" || scenario.StartsWith("disconnect", StringComparison.Ordinal))
            {
                // The B fixture has no survivable atmosphere. Isolate office retention, not survival or combat.
                // World presence may wake these maps again, so map pause alone is not protection for the body.
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                foreach (var map in entities.EntityQuery<MapComponent>().ToArray())
                    server.System<MapSystem>().SetPaused(map.Owner, true);
            }

            SWGlobalWarAssignment Assignment(int index)
            {
                Assert.That(registry.TryGetAssignment(sessions[index].UserId, out var assignment), Is.True);
                return assignment;
            }

            SWGlobalWarAppointmentCommand Command(int index, SWGlobalWarAppointmentAction action,
                int target = 0, string? role = null, Guid offer = default, Guid vacancy = default, int stage = -1)
            {
                registry.TryGetAssignment(sessions[index].UserId, out var actor);
                registry.TryGetAssignment(sessions[target].UserId, out var recipient);
                return new SWGlobalWarAppointmentCommand(state.Comp.Generation, Guid.NewGuid(), action,
                    actor?.Revision ?? 0, sessions[target].UserId, recipient?.Revision ?? 0,
                    role == null ? (ProtoId<JobPrototype>?) null : (ProtoId<JobPrototype>)("SWPoliticsTest" + role),
                    offer, vacancy, stage);
            }

            void PersonnelPresentation()
            {
                SWGlobalWarCharacterView View(int index) => registry.GetCharacterView(sessions[index],
                    entities.GetNetEntity(sessions[index].AttachedEntity!.Value));
                var sender = Assignment(0);
                var recipient = Assignment(1);
                var originalEntities = entities.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet();
                var denied = registry.GetCharacterView(sessions[1], entities.GetNetEntity(sender.Body));
                Assert.That(denied.Availability, Is.EqualTo(SWGlobalWarViewAvailability.Forbidden));
                Assert.That(denied.Offers.Concat(View(2).Offers), Is.Empty);
                Assert.That(View(1).Targets, Is.Empty);
                Assert.That(View(0).Targets.Single(t => t.User == recipient.User).Revision, Is.EqualTo(recipient.Revision));
                var command = Command(0, SWGlobalWarAppointmentAction.Offer, 1, "Logistics");
                Assert.That(registry.TryExecute(sessions[0], command, out var offer), Is.True);
                Assert.That(Assignment(1), Is.EqualTo(recipient), "Offer creation does not appoint the recipient.");
                Assert.That(View(1).Offers.Single().SenderRole, Is.EqualTo(SWGlobalWarRole.Head));
                Assert.That(View(1).Offers.Single().Sender, Is.EqualTo(entities.GetComponent<MetaDataComponent>(sender.Body).EntityName));
                Assert.That(View(2).Offers, Is.Empty);
                Assert.That(View(1).RecentResults, Is.Empty, "The recipient cannot see the sender's receipt.");
                Assert.That(registry.TryExecute(sessions[0], command with { Target = sessions[2].UserId },
                    out _, out var changedPayload), Is.False);
                Assert.That(changedPayload, Is.EqualTo(SWGlobalWarAppointmentFailure.InvalidRequest));
                Assert.That(state.Comp.Offers.Count, Is.EqualTo(1));
                Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Accept, offer: offer),
                    out _, out var foreignOffer), Is.False);
                Assert.That(foreignOffer, Is.EqualTo(SWGlobalWarAppointmentFailure.OfferUnavailable));
                Assert.That(registry.TryReserveEntry(state.Comp.Generation, new NetUserId(Guid.NewGuid()),
                    "SWPoliticsTestLogistics", out var entry), Is.True);
                Assert.That(View(1).Offers.Single().Failure, Is.EqualTo(SWGlobalWarAppointmentFailure.RoleOccupied));
                var acceptance = Command(1, SWGlobalWarAppointmentAction.Accept, offer: offer);
                Assert.That(registry.TryExecute(sessions[1], acceptance, out _, out var occupied), Is.False);
                Assert.That(occupied, Is.EqualTo(SWGlobalWarAppointmentFailure.RoleOccupied));
                registry.ReleaseEntry(state.Comp.Generation, entry.Id);
                Assert.That(registry.TryExecute(sessions[1], acceptance, out _, out var replayed), Is.False);
                Assert.That(replayed, Is.EqualTo(occupied), "A receipt records the outcome at execution, not current availability.");
                var freshAccept = Command(1, SWGlobalWarAppointmentAction.Accept, offer: offer);
                Assert.That(registry.TryExecute(sessions[1], freshAccept, out _), Is.True);
                var appointed = Assignment(1);
                Assert.That(registry.TryExecute(sessions[1], freshAccept, out _), Is.True);
                Assert.That(Assignment(1), Is.EqualTo(appointed));
                Assert.That(appointed.Body, Is.EqualTo(recipient.Body));
                Assert.That(appointed.Mind, Is.EqualTo(recipient.Mind));
                Assert.That(View(0).Outgoing.Single().Status, Is.EqualTo(SWGlobalWarOfferStatus.Accepted));
                Assert.That(View(1).Offers, Is.Empty);
                Assert.That(View(2).Outgoing, Is.Empty);
                Assert.That(entities.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet(),
                    Is.EquivalentTo(originalEntities), "Appointment creates no body or equipment.");
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer, 2, "Industry"),
                    out var declineOffer), Is.True);
                var unchanged = Assignment(2);
                Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Decline,
                    offer: declineOffer), out _), Is.True);
                Assert.That(Assignment(2), Is.EqualTo(unchanged));
                Assert.That(View(0).Outgoing.Single(o => o.Id == declineOffer).Status,
                    Is.EqualTo(SWGlobalWarOfferStatus.Declined));
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer, 2, "Industry"),
                    out var staleOffer), Is.True);
                var captured = View(2).Offers.Single();
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                Assert.That(View(0).Targets, Is.Empty);
                Assert.That(View(0).Roles, Is.Empty);
                Assert.That(View(2).Offers.Single().SenderRole, Is.EqualTo(captured.SenderRole));
                Assert.That(View(2).Offers.Single().Sender, Is.EqualTo(captured.Sender));
                Assert.That(View(2).Offers.Single().Failure, Is.EqualTo(SWGlobalWarAppointmentFailure.AuthorityChanged));
                Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Accept,
                    offer: staleOffer), out _, out var authority), Is.False);
                Assert.That(authority, Is.EqualTo(SWGlobalWarAppointmentFailure.AuthorityChanged));
                server.System<SharedMindSystem>().Visit(unchanged.Mind, entities.SpawnEntity(null, MapCoordinates.Nullspace));
                Assert.That(registry.GetCharacterView(sessions[2], entities.GetNetEntity(unchanged.Body)).Offers, Is.Empty);
            }

            void Appointments()
            {
                var firstBody = sessions[1].AttachedEntity;
                var firstMind = Assignment(1).Mind;
                var offerCommand = Command(0, SWGlobalWarAppointmentAction.Offer, 1, "Logistics");
                var notifications = server.System<SWPoliticsNotificationProbeSystem>();
                var notifiedOffer = Guid.Empty;
                notifications.AssignmentsChanged = () =>
                {
                    notifications.AssignmentsChanged = null;
                    Assert.That(registry.TryExecute(sessions[0], offerCommand, out notifiedOffer), Is.True);
                };
                Assert.That(registry.TryExecute(sessions[0], offerCommand, out var offer), Is.True);
                Assert.That(notifiedOffer, Is.EqualTo(offer));
                Assert.That(registry.TryExecute(sessions[0], offerCommand, out var repeated), Is.True);
                Assert.That(repeated, Is.EqualTo(offer));
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer, 2, "Logistics"),
                    out var second), Is.True);
                Assert.That(registry.TryReserveEntry(state.Comp.Generation, new NetUserId(Guid.NewGuid()),
                    "SWPoliticsTestLogistics", out var entry), Is.True);
                Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.Accept, offer: offer),
                    out _), Is.False, "Latejoin's reserved last place wins before internal acceptance.");
                registry.ReleaseEntry(state.Comp.Generation, entry.Id);
                Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.Accept, offer: offer),
                    out _), Is.True);
                Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Accept, offer: second),
                    out _), Is.False);
                Assert.That(Assignment(1).Role, Is.EqualTo(SWGlobalWarRole.LogisticsMinister));
                Assert.That(sessions[1].AttachedEntity, Is.EqualTo(firstBody));
                Assert.That(Assignment(1).Mind, Is.EqualTo(firstMind));
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(6));
                Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.LogisticsMinister), Is.Zero);
                Assert.That(jobs.GetRoundStartJobs(station)["SWPoliticsTestLogistics"], Is.Zero);
                Assert.That(registry.TryExecute(sessions[1], offerCommand, out _), Is.False);
                var resign = Command(1, SWGlobalWarAppointmentAction.Resign);
                Assert.That(registry.TryExecute(sessions[1], resign, out _), Is.True);
                Assert.That(registry.TryExecute(sessions[1], resign, out _), Is.True);
                Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.LogisticsMinister), Is.EqualTo(1));
                Assert.That(registry.TryExecute(sessions[1], resign with { Action = SWGlobalWarAppointmentAction.ClaimHead },
                    out _), Is.False, "An idempotency key cannot be reused with different content.");
            }

            void InvalidatedOffer()
            {
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer,
                    1, "Logistics"), out var offered), Is.True);
                var acceptance = Command(1, SWGlobalWarAppointmentAction.Accept, offer: offered);
                var recipient = Assignment(1);
                var minds = server.System<SharedMindSystem>();
                if (scenario == "offer-authority")
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                else if (scenario == "offer-recipient-dead")
                    server.System<MobStateSystem>().ChangeMobState(recipient.Body, MobState.Dead);
                else
                    minds.Visit(recipient.Mind, entities.SpawnEntity(null, MapCoordinates.Nullspace));

                var afterInvalidation = state.Comp.Assignments.ToDictionary(p => p.Key, p => p.Value);
                var existingEntities = entities.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet();
                var attached = sessions[1].AttachedEntity;
                Assert.That(registry.TryExecute(sessions[1], acceptance, out _), Is.False);
                Assert.That(state.Comp.Assignments, Is.EquivalentTo(afterInvalidation),
                    "Acceptance cannot change the state produced by death, resignation or a mind visit.");
                Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.LogisticsMinister), Is.EqualTo(1));
                Assert.That(sessions[1].AttachedEntity, Is.EqualTo(attached));
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(6));
                Assert.That(entities.EntityQuery<MetaDataComponent>().Select(c => c.Owner).ToHashSet(), Is.EquivalentTo(existingEntities),
                    "No body or item is created by the rejected appointment.");
                if (scenario == "offer-recipient-dead")
                    Assert.That(registry.TryGetAssignment(recipient.User, out _), Is.False,
                        "Death already ended the former office before the rejected acceptance.");
                else if (scenario == "offer-recipient-visit")
                    minds.UnVisit(recipient.Mind);
            }

            void Succession()
            {
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                Assert.That(registry.TryGetSuccession(SWGlobalWarFaction.Republic, sessions[4].UserId,
                    out var vacancy, out var stage, out var remaining, out var eligible), Is.True);
                Assert.That(stage, Is.Zero);
                Assert.That(remaining, Is.EqualTo(TimeSpan.FromSeconds(30)));
                Assert.That(eligible, Is.True);
                Assert.That(registry.TryReserveEntry(state.Comp.Generation, new NetUserId(Guid.NewGuid()),
                    "SWPoliticsTestHead", out _), Is.False, "Latejoin cannot bypass a minister's priority window.");
                Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.ClaimHead,
                    vacancy: vacancy, stage: 0), out _), Is.False);
                Assert.That(registry.TryExecute(sessions[4], Command(4, SWGlobalWarAppointmentAction.DeclineSuccession,
                    vacancy: vacancy, stage: 0), out _), Is.True);
                Assert.That(registry.TryGetSuccession(SWGlobalWarFaction.Republic, sessions[1].UserId,
                    out var sameVacancy, out stage, out _, out eligible), Is.True);
                Assert.That(sameVacancy, Is.EqualTo(vacancy));
                Assert.That(stage, Is.EqualTo(2), "All ministers declined and no senior officer exists.");
                Assert.That(Assignment(4).Role, Is.EqualTo(SWGlobalWarRole.WarMinister));
                Assert.That(registry.TryExecute(sessions[4], Command(4, SWGlobalWarAppointmentAction.ClaimHead,
                    vacancy: vacancy, stage: 0), out _), Is.False, "Old priority consent cannot accept a new phase.");
                Assert.That(registry.TryExecute(sessions[4], Command(4, SWGlobalWarAppointmentAction.ClaimHead,
                    vacancy: vacancy, stage: 2), out _), Is.True, "Declining priority does not ban volunteering.");
                Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.ClaimHead,
                    vacancy: vacancy, stage: 2), out _), Is.False);
                Assert.That(registry.TryGetAssignment(sessions[0].UserId, out _), Is.False);
                Assert.That(Assignment(4).Role, Is.EqualTo(SWGlobalWarRole.Head));
                Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.WarMinister), Is.EqualTo(1));
            }

            void IdentityAndForce()
            {
                var force = Assignment(3);
                Assert.That(registry.TryGetForceCarrier(force.User, out var carrier), Is.True);
                Assert.That(registry.TryExecute(sessions[3], Command(3, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                Assert.That(registry.TryGetForceCarrier(force.User, out var same), Is.True);
                Assert.That(same, Is.EqualTo(carrier));
                Assert.That(registry.Available(force.Faction, SWGlobalWarRole.ForceUser), Is.Zero);
                server.System<MobStateSystem>().ChangeMobState(force.Body, MobState.Dead);
                Assert.That(registry.TryGetForceCarrier(force.User, out same), Is.True);
                Assert.That(same, Is.EqualTo(carrier));
                server.System<MobStateSystem>().ChangeMobState(force.Body, MobState.Alive);
                Assert.That(registry.TryGetAssignment(force.User, out _), Is.False);
                Assert.That(registry.Available(force.Faction, SWGlobalWarRole.ForceUser), Is.Zero);

                var civil = Assignment(1);
                var display = entities.GetComponent<SWFactionMemberComponent>(civil.Body);
                display.IsLeader = true;
                var forged = new SWGlobalWarLedgerCommand(state.Comp.Generation, Guid.NewGuid(), 8,
                    SWGlobalWarLedgerAction.Transfer, civil.Faction, SWGlobalWarAccount.Treasury, 1,
                    SWGlobalWarAccount.War);
                Assert.That(ledger.TryExecute(sessions[1], civil.Revision, forged, out _), Is.False);
                var visited = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                server.System<SharedMindSystem>().Visit(head.Mind, visited);
                Assert.That(registry.TryGetActor(sessions[0], state.Comp.Generation, head.Revision, out _), Is.False);
                server.System<SharedMindSystem>().UnVisit(head.Mind);
                server.System<MobStateSystem>().ChangeMobState(head.Body, MobState.Dead);
                Assert.That(registry.TryGetAssignment(head.User, out _), Is.False);
                server.System<MobStateSystem>().ChangeMobState(head.Body, MobState.Alive);
                Assert.That(registry.TryGetActor(sessions[0], state.Comp.Generation, head.Revision, out _), Is.False);
            }

            void AccountingReasons()
            {
                var generation = state.Comp.Generation;
                var faction = SWGlobalWarFaction.Republic;
                var account = SWGlobalWarAccount.Industry;
                var order = new SWGlobalWarLedgerCommand(generation, Guid.NewGuid(), 0,
                    SWGlobalWarLedgerAction.Reserve, faction, account, 120001,
                    Employee: sessions[5].UserId, Purpose: "Factory supplies");
                void Denied(SWGlobalWarLedgerCommand command, SWGlobalWarFinanceFailure expected)
                {
                    var before = ledger.GetBalance(faction, account);
                    Assert.That(ledger.TryExecute(sessions[0], head.Revision, command, out _, out var reason), Is.False);
                    Assert.That(reason, Is.EqualTo(expected));
                    Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(before));
                }
                Denied(order, SWGlobalWarFinanceFailure.InsufficientFunds);
                var original = ledger.GetOwnFinanceResult(sessions[0], generation, order.Request);
                Assert.That(original!.Failure, Is.EqualTo(SWGlobalWarFinanceFailure.InsufficientFunds));
                Assert.That(original.EffectAmount, Is.Zero);
                Assert.That(ledger.TryExecuteSystem(order with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarLedgerAction.Credit, Amount = 1, Employee = null,
                }, out _), Is.True);
                Denied(order, SWGlobalWarFinanceFailure.InsufficientFunds);
                Assert.That(ledger.GetOwnFinanceResult(sessions[0], generation, order.Request), Is.EqualTo(original),
                    "A repeated refusal keeps its recorded reason even after funds become sufficient.");
                Denied(order with { Amount = 1 }, SWGlobalWarFinanceFailure.InvalidRequest);
                Assert.That(ledger.GetOwnFinanceResult(sessions[0], generation, order.Request), Is.EqualTo(original));
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, order with { Request = Guid.NewGuid() },
                    out var reservation, out var success), Is.True);
                Assert.That(success, Is.EqualTo(SWGlobalWarFinanceFailure.None));
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, order with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarLedgerAction.Release,
                    Reservation = reservation, Amount = 0, Employee = null,
                }, out _), Is.True);

                Denied(order with { Request = Guid.NewGuid(), Amount = 0 }, SWGlobalWarFinanceFailure.InvalidAmount);
                Denied(order with { Request = Guid.NewGuid(), Amount = 1, Purpose = " " }, SWGlobalWarFinanceFailure.InvalidPurpose);
                Denied(order with { Request = Guid.NewGuid(), Amount = 1, Employee = sessions[1].UserId },
                    SWGlobalWarFinanceFailure.EmployeeChanged);
                Denied(order with
                {
                    Request = Guid.NewGuid(), Amount = 1, Action = SWGlobalWarLedgerAction.Transfer, Destination = account,
                }, SWGlobalWarFinanceFailure.SameAccount);
                Denied(order with
                {
                    Request = Guid.NewGuid(), Amount = 0, Action = SWGlobalWarLedgerAction.Release,
                    Reservation = Guid.NewGuid(),
                }, SWGlobalWarFinanceFailure.OrderUnavailable);
                Assert.That(ledger.TryExecute(sessions[5], Assignment(5).Revision, order, out _, out var workerReason), Is.False);
                Assert.That(workerReason, Is.EqualTo(SWGlobalWarFinanceFailure.Denied),
                    "An unauthorized actor must not learn the account's balance through a refusal.");
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                Denied(order, SWGlobalWarFinanceFailure.Denied);
            }

            void AccountingPresentation()
            {
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var generation = state.Comp.Generation;
                var faction = SWGlobalWarFaction.Republic;
                var account = SWGlobalWarAccount.Industry;
                var originalName = entities.GetComponent<MetaDataComponent>(head.Body).EntityName;
                SWGlobalWarLedgerCommand Money(SWGlobalWarLedgerAction action, long amount = 0, Guid reservation = default) =>
                    new(generation, Guid.NewGuid(), 0, action, faction, account, amount,
                        Reservation: reservation, Employee: action == SWGlobalWarLedgerAction.Reserve ? sessions[5].UserId : null,
                        Purpose: action == SWGlobalWarLedgerAction.Reserve ? "Factory supplies" : string.Empty);
                Guid ReserveAndSpend()
                {
                    Assert.That(ledger.TryExecute(sessions[0], head.Revision, Money(SWGlobalWarLedgerAction.Reserve, 30000),
                        out var id), Is.True);
                    Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit, 10000, id), out _), Is.True);
                    return id;
                }
                SWGlobalWarFinancePage Page(int user) => ledger.GetFinancePage(sessions[user], faction,
                    new SWGlobalWarFinancePageRequest(generation, Assignment(user).Revision, true));
                var first = ReserveAndSpend();
                var workerView = ledger.GetFinanceView(sessions[5], faction);
                Assert.That(workerView.Budgets, Is.Empty);
                var workerOrders = ledger.GetFinancePage(sessions[5], faction,
                    new SWGlobalWarFinancePageRequest(generation, Assignment(5).Revision, false));
                Assert.That(workerOrders.Orders.Single().Remaining, Is.EqualTo(20000));
                Assert.That(workerOrders.Orders.Single().CanRelease, Is.False);
                Assert.That(Page(5).Entries, Is.Empty, "A worker's order is not access to the ministry journal.");
                var release = Money(SWGlobalWarLedgerAction.Release, reservation: first);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, release, out _), Is.True);
                var receipt = ledger.GetOwnFinanceResult(sessions[0], generation, release.Request);
                Assert.That(receipt, Is.Not.Null);
                Assert.That(receipt!.EffectAmount, Is.EqualTo(20000));
                var entry = accounts.Comp.Journal.Single(e => e.Command.Request == release.Request);
                Assert.That(entry.Command.Amount, Is.Zero, "The immutable intent remains a release, not a second debit.");
                Assert.That(entry.EffectAmount, Is.EqualTo(20000));
                Assert.That(entry.AuthorName, Is.EqualTo(originalName));
                Assert.That(entry.AuthorRole, Is.EqualTo(SWGlobalWarRole.Head));
                Assert.That(Page(0).Entries.Single(e => e.Revision == entry.Revision).Amount, Is.EqualTo(20000));
                var count = accounts.Comp.Journal.Count;
                var balance = ledger.GetBalance(faction, account);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, release, out _), Is.True);
                Assert.That(ledger.GetOwnFinanceResult(sessions[0], generation, release.Request), Is.EqualTo(receipt));
                Assert.That(accounts.Comp.Journal.Count, Is.EqualTo(count));
                Assert.That(ledger.GetBalance(faction, account), Is.EqualTo(balance));

                var expired = ReserveAndSpend();
                var timing = server.ResolveDependency<IGameTiming>();
                var timeBase = timing.TimeBase;
                try
                {
                    timing.TimeBase = (timeBase.Item1 + TimeSpan.FromSeconds(901), timeBase.Item2);
                    ledger.Update(0);
                    var expiry = accounts.Comp.Journal.Single(e => e.Reservation == expired && e.Command.Action == SWGlobalWarLedgerAction.Release);
                    var expiryView = Page(0).Entries.Single(e => e.Revision == expiry.Revision);
                    Assert.That(expiryView.Amount, Is.EqualTo(20000));
                    Assert.That(expiryView.Kind, Is.EqualTo(SWGlobalWarFinanceEntryKind.OrderExpired));
                    Assert.That(expiryView.Purpose, Is.Empty);
                    Assert.That(expiryView.AuthorRole, Is.Null);
                }
                finally { timing.TimeBase = timeBase; }

                var ended = ReserveAndSpend();
                Assert.That(registry.TryExecute(sessions[5], Command(5, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                var endedEntry = accounts.Comp.Journal.Single(e => e.Reservation == ended && e.Command.Action == SWGlobalWarLedgerAction.Release);
                Assert.That(Page(0).Entries.Single(e => e.Revision == endedEntry.Revision).Amount, Is.EqualTo(20000));
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer, 2, "Industry"), out var offer), Is.True);
                Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Accept, offer: offer), out _), Is.True);
                server.System<MetaDataSystem>().SetEntityName(head.Body, "Changed name after the transaction");
                Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                var historic = Page(2).Entries.Single(e => e.Revision == entry.Revision);
                Assert.That(historic.AuthorName, Is.EqualTo(originalName));
                Assert.That(historic.AuthorRole, Is.EqualTo(SWGlobalWarRole.Head));
                Assert.That(historic.Amount, Is.EqualTo(20000));
                Assert.That(ledger.GetFinanceView(sessions[0], faction).Budgets, Is.Empty);
                var formerHead = ledger.GetFinancePage(sessions[0], faction,
                    new SWGlobalWarFinancePageRequest(generation, head.Revision, true));
                Assert.That(formerHead.Allowed, Is.False);
                Assert.That(formerHead.Entries, Is.Empty);
                Assert.That(ledger.GetFinancePage(sessions[2], faction,
                    new SWGlobalWarFinancePageRequest(Guid.NewGuid(), Assignment(2).Revision, true)).Allowed, Is.False);
            }

            void Accounting()
            {
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var generation = state.Comp.Generation;
                long Total() => Enum.GetValues<SWGlobalWarAccount>().Sum(a =>
                    ledger.GetBalance(SWGlobalWarFaction.Republic, a).Balance);
                SWGlobalWarLedgerCommand Money(SWGlobalWarLedgerAction action, SWGlobalWarAccount account,
                    long amount = 0, SWGlobalWarAccount to = SWGlobalWarAccount.Treasury,
                    Guid reserve = default, NetUserId? employee = null) =>
                    new(generation, Guid.NewGuid(), accounts.Comp.Revision, action, SWGlobalWarFaction.Republic,
                        account, amount, to, reserve, employee, "test-industry-order");
                Assert.That(Total(), Is.EqualTo(900_000));
                var transfer = Money(SWGlobalWarLedgerAction.Transfer, SWGlobalWarAccount.Treasury, 200_000,
                    SWGlobalWarAccount.Industry);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, transfer, out _), Is.True);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, transfer, out _), Is.True);
                Assert.That(Total(), Is.EqualTo(900_000));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry).Balance,
                    Is.EqualTo(320_000));
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, transfer with { Amount = 1 }, out _), Is.False);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision,
                    Money(SWGlobalWarLedgerAction.Transfer, SWGlobalWarAccount.Treasury, -1, SWGlobalWarAccount.War),
                    out _), Is.False);
                var order = Money(SWGlobalWarLedgerAction.Reserve, SWGlobalWarAccount.Industry, 300_000,
                    employee: sessions[5].UserId);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, order, out var reservation), Is.True,
                    "Thirty thousand is a default, not a maximum.");
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry),
                    Is.EqualTo(new SWGlobalWarAccountBalance(320_000, 300_000)));
                Assert.That(ledger.TryExecute(sessions[0], head.Revision,
                    Money(SWGlobalWarLedgerAction.Transfer, SWGlobalWarAccount.Industry, 20_001), out _), Is.False);
                Assert.That(ledger.CanSpendOrder(sessions[5], generation, Assignment(5).Revision,
                    reservation, 120_000, "test-industry-order"), Is.True);
                Assert.That(ledger.CanSpendOrder(sessions[1], generation, Assignment(1).Revision,
                    reservation, 1, "test-industry-order"), Is.False);
                var spend = Money(SWGlobalWarLedgerAction.Commit, SWGlobalWarAccount.Industry, 120_000, reserve: reservation);
                Assert.That(ledger.TryExecuteSystem(spend, out _), Is.True);
                Assert.That(ledger.TryExecuteSystem(spend, out _), Is.True);
                Assert.That(Total(), Is.EqualTo(780_000));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry),
                    Is.EqualTo(new SWGlobalWarAccountBalance(200_000, 180_000)));
                var cancel = Money(SWGlobalWarLedgerAction.Release, SWGlobalWarAccount.Industry, reserve: reservation);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, cancel, out _), Is.True);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, cancel, out _), Is.True);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry).Available,
                    Is.EqualTo(200_000));
                Assert.That(Total(), Is.EqualTo(780_000));

                // An independent Empire consumption event must not invalidate a Republic UI's observed revision.
                var staleSnapshot = Money(SWGlobalWarLedgerAction.Reserve, SWGlobalWarAccount.War, 120_000);
                var tax = Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Treasury, 50)
                    with { Faction = SWGlobalWarFaction.Empire };
                Assert.That(ledger.TryExecuteSystem(tax, out _), Is.True);
                Assert.That(ledger.TryExecuteSystem(tax, out _), Is.True);
                Assert.That(ledger.TryExecuteSystem(staleSnapshot, out var warReserve), Is.True);
                Assert.That(ledger.TryExecuteSystem(staleSnapshot, out var sameReserve), Is.True);
                Assert.That(sameReserve, Is.EqualTo(warReserve));
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Reserve,
                    SWGlobalWarAccount.War, 120_000), out _), Is.False);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Available,
                    Is.EqualTo(60_000));
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Release,
                    SWGlobalWarAccount.War, reserve: warReserve), out _), Is.True);

                var expiring = new List<Guid>();
                for (var i = 0; i < 2; i++)
                {
                    Assert.That(ledger.TryExecute(sessions[0], head.Revision,
                        Money(SWGlobalWarLedgerAction.Reserve, SWGlobalWarAccount.Industry, 2_000,
                            employee: sessions[5].UserId), out var expiringId), Is.True);
                    expiring.Add(expiringId);
                }
                var afterExpiration = Money(SWGlobalWarLedgerAction.Transfer, SWGlobalWarAccount.Treasury,
                    1, SWGlobalWarAccount.Industry);
                var timing = server.ResolveDependency<IGameTiming>();
                var timeBase = timing.TimeBase;
                var probe = server.System<SWPoliticsNotificationProbeSystem>();
                var callbacks = 0;
                try
                {
                    // Boundary injection, not an assertion about the real auto_pause_empty game loop.
                    timing.TimeBase = (timeBase.Item1 + TimeSpan.FromSeconds(900), timeBase.Item2);
                    probe.LedgerChanged = () =>
                    {
                        probe.LedgerChanged = null;
                        callbacks++;
                        Assert.That(expiring.All(id => !ledger.TryGetReservation(id, out _)), Is.True);
                        Assert.That(ledger.TryExecute(sessions[0], head.Revision, afterExpiration, out _), Is.True,
                            "A synchronous repeat observes the completed receipt, not an in-progress request.");
                        Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Credit,
                            SWGlobalWarAccount.Treasury, 1) with { Faction = SWGlobalWarFaction.Empire }, out _), Is.True);
                    };
                    Assert.That(ledger.TryExecute(sessions[0], head.Revision, afterExpiration, out _), Is.True);
                    Assert.That(callbacks, Is.EqualTo(1));
                    foreach (var id in expiring)
                    {
                        Assert.That(accounts.Comp.Journal.Count(e => e.Command.Action == SWGlobalWarLedgerAction.Release &&
                            e.Reservation == id), Is.EqualTo(1));
                        Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Commit,
                            SWGlobalWarAccount.Industry, 1, reserve: id), out _), Is.False);
                    }
                    Assert.That(accounts.Comp.Journal.Count(e => e.Command.Request == afterExpiration.Request), Is.EqualTo(1));
                    Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry).Reserved, Is.Zero);
                }
                finally
                {
                    timing.TimeBase = timeBase;
                    probe.LedgerChanged = null;
                }

                var next = Money(SWGlobalWarLedgerAction.Reserve, SWGlobalWarAccount.Industry, 30_000,
                    employee: sessions[5].UserId);
                Assert.That(ledger.TryExecute(sessions[0], head.Revision, next, out _), Is.True);
                Assert.That(registry.TryExecute(sessions[5], Command(5, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.Industry).Reserved, Is.Zero);
                var overflow = Money(SWGlobalWarLedgerAction.Credit, SWGlobalWarAccount.Industry, long.MaxValue);
                Assert.That(ledger.TryExecuteSystem(overflow, out _), Is.False);
                Assert.That(ledger.TryExecuteSystem(Money(SWGlobalWarLedgerAction.Credit,
                    SWGlobalWarAccount.Industry, 1) with { Generation = Guid.NewGuid() }, out _), Is.False);
                Assert.That(Total(), Is.EqualTo(780_000));
            }

            void Access()
            {
                var bans = server.ResolveDependency<IBanManager>();
                var cache = (Dictionary<ICommonSession, List<ServerRoleBanDef>>) typeof(BanManager)
                    .GetField("_cachedRoleBans", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bans)!;
                var originals = sessions.ToDictionary(s => s, s => cache[s].ToList());
                var prefs = server.ResolveDependency<IServerPreferencesManager>();
                var originalProfile = (HumanoidCharacterProfile)prefs.GetPreferences(sessions[1].UserId).SelectedCharacter;
                var logistics = server.ProtoMan.Index<JobPrototype>("SWPoliticsTestLogistics");
                var industry = server.ProtoMan.Index<JobPrototype>("SWPoliticsTestIndustry");
                var oldWhitelist = industry.Whitelisted;
                var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
                var whitelist = server.CfgMan.GetCVar(CCVars.GameRoleWhitelist);
                void Ban(int index, string job)
                {
                    cache[sessions[index]].Add(new ServerRoleBanDef(null, sessions[index].UserId, null, null,
                        DateTimeOffset.UtcNow, null, null, TimeSpan.Zero, "Campaign eligibility test", NoteSeverity.High,
                        null, null, "Job:SWPoliticsTest" + job));
                }
                try
                {
                    Ban(1, "Logistics");
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer,
                        1, "Logistics"), out _), Is.False);
                    cache[sessions[1]].Clear();
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer,
                        2, "Logistics"), out var pending), Is.True);
                    Ban(2, "Logistics");
                    Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.Accept,
                        offer: pending), out _), Is.False);
                    Assert.That(Assignment(2).Role, Is.EqualTo(SWGlobalWarRole.Civilian));
                    Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.LogisticsMinister), Is.EqualTo(1));

                    server.CfgMan.SetCVar(CCVars.GameRoleTimers, true);
                    var body = sessions[1].AttachedEntity!.Value;
                    var appearance = server.System<SharedHumanoidAppearanceSystem>();
                    appearance.SetSpecies(body, "Human");
                    prefs.SetProfile(sessions[1].UserId, 0, originalProfile.WithSpecies("Zeltron")).Wait();
                    Assert.That(registry.CanAssumeCampaignJob(sessions[1], body, "SWPoliticsTestLogistics"), Is.True);
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Offer,
                        1, "Logistics"), out var humanOffer), Is.True);
                    Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.Accept,
                        offer: humanOffer), out _), Is.True);
                    Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                    prefs.SetProfile(sessions[1].UserId, 0, originalProfile.WithSpecies("Human")).Wait();
                    appearance.SetSpecies(body, "Zeltron");
                    Assert.That(registry.CanAssumeCampaignJob(sessions[1], body, "SWPoliticsTestLogistics"), Is.False);
                    Assert.That(registry.CanAssumeCampaignJob(sessions[1], body, "SWPoliticsTestIndustry"), Is.True,
                        "A job without a species gate must not inherit one from another role.");
                    server.CfgMan.SetCVar(CCVars.GameRoleWhitelist, true);
                    industry.Whitelisted = true;
                    Assert.That(registry.CanAssumeCampaignJob(sessions[1], body, "SWPoliticsTestIndustry"), Is.False);
                    appearance.SetSpecies(body, "Human");

                    Ban(4, "Head");
                    Ban(1, "Head");
                    Assert.That(registry.TryExecute(sessions[0], Command(0, SWGlobalWarAppointmentAction.Resign), out _), Is.True);
                    Assert.That(registry.TryGetSuccession(SWGlobalWarFaction.Republic, sessions[4].UserId,
                        out var vacancy, out var stage, out _, out _), Is.True);
                    Assert.That(stage, Is.EqualTo(2), "An ineligible minister cannot hold the priority window.");
                    Assert.That(registry.TryExecute(sessions[4], Command(4, SWGlobalWarAppointmentAction.ClaimHead,
                        vacancy: vacancy, stage: stage), out _), Is.False);
                    Assert.That(registry.TryExecute(sessions[1], Command(1, SWGlobalWarAppointmentAction.ClaimHead,
                        vacancy: vacancy, stage: stage), out _), Is.False);
                    Assert.That(registry.TryExecute(sessions[2], Command(2, SWGlobalWarAppointmentAction.ClaimHead,
                        vacancy: vacancy, stage: stage), out _), Is.True);
                }
                finally
                {
                    foreach (var (session, entries) in originals)
                        cache[session] = entries;
                    prefs.SetProfile(sessions[1].UserId, 0, originalProfile).Wait();
                    industry.Whitelisted = oldWhitelist;
                    server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                    server.CfgMan.SetCVar(CCVars.GameRoleWhitelist, whitelist);
                }
            }
        });
        if (scenario == "timers")
        {
            await pair.RunSeconds(29);
            await server.WaitAssertion(() => CheckStage(0));
            await pair.RunSeconds(2);
            await server.WaitAssertion(() => CheckStage(1));
            await pair.RunSeconds(30);
            await server.WaitAssertion(() => CheckStage(2));
        }
        if (scenario.StartsWith("disconnect", StringComparison.Ordinal))
        {
            var name = sessions[disconnectIndex].Name;
            var user = sessions[disconnectIndex].UserId;
            var body = sessions[disconnectIndex].AttachedEntity;
            SWGlobalWarAssignment originalAssignment = default!;
            SWGlobalWarForceCarrier? originalCarrier = null;
            TimeSpan disconnectedAt = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(registry.TryGetAssignment(user, out originalAssignment), Is.True);
                if (disconnectIndex == 3)
                {
                    Assert.That(registry.TryGetForceCarrier(user, out var carrier), Is.True);
                    originalCarrier = carrier;
                }
            });
            await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Head reserve test"));
            await pair.RunTicksSync(5);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<SWGlobalWarCampaignSystem>().TryGetCampaign(out var campaign), Is.True);
                disconnectedAt = server.System<SWGlobalWarCampaignSystem>().Elapsed(campaign);
            });
            await pair.RunSeconds(disconnectIndex == 0 ? 119 : 121);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<SWGlobalWarCampaignSystem>().TryGetCampaign(out var campaign), Is.True);
                var elapsed = server.System<SWGlobalWarCampaignSystem>().Elapsed(campaign);
                var mobState = entities.TryGetComponent<MobStateComponent>(body, out var mob) ? mob.CurrentState : (MobState?) null;
                var alive = mobState?.ToString() ?? "missing";
                TestContext.Out.WriteLine($"Retention user={user}, role={originalAssignment.Role}, elapsed={elapsed}, disconnectedAt={disconnectedAt}, body={body}, state={alive}");
                Assert.That(alive, Is.EqualTo("Alive"));
                Assert.That(registry.TryGetAssignment(user, out var current), Is.True);
                Assert.That(current, Is.EqualTo(originalAssignment));
                if (originalCarrier != null)
                {
                    Assert.That(registry.TryGetForceCarrier(user, out var carrier), Is.True);
                    Assert.That(carrier, Is.EqualTo(originalCarrier));
                }
            });
            pair.Client.SetConnectTarget(server);
            await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, name));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                var returned = server.PlayerMan.Sessions.Single(s => s.UserId == user);
                Assert.That(returned.AttachedEntity, Is.EqualTo(body));
                Assert.That(registry.TryGetAssignment(user, out var current), Is.True);
                Assert.That(current, Is.EqualTo(originalAssignment));
            });
            if (disconnectIndex == 0)
            {
                await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientDisconnect("Head timeout test"));
                await pair.RunTicksSync(5);
                await pair.RunSeconds(121);
                await server.WaitAssertion(() =>
                {
                    Assert.That(registry.TryGetAssignment(user, out _), Is.False);
                    foreach (var other in sessions.Skip(1))
                        Assert.That(registry.TryGetAssignment(other.UserId, out _), Is.True);
                    Assert.That(registry.Available(SWGlobalWarFaction.Republic, SWGlobalWarRole.ForceUser), Is.Zero);
                });
                pair.Client.SetConnectTarget(server);
                await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, name));
                await pair.RunTicksSync(10);
                await server.WaitAssertion(() => Assert.That(registry.TryGetAssignment(user, out _), Is.False));
            }
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();

        void CheckStage(int expected)
        {
            Assert.That(registry.TryGetSuccession(SWGlobalWarFaction.Republic, sessions[1].UserId,
                out _, out var stage, out _, out _), Is.True);
            Assert.That(stage, Is.EqualTo(expected));
            Assert.That(registry.TryGetAssignment(sessions[4].UserId, out var minister), Is.True);
            Assert.That(minister.Role, Is.EqualTo(SWGlobalWarRole.WarMinister), "Silence is not resignation.");
            if (expected < 2)
            {
                Assert.That(registry.TryGetRegistry(out var state), Is.True);
                Assert.That(registry.TryReserveEntry(state.Comp.Generation, new NetUserId(Guid.NewGuid()),
                    "SWPoliticsTestHead", out _), Is.False);
            }
        }
    }
}

/// <summary>Exercises reentrant subscribers without adding a production-only test seam.</summary>
public sealed class SWPoliticsNotificationProbeSystem : EntitySystem
{
    public Action? LedgerChanged;
    public Action? AssignmentsChanged;
    public Action? PoliticsChanged;
    public Action<SWGlobalWarOwnershipChangedEvent>? OwnershipChanged;
    public Action<SWGlobalWarCampaignOutcomeEvent>? Outcome;
    public Action? RoundEnd;
    public Action<EntityUid>? FtlCompleted;
    public Action<EntityUid>? DoorChecked;
    public delegate void Validation(ref SWGlobalWarValidateSpawnEvent args);
    public Validation? BeforeValidation;
    public bool ThrowDuringSpawn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarLedgerChangedEvent>((ref SWGlobalWarLedgerChangedEvent args) => LedgerChanged?.Invoke());
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent args) => AssignmentsChanged?.Invoke());
        SubscribeLocalEvent<SWGlobalWarPoliticsChangedEvent>((ref SWGlobalWarPoliticsChangedEvent args) => PoliticsChanged?.Invoke());
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>((ref SWGlobalWarOwnershipChangedEvent args) => OwnershipChanged?.Invoke(args));
        SubscribeLocalEvent<SWGlobalWarCampaignOutcomeEvent>((ref SWGlobalWarCampaignOutcomeEvent args) => Outcome?.Invoke(args));
        SubscribeLocalEvent<RoundEndTextAppendEvent>(_ => RoundEnd?.Invoke());
        SubscribeLocalEvent<SWWithdrawalFtlProbeComponent, Content.Server.Shuttles.Events.FTLCompletedEvent>((uid, _, _) => FtlCompleted?.Invoke(uid));
        SubscribeLocalEvent<SWWithdrawalDoorProbeComponent, Content.Shared.Doors.BeforeDoorOpenedEvent>((uid, _, _) => DoorChecked?.Invoke(uid));
        SubscribeLocalEvent<SWGlobalWarValidateSpawnEvent>((ref SWGlobalWarValidateSpawnEvent args) =>
            BeforeValidation?.Invoke(ref args), after: [typeof(SWGlobalWarAssignmentsSystem)]);
        SubscribeLocalEvent<PlayerSpawningEvent>(args =>
        {
            if (ThrowDuringSpawn)
                throw new InvalidOperationException("Intentional campaign spawn failure.");
        }, before: [typeof(SWGlobalWarSpawnSystem)]);
    }
}

[RegisterComponent]
public sealed partial class SWWithdrawalFtlProbeComponent : Component;

[RegisterComponent]
public sealed partial class SWWithdrawalDoorProbeComponent : Component;
