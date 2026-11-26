#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._SW.Targeting;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.TaskSystem.SeparatedTaskPipeline;
using Content.Server._SW.TaskSystem.StealObjectTask;
using Content.Server.GameTicking;
using Content.Shared._SW;
using Content.Shared._SW.ForceAbilities.SWStrangulateAbility;
using Content.Shared._SW.ForceAbilities.SWStrangulatуAbility;
using Content.Shared._SW.TaskSystem;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW;

[TestFixture]
public sealed class SWGameplayFixesTest
{
    private const string Map = "SWGameplayFixesTestMap";

    [TestPrototypes]
    private static readonly string Prototypes = @$"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            Passenger: [ -1, -1 ]
";

    /// <summary>
    /// The pipeline used to work during the map preload with the duration of the last round.
    /// </summary>
    [Test]
    public async Task PipelineWaitsForRound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();
        var task = new SWTaskPipelineInfo { TaskId = "KillCriminalTaskEmpire" };

        await server.WaitPost(() =>
        {
            var faction = server.EntMan.SpawnEntity("EmpireFaction", Robust.Shared.Map.MapCoordinates.Nullspace);
            var pipeline = server.EntMan.EnsureComponent<SWFactionSeparatedTaskPipelineComponent>(faction);
            pipeline.Tasks = new List<SWTaskPipelineInfo> { task };
        });

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        await pair.RunTicksSync(5);
        Assert.That(task.Added, Is.False, "The lobby must not run the task pipeline");

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(5);
        Assert.That(task.Added, Is.True);

        await server.WaitPost(() => ticker.RestartRound());
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SafeCounterSurvivesTheCap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var storage = server.System<SharedEntityStorageSystem>();

        await server.WaitAssertion(() =>
        {
            const string item = "SheetSteel1";
            var protos = new List<string> { item };

            var task = entMan.SpawnEntity(null, map.GridCoords);
            entMan.EnsureComponent<SWTaskComponent>(task).OwningFaction = "EmpireFaction";
            var collect = entMan.EnsureComponent<SWCollectObjectTaskComponent>(task);
            collect.ObjectsToCollect = new List<ObjectRequirement> { new(3, protos) };
            collect.ObjectsCollected = new List<ObjectRequirement> { new(0, protos) };
            var collected = collect.ObjectsCollected[0];

            var safe = entMan.SpawnEntity("SWTaskObjectsSafeEmpire", map.GridCoords);
            var items = new List<EntityUid>();
            for (var i = 0; i < 4; i++)
            {
                var uid = entMan.SpawnEntity(item, map.GridCoords);
                items.Add(uid);
                Assert.That(storage.Insert(uid, safe));
            }

            Assert.That(collected.Number, Is.EqualTo(3), "The 4th item used to drop the counter to 2");

            // 3 items left: still enough.
            Assert.That(storage.Remove(items[3], safe));
            Assert.That(collected.Number, Is.EqualTo(3));

            Assert.That(storage.Remove(items[2], safe));
            Assert.That(collected.Number, Is.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StrangulateStartsCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sith = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<SWStrangulateAbilityComponent>(sith);
            var victim = entMan.SpawnEntity("MobHuman", map.GridCoords);

            // Handled is what makes the action system start the cooldown.
            var ev = new SWRequestStrangulateEvent { Performer = sith, Target = victim };
            entMan.EventBus.RaiseLocalEvent(sith, (object) ev, true);
            Assert.That(ev.Handled, Is.True);
            Assert.That(entMan.HasComponent<SWBeingStrangulatedComponent>(victim));

            ev = new SWRequestStrangulateEvent { Performer = sith, Target = victim };
            entMan.EventBus.RaiseLocalEvent(sith, (object) ev, true);
            Assert.That(ev.Handled, Is.True);
            Assert.That(entMan.HasComponent<SWBeingStrangulatedComponent>(victim), Is.False);

            // A failed start spends nothing.
            ev = new SWRequestStrangulateEvent { Performer = sith, Target = map.Grid };
            entMan.EventBus.RaiseLocalEvent(sith, (object) ev, true);
            Assert.That(ev.Handled, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaperNeedsPen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var hands = server.System<SharedHandsSystem>();

        await server.WaitAssertion(() =>
        {
            var writer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var paper = entMan.SpawnEntity("Paper", map.GridCoords);
            var comp = entMan.GetComponent<PaperComponent>(paper);

            void Write(string text)
            {
                var message = new PaperComponent.PaperInputTextMessage(text) { Actor = writer };
                entMan.EventBus.RaiseLocalEvent(paper, (object) message, true);
            }

            Write("no pen");
            Assert.That(comp.Content, Is.EqualTo(string.Empty));

            var pen = entMan.SpawnEntity("Pen", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(writer, pen));
            Write("with pen");
            Assert.That(comp.Content, Is.EqualTo("with pen"));

            comp.StampedBy.Add(new StampDisplayInfo { StampedName = "test", StampedColor = Robust.Shared.Maths.Color.Red });
            Write("over a stamp");
            Assert.That(comp.Content, Is.EqualTo("with pen"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TurretConsoleTargetsHumanoidsWithoutId()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            foreach (var proto in new[] { "SWTurretConsoleEmpire", "SWTurretConsoleRepublic" })
            {
                var console = entMan.SpawnEntity(proto, map.GridCoords);
                Assert.That(entMan.GetComponent<SWTargetingConfigTargetsComponent>(console).TargetHumanoidsWithoutId, proto);
            }

            // A droid tablet stays peaceful until its owner sets targets.
            var tablet = entMan.SpawnEntity("SWDroidTargetingTabletEmpire", map.GridCoords);
            Assert.That(entMan.GetComponent<SWTargetingConfigTargetsComponent>(tablet).TargetHumanoidsWithoutId, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
