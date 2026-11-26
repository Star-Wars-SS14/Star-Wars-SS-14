using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._SW.GlobalWar.UI;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.Lathe.SWLathe;
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
using Robust.Shared.Containers;
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
    public async Task PhysicalFactoryBuiDebitsFiniteStockAndConsumesRealStackOnce(bool empire)
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
        var containers = server.System<SharedContainerSystem>();
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
        EntityUid lathe = default;
        NetEntity netLathe = default;
        SWGlobalWarAssignment actor = default!;
        var world = empire ? "SWSpawnTestSupplyTaxHome" : "SWSpawnTestSupplySource";
        Guid generation = default;
        long originalStock = 0;
        SWGlobalWarFactoryRequest manufacture = default!;
        EntityUid frame = default;
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
                lathe = server.EntMan.SpawnEntity("SWBlasterLathe", new EntityCoordinates(grid, 1.5f, .5f));
                netLathe = server.EntMan.GetNetEntity(lathe);
                server.System<SharedTransformSystem>().SetCoordinates(actor.Body, new(grid, 1.5f, 1.5f));
                originalStock = markets.Simulation!.Markets[world].Goods["SWGlobalWarS"].Stock;
                server.System<ServerUi>().OpenUi(lathe, SWGlobalWarFactoryUiKey.Key, actor.Body);
            });
            await pair.RunSeconds(20);
            SWGlobalWarFactoryRequest first = default!;
            await client.WaitAssertion(() =>
            {
                Assert.That(State().Operate, Is.True);
                first = new(generation, Guid.NewGuid(), actor.Revision, SWGlobalWarFactoryAction.Materialize,
                    SWGlobalWarSupplyKind.T1, selection: "SWGlobalWarS");
                Bui().SendPredictedMessage(first);
            });
            await pair.RunTicksSync(10);
            await client.WaitPost(() => Bui().SendPredictedMessage(first));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.Simulation!.Markets[world].Goods["SWGlobalWarS"].Stock,
                    Is.EqualTo(originalStock - SWGlobalWarMarketUnits.FromUnits(1)));
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities.Count, Is.EqualTo(1));
                Assert.That(server.System<SWLatheSystem>().TryAddMaterial(
                    (lathe, server.EntMan.GetComponent<SWLatheComponent>(lathe)), inventory.ContainedEntities.Single()),
                    Is.False, "The legacy lathe cannot independently consume the same campaign input.");
            });
            await client.WaitPost(() =>
            {
                Bui().SendPredictedMessage(new SWGlobalWarFactoryRequest(generation, Guid.NewGuid(), actor.Revision,
                    SWGlobalWarFactoryAction.Materialize, SWGlobalWarSupplyKind.T1, selection: "SWGlobalWarS"));
                Bui().SendPredictedMessage(new SWGlobalWarFactoryRequest(generation, Guid.NewGuid(), actor.Revision,
                    SWGlobalWarFactoryAction.Materialize, SWGlobalWarSupplyKind.T1, selection: "SWGlobalWarS"));
            });
            await pair.RunTicksSync(10);
            await client.WaitPost(() =>
            {
                manufacture = new(generation, Guid.NewGuid(), actor.Revision, SWGlobalWarFactoryAction.Produce,
                    SWGlobalWarSupplyKind.T1, selection: "SWBlasterFrameSLR");
                Bui().SendPredictedMessage(manufacture);
            });
            await pair.RunSeconds(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(markets.Simulation!.Markets[world].Goods["SWGlobalWarS"].Stock,
                    Is.EqualTo(originalStock - SWGlobalWarMarketUnits.FromUnits(3)), "Begin must not debit materialized stock twice.");
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities.Count, Is.EqualTo(1));
                frame = inventory.ContainedEntities.Single();
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(frame).EntityPrototype!.ID, Is.EqualTo("SWBlasterFrame"));
                Assert.That(server.EntMan.GetComponent<SWLatheComponent>(lathe).StashedMaterials, Is.Empty);
            });
            await client.WaitPost(() => Bui().SendPredictedMessage(manufacture));
            await pair.RunSeconds(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities, Is.EquivalentTo(new[] { frame }));
            });
            var netFrame = server.EntMan.GetNetEntity(frame);
            await client.WaitPost(() => Bui().SendPredictedMessage(new SWGlobalWarFactoryRequest(generation,
                Guid.NewGuid(), actor.Revision, SWGlobalWarFactoryAction.Take, SWGlobalWarSupplyKind.T1, netFrame)));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<SharedHandsSystem>().IsHolding(actor.Body, frame), Is.True);
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                var position = server.EntMan.GetComponent<TransformComponent>(lathe).Coordinates;
                var rifle = server.EntMan.SpawnEntity(SWGlobalWarMarketSystem.SupplyPrototype(SWGlobalWarSupplyKind.T2,
                    actor.Faction), position);
                if (containers.TryGetContainer(rifle, "gun_magazine", out var loadedMagazine))
                    foreach (var item in loadedMagazine.ContainedEntities.ToArray())
                        server.EntMan.DeleteEntity(item);
                Assert.That(containers.Insert(rifle, inventory), Is.True);
                for (var i = 0; i < 2; i++)
                    Assert.That(containers.Insert(server.EntMan.SpawnEntity("SWAdvancedElectronics", position), inventory), Is.True);
            });
            SWGlobalWarFactoryRequest upgrade = new(generation, Guid.NewGuid(), actor.Revision,
                SWGlobalWarFactoryAction.Produce, SWGlobalWarSupplyKind.T2, selection: "SWGlobalWarUpgradeRifle");
            await client.WaitPost(() => Bui().SendPredictedMessage(upgrade));
            await pair.RunSeconds(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities, Is.Empty, "The actual rifle and two AE are consumed before fabrication.");
            });
            await pair.RunSeconds(90);
            EntityUid improved = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities.Count, Is.EqualTo(1));
                improved = inventory.ContainedEntities.Single();
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(improved).EntityPrototype!.ID,
                    Is.EqualTo(empire ? "SWGlobalWarImprovedEmpireRifle" : "SWGlobalWarImprovedRepublicRifle"));
                Assert.That(containers.TryGetContainer(improved, "gun_magazine", out var magazine), Is.True);
                Assert.That(magazine.ContainedEntities, Is.Empty);
            });
            await client.WaitPost(() => Bui().SendPredictedMessage(upgrade));
            await pair.RunSeconds(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(containers.TryGetContainer(lathe, "sw-program-supply", out var inventory), Is.True);
                Assert.That(inventory.ContainedEntities, Is.EquivalentTo(new[] { improved }));
                originalStock = markets.Simulation!.Markets[world].Goods["SWGlobalWarS"].Stock;
                server.System<SharedTransformSystem>().SetCoordinates(actor.Body,
                    new(server.EntMan.GetComponent<TransformComponent>(lathe).GridUid!.Value, 4.5f, 4.5f));
            });
            await client.WaitPost(() => Bui().SendPredictedMessage(new SWGlobalWarFactoryRequest(generation,
                Guid.NewGuid(), actor.Revision, SWGlobalWarFactoryAction.Materialize, SWGlobalWarSupplyKind.T1,
                selection: "SWGlobalWarS")));
            await pair.RunTicksSync(10);
            await server.WaitAssertion(() => Assert.That(markets.Simulation!.Markets[world].Goods["SWGlobalWarS"].Stock,
                Is.EqualTo(originalStock)));
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
                client.EntMan.GetEntity(netLathe), SWGlobalWarFactoryUiKey.Key, out var bui), Is.True);
            return bui!;
        }
        SWGlobalWarFactoryState State() => (SWGlobalWarFactoryState) typeof(SWGlobalWarFactoryBoundUserInterface)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Bui())!;
    }
}
