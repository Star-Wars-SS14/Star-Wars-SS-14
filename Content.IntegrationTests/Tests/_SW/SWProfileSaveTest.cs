using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.Lobby;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Log;
using Robust.UnitTesting;
using Serilog.Events;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Two saves of one slot in one frame used to run in parallel, so the second one failed in EF with
/// DbUpdateConcurrencyException. The handler is async void, so that exception ended the server process.
/// The test database is synchronous and cannot overlap by itself, so these tests hold writes open through a proxy.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerPreferencesManager))]
public sealed class SWProfileSaveTest
{
    private const string NameOne = "Анна Первая";
    private const string NameTwo = "Анна Вторая";

    [Test]
    public async Task WritesOfOnePlayerRunInOrder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            InLobby = true,
            Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        await pair.RunTicksSync(5);

        var proxy = InstallProxy(server);
        var started = new List<string>();
        var running = new List<TaskCompletionSource>();
        proxy.Write = (method, args) =>
        {
            started.Add(Describe(method, args));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            running.Add(gate);
            return gate.Task;
        };

        var clientPrefs = client.ResolveDependency<IClientPreferencesManager>();
        await client.WaitPost(() =>
        {
            var index = clientPrefs.Preferences.SelectedCharacterIndex;
            var profile = (HumanoidCharacterProfile) clientPrefs.Preferences.SelectedCharacter;
            clientPrefs.UpdateCharacter(profile.WithName(NameOne), index);
            clientPrefs.UpdateCharacter(profile.WithName(NameTwo), index);
            clientPrefs.SelectCharacter(index);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(started, Is.EqualTo(new[] { NameOne }), "A second write started while the first was running."));

        await server.WaitPost(() => running[0].SetResult());
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(started, Is.EqualTo(new[] { NameOne, NameTwo })));

        await server.WaitPost(() => running[1].SetResult());
        await pair.RunTicksSync(5);
        await server.WaitPost(() => running[2].SetResult());
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(started, Is.EqualTo(new[] { NameOne, NameTwo, nameof(IServerDbManager.SaveSelectedCharacterIndexAsync) }));

            // The last save wins in the cache as well as in the order of the writes.
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var cached = (HumanoidCharacterProfile) prefs.GetPreferences(pair.Player!.UserId).SelectedCharacter;
            Assert.That(cached.Name, Is.EqualTo(NameTwo));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedSaveIsLoggedAndServerSurvives()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            InLobby = true,
            Dirty = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        await pair.RunTicksSync(5);

        // This test expects an error in the log, which would otherwise fail it by itself.
        pair.ServerLogHandler.FailureLevel = null;
        var log = new RecordingLogHandler();
        server.ResolveDependency<ILogManager>().GetSawmill("prefs").AddHandler(log);

        var proxy = InstallProxy(server);
        var started = new List<string>();
        proxy.Write = (method, args) =>
        {
            started.Add(Describe(method, args));
            return started.Count == 1
                ? Task.FromException(new DbUpdateConcurrencyException("Test failure of the first save."))
                : Task.CompletedTask;
        };

        var clientPrefs = client.ResolveDependency<IClientPreferencesManager>();
        await client.WaitPost(() =>
        {
            var index = clientPrefs.Preferences.SelectedCharacterIndex;
            var profile = (HumanoidCharacterProfile) clientPrefs.Preferences.SelectedCharacter;
            clientPrefs.UpdateCharacter(profile.WithName(NameOne), index);
            clientPrefs.UpdateCharacter(profile.WithName(NameTwo), index);
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.IsAlive, "The failed save killed the server.");
            Assert.That(started, Is.EqualTo(new[] { NameOne, NameTwo }), "The queue stopped after the failed save.");
            Assert.That(log.Errors.Any(e => e.Contains(nameof(DbUpdateConcurrencyException))),
                "The failed save left nothing in the log.");
        });

        await pair.CleanReturnAsync();
    }

    private static SWWriteProxy InstallProxy(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var field = typeof(ServerPreferencesManager).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var inner = (IServerDbManager) field.GetValue(prefs)!;

        var db = DispatchProxy.Create<IServerDbManager, SWWriteProxy>();
        var proxy = (SWWriteProxy) (object) db;
        proxy.Inner = inner;
        field.SetValue(prefs, db);
        return proxy;
    }

    private static string Describe(string method, object?[] args)
    {
        return method == nameof(IServerDbManager.SaveCharacterSlotAsync)
            ? ((HumanoidCharacterProfile) args[1]!).Name
            : method;
    }

    public class SWWriteProxy : DispatchProxy
    {
        private static readonly HashSet<string> Writes =
        [
            nameof(IServerDbManager.SaveCharacterSlotAsync),
            nameof(IServerDbManager.SaveSelectedCharacterIndexAsync),
            nameof(IServerDbManager.SaveConstructionFavoritesAsync),
            nameof(IServerDbManager.DeleteSlotAndSetSelectedIndex),
        ];

        public IServerDbManager Inner = default!;
        public Func<string, object?[], Task>? Write;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Write != null && Writes.Contains(targetMethod!.Name))
                return Write(targetMethod.Name, args!);

            return targetMethod!.Invoke(Inner, args);
        }
    }

    private sealed class RecordingLogHandler : ILogHandler
    {
        public readonly List<string> Errors = new();

        public void Log(string sawmillName, LogEvent message)
        {
            if (message.Level >= LogEventLevel.Error)
                Errors.Add(message.RenderMessage());
        }
    }
}
