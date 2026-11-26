using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.GlobalWar.Identity;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
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
    [Test]
    public async Task SupplyParticipantsUseUniqueCurrentIdentityAcrossOfficeDeathAndBodies()
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
        // The status event unloads this dummy's preference cache; reload it without a real database reconnect.
        await server.WaitPost(() => server.ResolveDependency<UserDbDataManager>().ClientConnected(sessions[2]));
        loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.ResolveDependency<UserDbDataManager>().IsLoadComplete(sessions[2]) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(sessions[2].UserId, out _));
        }
        Assert.That(loaded, Is.True);
        await pair.CleanReturnAsync();

        IEnumerable<bool> RunScenario()
        {
            try
            {
                Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(4));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                var generation = campaign.Comp.Generation;
                var identity = server.System<SWGlobalWarIdentitySystem>();
                var mobs = server.System<MobStateSystem>();
                var minds = server.System<SharedMindSystem>();
                Assert.That(registry.TryGetAssignment(sessions[0].UserId, out var head), Is.True);
                Assert.That(registry.TryGetAssignment(sessions[2].UserId, out var worker), Is.True);
                Assert.That(registry.TryGetAssignment(sessions[3].UserId, out var imperial), Is.True);
                foreach (var session in sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(3));
                Assert.That(Count(SWGlobalWarFaction.Empire), Is.EqualTo(1));
                var program = markets.Simulation!.SupplyPrograms[SWGlobalWarFaction.Republic];
                Assert.That(program.StartParticipants, Is.EqualTo(10));
                Assert.That(program.StartArsenalLimit, Is.EqualTo(13));
                Assert.That(registry.TryExecute(sessions[0], new(generation, Guid.NewGuid(),
                    SWGlobalWarAppointmentAction.Resign, head.Revision, head.User, head.Revision), out _), Is.True);
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(3), "Office loss does not erase participation.");
                mobs.ChangeMobState(worker.Body, MobState.Critical);
                yield return true;
                server.PlayerMan.SetStatus(sessions[2], SessionStatus.Disconnected);
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(3), "A critical offline current body is counted.");
                mobs.ChangeMobState(worker.Body, MobState.Dead);
                yield return true;
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(2), "An offline dead body is not counted.");
                mobs.ChangeMobState(imperial.Body, MobState.Dead);
                yield return true;
                Assert.That(Count(SWGlobalWarFaction.Empire), Is.EqualTo(1), "Connected death and waiting preserve readiness.");
                var coordinates = server.EntMan.GetComponent<TransformComponent>(head.Body).Coordinates;
                var npc = server.EntMan.SpawnEntity("MobHuman", coordinates);
                server.System<GodmodeSystem>().EnableGodmode(npc);
                yield return true;
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(2), "An unregistered NPC is not a participant.");
                minds.TransferTo(worker.Mind, npc);
                Assert.That(identity.TryEnroll(worker.Mind, SWGlobalWarFaction.Republic, generation), Is.True);
                Assert.That(identity.TryEnroll(worker.Mind, SWGlobalWarFaction.Republic, generation), Is.True);
                yield return true;
                Assert.That(Count(SWGlobalWarFaction.Republic), Is.EqualTo(3), "The replacement body counts once; the previous life does not add another member.");
                markets.Simulation.CaptureStartingSupply();
                Assert.That(program.StartArsenalLimit, Is.EqualTo(13));
                Assert.That(program.StartParticipants, Is.EqualTo(10));
                Assert.That(identity.CountSupplyParticipants(Guid.NewGuid(), SWGlobalWarFaction.Republic), Is.Zero);
                ticker.EndGameRule(campaign.Owner);

                int Count(SWGlobalWarFaction faction) => identity.CountSupplyParticipants(generation, faction);
            }
            finally
            {
                ticker.SetGamePreset(originalPreset);
            }
        }
    }
}
