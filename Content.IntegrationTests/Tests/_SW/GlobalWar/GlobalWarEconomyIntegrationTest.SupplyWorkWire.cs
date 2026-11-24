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
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SupplyPublishedWorkUsesActualHeldCargoAndRevocationDoesNotCancelOrder(bool pulled)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var markets = server.System<SWGlobalWarMarketSystem>();
        var registry = server.System<SWGlobalWarAssignmentsSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var workerSession = server.PlayerMan.Sessions.Single();
        await server.AddDummySessions(2);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(s =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(s) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(s.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var others = server.PlayerMan.Sessions.Where(s => s != workerSession).ToArray();
        var all = new[] { workerSession, others[0], others[1] };
        string[] jobs = ["SWPoliticsTestWorker", "SWPoliticsTestHead", "SWDiplomacyTestEHead"];
        for (var i = 0; i < all.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
                .GetPreferences(all[i].UserId).SelectedCharacter;
            ProtoId<JobPrototype> job = jobs[i];
            await pair.SetJobPriorities(all[i].UserId,
                profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        }
        var savedTime = timing.TimeBase;
        EntityUid terminal = default;
        EntityUid crate = default;
        EntityUid factory = default;
        EntityUid arsenal = default;
        EntityUid renamedPoint = default;
        string originalArsenalName = string.Empty;
        EntityCoordinates sourcePoint = default;
        EntityCoordinates receivePoint = default;
        NetEntity netTerminal = default;
        SWGlobalWarAssignment worker = default!;
        SWGlobalWarAssignment head = default!;
        Guid generation = default;
        Guid purchaseId = default;
        SWGlobalWarSupplyWorkCommand claim = default!;
        SWGlobalWarSupplyWorkCommand collect = default!;
        SWGlobalWarSupplyWorkCommand accept = default!;
        Guid remainingWork = default;
        var probe = server.System<SWPoliticsNotificationProbeSystem>();
        EntityUid[] actualGoods = [];
        long industryBalance = 0;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestSupplyAuthorityPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(registry.TryGetAssignment(workerSession.UserId, out worker), Is.True);
                Assert.That(registry.TryGetAssignment(others[0].UserId, out head), Is.True);
                Assert.That(worker.Role, Is.EqualTo(SWGlobalWarRole.IndustryWorker));
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                generation = campaign.Comp.Generation;
                foreach (var session in all)
                    server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
                var planets = server.System<SWGlobalWarPlanetSystem>();
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                sourcePoint = new(map!.Value, Vector2.Zero);
                receivePoint = new(map.Value, new Vector2(8, 0));
                factory = server.EntMan.SpawnEntity(null, sourcePoint);
                arsenal = server.EntMan.SpawnEntity(null, receivePoint);
                originalArsenalName = server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName;
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                var otherArsenal = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Value, new Vector2(4, 0)));
                renamedPoint = otherArsenal;
                Assert.That(markets.TryRegisterSupplyStore(generation, otherArsenal, arsenal: true), Is.True);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName,
                    Is.Not.EqualTo(server.EntMan.GetComponent<MetaDataComponent>(otherArsenal).EntityName));
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                model.SupplyPrograms[worker.Faction].Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent = 0;
                Advance(1200);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                purchaseId = Guid.NewGuid();
                Assert.That(markets.TryReserveSupplyPurchase(new(generation, purchaseId, accounts.Comp.Revision,
                    worker.Faction, factory, arsenal, SWGlobalWarSupplyKind.Medicine, 4), out _), Is.True);
                var batch = markets.GetSupplyProductionWork(generation, factory).Single();
                Assert.That(markets.TryBeginSupplyProduction(generation, batch.Request.Operation), Is.True);
                Advance(1651);
                var goods = Enumerable.Range(0, batch.Quantity).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                var transforms = server.System<SharedTransformSystem>();
                transforms.SetCoordinates(worker.Body, sourcePoint);
                crate = server.EntMan.SpawnEntity(pulled ? "SWBoxWood" : "ClothingBackpack", sourcePoint);
                if (pulled)
                    Assert.That(server.System<PullingSystem>().TryStartPull(worker.Body, crate), Is.True);
                else
                    Assert.That(server.System<SharedHandsSystem>().TryPickup(worker.Body, crate), Is.True);
                terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket", sourcePoint);
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, worker.Body);
                Assert.That(markets.GetSupplyView(worker)!.WarAvailable, Is.Null);
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Deliveries.Single().Available, Is.EqualTo(4));
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Deliveries.Single().RecipientName,
                    Is.EqualTo(server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName));
                for (var i = 0; i < 41; i++)
                {
                    var history = new SWGlobalWarSupplyWorkCommand(generation, Guid.NewGuid(), worker.Revision,
                        SWGlobalWarSupplyWorkAction.Claim, purchaseId);
                    Assert.That(markets.TryExecuteSupplyWork(workerSession, history, out _), Is.True);
                    Assert.That(markets.TryExecuteSupplyWork(workerSession, history with { Request = Guid.NewGuid(),
                        Action = SWGlobalWarSupplyWorkAction.Release, Target = history.Request }, out _), Is.True);
                }
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Work.Count, Is.EqualTo(8));
                industryBalance = ledger.GetBalance(worker.Faction, SWGlobalWarAccount.Industry).Balance;
                Assert.That(registry.TryGetAssignment(others[1].UserId, out var enemy), Is.True);
                Assert.That(markets.GetSupplyWorkBoard(enemy)!.Deliveries, Is.Empty);
                Assert.That(markets.TryExecuteSupplyWork(others[1], new(generation, Guid.NewGuid(), enemy.Revision,
                    SWGlobalWarSupplyWorkAction.Claim, purchaseId), out _), Is.False);
                // A real money reservation grants no authority over arbitrary supply cargo.
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Reserve, worker.Faction, SWGlobalWarAccount.Industry, 1,
                    Employee: worker.User, Purpose: "test-unrelated-money-grant"), out var money), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, new(generation, Guid.NewGuid(), worker.Revision,
                    SWGlobalWarSupplyWorkAction.Collect, money), out _), Is.False);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    SWGlobalWarLedgerAction.Release, worker.Faction, SWGlobalWarAccount.Industry, Reservation: money), out _), Is.True);

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
                Assert.That(State().Supply!.WarAvailable, Is.Null);
                Assert.That(State().Supply!.LedgerRevision, Is.Null);
                Select(menu.FindControl<OptionButton>("SupplyDeliveryPicker"), 0);
                Select(menu.FindControl<OptionButton>("SupplyWorkCarrierPicker"), 0);
                menu.FindControl<LineEdit>("SupplyWorkQuantity").SetText("5", invokeEvent: true);
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkClaim").Disabled, Is.True);
                Assert.That(menu.FindControl<RichTextLabel>("SupplyWorkQuantityError").Text, Is.Not.Empty);
                Assert.That(menu.FindControl<LineEdit>("SupplyWorkQuantity").Text, Is.EqualTo("5"));
                menu.FindControl<LineEdit>("SupplyWorkQuantity").SetText("2", invokeEvent: true);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkClaim"));
                claim = menu.FlowDraft.PendingSupplyWork!;
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.TryExecuteSupplyWork(workerSession, claim, out _), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(others[0], claim with { AssignmentRevision = head.Revision }, out _), Is.False);
                Assert.That(markets.TryExecuteSupplyWork(others[0], claim with { Request = Guid.NewGuid(),
                    AssignmentRevision = head.Revision, Quantity = 3 }, out var competing), Is.False,
                    "The two claimed physical items cannot be allocated to a second worker.");
                Assert.That(competing.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.QuantityChanged));
                Assert.That(markets.TryExecuteSupplyWork(workerSession, claim with { Request = Guid.NewGuid() }, out var busy), Is.False);
                Assert.That(busy.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.WorkBusy));
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Work.Single(w => w.Id == claim.Request).Number, Is.EqualTo(1));
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Deliveries.Single().Available, Is.EqualTo(2));
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                if (pulled)
                {
                    var pulling = server.System<PullingSystem>();
                    var transforms = server.System<SharedTransformSystem>();
                    var attempt = new SWGlobalWarSupplyWorkCommand(generation, Guid.NewGuid(), worker.Revision,
                        SWGlobalWarSupplyWorkAction.Collect, claim.Request, Carrier: server.EntMan.GetNetEntity(crate));
                    Assert.That(pulling.TryStopPull(crate, server.EntMan.GetComponent<PullableComponent>(crate)), Is.True);
                    transforms.SetCoordinates(head.Body, sourcePoint);
                    Assert.That(pulling.TryStartPull(head.Body, crate), Is.True);
                    Assert.That(markets.TryExecuteSupplyWork(workerSession, attempt, out _), Is.False);
                    Assert.That(markets.GetSupplyWorkBoard(worker)!.Carriers, Is.Empty);
                    Assert.That(pulling.TryStopPull(crate, server.EntMan.GetComponent<PullableComponent>(crate)), Is.True);
                    Assert.That(pulling.TryStartPull(worker.Body, crate), Is.True);
                    transforms.SetCoordinates(crate, new(sourcePoint.EntityId, new Vector2(5, 0)));
                    Assert.That(markets.TryExecuteSupplyWork(workerSession, attempt with { Request = Guid.NewGuid() }, out _), Is.False);
                    Assert.That(markets.GetSupplyWorkBoard(worker)!.Carriers, Is.Empty);
                    transforms.SetCoordinates(crate, sourcePoint);
                }
            });
            await client.WaitAssertion(() =>
            {
                var work = State().SupplyWork!.Work.Single(w => w.Id == claim.Request);
                Assert.That(work.Number, Is.EqualTo(1));
                Assert.That(Menu().FindControl<RichTextLabel>("SupplyWorkDetails").Text, Does.Contain(work.SourceName));
                Assert.That(Menu().FindControl<RichTextLabel>("SupplyWorkDetails").Text, Does.Contain(work.RecipientName));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyWorkCollect"));
                collect = Menu().FlowDraft.PendingSupplyWork!;
                client.System<ClientUi>().CloseUi(client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key);
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, worker.Body));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(Menu().FlowDraft.PendingSupplyWork, Is.Null);
                Assert.That(Menu().FlowDraft.SupplyWork, Is.EqualTo(claim.Request));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.TryExecuteSupplyWork(workerSession, collect, out _), Is.True);
                Assert.That(markets.SupplyWorkJournal.Count(j => j.Command.Request == collect.Request), Is.EqualTo(1));
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Work.Single(w => w.Id == claim.Request).Status,
                    Is.EqualTo(SWGlobalWarSupplyWorkStatus.Collected));
                Assert.That(server.EntMan.GetComponent<Content.Shared.Storage.StorageComponent>(crate).Container.ContainedEntities.Count, Is.EqualTo(2));
                Assert.That(markets.TryExecuteSupplyWork(others[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyWorkAction.Revoke, claim.Request, HeadDecision: true), out _), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, claim, out _), Is.False);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, collect, out _), Is.False);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, new(generation, Guid.NewGuid(), worker.Revision,
                    SWGlobalWarSupplyWorkAction.Accept, claim.Request), out _), Is.False);
                Assert.That(markets.Simulation!.SupplyPrograms[worker.Faction].Orders[purchaseId].Outstanding, Is.EqualTo(4));
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600));
                Assert.That(markets.SupplyWorkJournal.Last().Command.HeadDecision, Is.True);
                actualGoods = server.EntMan.GetComponent<Content.Shared.Storage.StorageComponent>(crate).Container.ContainedEntities.ToArray();
                var replacement = server.EntMan.SpawnEntity(pulled ? "SWBoxWood" : "ClothingBackpack", sourcePoint);
                var destination = server.EntMan.GetComponent<Content.Shared.Storage.StorageComponent>(replacement).Container;
                foreach (var item in actualGoods)
                    Assert.That(server.System<SharedContainerSystem>().Insert(item, destination), Is.True);
                if (pulled)
                    Assert.That(server.System<PullingSystem>().TryStopPull(crate, server.EntMan.GetComponent<PullableComponent>(crate)), Is.True);
                server.EntMan.DeleteEntity(crate);
                crate = replacement;
                if (pulled)
                    Assert.That(server.System<PullingSystem>().TryStartPull(worker.Body, crate), Is.True);
                else
                    Assert.That(server.System<SharedHandsSystem>().TryPickup(worker.Body, crate), Is.True);
                remainingWork = Guid.NewGuid();
                Assert.That(markets.TryExecuteSupplyWork(others[0], new(generation, remainingWork, head.Revision,
                    SWGlobalWarSupplyWorkAction.Claim, purchaseId, 2), out _), Is.True);
                server.EntMan.DeleteEntity(factory);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkAccept").Disabled, Is.True);
                var recovery = State().SupplyWork!.Deliveries.FindIndex(d => d.Carrier != null);
                Assert.That(recovery, Is.GreaterThanOrEqualTo(0));
                Select(menu.FindControl<OptionButton>("SupplyDeliveryPicker"), recovery);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyWorkClaim"));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() => Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyWorkAccept")));
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(600),
                    "A crate at the source is not cargo delivered to the receiving point eight metres away.");
                server.System<SharedTransformSystem>().SetCoordinates(worker.Body, receivePoint);
                if (pulled)
                    server.System<SharedTransformSystem>().SetCoordinates(crate, receivePoint);
                server.System<SharedTransformSystem>().SetCoordinates(terminal, receivePoint);
                probe.LedgerChanged = () =>
                {
                    Assert.That(markets.GetSupplyWorkBoard(worker)!.Work.Any(w => w.Status == SWGlobalWarSupplyWorkStatus.Completed), Is.True);
                    Assert.That(markets.GetSupplyWorkResults(worker).Any(r => r.Request == accept.Request && r.Success), Is.True);
                };
            });
            await pair.RunTicksSync(30);
            await client.WaitAssertion(() =>
            {
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyWorkAccept"));
                accept = Menu().FlowDraft.PendingSupplyWork!;
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                probe.LedgerChanged = null;
                Assert.That(markets.TryExecuteSupplyWork(workerSession, accept, out _), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, accept, out _), Is.True);
                var order = markets.Simulation!.SupplyPrograms[worker.Faction].Orders[purchaseId];
                Assert.That(order.Accepted, Is.EqualTo(2));
                Assert.That(order.Outstanding, Is.EqualTo(2));
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(300));
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industryBalance + 300));
                Assert.That(markets.SupplyWorkJournal.Count(j => j.Command.Request == accept.Request), Is.EqualTo(1));
                Assert.That(server.System<SharedContainerSystem>().TryGetContainer(arsenal, "sw-program-supply", out var receiving), Is.True);
                Assert.That(receiving!.ContainedEntities.Count, Is.EqualTo(2));
                Assert.That(receiving.ContainedEntities, Is.EquivalentTo(actualGoods));
                Assert.That(server.EntMan.GetComponent<Content.Shared.Storage.StorageComponent>(crate).Container.ContainedEntities, Is.Empty);
                Assert.That(markets.GetSupplyWorkBoard(worker)!.Deliveries, Is.Empty,
                    "The destroyed source prevents fresh escrow collection, but did not block actual in-transit cargo.");
                Assert.That(markets.TryConfigureSupply(others[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyAction.CancelPurchase, SWGlobalWarSupplyKind.Medicine, Target: purchaseId),
                    out var waiting), Is.False);
                Assert.That(waiting.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.ReturnPending));
                Assert.That(markets.TryExecuteSupplyWork(others[0], new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyWorkAction.Collect, remainingWork), out var returnBlocked), Is.False);
                Assert.That(returnBlocked.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.ReturnPending));
                Assert.That(markets.GetSupplyWorkBoard(head)!.Work.Single(w => w.Id == remainingWork).ReturnPending, Is.True);
                Assert.That(ledger.GetBalance(worker.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(300));
                Assert.That(registry.TryExecute(workerSession, new(generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign,
                    worker.Revision, worker.User, worker.Revision), out _), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(workerSession, accept, out _), Is.False);
                Assert.That(markets.GetSupplyWorkBoard(worker), Is.Null);
                Assert.That(markets.GetSupplyWorkResults(worker), Is.Empty);
                server.System<MetaDataSystem>().SetEntityName(renamedPoint, "test-player-renamed-point");
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                markets.StopMarkets(campaign.Owner);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName, Is.EqualTo(originalArsenalName));
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(renamedPoint).EntityName, Is.EqualTo("test-player-renamed-point"));
                markets.InitializeMarkets();
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                var nextLabel = server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName;
                Assert.That(nextLabel, Is.Not.EqualTo(originalArsenalName));
                markets.StopMarkets(campaign.Owner);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(arsenal).EntityName, Is.EqualTo(originalArsenalName));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                probe.LedgerChanged = null;
                if (clock.TryGetCampaign(out var campaign))
                    ticker.EndGameRule(campaign.Owner);
                timing.TimeBase = savedTime;
                ticker.SetGamePreset(originalPreset);
            });
        }
        await pair.CleanReturnAsync();

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
