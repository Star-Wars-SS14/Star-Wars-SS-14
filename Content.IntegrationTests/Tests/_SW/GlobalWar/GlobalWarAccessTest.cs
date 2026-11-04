using System;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Access;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarAccessTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: swGlobalWarPlanet
  id: AccessTestHome
  name: Home
  initialOwner: Republic
  surface: Empty
  orbit: Empty
  connections: [AccessTestEnemy]

- type: swGlobalWarPlanet
  id: AccessTestEnemy
  name: Enemy
  initialOwner: Empire
  orbit: Empty

- type: swGlobalWarPlanet
  id: AccessTestRemote
  name: Remote
  initialOwner: Republic
  orbit: Empty

- type: entity
  parent: SWGlobalWar
  id: AccessTestRule
  components:
  - type: SWGlobalWarGamerule
    planets: [AccessTestHome, AccessTestEnemy, AccessTestRemote]
""";

    [Test]
    public async Task PoliticalExceptionsAllowBreakoutButCannotForgeIdentityOrRoutes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var planets = server.System<SWGlobalWarPlanetSystem>();
            var policy = server.System<SWGlobalWarAccessPolicySystem>();
            var political = server.System<SWGlobalWarAccessTestSystem>();
            var minds = server.System<SharedMindSystem>();
            server.System<GameTicker>().StartGameRule("AccessTestRule", out var rule);
            var generation = entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            Assert.That(planets.TryGetPlanet("AccessTestHome", out var home), Is.True);
            Assert.That(planets.TryGetPlanet("AccessTestEnemy", out var enemy), Is.True);
            Assert.That(planets.TryGetPlanet("AccessTestRemote", out var remote), Is.True);
            Assert.That(planets.TryEnsureLocation(home!.Value, SWGlobalWarPlanetLocation.Surface, out var ground, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(home.Value, SWGlobalWarPlanetLocation.Orbit, out var orbit, out _), Is.True);
            var actor = entities.SpawnEntity(null, new EntityCoordinates(ground!.Value, 2, 2));
            var unknown = entities.SpawnEntity(null, new EntityCoordinates(ground.Value, 3, 2));
            var mind = minds.CreateMind(server.PlayerMan.Sessions.Single().UserId);
            minds.TransferTo(mind, actor);
            Assert.That(server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);
            var original = policy.EvaluateTransition(actor, home.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarTransitionKind.Landing, generation);
            Assert.That(original.Allowed, Is.True);
            Assert.That(planets.TryChangeOwnership(home.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarFaction.Empire, 0, generation, Guid.NewGuid(), out _), Is.True);
            var blocked = policy.EvaluateTransition(actor, home.Value, SWGlobalWarPlanetLocation.Orbit,
                SWGlobalWarTransitionKind.Landing, generation);
            Assert.That(blocked.Allowed, Is.False);
            Assert.That(blocked.PolicyRevision, Is.GreaterThan(original.PolicyRevision));

            // Political package C can permit a living garrison's breakout without changing ownership.
            try
            {
                political.AllowMilitaryTransitions = true;
                political.Generation = generation;
                Assert.That(policy.NotifyPolicyChanged(generation), Is.True);
                Assert.That(policy.EvaluateTransition(actor, home.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Landing, generation).Allowed, Is.True);
                Assert.That(policy.EvaluateTransition(unknown, home.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Landing, generation).Allowed, Is.False);
                Assert.That(policy.EvaluateTransition(actor, enemy!.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Ftl, generation).Allowed, Is.False, "Intersector entry cannot start on a surface.");
                server.System<SharedTransformSystem>().SetCoordinates(actor, new EntityCoordinates(orbit!.Value, 2, 2));
                Assert.That(policy.EvaluateTransition(actor, enemy.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Ftl, generation).Allowed, Is.True);
                Assert.That(policy.EvaluateTransition(actor, remote!.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Ftl, generation).Allowed, Is.False, "Political approval does not invent a graph edge.");
                Assert.That(policy.EvaluateTransition(actor, enemy.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarTransitionKind.Ftl, Guid.NewGuid()).Allowed, Is.False);
                Assert.That(entities.GetComponent<SWGlobalWarPlanetComponent>(home.Value).SurfaceOwner,
                    Is.EqualTo(SWGlobalWarFaction.Republic));
                Assert.That(entities.GetComponent<SWGlobalWarPlanetComponent>(home.Value).OrbitOwner,
                    Is.EqualTo(SWGlobalWarFaction.Empire));
            }
            finally
            {
                political.AllowMilitaryTransitions = false;
                political.Generation = Guid.Empty;
            }
        });
        await pair.CleanReturnAsync();
    }
}

/// <summary>Emulates a political policy consumer without implementing diplomatic gameplay in package B.</summary>
public sealed class SWGlobalWarAccessTestSystem : EntitySystem
{
    public bool AllowMilitaryTransitions;
    public Guid Generation;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarEvaluateAccessEvent>(OnEvaluate);
    }

    private void OnEvaluate(ref SWGlobalWarEvaluateAccessEvent args)
    {
        args.Allowed |= AllowMilitaryTransitions && args.Generation == Generation;
    }
}
