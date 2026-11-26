using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
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
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyRevokerKeepsOnlyAddressedForeignResultAfterReopen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var headSession = server.PlayerMan.Sessions.Single();
        await server.AddDummySessions(1);
        var loaded = false;
        for (var i = 0; i < 100 && !loaded; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(s =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(s) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(s.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var workerSession = server.PlayerMan.Sessions.Single(s => s != headSession);
        foreach (var (session, name) in new[] { (headSession, "SWPoliticsTestHead"), (workerSession, "SWPoliticsTestWorker") })
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(session.UserId).SelectedCharacter;
            ProtoId<JobPrototype> job = name;
            await pair.SetJobPriorities(session.UserId,
                profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        }
        var savedTime = timing.TimeBase;
        EntityUid terminal = default;
        NetEntity netTerminal = default;
        SWGlobalWarAssignment head = default!;
        Guid workId = default;
        Guid purchaseId = default;
        SWGlobalWarSupplyWorkCommand revoke = default!;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestSupplyAuthorityPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(registry.TryGetAssignment(headSession.UserId, out head), Is.True);
                Assert.That(registry.TryGetAssignment(workerSession.UserId, out var worker), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                foreach (var session in server.PlayerMan.Sessions)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var generation = campaign.Comp.Generation;
                var planets = server.System<SWGlobalWarPlanetSystem>();
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var point = new EntityCoordinates(map!.Value, Vector2.Zero);
                var source = server.EntMan.SpawnEntity(null, point);
                var recipient = server.EntMan.SpawnEntity(null, point);
                Assert.That(markets.TryRegisterSupplyStore(generation, source), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, recipient, arsenal: true), Is.True);
                markets.Simulation!.SupplyParticipants = _ => 20;
                markets.Simulation.SupplyPrograms[head.Faction].Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent = 0;
                Advance(1200);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                purchaseId = Guid.NewGuid();
                Assert.That(markets.TryReserveSupplyPurchase(new(generation, purchaseId, accounts.Comp.Revision,
                    head.Faction, source, recipient, SWGlobalWarSupplyKind.Medicine, 1), out _), Is.True);
                var batch = markets.GetSupplyProductionWork(generation, source).Single();
                Assert.That(markets.TryBeginSupplyProduction(generation, batch.Request.Operation), Is.True);
                Advance(1651);
                var goods = Enumerable.Range(0, batch.Quantity).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                for (var i = 0; i < 4; i++)
                {
                    workId = Guid.NewGuid();
                    Assert.That(markets.TryExecuteSupplyWork(workerSession, new(generation, workId, worker.Revision,
                        SWGlobalWarSupplyWorkAction.Claim, purchaseId), out _), Is.True);
                    if (i < 3)
                        Assert.That(markets.TryExecuteSupplyWork(workerSession, new(generation, Guid.NewGuid(), worker.Revision,
                            SWGlobalWarSupplyWorkAction.Release, workId), out _), Is.True);
                }
                Assert.That(markets.GetSupplyWorkBoard(head)!.Work.Count, Is.EqualTo(1));
                terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket", server.EntMan.GetComponent<TransformComponent>(head.Body).Coordinates);
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, head.Body);
                void Advance(int elapsed)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(elapsed) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                }
            });
            await pair.RunTicksSync(90);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Press(menu.FindControl<SWGlobalWarConsoleButton>("FreightTab"));
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkToggle"));
                Select(menu.FindControl<OptionButton>("SupplyWorkPicker"), 0);
                Assert.That(State().SupplyWork!.Work.Single().Own, Is.False);
                var decision = menu.FindControl<CheckBox>("SupplyWorkHead");
                decision.Pressed = true;
                ((Action<BaseButton.ButtonToggledEventArgs>) typeof(BaseButton)
                    .GetField("OnToggled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(decision)!)(new(true, decision, null!));
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkRevoke"));
                revoke = menu.FlowDraft.PendingSupplyWork!;
                Assert.That(revoke.HeadDecision, Is.True);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(AssertAddressedResult);
            await client.WaitAssertion(() => client.System<ClientUi>().CloseUi(client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, head.Body));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(AssertAddressedResult);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.SupplyWorkJournal.Count(j => j.Command.Request == revoke.Request), Is.EqualTo(1));
                Assert.That(markets.Simulation!.SupplyPrograms[head.Faction].Orders[purchaseId].Outstanding, Is.EqualTo(1));
                Assert.That(ledger.GetBalance(head.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(150));
                Assert.That(registry.TryExecute(headSession, new(head.Generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign,
                    head.Revision, head.User, head.Revision), out _), Is.True);
                Assert.That(markets.GetSupplyWorkBoard(head), Is.Null);
                Assert.That(markets.GetSupplyWorkResults(head), Is.Empty);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (clock.TryGetCampaign(out var campaign))
                    ticker.EndGameRule(campaign.Owner);
                timing.TimeBase = savedTime;
                ticker.SetGamePreset(originalPreset);
            });
        }
        await pair.CleanReturnAsync();

        void AssertAddressedResult()
        {
            var menu = Menu();
            var work = State().SupplyWork!.Work.Single();
            Assert.That(work.Id, Is.EqualTo(workId));
            Assert.That(work.Status, Is.EqualTo(SWGlobalWarSupplyWorkStatus.Revoked));
            Assert.That(work.Own, Is.False);
            Assert.That(menu.FlowDraft.SupplyWork, Is.EqualTo(workId));
            Assert.That(menu.FindControl<RichTextLabel>("SupplyWorkDetails").Text,
                Does.Contain(Loc.GetString("sw-global-war-supply-work-revoked")));
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkRevoke").Disabled, Is.True);
        }
        SWGlobalWarMarketConsoleMenu Menu()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return Read<SWGlobalWarMarketConsoleMenu>(bui!, "_menu");
        }
        SWGlobalWarMarketConsoleState State() => Read<SWGlobalWarMarketConsoleState>(Menu(), "_state");
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
    }
}
