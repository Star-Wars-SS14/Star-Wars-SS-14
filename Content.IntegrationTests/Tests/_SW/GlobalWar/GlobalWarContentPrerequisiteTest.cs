using System.Linq;
using System.Numerics;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
public sealed class GlobalWarContentPrerequisiteTest
{
    [Test]
    public async Task ServerRejectsMissingMapWhileClientDefersServerMapBindings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var valid = new MappingDataNode
        {
            { "id", "GlobalWarValidationProbe" },
            { "name", "Probe" },
            { "surface", "Empty" },
            { "orbit", "Empty" },
        };
        var invalid = valid.Copy();
        invalid["surface"] = new Robust.Shared.Serialization.Markdown.Value.ValueDataNode("MissingGlobalWarMap");
        await pair.Server.WaitAssertion(() =>
        {
            var serialization = pair.Server.ResolveDependency<ISerializationManager>();
            Assert.That(serialization.ValidateNode<SWGlobalWarPlanetPrototype>(valid).GetErrors(), Is.Empty);
            var errors = serialization.ValidateNode<SWGlobalWarPlanetPrototype>(invalid).GetErrors().ToArray();
            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(error => error.ErrorReason.Contains("MissingGlobalWarMap")), Is.True);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var serialization = pair.Client.ResolveDependency<ISerializationManager>();
            Assert.That(serialization.ValidateNode<SWGlobalWarPlanetPrototype>(valid).GetErrors(), Is.Empty);
            Assert.That(serialization.ValidateNode<SWGlobalWarPlanetPrototype>(invalid).GetErrors(), Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FirstAidKitContainsEverySupplyAndStillRejectsTools()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var map = server.System<MapSystem>().CreateMap(out _, runMapInit: true);
            var coordinates = new EntityCoordinates(map, Vector2.Zero);
            var kit = server.EntMan.SpawnEntity("CMFirstAidKitFilled", coordinates);
            var storage = server.EntMan.GetComponent<StorageComponent>(kit);
            var contents = storage.Container.ContainedEntities
                .Select(uid => server.MetaData(uid).EntityPrototype!.ID).ToArray();
            Assert.That(contents, Is.EquivalentTo(new[]
            {
                "CMHealthAnalyzer", "CMTricordrazineAutoInjectorNoSkill", "CMInaprovalineAutoInjector",
                "Gauze", "Ointment",
            }));
            var screwdriver = server.EntMan.SpawnEntity("Screwdriver", coordinates);
            Assert.That(server.System<SharedStorageSystem>().CanInsert(kit, screwdriver, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo("comp-storage-invalid-container"));
            Assert.That(storage.Container.ContainedEntities, Has.Count.EqualTo(5));
        });
        await pair.CleanReturnAsync();
    }
}
