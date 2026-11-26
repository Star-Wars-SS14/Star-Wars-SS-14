using Robust.Shared.Utility;
using System;
using System.Linq;
using System.Numerics;
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
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [Test]
    public async Task SupplyWindowFiltersBeforePagingPreservesConflictAndReturnAcrossOtherOperations()
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
        var session = server.PlayerMan.Sessions.Single();
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        EntityUid terminal = default;
        EntityUid actor = default;
        EntityUid factory = default;
        EntityCoordinates factoryPoint = default;
        NetEntity netTerminal = default;
        Guid generation = default;
        Guid orderId = default;
        var bactaOrderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid manualOrder = default;
        SWGlobalWarSupplyCommand manualCommand = default!;
        long quoteRevision = 0;
        Guid returnRequest = default;
        SWGlobalWarSupplyCommand lineCommand = default!;
        var savedTime = timing.TimeBase;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestSupplyAuthorityPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                generation = campaign.Comp.Generation;
                actor = head.Body;
                server.System<GodmodeSystem>().EnableGodmode(actor);
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                var planets = server.System<SWGlobalWarPlanetSystem>();
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                factoryPoint = new EntityCoordinates(map!.Value, Vector2.Zero);
                factory = server.EntMan.SpawnEntity(null, factoryPoint);
                var arsenal = server.EntMan.SpawnEntity(null, factoryPoint);
                Assert.That(markets.TryRegisterSupplyStore(generation, factory), Is.True);
                Assert.That(markets.TryRegisterSupplyStore(generation, arsenal, arsenal: true), Is.True);
                Advance(1200);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                // Real reserve/cancel receipts create mixed history; no fake orders or stock counters are inserted.
                foreach (var kind in new[] { SWGlobalWarSupplyKind.Ration, SWGlobalWarSupplyKind.Medicine })
                for (var i = 0; i < 41; i++)
                {
                    var bacta = kind == SWGlobalWarSupplyKind.Medicine && i == 0;
                    model.SupplyPrograms[head.Faction].Lines[SWGlobalWarSupplyKind.Medicine].Bacta = bacta;
                    var request = new SWGlobalWarSupplyPurchaseRequest(generation, bacta ? bactaOrderId : Guid.NewGuid(), accounts.Comp.Revision,
                        head.Faction, factory, arsenal, kind, 1, bacta);
                    Assert.That(markets.TryReserveSupplyPurchase(request, out _), Is.True);
                    Assert.That(markets.TryCancelSupplyPurchase(generation, request.Operation), Is.True);
                }
                Assert.That(markets.TryReserveSupplyProduction(new(generation, Guid.NewGuid(), factory, head.Faction,
                    SWGlobalWarSupplyKind.Medicine, 4), out var opening), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, opening.Request.Operation), Is.True);
                Advance(3001);
                var openingGoods = Output(opening);
                var containers = server.System<SharedContainerSystem>();
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var inventory), Is.True);
                foreach (var good in openingGoods.Skip(2))
                    Assert.That(containers.Remove(good, inventory!), Is.True);
                var medicine = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    head.Faction, factory, arsenal, SWGlobalWarSupplyKind.Medicine, 8);
                Assert.That(markets.TryReserveSupplyPurchase(medicine, out var purchase), Is.True);
                orderId = medicine.Operation;
                var production = markets.GetSupplyProductionWork(generation, factory).Single();
                Assert.That(production.Quantity, Is.EqualTo(10));
                var planned = markets.GetSupplyView(head, selectedKind: SWGlobalWarSupplyKind.Medicine)!;
                var plannedOrder = planned.Orders.Single(o => o.Id == orderId);
                Assert.That(plannedOrder.InitialReady, Is.EqualTo(2));
                Assert.That(plannedOrder.InitialProduction, Is.EqualTo(6));
                Assert.That(plannedOrder.Batch!.FreeIsPlanned, Is.True);
                Assert.That(plannedOrder.Batch.FreeMarket, Is.EqualTo(1));
                Assert.That(plannedOrder.Batch.FreeLocal, Is.EqualTo(3));
                Assert.That(markets.TryBeginSupplyProduction(generation, production.Request.Operation), Is.True);
                Advance(3902);
                Output(production);
                var ready = markets.GetSupplyView(head, selectedKind: SWGlobalWarSupplyKind.Medicine)!.Orders.Single(o => o.Id == orderId);
                Assert.That(ready.Batch!.FreeIsPlanned, Is.False);
                Assert.That(ready.Batch.AssignedToOrder, Is.EqualTo(6));
                Assert.That(ready.Batch.FreeMarket, Is.EqualTo(1));
                Assert.That(ready.Batch.FreeLocal, Is.EqualTo(3));
                terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket", server.EntMan.GetComponent<TransformComponent>(actor).Coordinates);
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor);

                EntityUid[] Output(SWGlobalWarSupplyProduction batch)
                {
                    var goods = Enumerable.Range(0, batch.Quantity).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                        markets.SupplyProductionOutputCoordinates(batch.Request.Operation))).ToArray();
                    Assert.That(markets.TryCompleteSupplyProduction(generation, batch.Request.Operation, goods), Is.True);
                    return goods;
                }
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
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyToggle"));
                Select(menu.FindControl<OptionButton>("SupplyKindPicker"), (int) SWGlobalWarSupplyKind.Medicine);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                var view = State().Supply!;
                Assert.That(view.TotalOrders, Is.EqualTo(42));
                Assert.That(view.Orders.Count, Is.EqualTo(40));
                Assert.That(view.Orders.All(o => o.Kind == SWGlobalWarSupplyKind.Medicine), Is.True);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyNext"));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(State().Supply!.Page, Is.EqualTo(1));
                Assert.That(State().Supply!.Orders.Count, Is.EqualTo(2));
                Select(Menu().FindControl<OptionButton>("SupplyKindPicker"), (int) SWGlobalWarSupplyKind.Ration);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(State().Supply!.Page, Is.Zero);
                Assert.That(State().Supply!.TotalOrders, Is.EqualTo(41));
                Assert.That(State().Supply!.Orders.All(o => o.Kind == SWGlobalWarSupplyKind.Ration), Is.True);
                Select(Menu().FindControl<OptionButton>("SupplyKindPicker"), (int) SWGlobalWarSupplyKind.Medicine);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                SelectOrder(bactaOrderId);
                Assert.That(menu.FindControl<RichTextLabel>("SupplyOrderDetails").Text,
                    Does.Contain(Loc.GetString("sw-global-war-supply-bacta-item")));
                SelectOrder();
                Assert.That(menu.FindControl<RichTextLabel>("SupplyOrderDetails").Text,
                    Does.Contain(Loc.GetString("sw-global-war-supply-kolto-item")));
                Assert.That(menu.FindControl<RichTextLabel>("SupplyAllocation").Text,
                    Does.Contain(Loc.GetString("sw-global-war-supply-actual-allocation", ("assigned", 6), ("market", 1), ("local", 3))));
                Assert.That(FormattedMessage.RemoveMarkupPermissive(menu.FindControl<RichTextLabel>("SupplyOrderPayment").Text ?? string.Empty),
                    Is.EqualTo(Loc.GetString("sw-global-war-supply-order-payment", ("amount", 1200))));
                menu.FindControl<LineEdit>("SupplyLocal").SetText("101", invokeEvent: true);
                Assert.That(menu.FindControl<RichTextLabel>("SupplyLocalError").Text,
                    Is.EqualTo(Loc.GetString("sw-global-war-supply-invalid-percent")));
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplySave").Disabled, Is.True);
                menu.FindControl<LineEdit>("SupplyLocal").SetText("40", invokeEvent: true);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
                Assert.That(markets.TryConfigureSupply(session, new(generation, Guid.NewGuid(), head.Revision,
                    SWGlobalWarSupplyAction.ConfigureLine, SWGlobalWarSupplyKind.Medicine, LocalPercent: 30), out _), Is.True);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Assert.That(menu.FindControl<LineEdit>("SupplyLocal").Text, Is.EqualTo("40"));
                Assert.That(menu.FindControl<RichTextLabel>("SupplyServerValues").Text,
                    Does.Contain(Loc.GetString("sw-global-war-supply-server-values", ("local", 30),
                        ("item", Loc.GetString("sw-global-war-supply-kolto-item")))));
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplySave").Disabled, Is.True);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyReapply"));
                lineCommand = menu.FlowDraft.PendingSupply!;
                Assert.That(lineCommand.LocalPercent, Is.EqualTo(40));
                Assert.That(lineCommand.ExpectedRevision, Is.EqualTo(1));
                menu.Close();
            });
            await pair.RunTicksSync(60);
            await server.WaitPost(() => server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                if (Menu().FlowDraft.PendingSupply != null)
                    Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyRetry"));
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.SupplyJournal.Count(j => j.Request == lineCommand.Request), Is.EqualTo(1));
                Assert.That(markets.Simulation!.SupplyPrograms[SWGlobalWarFaction.Republic]
                    .Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent, Is.EqualTo(40));
                server.EntMan.DeleteEntity(factory);
            });
            await client.WaitAssertion(() =>
            {
                SelectOrder();
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyCancel"));
                returnRequest = Menu().FlowDraft.PendingSupply!.Request;
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Assert.That(menu.FlowDraft.SupplyReturnRetries[orderId].Request, Is.EqualTo(returnRequest));
                menu.FindControl<LineEdit>("SupplyLocal").SetText("45", invokeEvent: true);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplySave"));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(Menu().FlowDraft.SupplyReturnRetries[orderId].Request, Is.EqualTo(returnRequest));
                Menu().Close();
            });
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
            {
                var rebuilt = server.EntMan.SpawnEntity(null, factoryPoint);
                Assert.That(markets.TryRegisterSupplyStore(generation, rebuilt), Is.True);
                factory = rebuilt;
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, actor);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                SelectOrder();
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyCancel"));
                Assert.That(Menu().FlowDraft.PendingSupply!.Request, Is.EqualTo(returnRequest));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Assert.That(menu.FlowDraft.SupplyReturnRetries.ContainsKey(orderId), Is.False);
                var order = State().Supply!.Orders.Single(o => o.Id == orderId);
                Assert.That(order.Outstanding, Is.Zero);
                Assert.That(order.ReservedPayment, Is.Zero);
                Assert.That(order.Batch!.AssignedToOrder, Is.Zero);
                Assert.That(order.Batch.FreeMarket, Is.EqualTo(6),
                    "Only six actual outputs of this batch were returned; the separate two ready inputs are not its output.");
                Assert.That(order.Batch.FreeLocal, Is.Zero, "The old source's three local items were destroyed with that source.");
                Select(menu.FindControl<OptionButton>("SupplySourcePicker"), 0);
                Select(menu.FindControl<OptionButton>("SupplyRecipientPicker"), 0);
                menu.FindControl<LineEdit>("SupplyQuantity").SetText("8", invokeEvent: true);
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyPreview"));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var quote = Menu().FlowDraft.SupplyQuote!;
                Assert.That(quote.Ready, Is.EqualTo(8));
                Assert.That(quote.Output, Is.Zero);
                Assert.That(quote.Cost, Is.EqualTo(1200));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Reserved, Is.Zero,
                    "A preview must not reserve money or goods.");
                var containers = server.System<SharedContainerSystem>();
                Assert.That(containers.TryGetContainer(factory, "sw-program-supply", out var inventory), Is.True);
                foreach (var entity in inventory!.ContainedEntities.Take(6).ToArray())
                    Assert.That(containers.Remove(entity, inventory), Is.True);
            });
            await client.WaitAssertion(() => Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyBuy")));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                Assert.That(Menu().FlowDraft.SupplyFeedback, Is.EqualTo(Loc.GetString("sw-global-war-supply-failure-revision")));
                Press(Menu().FindControl<SWGlobalWarConsoleButton>("SupplyPreview"));
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                var quote = menu.FlowDraft.SupplyQuote!;
                Assert.That(quote.Ready, Is.EqualTo(2));
                Assert.That(quote.Missing, Is.EqualTo(6));
                Assert.That(quote.Output, Is.EqualTo(10));
                Assert.That(quote.FreeMarket, Is.Zero);
                Assert.That(quote.FreeLocal, Is.EqualTo(4), "The future line now has the explicitly configured 45% local share.");
                Assert.That(quote.Duration, Is.EqualTo(TimeSpan.FromSeconds(900)));
                Assert.That(quote.Inputs.Single(i => i.Prototype == "SWGlobalWarF").Quantity, Is.EqualTo(20));
                Assert.That(quote.Inputs.Single(i => i.Prototype == "SWGlobalWarS").Quantity, Is.EqualTo(2));
                Assert.That(quote.Cost, Is.EqualTo(1200));
                Assert.That(quote.Failure, Is.EqualTo(SWGlobalWarSupplyFailure.None));
                quoteRevision = quote.LedgerRevision;
            });
            await server.WaitAssertion(() =>
            {
                var model = markets.Simulation!;
                var food = model.Markets["SWSpawnTestSupplyTaxHome"].Goods["SWGlobalWarF"];
                food.BaseConsumptionPerMinute = Q(2);
                var treasury = ledger.GetBalance(SWGlobalWarFaction.Empire, SWGlobalWarAccount.Treasury).Balance;
                var consumed = food.Consumed;
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(5), timing.TimeBase.Item2);
                markets.Update(0);
                Assert.That(food.Consumed, Is.GreaterThan(consumed));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Empire, SWGlobalWarAccount.Treasury).Balance,
                    Is.GreaterThan(treasury));
                food.BaseConsumptionPerMinute = 0;
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Assert.That(State().Supply!.LedgerRevision, Is.GreaterThan(quoteRevision));
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyBuy").Disabled, Is.False,
                    "Unrelated treasury taxes must leave the unchanged quote usable.");
                Press(menu.FindControl<SWGlobalWarConsoleButton>("SupplyBuy"));
                manualCommand = menu.FlowDraft.PendingSupply!;
                manualOrder = manualCommand.Request;
            });
            await pair.RunTicksSync(60);
            await server.WaitAssertion(() =>
            {
                var program = markets.Simulation!.SupplyPrograms[SWGlobalWarFaction.Republic];
                Assert.That(program.Orders[manualOrder].Outstanding, Is.EqualTo(8));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
                Assert.That(markets.GetSupplyProductionWork(generation, factory).Single().Quantity, Is.EqualTo(10));
                Assert.That(markets.SupplyJournal.Count(j => j.Request == manualOrder), Is.EqualTo(1));
                var reservations = markets.Simulation.ExternalReservationCount;
                Assert.That(markets.TryConfigureSupply(session, manualCommand, out _), Is.True);
                Assert.That(markets.TryConfigureSupply(session, manualCommand, out _), Is.True);
                Assert.That(markets.Simulation.ExternalReservationCount, Is.EqualTo(reservations));
                Assert.That(markets.SupplyJournal.Count(j => j.Request == manualOrder), Is.EqualTo(1));
                Assert.That(ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(registry.TryGetAssignment(session.UserId, out var head), Is.True);
                Assert.That(registry.TryExecute(session, new(generation, Guid.NewGuid(), SWGlobalWarAppointmentAction.Resign,
                    head.Revision, head.User, head.Revision), out _), Is.True);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                if (client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                        client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui) &&
                    Read<SWGlobalWarMarketConsoleMenu?>(bui!, "_menu") is { } menu)
                {
                    Assert.That(menu.FindControl<BoxContainer>("SupplyPanel").Visible, Is.False);
                    Assert.That(menu.FlowDraft.SupplyReturnRetries, Is.Empty);
                    Assert.That(menu.FindControl<RichTextLabel>("SupplyFunds").Text, Is.Empty);
                }
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

        SWGlobalWarMarketConsoleMenu Menu()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return Read<SWGlobalWarMarketConsoleMenu>(bui!, "_menu");
        }
        SWGlobalWarMarketConsoleState State() => Read<SWGlobalWarMarketConsoleState>(Menu(), "_state");
        void SelectOrder(Guid? selected = null)
        {
            var ids = Read<Guid[]>(Menu(), "_supplyOrders");
            var id = selected ?? orderId;
            Assert.That(ids, Does.Contain(id));
            Select(Menu().FindControl<OptionButton>("SupplyOrderPicker"), Array.IndexOf(ids, id));
        }
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Press(BaseButton button) => ((Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(button, null!));
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
    }
}
