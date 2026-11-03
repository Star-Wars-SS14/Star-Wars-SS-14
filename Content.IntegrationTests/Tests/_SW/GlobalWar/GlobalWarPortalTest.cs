using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Access;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Server.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed class GlobalWarPortalTest : MovementTest
{
    protected override int Tiles => 6;

    [TestCase("PortalRed", false, false)]
    [TestCase("PortalRed", true, false)]
    [TestCase("PortalRed", false, true)]
    [TestCase("Gateway", false, false)]
    [TestCase("Gateway", true, false)]
    [TestCase("Gateway", false, true)]
    public async Task PreparedPortalTraversalUsesPolicyAndPreservesPulls(string prototype, bool enemy, bool pull)
    {
        EntityUid surface = default;
        EntityUid orbit = default;
        EntityUid portal = default;
        EntityUid destination = default;
        await Server.WaitAssertion(() =>
        {
            var planets = Server.System<SWGlobalWarPlanetSystem>();
            Server.System<GameTicker>().StartGameRule("WorldTestRule", out var rule);
            var generation = SEntMan.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            Assert.That(planets.TryGetPlanet("WorldTestHome", out var world), Is.True);
            Assert.That(planets.TryEnsureLocation(world!.Value, SWGlobalWarPlanetLocation.Surface, out var sm, out _), Is.True);
            Assert.That(planets.TryEnsureLocation(world.Value, SWGlobalWarPlanetLocation.Orbit, out var om, out _), Is.True);
            surface = sm!.Value;
            orbit = om!.Value;
            Transform.SetCoordinates(MapData.Grid, new EntityCoordinates(surface, Vector2.Zero));
            MapData.MapUid = surface;
            MapData.MapId = Server.Transform(surface).MapID;
            PlayerCoords = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid, .5f, .5f));
            TargetCoords = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid, 1.5f, .5f));
            Transform.SetCoordinates(SPlayer, SEntMan.GetCoordinates(PlayerCoords));
            var targetGrid = MapMan.CreateGridEntity(Server.Transform(orbit).MapID);
            MapSystem.SetTiles(targetGrid.Owner, targetGrid.Comp,
                new List<(Vector2i, Tile)> { (Vector2i.Zero, new Tile(1)), (new Vector2i(-1, 0), new Tile(1)) });
            portal = SEntMan.SpawnEntity(prototype, new EntityCoordinates(MapData.Grid, -1.5f, .5f));
            SEntMan.EnsureComponent<SWGlobalWarPortalProbeComponent>(portal);
            destination = SEntMan.SpawnEntity(prototype, new EntityCoordinates(targetGrid.Owner, .5f, .5f));
            // Prepared Gateway traversal, matching OpenPortal; this does not test its BUI or cooldown.
            foreach (var uid in new[] { portal, destination })
            {
                var component = SEntMan.EnsureComponent<PortalComponent>(uid);
                component.CanTeleportToOtherMaps = true;
                component.RandomTeleport = false;
            }
            Assert.That(Server.System<LinkedEntitySystem>().OneWayLink(portal, destination), Is.True);
            Server.System<PvsOverrideSystem>().AddSessionOverride(destination, ServerSession);
            var minds = Server.System<SharedMindSystem>();
            var mind = minds.CreateMind(ServerSession.UserId);
            minds.TransferTo(mind, SPlayer);
            Assert.That(Server.System<SWGlobalWarIdentitySystem>().TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);
            if (enemy)
                Assert.That(planets.TryChangeOwnership(world.Value, SWGlobalWarPlanetLocation.Orbit,
                    SWGlobalWarFaction.Empire, 0, generation, Guid.NewGuid(), out _), Is.True);
            Server.System<SWGlobalWarPortalTestSystem>().Reset(SPlayer);
        });
        await AddAtmosphere(surface);
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var cPortal = ToClient(SEntMan.GetNetEntity(portal));
            var cDestination = ToClient(SEntMan.GetNetEntity(destination));
            CEntMan.EnsureComponent<SWGlobalWarPortalProbeComponent>(cPortal);
            Assert.That(CEntMan.GetComponent<LinkedEntityComponent>(cPortal).LinkedEntities, Has.Count.EqualTo(1));
            Assert.That(Client.Transform(cDestination).MapID, Is.Not.EqualTo(MapId.Nullspace));
            Assert.That(CEntMan.HasComponent<SWGlobalWarGuardedMapComponent>(Client.Transform(CPlayer).MapUid), Is.True);
            // Upstream does not network this configuration field: explicitly enable prediction eligibility.
            CEntMan.GetComponent<PortalComponent>(cPortal).CanTeleportToOtherMaps = true;
            CEntMan.GetComponent<PortalComponent>(cDestination).CanTeleportToOtherMaps = true;
            Client.System<SWGlobalWarPortalTestSystem>().Reset(CPlayer);
        });
        if (pull)
        {
            await SpawnTarget("MobHuman");
            await PressKey(ContentKeyFunctions.TryPullObject);
            await RunTicks(5);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.GetComponent<PullerComponent>(SPlayer).Pulling, Is.EqualTo(STarget));
                Assert.That(SEntMan.GetComponent<PullableComponent>(STarget!.Value).Puller, Is.EqualTo(SPlayer));
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(CEntMan.GetComponent<PullerComponent>(CPlayer).Pulling, Is.EqualTo(CTarget));
                Assert.That(CEntMan.GetComponent<PullableComponent>(CTarget!.Value).Puller, Is.EqualTo(CPlayer));
                Client.System<SWGlobalWarPortalTestSystem>().ExpectedPull = CTarget;
            });
        }
        await Move(DirectionFlag.West, pull ? 1.2f : .7f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<SWGlobalWarPortalTestSystem>().Attempts, Is.GreaterThan(0),
                "The test must reach the real portal policy hook.");
            Assert.That(Server.Transform(SPlayer).MapUid, Is.EqualTo(enemy || pull ? surface : orbit));
            if (enemy || pull)
                Assert.That(SEntMan.HasComponent<PortalTimeoutComponent>(SPlayer), Is.False);
            if (pull)
            {
                Assert.That(Server.Transform(STarget!.Value).MapUid, Is.EqualTo(surface));
                Assert.That(SEntMan.GetComponent<PullerComponent>(SPlayer).Pulling, Is.EqualTo(STarget));
                Assert.That(SEntMan.GetComponent<PullableComponent>(STarget.Value).Puller, Is.EqualTo(SPlayer));
            }
        });
        await Client.WaitAssertion(() =>
        {
            var probe = Client.System<SWGlobalWarPortalTestSystem>();
            Assert.That(probe.Collisions, Is.GreaterThan(0), "The visible target must exercise client prediction.");
            Assert.That(probe.CrossMapPredictions, Is.Zero);
            Assert.That(probe.TimeoutPredictions, Is.Zero);
            Assert.That(probe.BrokenPullPredictions, Is.Zero);
            if (pull)
            {
                Assert.That(CEntMan.GetComponent<PullerComponent>(CPlayer).Pulling, Is.EqualTo(CTarget));
                Assert.That(CEntMan.GetComponent<PullableComponent>(CTarget!.Value).Puller, Is.EqualTo(CPlayer));
            }
        });
    }
}

/// <summary>Observes real collision callbacks before network correction without authorizing transitions.</summary>
public sealed class SWGlobalWarPortalTestSystem : EntitySystem
{
    public EntityUid? Subject;
    public int Attempts;
    public int Collisions;
    public int CrossMapPredictions;
    public int TimeoutPredictions;
    public EntityUid? ExpectedPull;
    public int BrokenPullPredictions;

    public void Reset(EntityUid subject)
    {
        Subject = subject;
        Attempts = 0;
        Collisions = 0;
        CrossMapPredictions = 0;
        TimeoutPredictions = 0;
        ExpectedPull = null;
        BrokenPullPredictions = 0;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarPortalAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<SWGlobalWarPortalProbeComponent, StartCollideEvent>(OnCollision,
            after: new[] { typeof(SharedPortalSystem) });
    }

    private void OnAttempt(ref SWGlobalWarPortalAttemptEvent args)
    {
        if (args.Subject == Subject)
            Attempts++;
    }

    private void OnCollision(Entity<SWGlobalWarPortalProbeComponent> ent, ref StartCollideEvent args)
    {
        if (args.OtherEntity != Subject || args.OurFixtureId != "portalFixture")
            return;
        Collisions++;
        if (Transform(args.OtherEntity).MapUid != Transform(ent).MapUid)
            CrossMapPredictions++;
        if (HasComp<PortalTimeoutComponent>(args.OtherEntity))
            TimeoutPredictions++;
        if (ExpectedPull is { } target &&
            (!TryComp<PullerComponent>(args.OtherEntity, out var puller) || puller.Pulling != target ||
             !TryComp<PullableComponent>(target, out var pulled) || pulled.Puller != args.OtherEntity ||
             pulled.PullJointId is not { } jointId ||
             !TryComp<JointComponent>(target, out var joints) || !joints.GetJoints.ContainsKey(jointId)))
            BrokenPullPredictions++;
    }
}

[RegisterComponent]
public sealed partial class SWGlobalWarPortalProbeComponent : Component;
