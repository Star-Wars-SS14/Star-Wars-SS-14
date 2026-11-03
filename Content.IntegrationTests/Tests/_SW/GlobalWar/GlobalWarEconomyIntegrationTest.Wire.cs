using System;
using System.Linq;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Market;
using Robust.Client.UserInterface.Controls;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string WirePrototypes = """
- type: entity
  parent: SWGlobalWarMarketConsole
  id: SWDEconomyWireMarket
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: SWGlobalWarTravelConsole
  id: SWDEconomyWireTravel
  components:
  - type: ApcPowerReceiver
    needsPower: false
""";

    [Test]
    public async Task ReopenedMarketAndTravelRejectDelayedSnapshotsFromThePreviousSubscription()
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
        var session = server.PlayerMan.Sessions.Single();
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        EntityUid market = default;
        EntityUid travel = default;
        EntityUid actor = default;
        NetEntity netMarket = default;
        NetEntity netTravel = default;
        NetEntity viewer = default;
        Guid oldMarketNonce = default;
        Guid oldTravelNonce = default;
        SWGlobalWarMarketConsoleState oldMarketState = default!;
        SWGlobalWarTravelConsoleState oldTravelState = default!;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestEconomyViewPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(registry.TryGetAssignment(session.UserId, out var assignment), Is.True);
            Assert.That(assignment.Role, Is.EqualTo(SWGlobalWarRole.Head));
            actor = assignment.Body;
            server.System<GodmodeSystem>().EnableGodmode(actor);
            var coordinates = server.EntMan.GetComponent<TransformComponent>(actor).Coordinates;
            market = server.EntMan.SpawnEntity("SWDEconomyWireMarket", coordinates);
            travel = server.EntMan.SpawnEntity("SWDEconomyWireTravel", coordinates);
            netMarket = server.EntMan.GetNetEntity(market);
            netTravel = server.EntMan.GetNetEntity(travel);
            viewer = server.EntMan.GetNetEntity(actor);
            server.System<ServerUi>().OpenUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor);
            server.System<ServerUi>().OpenUi(travel, SWGlobalWarTravelConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var marketBui = MarketBui();
            var travelBui = TravelBui();
            oldMarketNonce = Read<Guid>(marketBui, "_subscription");
            oldTravelNonce = Read<Guid>(travelBui, "_subscription");
            oldMarketState = Read<SWGlobalWarMarketConsoleState>(Read<object>(marketBui, "_menu"), "_state");
            oldTravelState = Read<SWGlobalWarTravelConsoleState>(Read<object>(travelBui, "_menu"), "_state");
            Assert.That(oldMarketState.Planets.Any(p => p.Goods.Count > 0), Is.True,
                "The opening nonce must complete a real client/server handshake before any private view arrives.");
            Assert.That(oldTravelState.Destinations.Any(p => p.Market!.Count > 0), Is.True);
            Assert.That(oldMarketNonce, Is.Not.EqualTo(Guid.Empty));
            Assert.That(oldTravelNonce, Is.Not.EqualTo(Guid.Empty));
        });
        SWGlobalWarFlowCommand flowCommand = default!;
        await client.WaitAssertion(() =>
        {
            var menu = (SWGlobalWarMarketConsoleMenu) Read<object>(MarketBui(), "_menu");
            var flow = oldMarketState.Flows.Single(f => f.Id == "SWSpawnTestEconomyViewFoodR");
            typeof(SWGlobalWarMarketConsoleMenu).GetMethod("LoadFlow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(menu, [flow]);
            var rate = menu.FindControl<LineEdit>("FlowRate");
            rate.Text = "0.4";
            menu.UpdateState(oldMarketState);
            Assert.That(rate.Text, Is.EqualTo("0.4"), "A market refresh must not replace an unfinished draft.");
            var save = menu.FindControl<SWGlobalWarConsoleButton>("SaveFlow");
            Assert.That(save.Disabled, Is.False);
            // Exercise the real widget handler and BUI message transport; this is not an MCP mouse interaction.
            ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton).GetField("OnPressed",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(save)!)(new(save, null!));
            flowCommand = Read<SWGlobalWarFlowCommand>(menu, "_pendingFlow");
            Assert.That(save.Disabled, Is.True);
            Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("RetryFlow").Disabled, Is.False);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            var flow = server.System<SWGlobalWarMarketSystem>().Simulation!.Flows["SWSpawnTestEconomyViewFoodR"];
            Assert.That(flow.ExplicitRate, Is.EqualTo(0.4m));
            Assert.That(flow.Revision, Is.EqualTo(1));
        });
        await client.WaitAssertion(() =>
        {
            var menu = (SWGlobalWarMarketConsoleMenu) Read<object>(MarketBui(), "_menu");
            Assert.That(Read<SWGlobalWarFlowCommand?>(menu, "_pendingFlow"), Is.Null);
            Assert.That(Read<long>(menu, "_editingRevision"), Is.EqualTo(1));
            Assert.That(menu.FindControl<LineEdit>("FlowRate").Text, Is.EqualTo("0.4"));
            ((Action<SWGlobalWarFlowCommand>) typeof(SWGlobalWarMarketConsoleMenu).GetField("FlowRequested",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!)(flowCommand);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SWGlobalWarMarketSystem>().Simulation!.Flows["SWSpawnTestEconomyViewFoodR"].Revision,
                Is.EqualTo(1), "A real BUI retry must not create a second edit.");
        });
        await client.WaitPost(() => Menu().FindControl<LineEdit>("FlowRate").SetText("0.55", invokeEvent: true));
        await server.WaitPost(() => server.System<ServerUi>().CloseUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(20);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Menu().FindControl<LineEdit>("FlowRate").Text, Is.EqualTo("0.55"));
            Assert.That(Read<long>(Menu(), "_editingRevision"), Is.EqualTo(1));
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SWGlobalWarMarketSystem>().TryEditFlow(session, flowCommand with
            {
                Request = Guid.NewGuid(), ExpectedRevision = 1,
                Settings = flowCommand.Settings with { Rate = 36000 },
            }, out var otherEdit), Is.True);
            Assert.That(otherEdit.Flow!.Revision, Is.EqualTo(2));
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Menu().FindControl<LineEdit>("FlowRate").Text, Is.EqualTo("0.55"));
            Assert.That(Read<long>(Menu(), "_editingRevision"), Is.EqualTo(1), "A fresh snapshot cannot silently rebase the draft.");
            Assert.That(Menu().FindControl<BoxContainer>("FlowConflictActions").Visible, Is.True);
            Assert.That(Menu().FindControl<SWGlobalWarConsoleButton>("SaveFlow").Disabled, Is.True);
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReapplyFlow"));
            Assert.That(Menu().FlowDraft.PendingFlow!.ExpectedRevision, Is.EqualTo(2));
            Assert.That(Menu().FlowDraft.PendingFlow!.Settings.Rate, Is.EqualTo(33000));
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            var flow = server.System<SWGlobalWarMarketSystem>().Simulation!.Flows[flowCommand.Flow!];
            Assert.That(flow.Revision, Is.EqualTo(3));
            Assert.That(flow.ExplicitRate, Is.EqualTo(0.55m));
        });
        SWGlobalWarFlowCommand create = default!;
        var beforeCreate = 0;
        await server.WaitPost(() => beforeCreate = server.System<SWGlobalWarMarketSystem>().Simulation!.Flows.Count);
        await client.WaitAssertion(() =>
        {
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("NewFlow"));
            Assert.That(Menu().FindControl<OptionButton>("FlowPicker").SelectedId, Is.EqualTo(-1));
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("SaveFlow"));
            create = Menu().FlowDraft.PendingFlow!;
            Assert.That(create.Flow, Is.Null);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SWGlobalWarMarketSystem>().Simulation!.Flows.Count, Is.EqualTo(beforeCreate + 1));
            server.System<ServerUi>().CloseUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(20);
        await client.WaitPost(() =>
        {
            // Model the unanswered create in the durable cache after the real server accepted it.
            var draft = client.System<SWGlobalWarMarketViewSystem>().GetDraft(netMarket);
            draft.PendingFlow = create;
            draft.Flow = null;
            draft.FlowRevision = -1;
        });
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Menu().FlowDraft.PendingFlow, Is.Null, "The exact own receipt resolves an unanswered create after reopening.");
            Assert.That(Menu().FlowDraft.Flow, Is.Not.Null);
            Assert.That(Menu().FindControl<LineEdit>("FlowRate").Text, Is.EqualTo("0.55"));
            ((Action<SWGlobalWarFlowCommand>) typeof(SWGlobalWarMarketConsoleMenu).GetField("FlowRequested",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Menu())!)(create);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
            Assert.That(server.System<SWGlobalWarMarketSystem>().Simulation!.Flows.Count, Is.EqualTo(beforeCreate + 1)));
        await client.WaitAssertion(() =>
        {
            // Presentation fixture isolates list churn from the server's current appointments.
            using var editor = new SWGlobalWarMarketConsoleMenu();
            var basis = Read<SWGlobalWarMarketConsoleState>(Menu(), "_state");
            var workerA = new SWGlobalWarFlowWorkerView(new NetUserId(Guid.NewGuid()), 1, "Worker A");
            var workerB = new SWGlobalWarFlowWorkerView(new NetUserId(Guid.NewGuid()), 2, "Worker B");
            var grantA = new SWGlobalWarFlowGrantView(Guid.NewGuid(), flowCommand.Flow,
                flowCommand.Settings.Source.Id, flowCommand.Settings.Destination.Id, flowCommand.Settings.Commodity.Id,
                0, 60000, 0, 1, basis.Time + TimeSpan.FromMinutes(10), workerA.Name, false);
            var grantB = grantA with { Id = Guid.NewGuid(), Employee = workerB.Name };
            editor.UpdateState(State([grantA, grantB], [workerA, workerB]));
            Select(editor.FindControl<OptionButton>("FlowGrantPicker"), 1);
            Select(editor.FindControl<OptionButton>("FlowWorker"), 1);
            editor.UpdateState(State([grantB, grantA], [workerB with { Name = "Renamed B" }, workerA]));
            Assert.That(editor.FlowDraft.Grant, Is.EqualTo(grantB.Id));
            Assert.That(editor.FlowDraft.Worker!.User, Is.EqualTo(workerB.User));
            Assert.That(editor.FindControl<OptionButton>("FlowWorker").SelectedId, Is.Zero);
            Assert.That(editor.FindControl<RichTextLabel>("FlowGrantDetails").Text,
                Does.Contain(flowCommand.Flow));
            SWGlobalWarFlowRevokeCommand sentRevoke = default!;
            editor.FlowRevokeRequested += revoke => sentRevoke = revoke;
            Press(editor.FindControl<SWGlobalWarConsoleButton>("RevokeFlow"));
            Assert.That(sentRevoke.Grant, Is.EqualTo(grantB.Id));
            var firstRequest = sentRevoke.Request;
            Press(editor.FindControl<SWGlobalWarConsoleButton>("RetryFlow"));
            Assert.That(sentRevoke.Request, Is.EqualTo(firstRequest));
            editor.ReceiveFlowResult(new(firstRequest, true, SWGlobalWarEconomyFailure.None));
            editor.UpdateState(State([grantB with { Revoked = true }, grantA], [workerB, workerA]));
            Assert.That(editor.FindControl<RichTextLabel>("FlowResult").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-economy-revoked")));
            editor.UpdateState(State([grantA], [workerA]));
            Assert.That(editor.FlowDraft.Grant, Is.EqualTo(Guid.Empty));
            Assert.That(editor.FlowDraft.Worker, Is.Null);
            Assert.That(editor.FindControl<OptionButton>("FlowWorker").SelectedId, Is.EqualTo(-1));
            Assert.That(editor.FindControl<RichTextLabel>("FlowResult").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-economy-selection-lost")));

            SWGlobalWarMarketConsoleState State(System.Collections.Generic.List<SWGlobalWarFlowGrantView> grants,
                System.Collections.Generic.List<SWGlobalWarFlowWorkerView> workers) => new(basis.Ready, basis.Time,
                basis.Planets, basis.Connections, basis.Shipments, basis.CurrentPlanet, basis.Generation, basis.Assignment,
                basis.AssignmentRevision, flows: basis.Flows, flowResults: basis.FlowResults,
                canManageFlows: true, head: true, flowGrants: grants, flowWorkers: workers, neutralOffers: basis.NeutralOffers);
        });
        await client.WaitAssertion(() =>
        {
            var worlds = Read<string[]>(Menu(), "_growthWorlds");
            Select(Menu().FindControl<OptionButton>("GrowthWorld"), Array.IndexOf(worlds, "SWSpawnTestEconomyViewRFarm"));
            Menu().FindControl<LineEdit>("GrowthPercent").SetText("0", invokeEvent: true);
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("GrowthSave"));
            Assert.That(Menu().FlowDraft.PendingGrowth!.Percent, Is.Zero);
        });
        await pair.RunTicksSync(60);
        long growthAtZero = 0;
        long mandatoryAtZero = 0;
        decimal populationAtZero = 0;
        await server.WaitAssertion(() =>
        {
            var farm = server.System<SWGlobalWarMarketSystem>().Simulation!.Markets["SWSpawnTestEconomyViewRFarm"];
            Assert.That(farm.GrowthFoodFraction, Is.Zero);
            growthAtZero = farm.Goods["SWGlobalWarF"].GrowthConsumed;
            mandatoryAtZero = farm.Goods["SWGlobalWarF"].Consumed;
            populationAtZero = farm.Population;
        });
        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            var farm = server.System<SWGlobalWarMarketSystem>().Simulation!.Markets["SWSpawnTestEconomyViewRFarm"];
            Assert.That(farm.Goods["SWGlobalWarF"].GrowthConsumed, Is.EqualTo(growthAtZero));
            Assert.That(farm.Goods["SWGlobalWarF"].Consumed, Is.GreaterThan(mandatoryAtZero));
            populationAtZero = farm.Population;
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(Menu().FlowDraft.PendingGrowth, Is.Null);
            Menu().FindControl<LineEdit>("GrowthPercent").SetText("25", invokeEvent: true);
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("GrowthSave"));
        });
        await pair.RunSeconds(61);
        await server.WaitAssertion(() =>
        {
            var farm = server.System<SWGlobalWarMarketSystem>().Simulation!.Markets["SWSpawnTestEconomyViewRFarm"];
            Assert.That(farm.GrowthFoodFraction, Is.EqualTo(0.25m));
            Assert.That(farm.Goods["SWGlobalWarF"].GrowthConsumed, Is.GreaterThan(growthAtZero));
            Assert.That(farm.Population, Is.GreaterThan(populationAtZero));
            Assert.That(farm.Goods["SWGlobalWarF"].UnmetConsumption, Is.Zero,
                "The real ledger-backed extra consumption preserves mandatory food satisfaction.");
        });
        await client.WaitPost(() => Menu().FindControl<LineEdit>("GrowthPercent").SetText("20", invokeEvent: true));
        await server.WaitAssertion(() =>
        {
            Assert.That(registry.TryGetAssignment(session.UserId, out var assignment), Is.True);
            var system = server.System<SWGlobalWarMarketSystem>();
            var farm = system.Simulation!.Markets["SWSpawnTestEconomyViewRFarm"];
            Assert.That(system.TryConfigureGrowth(session, new(assignment.Generation, Guid.NewGuid(), assignment.Revision,
                SWGlobalWarGrowthAction.Set, farm.Planet, farm.GrowthRevision, 15), out _), Is.True);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            Assert.That(Menu().FindControl<LineEdit>("GrowthPercent").Text, Is.EqualTo("20"));
            Assert.That(Menu().FindControl<SWGlobalWarConsoleButton>("GrowthReapply").Visible, Is.True);
            Assert.That(Menu().FindControl<SWGlobalWarConsoleButton>("GrowthSave").Disabled, Is.True);
            Menu().FindControl<LineEdit>("GrowthMinutes").SetText("", invokeEvent: true);
            Menu().FindControl<LineEdit>("GrowthMinimum").SetText("invalid", invokeEvent: true);
            Press(Menu().FindControl<SWGlobalWarConsoleButton>("GrowthReapply"));
            Assert.That(Menu().FlowDraft.PendingGrowth!.Percent, Is.EqualTo(20), "Set ignores unrelated grant inputs.");
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() => Assert.That(server.System<SWGlobalWarMarketSystem>().Simulation!
            .Markets["SWSpawnTestEconomyViewRFarm"].GrowthFoodFraction, Is.EqualTo(0.2m)));
        await client.WaitAssertion(() =>
        {
            var worlds = Read<string[]>(Menu(), "_growthWorlds");
            Select(Menu().FindControl<OptionButton>("GrowthWorld"), Array.IndexOf(worlds, "SWSpawnTestEconomyViewR"));
            Assert.That(Menu().FindControl<RichTextLabel>("GrowthResult").Text, Is.Empty,
                "Success from the farm is not shown as success for the capital.");
            using var editor = new SWGlobalWarMarketConsoleMenu();
            var basis = Read<SWGlobalWarMarketConsoleState>(Menu(), "_state");
            var grant = new SWGlobalWarGrowthGrantView(Guid.NewGuid(), "SWSpawnTestEconomyViewRFarm", 0, 25,
                "Worker", basis.Time + TimeSpan.FromMinutes(10), false, SWGlobalWarGrowthFailure.None);
            SWGlobalWarMarketConsoleState State(bool hasWorld, bool revoked = false) => new(basis.Ready, basis.Time,
                basis.Planets, basis.Connections, basis.Shipments, basis.CurrentPlanet, basis.Generation, basis.Assignment,
                basis.AssignmentRevision, canManageFlows: true, head: true,
                growth: hasWorld ? basis.Growth : [], growthGrants: [grant with
                {
                    Revoked = revoked, Failure = revoked ? SWGlobalWarGrowthFailure.GrantRevoked : SWGlobalWarGrowthFailure.None,
                }]);
            editor.UpdateState(State(true));
            Select(editor.FindControl<OptionButton>("GrowthWorld"), Array.IndexOf(Read<string[]>(editor, "_growthWorlds"), grant.Planet));
            Select(editor.FindControl<OptionButton>("GrowthGrantPicker"), 0);
            editor.FindControl<LineEdit>("GrowthPercent").SetText("26", invokeEvent: true);
            editor.FindControl<LineEdit>("GrowthMinutes").SetText("", invokeEvent: true);
            editor.FindControl<LineEdit>("GrowthMinimum").SetText("invalid", invokeEvent: true);
            SWGlobalWarGrowthCommand revoke = default!;
            editor.GrowthRequested += command => revoke = command;
            Press(editor.FindControl<SWGlobalWarConsoleButton>("GrowthRevoke"));
            Assert.That(revoke.Action, Is.EqualTo(SWGlobalWarGrowthAction.Revoke));
            Assert.That(revoke.Grant, Is.EqualTo(grant.Id));
            editor.ReceiveGrowthResult(new(revoke.Request, revoke.Action, true, SWGlobalWarGrowthFailure.None));
            editor.UpdateState(State(true, true));
            Assert.That(editor.FindControl<RichTextLabel>("GrowthScope").Text,
                Does.Contain(Loc.GetString("sw-global-war-growth-failure-grantrevoked")));
            editor.UpdateState(State(false));
            Assert.That(editor.FindControl<RichTextLabel>("GrowthResult").Text,
                Is.EqualTo(Loc.GetString("sw-global-war-growth-lost-world")));
        });
        await server.WaitAssertion(() =>
        {
            server.System<ServerUi>().CloseUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor);
            server.System<ServerUi>().CloseUi(travel, SWGlobalWarTravelConsoleUiKey.Key, actor);
            Assert.That(registry.TryGetAssignment(session.UserId, out var assignment), Is.True);
            Assert.That(registry.TryGetRegistry(out var state), Is.True);
            Assert.That(registry.TryExecute(session, new SWGlobalWarAppointmentCommand(state.Comp.Generation,
                Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign, assignment.Revision,
                assignment.User, assignment.Revision), out _), Is.True);
        });
        await pair.RunTicksSync(20);
        await server.WaitPost(() =>
        {
            server.System<ServerUi>().OpenUi(market, SWGlobalWarMarketConsoleUiKey.Key, actor);
            server.System<ServerUi>().OpenUi(travel, SWGlobalWarTravelConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var marketBui = MarketBui();
            var travelBui = TravelBui();
            var currentMarket = Read<SWGlobalWarMarketConsoleState>(Read<object>(marketBui, "_menu"), "_state");
            var currentTravel = Read<SWGlobalWarTravelConsoleState>(Read<object>(travelBui, "_menu"), "_state");
            Assert.That(currentMarket.Planets.All(p => p.Goods.Count == 0), Is.True);
            Assert.That(currentTravel.Destinations.All(p => p.Market!.Count == 0), Is.True);
            Assert.That(Menu().FlowDraft.PendingFlow, Is.Null);
            Assert.That(Menu().FlowDraft.Flow, Is.Null);
            Assert.That(Read<Guid>(marketBui, "_subscription"), Is.Not.EqualTo(oldMarketNonce));
            Assert.That(Read<Guid>(travelBui, "_subscription"), Is.Not.EqualTo(oldTravelNonce));
            // Inject at the actual client BUI receive boundary after a genuine close, revoke and reopen.
            // A large sequence isolates subscription rejection from ordinary sequence ordering.
            Deliver(marketBui, new SWGlobalWarMarketSnapshot(oldMarketState, viewer, long.MaxValue, oldMarketNonce));
            Deliver(travelBui, new SWGlobalWarTravelSnapshot(oldTravelState, viewer, long.MaxValue, oldTravelNonce));
            Assert.That(Read<SWGlobalWarMarketConsoleState>(Read<object>(marketBui, "_menu"), "_state"), Is.SameAs(currentMarket));
            Assert.That(Read<SWGlobalWarTravelConsoleState>(Read<object>(travelBui, "_menu"), "_state"), Is.SameAs(currentTravel));
            Deliver(marketBui, new SWGlobalWarMarketSnapshot(oldMarketState, netMarket, long.MaxValue,
                Read<Guid>(marketBui, "_subscription")));
            Deliver(travelBui, new SWGlobalWarTravelSnapshot(oldTravelState, netTravel, long.MaxValue,
                Read<Guid>(travelBui, "_subscription")));
            Assert.That(Read<SWGlobalWarMarketConsoleState>(Read<object>(marketBui, "_menu"), "_state"), Is.SameAs(currentMarket));
            Assert.That(Read<SWGlobalWarTravelConsoleState>(Read<object>(travelBui, "_menu"), "_state"), Is.SameAs(currentTravel));
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();

        SWGlobalWarMarketConsoleBoundUserInterface MarketBui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netMarket), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarMarketConsoleMenu Menu() => Read<SWGlobalWarMarketConsoleMenu>(MarketBui(), "_menu");
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
        SWGlobalWarTravelConsoleBoundUserInterface TravelBui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarTravelConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTravel), SWGlobalWarTravelConsoleUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Deliver(BoundUserInterface bui, BoundUserInterfaceMessage message) => bui.GetType()
            .GetMethod("ReceiveMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bui, [message]);
    }
}
