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
    public async Task ReturnWindowReopensItsExactRequestAndKeepsTheDeliveredReceipt()
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
        SWGlobalWarReturnSelection selected = default!;
        SWGlobalWarReturnCommand command = default!;
        const string source = "SWSpawnTestReturnA";
        const string transit = "SWSpawnTestReturnB";
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestReturnPreset");
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
        var dispatched = false;
        for (var i = 0; i < 120 && !dispatched; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => dispatched = markets.Simulation!.Shipments.Count > 0);
        }
        Assert.That(dispatched, Is.True);
        await server.WaitAssertion(() =>
        {
            var flow = markets.Simulation!.Flows["SWSpawnTestReturnFood"];
            Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
            Assert.That(markets.TryEditFlow(session, new(generation, Guid.NewGuid(), head.Revision, flow.Id,
                flow.Revision, new(source, "SWSpawnTestReturnC", "SWGlobalWarF", Q(180), Enabled: false)), out _), Is.True);
            ChangeOrbit(SWGlobalWarFaction.Empire);
        });
        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            var part = markets.Simulation!.Shipments.Single();
            Assert.That(part.ArrivesAt, Is.Null);
            selected = SWGlobalWarMarketSystem.ReturnSelection(part);
        });
        Guid oldNonce = default;
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            oldNonce = Read<Guid>(Bui(), "_subscription");
            ChoosePart(menu, selected.Shipment);
            var destinations = Read<string[]>(menu, "_returnDestinations");
            Select(menu.FindControl<OptionButton>("ReturnDestination"), Array.IndexOf(destinations, source));
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("ReturnSend").Disabled, Is.False);
            Assert.That(menu.FindControl<ScrollContainer>("FreightScroll").GetScrollValue().Y, Is.Zero);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("ReturnSend"));
            command = menu.FlowDraft.PendingReturn!;
            Assert.That(command.Part, Is.EqualTo(selected));
            menu.Close();
        });
        await pair.RunTicksSync(60);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Read<Guid>(Bui(), "_subscription"), Is.Not.EqualTo(oldNonce));
            if (Menu().FlowDraft.PendingReturn != null)
            {
                Assert.That(Menu().FlowDraft.PendingReturn, Is.EqualTo(command));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnRetry"));
            }
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.ReturnJournal.Count(j => j.Command.Request == command.Request), Is.EqualTo(1));
            Assert.That(markets.Simulation!.Markets[source].Goods["SWGlobalWarF"].Incoming, Is.EqualTo(selected.Quantity));
        });
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            Assert.That(menu.FlowDraft.PendingReturn, Is.Null);
            Assert.That(menu.FlowDraft.LastReturnRequest, Is.EqualTo(command.Request));
            // Model an unanswered local command, then send the identical payload through the real BUI again.
            menu.FlowDraft.PendingReturn = command;
            typeof(SWGlobalWarMarketConsoleMenu).GetMethod("RefreshReturns", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(menu, null);
            Press(menu.FindControl<SWGlobalWarConsoleButton>("ReturnRetry"));
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.ReturnJournal.Count(j => j.Command.Request == command.Request), Is.EqualTo(1));
            ChangeOrbit(SWGlobalWarFaction.Republic);
        });
        await pair.RunTicksSync(300);
        await server.WaitAssertion(() =>
        {
            Assert.That(markets.TryConfigureReturn(session, command, out var receipt), Is.True);
            Assert.That(receipt.Receipt!.Received, Is.EqualTo(selected.Quantity));
            Assert.That(markets.Simulation!.Shipments, Is.Empty);
            for (var i = 0; i < 16; i++)
                Assert.That(markets.TryConfigureReturn(session, command with
                {
                    Request = Guid.NewGuid(), Action = SWGlobalWarReturnAction.Revoke, Grant = Guid.NewGuid(),
                }, out _), Is.False);
            server.System<ServerUi>().CloseUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(10);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            var state = Read<SWGlobalWarMarketConsoleState>(menu, "_state");
            Assert.That(state.ReturnResults, Has.Count.EqualTo(17));
            Assert.That(state.ReturnResults.Single(r => r.Request == command.Request).Receipt!.Received, Is.EqualTo(selected.Quantity));
            Assert.That(menu.FindControl<RichTextLabel>("ReturnReceipt").Text, Does.Contain("3"));
            Assert.That(menu.FindControl<RichTextLabel>("ReturnResult").Text, Is.EqualTo(Loc.GetString("sw-global-war-return-accepted")));
            Assert.That(menu.FindControl<RichTextLabel>("ReturnReason").Text, Is.Empty);
            Assert.That(SWGlobalWarMarketConsoleMenu.ReturnUnits(1), Is.EqualTo("1/60000"));
            Assert.That(SWGlobalWarMarketConsoleMenu.ReturnUnits(24), Is.EqualTo((0.0004m).ToString("0.#####")));
            Assert.That(SWGlobalWarMarketConsoleMenu.ReturnUnits(Q(3)), Is.EqualTo("3"));

            // Presentation race: a refreshed part never silently changes the payload being confirmed.
            using var editor = new SWGlobalWarMarketConsoleMenu();
            var part = selected with { Quantity = 24 };
            SWGlobalWarMarketConsoleState State(SWGlobalWarReturnSelection current, Guid? assignment = null)
            {
                var candidates = Enumerable.Range(0, 40).Select(i => new SWGlobalWarReturnCandidate(
                    i == 0 ? current : current with { Shipment = Guid.NewGuid() }, "SWGlobalWarF",
                    "sw-global-war-trade-blockade", SWGlobalWarReturnFailure.None,
                    [new(source, SWGlobalWarReturnFailure.None)])).ToList();
                return new(true, state.Time, state.Planets, state.Connections, [], null,
                    state.Generation, assignment ?? state.Assignment, state.AssignmentRevision,
                    canManageFlows: true, returnCandidates: candidates);
            }
            editor.UpdateState(State(part));
            ChoosePart(editor, part.Shipment);
            Select(editor.FindControl<OptionButton>("ReturnDestination"), 0);
            Assert.That(editor.FindControl<SWGlobalWarConsoleButton>("ReturnSend").Text,
                Does.Contain((0.0004m).ToString("0.#####")));
            editor.UpdateState(State(part with { Quantity = 1 }));
            Assert.That(editor.FlowDraft.ReturnPart, Is.EqualTo(part));
            Assert.That(editor.FindControl<SWGlobalWarConsoleButton>("ReturnSend").Disabled, Is.True);
            SWGlobalWarReturnCommand? sent = null;
            editor.ReturnRequested += value => sent = value;
            Press(editor.FindControl<SWGlobalWarConsoleButton>("ReturnSend"));
            Assert.That(sent, Is.Null);
            Press(editor.FindControl<SWGlobalWarConsoleButton>("ReturnReload"));
            Assert.That(editor.FindControl<SWGlobalWarConsoleButton>("ReturnSend").Text, Does.Contain("1/60000"));
            Press(editor.FindControl<SWGlobalWarConsoleButton>("ReturnSend"));
            Assert.That(sent!.Part.Quantity, Is.EqualTo(1));
            editor.ReceiveReturnResult(new(sent.Request, sent.Action, false, SWGlobalWarReturnFailure.NoSpace));
            editor.UpdateState(State(part with { Quantity = 1 }));
            Assert.That(editor.FlowDraft.ReturnDestination, Is.EqualTo(source));
            Assert.That(editor.FindControl<RichTextLabel>("ReturnResult").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-return-failure-nospace")));
            editor.UpdateState(State(part, Guid.Empty));
            Assert.That(editor.FlowDraft.ReturnPart, Is.Null);
            Assert.That(editor.FlowDraft.PendingReturn, Is.Null);
            Assert.That(editor.FindControl<BoxContainer>("ReturnPanel").Visible, Is.False);
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();

        void ChangeOrbit(SWGlobalWarFaction owner)
        {
            Assert.That(server.System<SWGlobalWarPlanetSystem>().TryGetPlanet(transit, out var planet), Is.True);
            var state = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(planet!.Value);
            var steps = server.System<SWGlobalWarCampaignStepSystem>();
            Assert.That(steps.TryQueueOwnership(planet.Value, SWGlobalWarPlanetLocation.Orbit, owner,
                state.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
            Assert.That(steps.CommitCampaignStep(generation, server.ResolveDependency<IGameTiming>().CurTick), Is.True);
        }
        SWGlobalWarMarketConsoleBoundUserInterface Bui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarMarketConsoleMenu Menu() => Read<SWGlobalWarMarketConsoleMenu>(Bui(), "_menu");
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void ChoosePart(SWGlobalWarMarketConsoleMenu menu, Guid id) => typeof(SWGlobalWarMarketConsoleMenu)
            .GetMethod("SelectReturn", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, [id]);
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
    }
}
