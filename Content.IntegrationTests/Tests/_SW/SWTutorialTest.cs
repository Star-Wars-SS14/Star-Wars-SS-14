using System.Linq;
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
                if (point.Point == "door" && xform.MapUid == mapUid)
                    server.System<SharedTransformSystem>().SetCoordinates(session.AttachedEntity!.Value, xform.Coordinates);
            }
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            Assert.That(tutorial.GetStudentStep(session.UserId), Is.EqualTo("SWTutorialStepRifle"),
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
            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToServer, out var notReady), Is.False);
            Assert.That(notReady?.Id, Is.EqualTo("sw-tutorial-error-mode-not-ready"));

            server.CfgMan.SetCVar(SWVars.TutorialMaxStudents, 0);
            Assert.That(tutorial.TryStart(session, SWTutorialChoice.NewToGame, out var full), Is.False);
            Assert.That(full?.Id, Is.EqualTo("sw-tutorial-error-full"));
            server.CfgMan.SetCVar(SWVars.TutorialMaxStudents, SWVars.TutorialMaxStudents.DefaultValue);
        });

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
