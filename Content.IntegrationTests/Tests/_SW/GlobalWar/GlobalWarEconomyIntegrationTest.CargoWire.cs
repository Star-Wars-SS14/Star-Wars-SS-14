using System;
using System.Linq;
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
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;
using ClientUi = Robust.Client.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarEconomyIntegrationTest
{
    /// <summary>The console that assigns a task stands far away, so a line's and an arsenal's own window finish it.</summary>
    [Test]
    public async Task CargoWorkLoadsACrateAtTheLineAndPaysOnceAtTheArsenalWindow()
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
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var savedTime = timing.TimeBase;
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
        ProtoId<JobPrototype> job = "SWPoliticsTestWorker";
        await pair.SetJobPriorities(session.UserId,
            profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never)).Append((job, JobPriority.High)).ToArray());
        SWGlobalWarAssignment actor = default!;
        Guid generation = default;
        EntityUid line = default;
        EntityUid arsenal = default;
        EntityUid crate = default;
        EntityUid grid = default;
        NetEntity netLine = default;
        NetEntity netArsenal = default;
        SWGlobalWarSupplyPurchase purchase = default!;
        long industry = 0;
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
                Assert.That(planets.TryGetPlanet("SWSpawnTestSupplySource", out var planet), Is.True);
                Assert.That(planets.TryEnsureLocation(planet!.Value, SWGlobalWarPlanetLocation.Surface, out var map, out _), Is.True);
                var mapGrid = server.ResolveDependency<IMapManager>().CreateGridEntity(server.EntMan.GetComponent<MapComponent>(map!.Value).MapId);
                grid = mapGrid.Owner;
                var maps = server.System<SharedMapSystem>();
                for (var x = -2; x <= 9; x++)
                for (var y = -2; y <= 4; y++)
                    maps.SetTile(mapGrid, new Vector2i(x, y), new Tile(1));
                var apc = server.EntMan.SpawnEntity("APCBasic", new EntityCoordinates(grid, 3.5f, -1.5f));
                for (var x = 0; x <= 7; x++)
                    server.EntMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, x + .5f, -1.5f));
                var batteries = server.System<SharedBatterySystem>();
                batteries.SetMaxCharge(apc, 1_000_000);
                batteries.SetCharge(apc, 1_000_000);
                line = server.EntMan.SpawnEntity("SWGlobalWarFactoryStorage", new EntityCoordinates(grid, 1.5f, .5f));
                arsenal = server.EntMan.SpawnEntity("SWGlobalWarEquipmentStorage", new EntityCoordinates(grid, 6.5f, .5f));
                netLine = server.EntMan.GetNetEntity(line);
                netArsenal = server.EntMan.GetNetEntity(arsenal);
                server.System<SharedTransformSystem>().SetCoordinates(actor.Body, new(grid, 1.5f, 1.5f));
                crate = server.EntMan.SpawnEntity("SWCrateWood", new EntityCoordinates(grid, .5f, 1.5f));
                Assert.That(server.System<PullingSystem>().TryStartPull(actor.Body, crate), Is.True);
            });
            await pair.RunSeconds(2);
            await server.WaitAssertion(() =>
            {
                var model = markets.Simulation!;
                model.SupplyParticipants = _ => 20;
                model.SupplyPrograms[actor.Faction].Lines[SWGlobalWarSupplyKind.Medicine].LocalPercent = 0;
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(1200) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                markets.Update(0);
                var recipe = new SWGlobalWarSupplyProductionRequest(generation, Guid.NewGuid(), line, actor.Faction,
                    SWGlobalWarSupplyKind.Medicine, 1);
                Assert.That(markets.TryReserveSupplyProduction(recipe, out _), Is.True);
                Assert.That(markets.TryBeginSupplyProduction(generation, recipe.Operation), Is.True);
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(1651) - clock.Elapsed(campaign), timing.TimeBase.Item2);
                markets.Update(0);
                var goods = Enumerable.Range(0, 5).Select(_ => server.EntMan.SpawnEntity("SWKoltoMedipen",
                    markets.SupplyProductionOutputCoordinates(recipe.Operation))).ToArray();
                Assert.That(markets.TryCompleteSupplyProduction(generation, recipe.Operation, goods), Is.True);
                Assert.That(ledger.TryGetLedger(out var accounts), Is.True);
                var request = new SWGlobalWarSupplyPurchaseRequest(generation, Guid.NewGuid(), accounts.Comp.Revision,
                    actor.Faction, line, arsenal, SWGlobalWarSupplyKind.Medicine, 4);
                Assert.That(markets.TryReserveSupplyPurchase(request, out purchase), Is.True);
                Assert.That(markets.TryExecuteSupplyWork(session, new(generation, Guid.NewGuid(), actor.Revision,
                    SWGlobalWarSupplyWorkAction.Claim, request.Operation, 4), out var claimed), Is.True, claimed.Failure.ToString());
                industry = ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Industry).Balance;
                server.System<ServerUi>().OpenUi(line, SWGlobalWarFactoryUiKey.Key, actor.Body);
            });
            await pair.RunTicksSync(60);
            await client.WaitAssertion(() =>
            {
                var work = State(netLine).CargoWork!;
                Assert.That(work.Accept, Is.False);
                Assert.That(work.Count, Is.EqualTo(4));
                Assert.That(work.InReach, Is.True);
                Assert.That(Bui(netLine).GetType().GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(Bui(netLine)), Is.Not.Null);
                Bui(netLine).SendPredictedMessage(new SWGlobalWarFactoryRequest(generation, Guid.NewGuid(), actor.Revision,
                    SWGlobalWarFactoryAction.CargoWork, SWGlobalWarSupplyKind.Medicine, work.Carrier, work.Work.ToString()));
            });
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
            {
                var contents = server.EntMan.GetComponent<EntityStorageComponent>(crate).Contents.ContainedEntities;
                Assert.That(contents, Is.EquivalentTo(purchase.Goods));
                var pulling = server.System<PullingSystem>();
                var transforms = server.System<SharedTransformSystem>();
                Assert.That(pulling.TryStopPull(crate, server.EntMan.GetComponent<PullableComponent>(crate)), Is.True);
                transforms.SetCoordinates(actor.Body, new(grid, 6.5f, 1.5f));
                transforms.SetCoordinates(crate, new(grid, 5.5f, 1.5f));
                Assert.That(pulling.TryStartPull(actor.Body, crate), Is.True);
                server.System<ServerUi>().OpenUi(arsenal, SWGlobalWarFactoryUiKey.Key, actor.Body);
            });
            await pair.RunTicksSync(60);
            SWGlobalWarFactoryRequest handOver = default!;
            await client.WaitAssertion(() =>
            {
                var work = State(netArsenal).CargoWork!;
                Assert.That(work.Accept, Is.True);
                Assert.That(work.InReach, Is.True);
                handOver = new(generation, Guid.NewGuid(), actor.Revision, SWGlobalWarFactoryAction.CargoWork,
                    SWGlobalWarSupplyKind.Medicine, work.Carrier, work.Work.ToString());
                Bui(netArsenal).SendPredictedMessage(handOver);
            });
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
            {
                Assert.That(purchase.Obligation.Accepted, Is.EqualTo(4));
                Assert.That(ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 600));
                Assert.That(server.EntMan.GetComponent<EntityStorageComponent>(crate).Contents.ContainedEntities, Is.Empty);
                Assert.That(server.System<SharedContainerSystem>().TryGetContainer(arsenal, "sw-program-supply", out var stock), Is.True);
                Assert.That(stock!.ContainedEntities, Is.EquivalentTo(purchase.Goods));
            });
            await client.WaitPost(() => Bui(netArsenal).SendPredictedMessage(handOver));
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
                Assert.That(ledger.GetBalance(actor.Faction, SWGlobalWarAccount.Industry).Balance, Is.EqualTo(industry + 600),
                    "Replaying the same request pays nothing."));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (clock.TryGetCampaign(out var campaign))
                    ticker.EndGameRule(campaign.Owner);
                ticker.SetGamePreset(oldPreset);
                timing.TimeBase = savedTime;
            });
        }
        await pair.CleanReturnAsync();

        SWGlobalWarFactoryBoundUserInterface Bui(NetEntity net)
        {
            Assert.That(client.System<ClientUi>().TryGetOpenUi<SWGlobalWarFactoryBoundUserInterface>(
                client.EntMan.GetEntity(net), SWGlobalWarFactoryUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarFactoryState State(NetEntity net) => (SWGlobalWarFactoryState) typeof(SWGlobalWarFactoryBoundUserInterface)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Bui(net))!;
    }
}
