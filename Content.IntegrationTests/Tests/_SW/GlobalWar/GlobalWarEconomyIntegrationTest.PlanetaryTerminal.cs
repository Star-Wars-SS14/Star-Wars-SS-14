using System;
using System.Linq;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Server.Damage.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._SW.GlobalWar;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MilitaryVendorAndTerminalShareStockAndRefundWarBudget(bool empire)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var session = server.PlayerMan.Sessions.Single();
        var ticker = server.System<GameTicker>();
        var oldPreset = ticker.Preset;
        var markets = server.System<SWGlobalWarMarketSystem>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.ResolveDependency<UserDbDataManager>().IsLoadComplete(session) &&
                server.ResolveDependency<IServerPreferencesManager>().TryGetCachedPreferences(session.UserId, out _));
        }
        Assert.That(loaded, Is.True);
        var profile = (HumanoidCharacterProfile) server.ResolveDependency<IServerPreferencesManager>()
            .GetPreferences(session.UserId).SelectedCharacter;
        ProtoId<JobPrototype> job = empire ? "SWDiplomacyTestEHead" : "SWPoliticsTestHead";
        await pair.SetJobPriorities(session.UserId,
            profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        EntityUid vendor = default;
        EntityUid terminal = default;
        NetEntity netTerminal = default;
        SWGlobalWarAssignment actor = default!;
        var world = empire ? "SWSpawnTestSupplyTaxHome" : "SWSpawnTestSupplySource";
        const string commodity = "SWGlobalWarSupplyT2";
        const int price = 1500;
        Guid generation = default;
        long openingMoney = 0;
        int openingStock = 0;
        try
        {
            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "SWSpawnTestMap");
                ticker.SetGamePreset("SWSpawnTestSupplyAuthorityPreset");
                ticker.ToggleReadyAll(true);
                ticker.StartRound();
                Assert.That(assignments.TryGetAssignment(session.UserId, out actor), Is.True);
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                generation = campaign.Comp.Generation;
                server.System<GodmodeSystem>().EnableGodmode(actor.Body);
                var planets = server.System<SWGlobalWarPlanetSystem>();
                Assert.That(planets.TryGetPlanet(world, out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface,
                    out var map, out _), Is.True);
                var mapId = server.EntMan.GetComponent<MapComponent>(map!.Value).MapId;
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                var maps = server.System<SharedMapSystem>();
                for (var x = -2; x <= 4; x++)
                for (var y = -2; y <= 4; y++)
                    maps.SetTile(grid, new Vector2i(x, y), new Tile(1));
                var apc = server.EntMan.SpawnEntity("APCBasic", new EntityCoordinates(grid, .5f, .5f));
                server.EntMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, .5f, .5f));
                var batteries = server.System<SharedBatterySystem>();
                batteries.SetMaxCharge(apc, 1_000_000);
                batteries.SetCharge(apc, 1_000_000);
                vendor = server.EntMan.SpawnEntity("SWGlobalWarEquipmentStorage", new EntityCoordinates(grid, 1.5f, .5f));
                terminal = server.EntMan.SpawnEntity("SWGlobalWarMarketStorage", new EntityCoordinates(grid, .5f, 1.5f));
                netTerminal = server.EntMan.GetNetEntity(vendor);
                server.System<SharedTransformSystem>().SetCoordinates(actor.Body, new(grid, 1.5f, 1.5f));
                Assert.That(markets.Simulation!.DepositMarketStock(world, commodity, SWGlobalWarMarketUnits.PerUnit), Is.True);
                Assert.That(ledger.TryExecuteSystem(new(generation, Guid.NewGuid(), 0, SWGlobalWarLedgerAction.Credit,
                    actor.Faction, SWGlobalWarAccount.War, price, Purpose: "terminal-test"), out _), Is.True);
                openingMoney = ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Balance;
                openingStock = markets.Simulation.Markets[world].Goods[commodity].Stock;
                server.System<ServerUi>().OpenUi(vendor, SWGlobalWarFactoryUiKey.Key, actor.Body);
            });
            await pair.RunSeconds(5);
            var buy = new SWGlobalWarFactoryRequest(generation, Guid.NewGuid(), actor.Revision,
                SWGlobalWarFactoryAction.BuyMarket, default, selection: commodity);
            await client.WaitAssertion(() =>
            {
                Assert.That(State().MarketGoods!.Any(g => g.Commodity == "SWGlobalWarS"), Is.False);
                Assert.That(State().MarketGoods!.Single(g => g.Commodity == commodity).Price, Is.EqualTo(price));
                Bui().SendPredictedMessage(buy);
            });
            await pair.RunTicksSync(12);
            await client.WaitPost(() => Bui().SendPredictedMessage(buy));
            await pair.RunTicksSync(12);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.Simulation!.Markets[world].Goods[commodity].Stock,
                    Is.EqualTo(openingStock - SWGlobalWarMarketUnits.PerUnit));
                Assert.That(ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(openingMoney - price));
                netTerminal = server.EntMan.GetNetEntity(terminal);
                server.System<ServerUi>().OpenUi(terminal, SWGlobalWarFactoryUiKey.Key, actor.Body);
            });
            await pair.RunSeconds(2);
            var deposit = new SWGlobalWarFactoryRequest(generation, Guid.NewGuid(), actor.Revision,
                SWGlobalWarFactoryAction.DepositMarket, default);
            await client.WaitAssertion(() =>
            {
                Assert.That(State().MarketGoods!.Any(g => g.Commodity == "SWGlobalWarS"), Is.True);
                Bui().SendPredictedMessage(deposit);
            });
            await pair.RunTicksSync(12);
            await client.WaitPost(() => Bui().SendPredictedMessage(deposit));
            await pair.RunTicksSync(12);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.Simulation!.Markets[world].Goods[commodity].Stock, Is.EqualTo(openingStock));
                Assert.That(ledger.GetBalance(actor.Faction, SWGlobalWarAccount.War).Balance, Is.EqualTo(openingMoney));
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (clock.TryGetCampaign(out var campaign))
                    ticker.EndGameRule(campaign.Owner);
                ticker.SetGamePreset(oldPreset);
            });
        }
        await pair.CleanReturnAsync();

        SWGlobalWarFactoryBoundUserInterface Bui()
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarFactoryBoundUserInterface>(
                client.EntMan.GetEntity(netTerminal), SWGlobalWarFactoryUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarFactoryState State() => (SWGlobalWarFactoryState) typeof(SWGlobalWarFactoryBoundUserInterface)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Bui())!;
    }
}
