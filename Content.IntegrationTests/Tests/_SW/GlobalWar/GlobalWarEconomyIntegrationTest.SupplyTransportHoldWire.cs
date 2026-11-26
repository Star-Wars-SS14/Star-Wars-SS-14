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
    public async Task SupplyTransportLateBlockadeExplainsFundedProductionInBui()
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
        await pair.SetJobPriorities(("SWPoliticsTestHead", JobPriority.High));
        NetEntity netTerminal = default;
        SWGlobalWarSupplyPurchase purchase = default!;
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
                var generation = campaign.Comp.Generation;
                server.System<GodmodeSystem>().EnableGodmode(head.Body);
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                model.SupplyPrograms[head.Faction].Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent = 0;
                var factory = Store("SWSpawnTestSupplyTransportFarm", false);
                var primary = Store("SWSpawnTestSupplyTransportHome", true);
                Advance(1200);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                Assert.That(markets.TryReserveSupplyPurchase(new(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    head.Faction, factory, primary, SWGlobalWarSupplyKind.Medicine, 8), out purchase), Is.True);
                Assert.That(purchase.InitialReady, Is.Zero);
                Assert.That(purchase.Production, Is.Not.Null);
                var production = purchase.Production!.Value;
                Assert.That(markets.TryBeginSupplyProduction(generation, production), Is.True);
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplyTransportHome", out var world), Is.True);
                var planet = server.EntMan.GetComponent<SWGlobalWarPlanetComponent>(world!.Value);
                var steps = server.System<SWGlobalWarCampaignStepSystem>();
                Assert.That(steps.TryQueueOwnership(world.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, planet.OwnershipRevision, generation, Guid.NewGuid()), Is.True);
                Assert.That(steps.CommitCampaignStep(generation, timing.CurTick), Is.True);
                Advance(2100);
                var output = Enumerable.Range(0, 10).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(production))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, production, output), Is.True);
                Advance(2101);
                Assert.That(purchase.Goods, Has.Count.EqualTo(8));
                Assert.That(model.Shipments.Any(s => s.SupplyManifest?.Purchase == purchase.Request.Operation), Is.False);
                Assert.That(purchase.Obligation.Outstanding, Is.EqualTo(8));
                Assert.That(ledger.GetBalance(head.Faction, SWGlobalWarAccount.War).Reserved, Is.EqualTo(1200));
                var terminal = server.EntMan.SpawnEntity("SWDEconomyWireMarket",
                    server.EntMan.GetComponent<TransformComponent>(head.Body).Coordinates);
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarMarketConsoleUiKey.Key, head.Body);

                EntityUid Store(string worldId, bool arsenal)
                {
                    Assert.That(planets.TryGetPlanet(worldId, out var entity), Is.True);
                    Assert.That(planets.TryEnsureLocation(entity!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                    var store = server.EntMan.SpawnEntity(null, new EntityCoordinates(map!.Value, Vector2.Zero));
                    Assert.That(markets.TryRegisterSupplyStore(generation, store, arsenal), Is.True);
                    return store;
                }
                void Advance(int seconds)
                {
                    timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                    markets.Update(0);
                }
            });
            await pair.RunTicksSync(90);
            await client.WaitAssertion(() => Select(Menu().FindControl<OptionButton>("SupplyKindPicker"), (int) SWGlobalWarSupplyKind.Medicine));
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var menu = Menu();
                Select(menu.FindControl<OptionButton>("SupplyOrderPicker"),
                    Array.IndexOf(Read<Guid[]>(menu, "_supplyOrders"), purchase.Request.Operation));
                Assert.That(menu.FindControl<RichTextLabel>("SupplyOrderTransport").Text,
                    Is.EqualTo(Loc.GetString("sw-global-war-supply-order-route-blocked")));
                Assert.That(menu.FindControl<RichTextLabel>("SupplyOrderParts").Text, Is.Empty);
                var view = Read<SWGlobalWarMarketConsoleState>(menu, "_state").Supply!.Orders.Single();
                Assert.That(view.Number, Is.EqualTo(purchase.Number));
                Assert.That(view.Outstanding, Is.EqualTo(8));
                Assert.That(view.ReservedPayment, Is.EqualTo(1200));
                Assert.That(view.Batch!.Stage, Is.EqualTo(SWGlobalWarSupplyStage.Complete));
                Assert.That(menu.FindControl<SWGlobalWarConsoleButton>("SupplyCancel").Visible, Is.True);
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

        SWGlobalWarMarketConsoleMenu Menu()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarMarketConsoleBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarMarketConsoleUiKey.Key, out var bui), Is.True);
            return Read<SWGlobalWarMarketConsoleMenu>(bui!, "_menu");
        }
        static T Read<T>(object target, string field) => (T) target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        static void Select(OptionButton button, int id) => ((Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button)!)(new(id, button));
    }
}
