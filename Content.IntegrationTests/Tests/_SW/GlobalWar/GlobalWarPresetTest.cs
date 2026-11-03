#nullable enable
using System.Collections.Generic;
using Content.Server._SW.GlobalWar;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarPresetTest
{
    [Test]
    public async Task TestPresetConfigurationAndAsteroidStationLoading()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var ticker = server.System<GameTicker>();
        var stations = server.System<StationSystem>();
        var maps = server.System<MapSystem>();

        await server.WaitAssertion(() =>
        {
            var preset = prototypes.Index<GamePresetPrototype>("SWGlobalWar");
            Assert.That(ticker.FindGamePreset("globalwar"), Is.SameAs(preset));
            Assert.That(prototypes.Index<GameMapPoolPrototype>(preset.MapPool!).Maps, Contains.Item("Empty"));
            Assert.That(preset.Rules, Contains.Item(new EntProtoId("SWGlobalWarCampaignProduction")));
            var ruleUid = ticker.AddGameRule("SWGlobalWar");
            var globalWarGamerule = entities.GetComponent<SWGlobalWarGameruleComponent>(ruleUid);
            Assert.That(globalWarGamerule.Planets.Count, Is.EqualTo(3));
            var locationMaps = new HashSet<EntityUid>();
            foreach (var id in globalWarGamerule.Planets)
            {
                var definition = prototypes.Index(id);
                Assert.That(definition.Surface!.Value.Id, Is.EqualTo("oldBalmora"));
                Assert.That(prototypes.Index(definition.Surface.Value).MapPath.ToString(), Is.EqualTo("/Maps/SW/oldBalmorra.yml"));
                var orbitPrototype = prototypes.Index(definition.Orbit!.Value);
                Assert.That(orbitPrototype.MapPath.ToString(), Is.EqualTo("/Maps/SW/asteroid.yml"));

                // Verify station creation before MapInit, which has unrelated legacy container-fill errors.
                var grids = ticker.LoadGameMap(orbitPrototype, out var mapId);
                var orbit = maps.GetMap(mapId);
                Assert.That(locationMaps.Add(orbit), Is.True);
                Assert.That(grids.Count, Is.EqualTo(1));
                Assert.That(stations.GetOwningStation(grids[0]), Is.Not.Null);
                var station = stations.GetOwningStation(grids[0])!.Value;
                Assert.That(server.MetaData(station).EntityPrototype!.Abstract, Is.False);
                Assert.That(server.Transform(grids[0]).MapUid, Is.EqualTo(orbit));
                Assert.That(entities.GetComponent<FTLDestinationComponent>(orbit).Enabled, Is.True);
            }

            ProtoId<SWGlobalWarPlanetPrototype> alpha = "SWGlobalWarTestAlpha";
            ProtoId<SWGlobalWarPlanetPrototype> beta = "SWGlobalWarTestBeta";
            ProtoId<SWGlobalWarPlanetPrototype> gamma = "SWGlobalWarTestGamma";
            Assert.That(prototypes.Index(alpha).Connections, Is.EquivalentTo(new[] { beta }));
            Assert.That(prototypes.Index(beta).Connections, Is.EquivalentTo(new[] { alpha, gamma }));
            Assert.That(prototypes.Index(gamma).Connections, Is.EquivalentTo(new[] { beta }));

            // The original Space War game map must also use a concrete station prototype.
            ProtoId<GameMapPrototype> asteroid = "Asteroid";
            var originalGrids = ticker.LoadGameMap(prototypes.Index(asteroid), out var originalMap);
            Assert.That(stations.GetOwningStation(originalGrids[0]), Is.Not.Null);
        });

        await pair.RunSeconds(1);
        await pair.CleanReturnAsync();
    }
}
