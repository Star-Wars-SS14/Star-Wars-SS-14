using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
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
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task HubWindowReopensTheSameWorkAndRejectsOldPrivateSnapshotsAfterResignation()
    {
        // Fresh: this test waits for the console's periodic snapshot. Tests that jump the clock forward and restore it
        // leave that one-second gate up to half an hour ahead on a reused server, and no snapshot then arrives.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true, DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var session = server.PlayerMan.Sessions.Single();
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        EntityUid hub = default;
        EntityUid actor = default;
        NetEntity netHub = default;
        NetEntity viewer = default;
        SWGlobalWarAssignment assignment = default!;
        SWGlobalWarHubCommand command = default!;
        SWGlobalWarHubView oldView = default!;
        Guid oldSubscription = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
            ticker.SetGamePreset("SWSpawnTestEconomyViewPreset");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(registry.TryGetAssignment(session.UserId, out assignment), Is.True);
            actor = assignment.Body;
            server.System<GodmodeSystem>().EnableGodmode(actor);
            var planets = server.System<SWGlobalWarPlanetSystem>();
            Assert.That(planets.TryGetPlanet("SWSpawnTestEconomyViewRFarm", out var planet), Is.True);
            Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Orbit, out var map, out _), Is.True);
            Assert.That(planets.TryGetOrbitStation(planet.Value, out var grid), Is.True);
            server.System<SharedPhysicsSystem>().SetBodyType(grid!.Value, BodyType.Static);
            var maps = server.System<SharedMapSystem>();
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -2; x <= 3; x++)
            for (var y = -2; y <= 3; y++)
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            maps.SetTiles(grid.Value, server.EntMan.GetComponent<MapGridComponent>(grid.Value), tiles);
            maps.SetPaused(map!.Value, false);
            server.System<SharedTransformSystem>().SetCoordinates(actor, new EntityCoordinates(grid.Value, .5f, 1.5f));
            hub = server.EntMan.SpawnEntity("SWSpawnTestOrbitalHub", new EntityCoordinates(grid.Value, .5f, .5f));
            netHub = server.EntMan.GetNetEntity(hub);
            viewer = server.EntMan.GetNetEntity(actor);
            Assert.That(ledger.TryExecuteSystem(new(assignment.Generation, Guid.NewGuid(), 0,
                SWGlobalWarLedgerAction.Credit, assignment.Faction, SWGlobalWarAccount.Logistics, 150000,
                Purpose: "hub-wire-fixture"), out _), Is.True);
        });
        await pair.RunTicksSync(30);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(hub, SWGlobalWarHubUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var bui = HubBui();
            var menu = Menu();
            oldView = Read<SWGlobalWarHubView>(menu, "_state");
            oldSubscription = Read<Guid>(bui, "_subscription");
            Assert.That(oldView.Ready, Is.True);
            Assert.That(oldView.HeadDecisionRequired, Is.False, "The head covers a vacant logistics ministry.");
            var start = menu.FindControl<Button>("Start");
            Assert.That(start.Disabled, Is.False);
            Press(start);
            command = menu.Draft.Start!;
            Assert.That(command, Is.Not.Null);
            Assert.That(start.Disabled, Is.True);
        });
        await pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            var result = markets.GetHubWorkResult(session, hub, assignment.Generation, assignment.Revision, command.Request);
            Assert.That(result!.Status, Is.EqualTo(SWGlobalWarHubStatus.Working));
            Assert.That(ledger.GetBalance(assignment.Faction, SWGlobalWarAccount.Logistics),
                Is.EqualTo(new SWGlobalWarAccountBalance(300000, 300000)));
            server.System<ServerUi>().CloseUi(hub, SWGlobalWarHubUiKey.Key, actor);
        });
        await pair.RunTicksSync(20);
        await client.WaitAssertion(() =>
        {
            // Simulate a missing start reply in the durable client cache. Reopening must reconcile this exact request.
            var draft = client.System<SWGlobalWarHubViewSystem>().GetDraft(netHub);
            draft.Start = command;
            draft.Maximum = "75";
            draft.Minutes = "7";
        });
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(hub, SWGlobalWarHubUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var bui = HubBui();
            var menu = Menu();
            var state = Read<SWGlobalWarHubView>(menu, "_state");
            Assert.That(state.Active!.Request, Is.EqualTo(command.Request));
            Assert.That(menu.Draft.Pending, Is.False, "The fresh own receipt resolves the old pending start.");
            Assert.That(menu.FindControl<LineEdit>("Maximum").Text, Is.EqualTo("75"));
            Assert.That(menu.FindControl<LineEdit>("Minutes").Text, Is.EqualTo("7"));
            Assert.That(Read<Guid>(bui, "_subscription"), Is.Not.EqualTo(oldSubscription));
            Deliver(bui, new SWGlobalWarHubSnapshot(oldView, viewer, oldSubscription, long.MaxValue));
            Assert.That(Read<SWGlobalWarHubView>(menu, "_state"), Is.SameAs(state));
        });
        // Keep normal server and client ticks for the complete physical interval; no campaign-clock jumps here.
        await pair.RunSeconds(121);
        await server.WaitAssertion(() =>
        {
            var result = markets.GetHubWorkResult(session, hub, assignment.Generation, assignment.Revision, command.Request);
            Assert.That(result!.Status, Is.EqualTo(SWGlobalWarHubStatus.Completed));
            Assert.That(markets.Simulation!.Markets["SWSpawnTestEconomyViewRFarm"].HubCapacity, Is.EqualTo(50));
            Assert.That(ledger.GetBalance(assignment.Faction, SWGlobalWarAccount.Logistics),
                Is.EqualTo(new SWGlobalWarAccountBalance(0, 0)));
            Assert.That(markets.TryStartHubUpgrade(session, hub, command, out var replay, out _), Is.True);
            Assert.That(replay, Is.EqualTo(result));
            server.System<ServerUi>().CloseUi(hub, SWGlobalWarHubUiKey.Key, actor);
            Assert.That(markets.TryCancelHubUpgrade(session, hub, assignment.Generation, assignment.Revision,
                command.Request, out var cancelResult), Is.True);
            Assert.That(cancelResult!.Status, Is.EqualTo(SWGlobalWarHubStatus.Completed));
        });
        await pair.RunTicksSync(20);
        await client.WaitPost(() =>
        {
            var draft = client.System<SWGlobalWarHubViewSystem>().GetDraft(netHub);
            draft.Cancel = command.Request;
            draft.LastCancellation = command.Request;
        });
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(hub, SWGlobalWarHubUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            Assert.That(menu.Draft.Pending, Is.False);
            Assert.That(FormattedMessage.RemoveMarkupPermissive(menu.FindControl<RichTextLabel>("Result").Text ?? string.Empty), Does.Contain(
                Loc.GetString("sw-global-war-hub-already-completed", ("capacity", 50), ("price", 300000))));
            Assert.That(menu.FindControl<RichTextLabel>("Result").Text, Does.Not.Contain(
                Loc.GetString("sw-global-war-hub-started")));
        });
        // No request is sent here: these pending commands model an unanswered command in the
        // durable cache while a real unrelated ownership transaction publishes its events.
        for (var pendingKind = 0; pendingKind < 4; pendingKind++)
        {
            var pendingRequest = Guid.NewGuid();
            await client.WaitPost(() =>
            {
                var draft = Menu().Draft;
                draft.ClearPending();
                if (pendingKind == 0)
                    draft.Start = command with { Request = pendingRequest, ExpectedCapacity = 50 };
                else if (pendingKind == 1)
                    draft.GrantCommand = new(assignment.Generation, pendingRequest, assignment.Revision,
                        assignment.User, assignment.Revision, Guid.NewGuid(), 75, TimeSpan.FromMinutes(10));
                else if (pendingKind == 2)
                    draft.Revoke = new(assignment.Generation, pendingRequest, assignment.Revision, Guid.NewGuid(), false, Guid.Empty);
                else
                    draft.Cancel = command.Request;
                draft.LastCancellation = command.Request;
            });
            await server.WaitAssertion(() =>
            {
                var planets = server.System<SWGlobalWarPlanetSystem>();
                Assert.That(planets.TryGetPlanet("SWSpawnTestEconomyViewR", out var other), Is.True);
                var data = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(other!.Value);
                var steps = server.System<SWGlobalWarCampaignStepSystem>();
                var sequence = Read<long>(server.System<SWGlobalWarHubConsoleSystem>(), "_sequence");
                Assert.That(steps.TryQueueOwnership(other.Value, SWGlobalWarPlanetLocation.Surface,
                    pendingKind % 2 == 0 ? SWGlobalWarFaction.Empire : SWGlobalWarFaction.Republic,
                    data.OwnershipRevision, assignment.Generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(assignment.Generation, server.ResolveDependency<IGameTiming>().CurTick), Is.True);
                Assert.That(Read<long>(server.System<SWGlobalWarHubConsoleSystem>(), "_sequence"), Is.EqualTo(sequence),
                    "The closing ownership event sends no transient denied snapshot.");
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var draft = Menu().Draft;
                Assert.That(draft.LastCancellation, Is.EqualTo(command.Request));
                Assert.That(draft.Maximum, Is.EqualTo("75"));
                if (pendingKind == 0)
                    Assert.That(draft.Start!.Request, Is.EqualTo(pendingRequest));
                else if (pendingKind == 1)
                    Assert.That(draft.GrantCommand!.Request, Is.EqualTo(pendingRequest));
                else if (pendingKind == 2)
                    Assert.That(draft.Revoke!.Request, Is.EqualTo(pendingRequest));
                else
                {
                    Assert.That(draft.Pending, Is.False, "A valid completed receipt resolves cancellation.");
                    Assert.That(FormattedMessage.RemoveMarkupPermissive(Menu().FindControl<RichTextLabel>("Result").Text ?? string.Empty), Does.Contain(
                        Loc.GetString("sw-global-war-hub-already-completed", ("capacity", 50), ("price", 300000))));
                }
            });
        }
        await client.WaitAssertion(() =>
        {
            var menu = Menu();
            var state = Read<SWGlobalWarHubView>(menu, "_state");
            var receipt = menu.Draft.Confirmed!;
            foreach (var status in new[] { SWGlobalWarHubStatus.Completed, SWGlobalWarHubStatus.Cancelled })
            {
                // Cancelled history is a presentation fixture; the actual completed receipt remains unchanged server-side.
                var historical = receipt with { Status = status, Failure = SWGlobalWarHubFailure.CancelledByUser };
                var snapshot = state with { OwnResults = [historical] };
                menu.Draft.Confirmed = historical;
                var grantRequest = Guid.NewGuid();
                menu.Draft.GrantCommand = new(assignment.Generation, grantRequest, assignment.Revision,
                    assignment.User, assignment.Revision, Guid.NewGuid(), 999, TimeSpan.FromMinutes(10));
                menu.ReceiveReply(new(grantRequest, false, SWGlobalWarHubFailure.Denied, null, viewer, Guid.Empty));
                menu.UpdateState(snapshot);
                Assert.That(menu.FindControl<RichTextLabel>("Result").Text,
                    Is.EqualTo(Loc.GetString("sw-global-war-hub-failure-denied")));
                Assert.That(menu.Draft.Confirmed, Is.EqualTo(historical));
                Assert.That(menu.FindControl<RichTextLabel>("Receipt").Visible, Is.True);
                var newStart = command with { Request = Guid.NewGuid(), ExpectedCapacity = 50 };
                menu.Draft.Start = newStart;
                menu.Draft.LastAttempt = newStart.Request;
                menu.ReceiveReply(new(newStart.Request, false, SWGlobalWarHubFailure.Funding, null, viewer, Guid.Empty));
                menu.UpdateState(snapshot);
                Assert.That(menu.FindControl<RichTextLabel>("Result").Text,
                    Is.EqualTo(Loc.GetString("sw-global-war-hub-failure-funding")));
                Assert.That(menu.Draft.Confirmed, Is.EqualTo(historical));
            }
        });
        await server.WaitAssertion(() =>
        {
            server.System<ServerUi>().CloseUi(hub, SWGlobalWarHubUiKey.Key, actor);
            Assert.That(registry.TryExecute(session, new(assignment.Generation, Guid.NewGuid(),
                SWGlobalWarAppointmentAction.Resign, assignment.Revision, assignment.User, assignment.Revision), out _), Is.True);
            var restricted = markets.GetHubView(session, hub);
            Assert.That(restricted.Ready, Is.False);
            Assert.That(restricted.OwnResults, Is.Empty);
            Assert.That(restricted.Grants, Is.Empty);
            Assert.That(restricted.Orders, Is.Empty);
        });
        await pair.RunTicksSync(20);
        await server.WaitPost(() => server.System<ServerUi>().OpenUi(hub, SWGlobalWarHubUiKey.Key, actor));
        await pair.RunTicksSync(60);
        await client.WaitAssertion(() =>
        {
            var bui = HubBui();
            var menu = Menu();
            var restricted = Read<SWGlobalWarHubView>(menu, "_state");
            Assert.That(restricted.Ready, Is.False);
            Assert.That(menu.Draft.Pending, Is.False);
            Assert.That(menu.Draft.Maximum, Is.Empty);
            Deliver(bui, new SWGlobalWarHubSnapshot(oldView, viewer, oldSubscription, long.MaxValue));
            Deliver(bui, new SWGlobalWarHubSnapshot(oldView, netHub, Read<Guid>(bui, "_subscription"), long.MaxValue));
            Assert.That(Read<SWGlobalWarHubView>(menu, "_state"), Is.SameAs(restricted));
            Assert.That(menu.FindControl<Button>("Start").Disabled, Is.True);
        });
        await server.WaitPost(() => ticker.SetGamePreset(originalPreset));
        await pair.CleanReturnAsync();

        SWGlobalWarHubBoundUserInterface HubBui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarHubBoundUserInterface>(client.EntMan.GetEntity(netHub),
                SWGlobalWarHubUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarHubMenu Menu() => Read<SWGlobalWarHubMenu>(HubBui(), "_menu");
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Deliver(BoundUserInterface bui, BoundUserInterfaceMessage message) => bui.GetType()
            .GetMethod("ReceiveMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bui, [message]);
    }
}
