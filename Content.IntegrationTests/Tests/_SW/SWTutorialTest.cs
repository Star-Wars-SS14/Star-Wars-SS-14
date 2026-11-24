using System.Linq;
using Content.Server._SW.Lathe.SWLathe;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Content.Server._SW.Tutorial;
using Content.Server.GameTicking;
using Content.Shared._SW.TaskSystem;
using Content.Shared._SW.Tutorial;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
[TestOf(typeof(SWTutorialSystem))]
public sealed class SWTutorialTest
{
    /// <summary>
    /// Start, step, exit: the map and the mind are gone afterwards, nothing touched the real round.
    /// </summary>
    [Test]
    public async Task StartWalkAndExit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<SWTutorialSystem>();
        var maps = server.System<SharedMapSystem>();
        var mapsBefore = 0;
        var mindsBefore = 0;

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            mapsBefore = maps.GetAllMapIds().Count();
            mindsBefore = Count<MindComponent>(entMan);

            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToGame, out var error), Is.True, error?.Id);
            Assert.That(tutorial.IsStudent(session.UserId));
            Assert.That(maps.GetAllMapIds().Count(), Is.EqualTo(mapsBefore + 1));

            var body = session.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<SWTutorialPlayerComponent>(body));
            Assert.That(Count<SWFactionMemberComponent>(entMan), Is.Zero, "A student has no real faction.");

            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToGame, out _), Is.False, "Started twice.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var mapUid = entMan.GetComponent<TransformComponent>(session.AttachedEntity!.Value).MapUid;
            var query = entMan.EntityQueryEnumerator<SWTutorialPointComponent, TransformComponent>();
            while (query.MoveNext(out var point, out var xform))
            {
                if (point.Point == "supply" && xform.MapUid == mapUid)
                    server.System<SharedTransformSystem>().SetCoordinates(session.AttachedEntity!.Value, xform.Coordinates);
            }
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            Assert.That(tutorial.GetStudentStep(session.UserId), Is.EqualTo("SWTutorialCivilPick"),
                "Reaching the door did not finish the first step.");

            Assert.That(tutorial.TryExit(session), Is.True);
            Assert.That(tutorial.IsStudent(session.UserId), Is.False);
            Assert.That(ticker.PlayerGameStatuses[session.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(maps.GetAllMapIds().Count(), Is.EqualTo(mapsBefore), "The private map was not deleted.");
            Assert.That(Count<MindComponent>(entMan), Is.EqualTo(mindsBefore), "The round end summary would list this mind.");
            Assert.That(tutorial.TryExit(pair.Player!), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A student before the round starts must not count as ready. GameTicker asserts this in debug builds.
    /// Then a restart while still in the tutorial brings the player back to the lobby.
    /// </summary>
    [Test]
    public async Task RoundStartAndRestartDuringTutorial()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, InLobby = true, Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var tutorial = server.System<SWTutorialSystem>();

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToGame, out var error), Is.True, error?.Id);
            Assert.That(ticker.ReadyPlayerCount(), Is.Zero);
            ticker.StartRound();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(tutorial.IsStudent(session.UserId), "The round start took the student out of the tutorial.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() => ticker.RestartRound());

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            Assert.That(tutorial.IsStudent(session.UserId), Is.False);
            Assert.That(session.AttachedEntity, Is.Null);
            Assert.That(ticker.PlayerGameStatuses[session.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A mode with no scenario and a full tutorial say so instead of starting anything.
    /// </summary>
    [Test]
    public async Task RefusesWhatItCannotStart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, InLobby = true, Dirty = true });
        var server = pair.Server;
        var tutorial = server.System<SWTutorialSystem>();

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            Assert.That(tutorial.GetScenario(SWTutorialChoice.NewToServer), Is.Not.Null,
                "Experienced SS14 players must have a real short production scenario.");

            server.CfgMan.SetCVar(SWVars.TutorialMaxStudents, 0);
            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToGame, out var full), Is.False);
            Assert.That(full?.Id, Is.EqualTo("sw-tutorial-error-full"));
            server.CfgMan.SetCVar(SWVars.TutorialMaxStudents, SWVars.TutorialMaxStudents.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Real inputs and output, not a Next button or a synthetic completion event.</summary>
    [Test]
    public async Task CivilianProductionAndCheckpoint()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, InLobby = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var tutorial = server.System<SWTutorialSystem>();
        var hands = server.System<SharedHandsSystem>();
        var storage = server.System<SharedEntityStorageSystem>();
        var transform = server.System<SharedTransformSystem>();
        EntityUid body = default, map = default, material = default, box = default, machine = default;
        await pair.RunTicksSync(5);
        EntityCoordinates Point(string name)
        {
            var query = entities.EntityQueryEnumerator<SWTutorialPointComponent, TransformComponent>();
            while (query.MoveNext(out var point, out var coordinates))
                if (point.Point == name && coordinates.MapUid == map)
                    return coordinates.Coordinates;
            throw new AssertionException($"Missing point {name}");
        }
        EntityUid Item(string prototype)
        {
            var query = entities.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var metadata, out var coordinates))
                if (metadata.EntityPrototype?.ID == prototype && coordinates.MapUid == map)
                    return uid;
            throw new AssertionException($"Missing item {prototype}");
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryStart(pair.Player!, SWTutorialChoice.NewToGame, out var error, restart: true), Is.True, error?.Id);
            body = pair.Player!.AttachedEntity!.Value;
            map = entities.GetComponent<TransformComponent>(body).MapUid!.Value;
            transform.SetCoordinates(body, Point("supply"));
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilPick"));
            material = Item("SWSheetLantanid1");
            box = Item("CrateGenericSteel");
            machine = Item("SWTutorialElectronicsLathe");
            transform.SetCoordinates(body, Point("material"));
            Assert.That(hands.TryPickupAnyHand(body, material), Is.True);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilStore"));
            // Exercise the actual storage container; no tutorial condition or stage is mutated.
            Assert.That(storage.Insert(material, box), Is.True);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilRetrieve"));
            storage.OpenStorage(box);
            transform.SetCoordinates(body, entities.GetComponent<TransformComponent>(material).Coordinates);
            Assert.That(hands.TryPickupAnyHand(body, material), Is.True);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilProduce"));
            transform.SetCoordinates(body, Point("machineInput"));
            Assert.That(hands.TryDrop(body, material, Point("machineInput")), Is.True);
        });
        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(material), Is.False, "The real lathe must consume the delivered sheet.");
            var module = Item("SWElectronics1");
            Assert.That(module.Valid, Is.True);
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilProduce"), "The step ends with the module in hand.");
            transform.SetCoordinates(body, entities.GetComponent<TransformComponent>(module).Coordinates);
            Assert.That(hands.TryPickupAnyHand(body, module), Is.True);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilRadio"));
            Assert.That(tutorial.TryExit(pair.Player!), Is.True);
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryStart(pair.Player!, SWTutorialChoice.NewToGame, out var error), Is.True, error?.Id);
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilRadio"), "Resume the exercise boundary, not the old entity UIDs.");
            Assert.That(entities.GetComponent<TransformComponent>(pair.Player!.AttachedEntity!.Value).MapUid, Is.Not.EqualTo(map));
            Assert.That(tutorial.TryExit(pair.Player!), Is.True);
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryStart(pair.Player!, SWTutorialChoice.NewToGame, out var error, restart: true), Is.True, error?.Id);
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilOrient"));
            Assert.That(tutorial.TryExit(pair.Player!), Is.True);
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(tutorial.TryStart(pair.Player!, SWTutorialChoice.NewToGame, out var error), Is.True, error?.Id);
            Assert.That(tutorial.GetStudentStep(pair.Player!.UserId), Is.EqualTo("SWTutorialCivilOrient"),
                "An explicit restart must replace the old later checkpoint before a subsequent resume.");
            Assert.That(tutorial.TryExit(pair.Player!), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();
    }

    private static int Count<T>(IEntityManager entMan) where T : IComponent
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<T>();
        while (query.MoveNext(out _, out _))
        {
            count++;
        }

        return count;
    }
}
