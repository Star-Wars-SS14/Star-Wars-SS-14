#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarAdmissionTest
{
    [TestPrototypes]
    private static readonly string Prototypes = BuildPrototypes();

    private static string BuildPrototypes()
    {
        var jobs = string.Join("\n", new[] { "FullR", "FullE", "CivilR", "CivilE", "WeightedR", "RestrictedR", "RestrictedE" }
            .Select(id => $"""
- type: job
  id: SWAdmission{id}
  name: job-name-passenger
  playTimeTracker: SWAdmission{id}Tracker
  setPreference: true
  weight: {(id == "WeightedR" ? 20 : 0)}
  startingGear: CaptainGear
  icon: JobIconCaptain
{(id.StartsWith("Restricted", System.StringComparison.Ordinal) ? "  requirements:\n  - !type:OverallPlaytimeRequirement\n    time: 999h" : "")}

- type: playTimeTracker
  id: SWAdmission{id}Tracker

"""));
        return jobs + """

- type: entity
  parent: SWSpawnTestRule
  id: SWAdmissionRule
  components:
  - type: SWGlobalWarSpawnConfiguration
    civilianRoles: {Republic: SWAdmissionCivilR, Empire: SWAdmissionCivilE}
    roles:
      SWSpawnTestRepublic: {faction: Republic}
      SWSpawnTestEmpire: {faction: Empire}
      SWSpawnTestForceRole: {faction: Republic, force: true}
      SWAdmissionFullR: {faction: Republic}
      SWAdmissionFullE: {faction: Empire}
      SWAdmissionCivilR: {faction: Republic}
      SWAdmissionCivilE: {faction: Empire}
      SWAdmissionWeightedR: {faction: Republic}
      SWAdmissionRestrictedR: {faction: Republic}
      SWAdmissionRestrictedE: {faction: Empire}
    stations:
      Empire:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWSpawnTestEmpire: [1, 1]
            SWAdmissionFullE: [0, 0]
            SWAdmissionCivilE: [1, 1]
            SWAdmissionRestrictedE: [1, 1]
      Republic:
        stationProto: SWSpawnTestRoleStation
        components:
        - type: StationJobs
          availableJobs:
            SWSpawnTestRepublic: [1, 1]
            SWSpawnTestForceRole: [1, 1]
            SWAdmissionFullR: [0, 0]
            SWAdmissionCivilR: [2, 2]
            SWAdmissionWeightedR: [1, 1]
            SWAdmissionRestrictedR: [1, 1]

- type: entity
  parent: SWAdmissionRule
  id: SWAdmissionRestrictedRule
  components:
  - type: SWGlobalWarSpawnConfiguration
    civilianRoles: {Republic: SWAdmissionRestrictedR, Empire: SWAdmissionCivilE}

- type: gamePreset
  id: SWAdmissionPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWAdmissionRule]

- type: gamePreset
  id: SWAdmissionRestrictedPreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWAdmissionRestrictedRule]
""";
    }

    [TestCase("empty", null)]
    [TestCase("common", null)]
    [TestCase("one-side", "SWAdmissionCivilR")]
    [TestCase("mixed", null)]
    [TestCase("mixed-filtered", null)]
    [TestCase("stay", null)]
    [TestCase("civilian", "SWAdmissionCivilR")]
    [TestCase("priority", "SWSpawnTestEmpire")]
    [TestCase("weight", "SWAdmissionWeightedR")]
    [TestCase("lock", "SWSpawnTestRepublic")]
    [TestCase("restricted", null)]
    [TestCase("no-point", null)]
    public async Task RoundAdmissionPreservesPrioritiesRestrictionsAndFactionConsent(string scenario, string? expectedJob)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var player = server.PlayerMan.Sessions.Single();
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var probe = server.System<SWGlobalWarSpawnFixtureSystem>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var profile = (HumanoidCharacterProfile)prefs.GetPreferences(player.UserId).SelectedCharacter;
        var originalProfile = profile;
        var priorities = new Dictionary<ProtoId<JobPrototype>, JobPriority>();
        switch (scenario)
        {
            case "common": priorities["SWSpawnHookJob"] = JobPriority.High; break;
            case "mixed-filtered":
                priorities["SWAdmissionFullR"] = JobPriority.High;
                priorities["SWAdmissionRestrictedE"] = JobPriority.Low;
                break;
            case "mixed":
                priorities["SWAdmissionFullE"] = JobPriority.Low;
                priorities["SWAdmissionFullR"] = JobPriority.High;
                break;
            case "civilian": priorities["SWAdmissionCivilR"] = JobPriority.High; break;
            case "priority":
                priorities["SWSpawnTestRepublic"] = JobPriority.Medium;
                priorities["SWSpawnTestEmpire"] = JobPriority.High;
                break;
            case "weight":
                priorities["SWAdmissionWeightedR"] = JobPriority.Low;
                priorities["SWSpawnTestEmpire"] = JobPriority.High;
                break;
            case "lock":
                priorities["SWSpawnTestEmpire"] = JobPriority.High;
                priorities["SWSpawnTestRepublic"] = JobPriority.Medium;
                break;
            case "empty": break;
            default: priorities["SWAdmissionFullR"] = JobPriority.High; break;
        }
        profile = profile.WithJobPriorities(priorities).WithPreferenceUnavailable(
            scenario is "stay" or "civilian" ? PreferenceUnavailableMode.StayInLobby : PreferenceUnavailableMode.SpawnAsOverflow);
        await server.WaitPost(() => prefs.SetProfile(player.UserId, 0, profile).Wait());
        var oldTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(((HumanoidCharacterProfile)prefs.GetPreferences(player.UserId).SelectedCharacter).JobPriorities,
                    Is.EquivalentTo(priorities));
                probe.DeleteSpawnAnchors = scenario == "no-point";
                if (scenario == "lock")
                {
                    probe.BeforeRound = _ =>
                    {
                        Assert.That(server.System<SWGlobalWarGameruleSystem>().TryGetGlobalWarGamerule(out var rule), Is.True);
                        var minds = server.System<SharedMindSystem>();
                        var mind = minds.CreateMind(player.UserId);
                        Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind,
                            SWGlobalWarFaction.Republic, rule.Comp.Generation), Is.True);
                        minds.SetUserId(mind, null);
                        server.EntMan.DeleteEntity(mind.Owner);
                    };
                }
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, scenario is "restricted" or "mixed-filtered");
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset(scenario == "restricted" ? "SWAdmissionRestrictedPreset" : "SWAdmissionPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(expectedJob == null ? 0 : 1));
                var assigned = server.EntMan.EntityQuery<StationJobsComponent>()
                    .SelectMany(s => s.PlayerJobs.GetValueOrDefault(player.UserId) ?? new()).Select(j => j.Id).ToArray();
                Assert.That(assigned, Is.EquivalentTo(expectedJob == null ? new string[0] : new[] { expectedJob }));
                if (expectedJob == null)
                {
                    Assert.That(player.AttachedEntity, Is.Null);
                    Assert.That(player.ContentData()!.Mind, Is.Null);
                    Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetMembership(player.UserId, out _), Is.False);
                }
                else
                {
                    Assert.That(player.AttachedEntity, Is.Not.Null);
                    Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetActor(player, player.AttachedEntity!.Value,
                        out _, out var faction), Is.True);
                    Assert.That(faction, Is.EqualTo(expectedJob == "SWSpawnTestEmpire"
                        ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Republic));
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                probe.BeforeRound = null;
                probe.DeleteSpawnAnchors = false;
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, oldTimers);
                ticker.RestartRound();
                prefs.SetProfile(player.UserId, 0, originalProfile).Wait();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task BatchAllocationReservesFiniteCivilianPlacesAndTheSingleForceSlot(bool force)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        await server.AddDummySessions(2);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p)));
        }
        Assert.That(loaded, Is.True);
        var sessions = server.PlayerMan.Sessions.ToArray();
        var original = sessions.ToDictionary(p => p.UserId,
            p => (HumanoidCharacterProfile)prefs.GetPreferences(p.UserId).SelectedCharacter);
        try
        {
            for (var i = 0; i < sessions.Length; i++)
            {
                var user = sessions[i].UserId;
                var job = force ? "SWSpawnTestForceRole" : i == 0 ? "SWAdmissionCivilR" : "SWAdmissionFullR";
                var profile = original[user].WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
                {
                    [job] = JobPriority.High,
                }).WithPreferenceUnavailable(force ? PreferenceUnavailableMode.StayInLobby : PreferenceUnavailableMode.SpawnAsOverflow);
                await server.WaitPost(() => prefs.SetProfile(user, 0, profile).Wait());
            }
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWAdmissionPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                var expected = force ? 1 : 2;
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(expected));
                Assert.That(sessions.Count(p => p.AttachedEntity != null), Is.EqualTo(expected));
                Assert.That(server.System<SWGlobalWarSpawnSystem>().TryGetRoleStation(SWGlobalWarFaction.Republic,
                    out var station), Is.True);
                var jobs = server.System<StationJobsSystem>();
                var actualJob = force ? "SWSpawnTestForceRole" : "SWAdmissionCivilR";
                Assert.That(jobs.TryGetJobSlot(station, actualJob, out var slots), Is.True);
                Assert.That(slots, Is.Zero);
                foreach (var player in sessions)
                {
                    if (player.AttachedEntity == null)
                    {
                        Assert.That(player.ContentData()!.Mind, Is.Null);
                        Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out _), Is.False);
                    }
                    else
                    {
                        Assert.That(jobs.TryGetPlayerJobs(station, player.UserId, out var assigned), Is.True);
                        Assert.That(assigned!.Select(j => j.Id), Is.EquivalentTo(new[] { actualJob }));
                    }
                }
                if (!force)
                {
                    Assert.That(sessions[0].AttachedEntity, Is.Not.Null, "The explicitly selected civilian is allocated before fallback.");
                    Assert.That(sessions.Skip(1).Count(p => p.AttachedEntity != null), Is.EqualTo(1));
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                foreach (var (user, profile) in original)
                    prefs.SetProfile(user, 0, profile).Wait();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }

    [TestCase("full", true)]
    [TestCase("stay", false)]
    [TestCase("restricted-source", true)]
    [TestCase("restricted-civilian", false)]
    [TestCase("owner-flip", false)]
    public async Task ClientFallbackUsesChosenFactionRestrictionsAndFinalSpawnValidation(string scenario, bool succeeds)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var player = server.PlayerMan.Sessions.Single();
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var probe = server.System<SWGlobalWarSpawnFixtureSystem>();
        var original = (HumanoidCharacterProfile)prefs.GetPreferences(player.UserId).SelectedCharacter;
        var profile = original.WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>())
            .WithPreferenceUnavailable(scenario == "stay" ? PreferenceUnavailableMode.StayInLobby : PreferenceUnavailableMode.SpawnAsOverflow);
        await server.WaitPost(() => prefs.SetProfile(player.UserId, 0, profile).Wait());
        var oldTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        EntityUid station = default;
        var requested = scenario == "restricted-source" ? "SWAdmissionRestrictedR" : "SWAdmissionFullR";
        var civilian = scenario == "restricted-civilian" ? "SWAdmissionRestrictedR" : "SWAdmissionCivilR";
        var mutated = false;
        var originalSlots = 0;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, true);
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset(scenario == "restricted-civilian" ? "SWAdmissionRestrictedPreset" : "SWAdmissionPreset");
                ticker.ToggleReadyAll(false);
                ticker.StartRound();
                Assert.That(server.System<SWGlobalWarSpawnSystem>().TryGetRoleStation(SWGlobalWarFaction.Republic,
                    out station), Is.True);
                Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(station, civilian, out var slots), Is.True);
                originalSlots = slots!.Value;
                if (scenario == "owner-flip")
                {
                    probe.BeforeValidation = args =>
                    {
                        if (args.Player.UserId != player.UserId || args.Cancelled)
                            return;
                        Assert.That(args.Job, Is.EqualTo("SWAdmissionCivilR"));
                        var planets = server.System<SWGlobalWarPlanetSystem>();
                        Assert.That(planets.TryGetPlanet("SWSpawnTestRCapital", out var planet), Is.True);
                        Assert.That(server.System<SWGlobalWarGameruleSystem>().TryGetGlobalWarGamerule(out var rule), Is.True);
                        var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
                        Assert.That(planets.TryChangeOwnership(planet.Value, SWGlobalWarPlanetLocation.Orbit,
                            SWGlobalWarFaction.Empire, data.OwnershipRevision, rule.Comp.Generation, System.Guid.NewGuid(), out _), Is.True);
                        mutated = true;
                    };
                }
            });
            await pair.Client.WaitPost(() => pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand(
                $"joingame {requested} {server.EntMan.GetNetEntity(station).Id}"));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(succeeds ? 1 : 0));
                Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(station, civilian, out var slots), Is.True);
                Assert.That(slots, Is.EqualTo(originalSlots - (succeeds ? 1 : 0)));
                if (scenario == "owner-flip")
                    Assert.That(mutated, Is.True);
                if (!succeeds)
                {
                    Assert.That(player.AttachedEntity, Is.Null);
                    Assert.That(player.ContentData()!.Mind, Is.Null);
                    Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetMembership(player.UserId, out _), Is.False);
                }
                else
                {
                    Assert.That(server.System<StationJobsSystem>().TryGetPlayerJobs(station, player.UserId, out var assigned), Is.True);
                    Assert.That(assigned!.Select(j => j.Id), Is.EquivalentTo(new[] { civilian }));
                    Assert.That(server.System<SWGlobalWarIdentitySystem>().TryGetActor(player, player.AttachedEntity!.Value,
                        out _, out var faction), Is.True);
                    Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));
                    foreach (var other in server.EntMan.EntityQuery<StationJobsComponent>().Where(s => s.Owner != station))
                        Assert.That(server.System<StationJobsSystem>().TryGetPlayerJobs(other.Owner, player.UserId, out _), Is.False);
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                probe.BeforeValidation = null;
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, oldTimers);
                ticker.RestartRound();
                prefs.SetProfile(player.UserId, 0, original).Wait();
            });
        }
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();
    }

}
