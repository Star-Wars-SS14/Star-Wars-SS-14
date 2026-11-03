using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Damage.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestPrototypes]
    private const string SupplyTransportWirePrototypes = """
- type: entity
  parent: SWSpawnTestSupplyAuthorityRule
  id: SWSpawnTestSupplyTransportWireRule
  components:
  - type: SWGlobalWarGamerule
    planets: [SWSpawnTestSupplyTransportHome, SWSpawnTestSupplyTransportFarm, SWSpawnTestSupplyTransportIndustry, SWSpawnTestSupplyTransportTransit, SWSpawnTestLnEHome]
  - type: SWGlobalWarSpawnConfiguration
    roles:
      SWPoliticsTestHead: { faction: Republic, role: Head }
      SWPoliticsTestWorker: { faction: Republic, role: LogisticsWorker }

- type: gamePreset
  id: SWSpawnTestSupplyTransportWirePreset
  name: sw-global-war-title
  description: sw-global-war-description
  rules: [SWSpawnTestSupplyTransportWireRule]
""";

    [Test]
    public async Task SupplyTransportBuiKeepsExactStoreAndSeparatelyApprovesNewArsenal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var ticker = server.System<GameTicker>();
        var originalPreset = ticker.Preset;
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var markets = server.System<SWGlobalWarMarketSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var session = server.PlayerMan.Sessions.Single();
        await server.AddDummySessions(1);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<UserDbDataManager>().IsLoadComplete(p) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var worker = server.PlayerMan.Sessions.Single(p => p.UserId != session.UserId);
        await pair.SetJobPriorities(worker.UserId, ("SWPoliticsTestWorker", JobPriority.High));
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        EntityUid actor = default;
        EntityUid terminal = default;
        NetEntity netTerminal = default;
        NetEntity warehouseNet = default;
        NetEntity newArsenalNet = default;
        EntityUid primaryStore = default;
        Guid otherPartId = default;
        Guid secondPurchaseId = default;
        Guid generation = default;
        Guid purchaseId = default;
        Guid partId = default;
        SWGlobalWarSupplyPurchase purchase = default!;
        SWGlobalWarReturnCommand returnCommand = default!;
        SWGlobalWarSupplyCommand approval = default!;
        Guid returnGrant = default;
        var savedTime = timing.TimeBase;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestSupplyTransportWirePreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(assignments.TryGetAssignment(session.UserId, out var head), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                generation = campaign.Comp.Generation;
                actor = head.Body;
                server.System<GodmodeSystem>().EnableGodmode(actor);
                Assert.That(assignments.TryGetAssignment(worker.UserId, out var workerAssignment), Is.True);
                Assert.That(workerAssignment.Role, Is.EqualTo(SWGlobalWarRole.LogisticsWorker));
                server.System<GodmodeSystem>().EnableGodmode(workerAssignment.Body);
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 40;
                model.SupplyPrograms[head.Faction].Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent = 0;
                var factory = Store("SWSpawnTestSupplyTransportFarm", false);
                var primary = Store("SWSpawnTestSupplyTransportHome", true);
                primaryStore = primary;
                var warehouse = Store("SWSpawnTestSupplyTransportTransit", false);
                warehouseNet = server.EntMan.GetNetEntity(warehouse);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), factory, head.Faction,
                    SWGlobalWarSupplyKind.Medicine, 4);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                Advance(1800);
                var goods = Enumerable.Range(0, 20).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Assert.That(ledger.TryGetLedger(out var state), Is.True);
                purchaseId = Guid.NewGuid();
                Assert.That(markets.TryReserveSupplyPurchase(new(generation, purchaseId, state.Comp.Revision,
                    head.Faction, factory, primary, SWGlobalWarSupplyKind.Medicine, 8), out purchase), Is.True);
                secondPurchaseId = Guid.NewGuid();
                Assert.That(markets.TryReserveSupplyPurchase(new(generation, secondPurchaseId, state.Comp.Revision,
                    head.Faction, factory, primary, SWGlobalWarSupplyKind.Medicine, 8), out var second), Is.True);
                Assert.That(second.Number, Is.Not.EqualTo(purchase.Number));
                Assert.That(markets.TryCancelSupplyPurchase(generation, secondPurchaseId), Is.True);
                newArsenalNet = server.EntMan.GetNetEntity(Store("SWSpawnTestSupplyTransportTransit", true, 10));
                model.Markets["SWSpawnTestSupplyTransportFarm"].SendBudget = 2;
                Advance(1801);
                model.Markets["SWSpawnTestSupplyTransportFarm"].HubCapacity = 0;
                partId = model.Shipments.Single(s => s.SupplyManifest?.Items.Count == 5).Id;
                otherPartId = model.Shipments.Single(s => s.SupplyManifest?.Items.Count == 3).Id;
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplyTransportHome", out var world), Is.True);
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(world!.Value);
                var steps = server.System<SWGlobalWarCampaignStepSystem>();
                Assert.That(steps.TryQueueOwnership(world.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, planet.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket", server.EntMan.GetComponent<TransformComponent>(actor).Coordinates);
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor);
            });
            await pair.RunTicksSync(120);
            await server.WaitAssertion(() =>
            {
                var part = markets.Simulation!.Shipments.Single(s => s.Id == otherPartId);
                Assert.That(part.ArrivesAt, Is.Null);
                Assert.That(markets.Simulation.TryReturnShipment(new(generation, Guid.NewGuid(), part.Id,
                    SWGlobalWarFaction.Republic, "SWSpawnTestSupplyTransportTransit", part.Quantity,
                    server.EntMan.GetEntity(warehouseNet)), out _), Is.True);
                Advance((int) markets.Simulation.Time.TotalSeconds + 1);
                Assert.That(part.SupplyManifest!.Stored, Is.True);
            });
            await client.WaitAssertion(() =>
            {
                Assert.That(State().Shipments.Single(s => s.Id == partId).Supply!.Number, Is.EqualTo(purchase.Number));
                Assert.That(State().SupplyWork!.Deliveries.Any(d => d.Purchase == purchaseId), Is.False);
                ChooseReturn(warehouseNet);
                Toggle(Menu().FindControl<CheckBox>("ReturnHead"));
                Select(Menu().FindControl<OptionButton>("ReturnWorker"),
                    State().FlowWorkers.FindIndex(w => w.User == worker.UserId));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnGrant"));
                Assert.That(Menu().FlowDraft.PendingReturn!.Action, Is.EqualTo(SWGlobalWarReturnAction.Grant));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var grant = State().ReturnGrants.Single();
                returnGrant = grant.Id;
                Assert.That(grant.DestinationStore, Is.EqualTo(warehouseNet));
                Assert.That(grant.Target!.Store, Is.EqualTo(warehouseNet));
                Assert.That(grant.Target.AcceptsSupply, Is.False);
                Select(Menu().FindControl<OptionButton>("ReturnGrantPicker"),
                    Array.IndexOf(Read<string[]>(Menu(), "_returnGrantIds"), grant.Id.ToString()));
                Assert.That(Menu().FindControl<RichTextLabel>("ReturnScope").Text, Does.Contain(grant.Target.Name));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnSend"));
                returnCommand = Menu().FlowDraft.PendingReturn!;
                Assert.That(returnCommand.DestinationStore, Is.EqualTo(warehouseNet));
                Assert.That(returnCommand.HeadDecision, Is.True);
                Menu().Close();
            });
            await pair.RunTicksSync(60);
            await server.WaitPost(() =>
            {
                var source = markets.Simulation!.Markets["SWSpawnTestSupplyTransportFarm"];
                source.HubCapacity = 60;
                source.SendBudget = 5;
            });
            await pair.RunTicksSync(180);
            await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                if (Menu().FlowDraft.PendingReturn != null)
                {
                    Assert.That(Menu().FlowDraft.PendingReturn, Is.EqualTo(returnCommand));
                    Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnRetry"));
                }
                Assert.That(Menu().FlowDraft.ReturnStore, Is.EqualTo(warehouseNet));
                var grant = State().ReturnGrants.Single(g => g.Id == returnGrant);
                Assert.That(Menu().FindControl<RichTextLabel>("ReturnScope").Text, Does.Contain(grant.Target!.Name));
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.ReturnJournal.Count(j => j.Command.Request == returnCommand.Request), Is.EqualTo(1));
                Assert.That(markets.TryConfigureReturn(session, returnCommand with { DestinationStore = newArsenalNet }, out _), Is.False);
                Assert.That(purchase.Obligation.Accepted, Is.Zero);
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
                Assert.That(markets.Simulation!.Shipments.Single(s => s.Id == partId).SupplyManifest!.Stored, Is.True);
            });
            await client.WaitAssertion(() => Select(Menu().FindControl<OptionButton>("SupplyKindPicker"), (int) SWGlobalWarSupplyKind.Medicine));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                var orders = State().Supply!.Orders;
                Assert.That(orders.Single(o => o.Id == purchaseId).Number,
                    Is.Not.EqualTo(orders.Single(o => o.Id == secondPurchaseId).Number));
                Select(menu.FindControl<OptionButton>("SupplyOrderPicker"), Array.IndexOf(Read<Guid[]>(menu, "_supplyOrders"), purchaseId));
                Select(menu.FindControl<OptionButton>("SupplyArsenalPicker"), Array.IndexOf(Read<NetEntity[]>(menu, "_supplyArsenals"), newArsenalNet));
                Toggle(menu.FindControl<CheckBox>("SupplyHeadDecision"));
                Assert.That(menu.FlowDraft.SupplyArsenalDecision!.Outstanding, Is.EqualTo(8));
                Assert.That(menu.FindControl<RichTextLabel>("SupplyOrderParts").Text, Does.Contain(partId.ToString()[..8]));
                var receipt = State().ReturnResults.Single(r => r.Request == returnCommand.Request).Receipt!;
                Assert.That(receipt.Target!.Store, Is.EqualTo(warehouseNet));
                Assert.That(receipt.Target.AcceptsSupply, Is.False);
                Assert.That(menu.FindControl<RichTextLabel>("ReturnReceipt").Text, Does.Contain(receipt.Target.Name));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplyTransportHome", out var world), Is.True);
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(world!.Value);
                var steps = server.System<SWGlobalWarCampaignStepSystem>();
                Assert.That(steps.TryQueueOwnership(world.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Republic, planet.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                var part = markets.Simulation!.Shipments.Single(s => s.Id == otherPartId);
                Assert.That(markets.Simulation.TryReturnShipment(new(generation, Guid.NewGuid(), part.Id,
                    SWGlobalWarFaction.Republic, "SWSpawnTestSupplyTransportHome", part.Quantity, primaryStore), out _), Is.True);
                Advance((int) markets.Simulation.Time.TotalSeconds + 5);
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(3));
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(5));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(Menu().FlowDraft.SupplyArsenalDecision!.Outstanding, Is.EqualTo(8));
                Assert.That(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyApproveArsenal").Disabled, Is.True);
                Assert.That(Menu().FindControl<RichTextLabel>("SupplyArsenalChanged").Text, Does.Contain("8").And.Contain("5"));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyApproveArsenal"));
                Assert.That(Menu().FlowDraft.PendingSupply, Is.Null);
                var stored = State().Shipments.Single(s => s.Id == partId);
                Assert.That(stored.Supply!.Accepted, Is.EqualTo(3));
                Assert.That(stored.Supply.Outstanding, Is.EqualTo(5));
                Assert.That(stored.Supply.AcceptsSupply, Is.False);
                ChooseReturn(newArsenalNet);
                Assert.That(State().ReturnCandidates.Single(c => c.Part.Shipment == partId).Destinations
                    .Single(d => d.Store == newArsenalNet).AcceptsSupply, Is.False);
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnSend"));
                Assert.That(Menu().FlowDraft.PendingReturn!.DestinationStore, Is.EqualTo(newArsenalNet));
            });
            await pair.RunTicksSync(90);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyArsenalReload"));
                Assert.That(menu.FlowDraft.SupplyArsenalDecision!.Outstanding, Is.EqualTo(5));
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyApproveArsenal").Disabled, Is.False);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyApproveArsenal"));
                approval = menu.FlowDraft.PendingSupply!;
                Assert.That(approval.Action, Is.EqualTo(SWGlobalWarSupplyAction.ApproveArsenal));
                Assert.That(approval.Recipient, Is.EqualTo(newArsenalNet));
                Assert.That(approval.Quantity, Is.EqualTo(5));
                Assert.That(approval.HeadDecision, Is.True);
                menu.Close();
            });
            await pair.RunTicksSync(60);
            await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                if (menu.FlowDraft.PendingSupply != null)
                {
                    Assert.That(menu.FlowDraft.PendingSupply, Is.EqualTo(approval));
                    Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyRetry"));
                }
                Assert.That(menu.FlowDraft.SupplyArsenal, Is.EqualTo(newArsenalNet));
                var receipt = State().ReturnResults.Last(r => r.Receipt?.Target?.Store == newArsenalNet).Receipt!;
                Assert.That(receipt.Target!.AcceptsSupply, Is.False,
                    "The original return authorized storage; later arsenal approval must not rewrite that receipt.");
                Assert.That(menu.FindControl<RichTextLabel>("ReturnReceipt").Text, Does.Contain(receipt.Target.Name));
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(8));
                Assert.That(purchase.ArsenalRevision, Is.EqualTo(1));
                Assert.That(markets.SupplyJournal.Count(j => j.Request == approval.Request), Is.EqualTo(1));
                Assert.That(markets.TryConfigureSupply(session, approval, out _), Is.True);
                Assert.That(markets.TryConfigureSupply(session, approval with { Quantity = 3 }, out _), Is.False);
                Assert.That(purchase.ArsenalRevision, Is.EqualTo(1));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Reserved, Is.Zero);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset(originalPreset);
                timing.TimeBase = savedTime;
            });
        }
        await pair.CleanReturnAsync();

        EntityUid Store(string world, bool arsenal, float x = 0)
        {
            Assert.That(planets.TryGetPlanet(world, out var entity), Is.True);
            Assert.That(planets.TryEnsureLocation(entity!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
            var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, new Vector2(x, 0)));
            Assert.That(markets.TryRegisterSupplyStore(generation, store, arsenal), Is.True);
            return store;
        }
        void Advance(int seconds)
        {
            Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
            timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign), timing.TimeBase.Item2);
            markets.Update(0);
        }
        SWGlobalWarMarketConsoleMenu Menu()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return Read<SWGlobalWarMarketConsoleMenu>(bui!, "_menu");
        }
        SWGlobalWarMarketConsoleState State() => Read<SWGlobalWarMarketConsoleState>(Menu(), "_state");
        void ChooseReturn(NetEntity store)
        {
            typeof(SWGlobalWarMarketConsoleMenu).GetMethod("SelectReturn", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Menu(), [partId]);
            var destinations = State().ReturnCandidates.Single(c => c.Part.Shipment == partId).Destinations;
            Select(Menu().FindControl<OptionButton>("ReturnDestination"), destinations.FindIndex(d => d.Store == store));
            Assert.That(Menu().FindControl<SWGlobalWarConsoleButton>("ReturnSend").Disabled, Is.False);
        }
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
        static void Toggle(CheckBox button)
        {
            button.Pressed = true;
            ((Action<BaseButton.ButtonToggledEventArgs>) typeof(BaseButton)
                .GetField("OnToggled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(true, button, null!));
        }
    }
}
