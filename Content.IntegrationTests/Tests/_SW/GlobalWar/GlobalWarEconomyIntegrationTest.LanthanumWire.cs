using System;
using System.Linq;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task LanthanumWindowKeepsExactIntentAndCurrentAuthority()
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
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var session = server.PlayerMan.Sessions.Single();
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        EntityUid terminal = default;
        EntityUid actor = default;
        NetEntity netTerminal = default;
        Guid generation = default;
        SWGlobalWarLanthanumCommand command = default!;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestLanthanumPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(registry.TryGetAssignment(session.UserId, out var assignment), Is.True);
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            generation = campaign.Comp.Generation;
            actor = assignment.Body;
            server.System<GodmodeSystem>().EnableGodmode(actor);
            terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket", server.EntMan.GetComponent<TransformComponent>(actor).Coordinates);
            netTerminal = server.EntMan.GetNetEntity(terminal);
            server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(90);
        Guid oldNonce = default;
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            oldNonce = Read<Guid>(Bui(), "_subscription");
            Assert.That(menu.FindControl<BoxContainer>("LanthanumPanel").Visible, Is.True);
            Assert.That(menu.FindControl<BoxContainer>("LanthanumBody").Visible, Is.False);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumToggle"));
            Assert.That(menu.FindControl<BoxContainer>("LanthanumBody").Visible, Is.True);
            Assert.That(menu.FindControl<RichTextLabel>("LanthanumExecution").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-ln-wait-enclaveavailable")));
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumSave").Disabled, Is.False);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumSave"));
            command = menu.FlowDraft.PendingLanthanum!;
            Assert.That(command.Automatic, Is.False);
            menu.Close();
        });
        await pair.RunTicksSync(60);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Read<Guid>(Bui(), "_subscription"), Is.Not.EqualTo(oldNonce));
            var menu = Menu();
            if (menu.FlowDraft.PendingLanthanum != null)
            {
                Assert.That(menu.FlowDraft.PendingLanthanum, Is.EqualTo(command));
                Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumRetry"));
            }
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            Assert.That(menu.FlowDraft.PendingLanthanum, Is.Null);
            Assert.That(menu.FindControl<RichTextLabel>("LanthanumCurrent").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-ln-doctrine-off")));
            Assert.That(menu.FindControl<RichTextLabel>("LanthanumResult").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-ln-disabled")));
            // Simulate an unanswered request locally, then retry its exact payload through the real BUI.
            menu.FlowDraft.PendingLanthanum = command;
            Refresh(menu);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumRetry"));
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.LanthanumJournal.Count(j => j.Operation == command.Request), Is.EqualTo(1));
            var doctrine = markets.Simulation!.LanthanumDoctrines[SWGlobalWarFaction.Republic];
            Assert.That(doctrine.Revision, Is.EqualTo(1));
            Assert.That(doctrine.Automatic, Is.False);
            Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
            Assert.That(markets.TryConfigureLanthanum(session,
                new(generation, Guid.NewGuid(), head.Revision, 1, true), out _), Is.True);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumSave").Disabled, Is.True);
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumReapply").Visible, Is.True);
            Assert.That(menu.FlowDraft.LanthanumAutomatic, Is.True, "Concurrent state does not silently reverse preserved intent.");
            Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumSave"));
            Assert.That(menu.FlowDraft.PendingLanthanum, Is.Null);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("LanthanumReapply"));
            Assert.That(menu.FlowDraft.PendingLanthanum!.Automatic, Is.True);
            Assert.That(menu.FlowDraft.PendingLanthanum.ExpectedRevision, Is.EqualTo(2));
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.Simulation!.LanthanumDoctrines[SWGlobalWarFaction.Republic].Revision, Is.EqualTo(3));
        });
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            var state = Read<SWGlobalWarMarketConsoleState>(menu, "_state");
            using var presentation = new SWGlobalWarMarketConsoleMenu();
            SWGlobalWarMarketConsoleState State(SWGlobalWarLanthanumView? view, bool head, Guid assignment) =>
                new(true, state.Time, state.Planets, state.Connections, [], null, state.Generation, assignment,
                    state.AssignmentRevision, head: head, lanthanum: view);
            presentation.UpdateState(State(state.Lanthanum, true, state.Assignment));
            var commodities = Read<System.Collections.Generic.List<Robust.Shared.Prototypes.ProtoId<SWGlobalWarCommodityPrototype>>>(presentation, "_commodities");
            var filter = presentation.FindControl<OptionButton>("CommodityFilter");
            Select(filter, commodities.FindIndex(c => c.Id == "SWGlobalWarF") + 1);
            Assert.That(presentation.FindControl<BoxContainer>("LanthanumPanel").Visible, Is.False);
            Select(filter, commodities.FindIndex(c => c.Id == "SWGlobalWarLn") + 1);
            Assert.That(presentation.FindControl<BoxContainer>("LanthanumPanel").Visible, Is.True);
            presentation.UpdateState(State(state.Lanthanum, false, Guid.NewGuid()));
            Assert.That(presentation.FindControl<SWGlobalWarConsoleButton>("LanthanumSave").Visible, Is.False);
            SWGlobalWarLanthanumCommand? sent = null;
            presentation.LanthanumRequested += value => sent = value;
            Press(presentation.FindControl<SWGlobalWarConsoleButton>("LanthanumSave"));
            Assert.That(sent, Is.Null);
            presentation.UpdateState(State(null, false, Guid.Empty));
            Assert.That(presentation.FindControl<BoxContainer>("LanthanumPanel").Visible, Is.False);
            Assert.That(presentation.FlowDraft.PendingLanthanum, Is.Null);
            Assert.That(presentation.FindControl<RichTextLabel>("LanthanumDeposits").Text, Is.Empty);
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
            Assert.That(registry.TryExecute(session, new(generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign,
                head.Revision, head.User, head.Revision), out _), Is.True);
            Assert.That(markets.TryConfigureLanthanum(session, command, out _), Is.False);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            if (client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                    client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui) &&
                Read<SWGlobalWarMarketConsoleMenu?>(bui!, "_menu") is { } menu)
            {
                Assert.That(menu.FindControl<BoxContainer>("LanthanumPanel").Visible, Is.False);
                Assert.That(menu.FlowDraft.PendingLanthanum, Is.Null);
            }
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();

        SWGlobalWarMarketConsoleBoundUserInterface Bui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarMarketConsoleMenu Menu() => Read<SWGlobalWarMarketConsoleMenu>(Bui(), "_menu");
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Refresh(SWGlobalWarMarketConsoleMenu menu) => typeof(SWGlobalWarMarketConsoleMenu)
            .GetMethod("RefreshLanthanum", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, null);
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
    }
}
