#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._SW.Droids;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Combat;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Damage.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

/// <summary>Force transport, the intelligence datapad and ordered defence droids, on the real campaign and real maps.</summary>
[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed partial class GlobalWarMechanicsTest
{
    private sealed class Campaign
    {
        public required Pair.TestPair Pair;
        public required ICommonSession[] Sessions;
        public Guid Generation;

        public RobustIntegrationTest.ServerIntegrationInstance Server => Pair.Server;

        public SWGlobalWarAssignment Actor(int index)
        {
            Assert.That(Server.System<SWGlobalWarAssignmentsSystem>().TryGetAssignment(Sessions[index].UserId, out var actor),
                Is.True);
            return actor;
        }

        public EntityUid Planet(string id)
        {
            Assert.That(Server.System<SWGlobalWarPlanetSystem>().TryGetPlanet(id, out var planet), Is.True, id);
            return planet!.Value;
        }

        public EntityUid Anchor(string sector, SWGlobalWarPlanetLocation location, string anchor)
        {
            Assert.That(Server.System<SWGlobalWarPlanetSystem>().TryEnsureLocation(Planet(sector), location, out _, out _),
                Is.True, sector);
            Assert.That(Server.System<SWGlobalWarMapAnchorSystem>().TryResolveAnchor(new SWGlobalWarAnchorRef
            {
                Sector = sector, Location = location, AnchorId = anchor,
            }, out var uid), Is.True, $"{sector} {location} {anchor}");
            return uid!.Value;
        }

        public void StandBeside(EntityUid body, EntityUid target)
        {
            var transform = Server.System<SharedTransformSystem>();
            var interaction = Server.System<SharedInteractionSystem>();
            var center = Server.EntMan.GetComponent<TransformComponent>(target).Coordinates;
            foreach (var offset in new[] { new Vector2(0, -1), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(1, 0) })
            {
                transform.SetCoordinates(body, center.Offset(offset));
                if (interaction.InRangeUnobstructed(body, target, range: 2f))
                    return;
            }
            Assert.Fail($"No reachable floor beside {Server.EntMan.ToPrettyString(target)}");
        }

        public async Task Advance(double seconds, int ticks = 45)
        {
            var timing = Server.ResolveDependency<IGameTiming>();
            await Server.WaitPost(() => timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromSeconds(seconds), timing.TimeBase.Item2));
            await Pair.RunTicksSync(ticks);
        }

        /// <summary>Peace ends by the head's real declaration, after the real peace period.</summary>
        public async Task DeclareWar(int head)
        {
            var timing = Server.ResolveDependency<IGameTiming>();
            var clock = Server.System<SWGlobalWarCampaignSystem>();
            var politics = Server.System<SWGlobalWarPoliticsSystem>();
            await Server.WaitPost(() =>
            {
                Assert.That(clock.TryGetCampaign(out var campaign), Is.True);
                timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromMinutes(61) - clock.Elapsed(campaign), timing.TimeBase.Item2);
            });
            await Pair.RunTicksSync(45);
            await Server.WaitAssertion(() =>
            {
                Assert.That(politics.TryGetPolitics(out var state), Is.True);
                if (state.Comp.Relations == SWGlobalWarRelations.War)
                    return;
                Assert.That(politics.Execute(Sessions[head], new SWGlobalWarPoliticalCommand(Generation, Guid.NewGuid(),
                    Actor(head).Revision, SWGlobalWarPoliticalAction.DeclareWar, state.Comp.RelationsRevision)).Success, Is.True);
            });
        }
    }

    private static async Task<Campaign> Start(Pair.TestPair pair, params string[] jobs)
    {
        var server = pair.Server;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var clientUser = server.PlayerMan.Sessions.Single().UserId;
        await server.AddDummySessions(jobs.Length - 1);
        var loaded = false;
        for (var attempt = 0; attempt < 100 && !loaded; attempt++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => loaded = server.PlayerMan.Sessions.All(p =>
                server.ResolveDependency<Content.Server.Database.UserDbDataManager>().IsLoadComplete(p) &&
                preferences.TryGetCachedPreferences(p.UserId, out _)));
        }
        Assert.That(loaded, Is.True);
        var sessions = server.PlayerMan.Sessions.OrderBy(p => p.UserId != clientUser).ToArray();
        for (var i = 0; i < sessions.Length; i++)
        {
            var profile = (HumanoidCharacterProfile) preferences.GetPreferences(sessions[i].UserId).SelectedCharacter;
            await pair.SetJobPriorities(sessions[i].UserId, profile.JobPriorities.Keys.Select(j => (j, JobPriority.Never))
                .Append(((ProtoId<JobPrototype>) jobs[i], JobPriority.High)).ToArray());
        }
        var campaign = new Campaign { Pair = pair, Sessions = sessions };
        var ticker = server.System<GameTicker>();
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameMap, "Empty");
            ticker.SetGamePreset("SWGlobalWar");
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
            Assert.That(ticker.PlayersJoinedRoundNormally, Is.EqualTo(jobs.Length));
            Assert.That(server.System<SWGlobalWarCampaignSystem>().TryGetCampaign(out var state), Is.True);
            campaign.Generation = state.Comp.Generation;
            foreach (var session in sessions)
                server.System<GodmodeSystem>().EnableGodmode(session.AttachedEntity!.Value);
        });
        return campaign;
    }

    [TestCase("SWGlobalWarRepublicForceUser", "SWGlobalWarR9", SWGlobalWarFaction.Republic)]
    [TestCase("SWGlobalWarEmpireForceUser", "SWGlobalWarE9", SWGlobalWarFaction.Empire)]
    public async Task ForceUserLeavesTheirWorldOnItsOwnTransport(string job, string world, SWGlobalWarFaction faction)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, job);
        var server = pair.Server;
        var entities = server.EntMan;
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var ownership = server.System<SWGlobalWarShipOwnershipSystem>();
        var travel = server.System<SWGlobalWarTravelSystem>();

        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            body = campaign.Actor(0).Body;
            Assert.That(planets.TryGetPhysicalLocation(body, out var here, out var location), Is.True);
            Assert.That(here, Is.EqualTo(campaign.Planet(world)));
            Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Surface));
            var teleporter = campaign.Anchor(world, SWGlobalWarPlanetLocation.Surface, "landingA");
            campaign.StandBeside(body, teleporter);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var teleporter = campaign.Anchor(world, SWGlobalWarPlanetLocation.Surface, "landingA");
            Assert.That(server.System<SWGlobalWarPlanetTeleporterSystem>().TryTeleport(teleporter, body, out var reason),
                Is.True, reason);
            Assert.That(planets.TryGetPhysicalLocation(body, out _, out var location), Is.True);
            Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Orbit));
        });
        await pair.RunSeconds(5);

        EntityUid hull = default;
        await server.WaitAssertion(() =>
        {
            var hulls = entities.EntityQuery<SWGlobalWarFleetShipComponent>().Select(c => c.Owner)
                .Where(s => ownership.TryGetOwner(s, campaign.Generation, out var owner, out _) && owner == faction &&
                            planets.TryGetPhysicalLocation(s, out var here, out _) && here == campaign.Planet(world))
                .ToArray();
            Assert.That(hulls, Has.Length.EqualTo(1), "Exactly one hull at this world, granted once.");
            hull = hulls[0];
            Assert.That(entities.GetComponent<SWGlobalWarFleetShipComponent>(hull).Class, Is.EqualTo(SWGlobalWarShipClass.Transport));
            Assert.That(planets.TryGetPhysicalLocation(hull, out var here, out var location), Is.True);
            Assert.That(here, Is.EqualTo(campaign.Planet(world)));
            Assert.That(location, Is.EqualTo(SWGlobalWarPlanetLocation.Orbit));
            Assert.That(entities.GetComponent<DockingComponent>(
                campaign.Anchor(world, SWGlobalWarPlanetLocation.Orbit, "dockTransport")).Docked, Is.True);
            var bridge = entities.EntityQuery<SWGlobalWarTravelConsoleComponent>().Select(c => c.Owner)
                .First(c => entities.GetComponent<TransformComponent>(c).GridUid == hull);
            campaign.StandBeside(body, bridge);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var bridge = entities.EntityQuery<SWGlobalWarTravelConsoleComponent>().Select(c => c.Owner)
                .First(c => entities.GetComponent<TransformComponent>(c).GridUid == hull);
            var destination = server.ResolveDependency<IPrototypeManager>()
                .Index<SWGlobalWarPlanetPrototype>(world).Connections.First(c =>
                    server.ResolveDependency<IPrototypeManager>().Index(c).InitialOwner == faction);
            Assert.That(travel.TryStartTravel(bridge, body, destination, out var reason), Is.True, reason);
            Assert.That(entities.HasComponent<FTLComponent>(hull), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IntelligenceAgentScoutsWithTheKitDatapadAndCannotFightInPeace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicIntelligence", "SWGlobalWarRepublicHead",
            "SWGlobalWarRepublicSoldier", "SWGlobalWarEmpireIntelligence");
        var server = pair.Server;
        var entities = server.EntMan;
        var recon = server.System<SWGlobalWarReconSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var hands = server.System<SharedHandsSystem>();

        const string enemy = "SWGlobalWarE1";
        EntityUid datapad = default;
        await server.WaitAssertion(() =>
        {
            foreach (var index in new[] { 0, 3 })
            {
                Assert.That(hands.EnumerateHeld(campaign.Actor(index).Body).Count(i =>
                        entities.GetComponent<MetaDataComponent>(i).EntityPrototype?.ID == "SWGlobalWarReconDatapad"),
                    Is.EqualTo(1), $"The kit of {campaign.Actor(index).Faction} intelligence has a datapad.");
            }
            datapad = hands.EnumerateHeld(campaign.Actor(0).Body).Single();
            campaign.StandBeside(campaign.Actor(0).Body,
                campaign.Anchor(enemy, SWGlobalWarPlanetLocation.Surface, "capturePrimary"));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var agent = campaign.Actor(0).Body;
            Assert.That(politics.CanConductHostility(agent, campaign.Planet(enemy), campaign.Generation), Is.False,
                "No weapons in peacetime, wherever the agent stands.");
            var market = entities.EntityQuery<SWGlobalWarMarketConsoleComponent>().Select(c => c.Owner).First(c =>
                entities.GetComponent<TransformComponent>(c).MapUid ==
                entities.GetComponent<TransformComponent>(agent).MapUid);
            campaign.StandBeside(agent, market);
            Assert.That(recon.TryObserve(datapad, agent, market), Is.True);
            Assert.That(recon.TryObserve(datapad, agent, market), Is.False, "One observation per 10 seconds.");

            Assert.That(recon.GetReports(agent, out var authorized), Has.Length.EqualTo(1));
            Assert.That(authorized, Is.True);
            Assert.That(recon.GetReports(agent, out _).Single().Observations, Has.Length.GreaterThan(2), "Enemy stock and defenders.");
            Assert.That(recon.GetReports(campaign.Actor(1).Body, out authorized), Has.Length.EqualTo(1), "The head reads it.");
            Assert.That(authorized, Is.True);
            Assert.That(recon.GetReports(campaign.Actor(3).Body, out _), Is.Empty, "The other side learns nothing.");

            // A stolen datapad does not make its thief an agent.
            var soldier = campaign.Actor(2).Body;
            campaign.StandBeside(soldier, market);
            Assert.That(recon.TryObserve(datapad, soldier, market), Is.False);
        });
        await campaign.DeclareWar(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(politics.CanConductHostility(campaign.Actor(0).Body, campaign.Planet(enemy), campaign.Generation),
                Is.True, "The agent fights as a soldier once war is open.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DefenceConsoleOrdersPaidFiniteDroidsThatHoldTheCaptureConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicWarMinister", "SWGlobalWarRepublicHead",
            "SWGlobalWarEmpireSoldier", "SWGlobalWarEmpireHead");
        var server = pair.Server;
        var entities = server.EntMan;
        var defence = server.System<SWGlobalWarDefenceSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var capture = server.System<SWGlobalWarCaptureSystem>();
        var loyalty = server.System<SWDroidLoyaltySystem>();
        var mobs = server.System<MobStateSystem>();

        const string home = "SWGlobalWarR0";
        var location = SWGlobalWarPlanetLocation.Orbit;
        long Budget() => ledger.GetBalance(SWGlobalWarFaction.Republic, SWGlobalWarAccount.War).Available;
        EntityUid[] Droids() => entities.EntityQuery<SWDroidCommandableComponent>().Select(c => c.Owner)
            .Where(d => mobs.IsAlive(d)).ToArray();

        long budget = 0;
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "defenceControl");
            campaign.StandBeside(campaign.Actor(0).Body, console);
            campaign.StandBeside(campaign.Actor(1).Body, console);
            campaign.StandBeside(campaign.Actor(2).Body, console);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "defenceControl");
            budget = Budget();

            Assert.That(defence.TryOrderDroid(console, campaign.Actor(1).Body, out _), Is.False, "A head with a minister in place has no war budget.");
            Assert.That(defence.TryOrderDroid(console, campaign.Actor(2).Body, out _), Is.False, "An enemy cannot order here.");
            Assert.That(Budget(), Is.EqualTo(budget));

            for (var i = 0; i < 3; i++)
                Assert.That(defence.TryOrderDroid(console, campaign.Actor(0).Body, out var failure), Is.True, failure);
            Assert.That(defence.TryOrderDroid(console, campaign.Actor(0).Body, out var limit), Is.False);
            Assert.That(limit, Is.EqualTo("sw-global-war-defence-order-location-limit"));

            Assert.That(Budget(), Is.EqualTo(budget - 3 * 8000));
            Assert.That(Droids(), Is.Empty, "Nothing stands there before the build time.");
        });
        await campaign.Advance(62);
        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var droids = Droids();
            Assert.That(droids, Has.Length.EqualTo(3));
            var console = campaign.Anchor(home, location, "capturePrimary");
            var center = server.System<SharedTransformSystem>().GetWorldPosition(console);
            foreach (var droid in droids)
            {
                Assert.That(entities.GetComponent<SWDroidCommandableComponent>(droid).Faction, Is.EqualTo("RepublicFaction"));
                var offset = Vector2.Abs(server.System<SharedTransformSystem>().GetWorldPosition(droid) - center);
                Assert.That(Math.Max(offset.X, offset.Y), Is.LessThanOrEqualTo(6f), "Each droid stands in the holding zone.");
                Assert.That(loyalty.IsTargetProtected(droid, campaign.Actor(0).Body), Is.True, "Never fires on its own side.");
                Assert.That(loyalty.IsTargetProtected(droid, campaign.Actor(2).Body), Is.False, "Fires on the enemy.");
                Assert.That(loyalty.IsTargetProtected(droid, droids.First(d => d != droid)), Is.True);
            }
            Assert.That(Budget(), Is.EqualTo(budget - 3 * 8000), "Delivery settles the reservation, never charges twice.");
        });

        // The side's cap holds across locations.
        foreach (var (sector, count) in new[] { (home, 3), ("SWGlobalWarR1", 3), ("SWGlobalWarR2", 3), ("SWGlobalWarR3", 0) })
        {
            await server.WaitAssertion(() =>
            {
                campaign.StandBeside(campaign.Actor(0).Body, campaign.Anchor(sector, SWGlobalWarPlanetLocation.Surface, "defenceControl"));
            });
            await pair.RunSeconds(1);
            await server.WaitAssertion(() =>
            {
                var console = campaign.Anchor(sector, SWGlobalWarPlanetLocation.Surface, "defenceControl");
                for (var i = 0; i < count; i++)
                    Assert.That(defence.TryOrderDroid(console, campaign.Actor(0).Body, out var failure), Is.True, failure);
                if (count > 0)
                    return;
                Assert.That(defence.TryOrderDroid(console, campaign.Actor(0).Body, out var refused), Is.False);
                Assert.That(refused, Is.EqualTo("sw-global-war-defence-order-side-limit"));
            });
        }

        // War: droids hold the objective with no operator until they are gone.
        await campaign.DeclareWar(1);
        var damage = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Heat");
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "capturePrimary");
            campaign.StandBeside(campaign.Actor(2).Body, console);
            campaign.StandBeside(campaign.Actor(0).Body, campaign.Anchor(home, SWGlobalWarPlanetLocation.Surface, "defenceControl"));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "capturePrimary");
            Assert.That(capture.TryExecute(campaign.Sessions[2], console, campaign.Generation, campaign.Actor(2).Revision,
                Guid.NewGuid(), false), Is.True);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "capturePrimary");
            Assert.That(capture.GetView(campaign.Sessions[2], console).Status, Is.EqualTo(SWGlobalWarCaptureStatus.Contested));
            var map = entities.GetComponent<TransformComponent>(console).MapUid;
            foreach (var droid in Droids().Where(d => entities.GetComponent<TransformComponent>(d).MapUid == map))
                server.System<DamageableSystem>().TryChangeDamage(droid, new DamageSpecifier(damage, 500), true);
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor(home, location, "capturePrimary");
            Assert.That(capture.GetView(campaign.Sessions[2], console).Status, Is.EqualTo(SWGlobalWarCaptureStatus.Capturing));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The balance check asked for MEGA-048, with scripted soldiers: wielded rifles, armour, perfect aim, shared fire
    /// from the first tick, no cover, no movement. A model of the fight, not a playtest. Run it when the limit changes.
    /// Droids exist to stop a small raid and buy time: a full squad gets through, a pair does not.
    /// </summary>
    [TestCase(5, 3, 4, 8), TestCase(2, 3, 0, 2), Explicit]
    public async Task ScriptedSoldiersAgainstTheDroidLimit(int soldierCount, int droidCount, int minWins, int maxWins)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicHead");
        await campaign.DeclareWar(0);
        var server = pair.Server;
        var entities = server.EntMan;
        var mobs = server.System<MobStateSystem>();
        var guns = server.System<SharedGunSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var lookup = server.System<EntityLookupSystem>();
        var transform = server.System<SharedTransformSystem>();

        List<EntityCoordinates> Cells(EntityUid console, int radius)
        {
            var center = entities.GetComponent<TransformComponent>(console).Coordinates;
            var result = new List<EntityCoordinates>();
            for (var x = -radius; x <= radius; x++)
            for (var y = -radius; y <= radius; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                    continue;
                var cell = center.Offset(new Vector2(x, y));
                var position = transform.ToMapCoordinates(cell);
                var bounds = Box2.CenteredAround(position.Position, new Vector2(0.7f));
                if (!lookup.AnyEntitiesIntersecting(position.MapId, bounds, LookupFlags.Static | LookupFlags.Dynamic) &&
                    interaction.InRangeUnobstructed(console, cell, radius * 1.5f))
                    result.Add(cell);
            }
            return result;
        }

        var wins = 0;
        var attempts = 0;
        var lossesInWins = new List<int>();
        // Written after the fights: the server thread can belong to the output of an earlier test.
        var log = new List<string>();
        var worlds = new[] { ("SWGlobalWarR0", SWGlobalWarPlanetLocation.Orbit), ("SWGlobalWarR1", SWGlobalWarPlanetLocation.Surface) };
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var (world, location) = worlds[attempt % worlds.Length];
            var droids = new List<EntityUid>();
            var soldiers = new List<(EntityUid Body, EntityUid Rifle)>();
            await server.WaitAssertion(() =>
            {
                var console = campaign.Anchor(world, location, "capturePrimary");
                var posts = Cells(console, 2);
                Assert.That(posts, Has.Count.GreaterThanOrEqualTo(4), world);
                for (var i = 0; i < droidCount; i++)
                    droids.Add(entities.SpawnEntity("SWGlobalWarDroidRepublic", posts[i * posts.Count / droidCount]));
            });
            // Droids become ready first, so that nobody is shot while the attackers are still being placed.
            await pair.RunSeconds(2);
            await server.WaitAssertion(() =>
            {
                var console = campaign.Anchor(world, location, "capturePrimary");
                // Inside the droids' 10 m range and sight; a corner of a wider square would be out of it.
                var firing = Cells(console, 6);
                Assert.That(firing, Has.Count.GreaterThanOrEqualTo(soldierCount), world);
                var inventory = server.System<Content.Shared.Inventory.InventorySystem>();
                var hands = server.System<SharedHandsSystem>();
                for (var i = 0; i < soldierCount; i++)
                {
                    var body = entities.SpawnEntity("MobHuman", firing[i * firing.Count / soldierCount]);
                    var suit = entities.SpawnEntity("SWImperialSuit", firing[i * firing.Count / soldierCount]);
                    inventory.TryEquip(body, suit, "outerClothing", silent: true, force: true);
                    var rifle = entities.SpawnEntity("SWE1BlasterRifle", firing[i * firing.Count / soldierCount]);
                    Assert.That(hands.TryPickupAnyHand(body, rifle), Is.True);
                    server.System<SharedWieldableSystem>().TryWield(rifle, entities.GetComponent<WieldableComponent>(rifle), body);
                    soldiers.Add((body, rifle));
                }
            });
            var elapsed = 0;
            while (elapsed < 120 * 30)
            {
                var done = false;
                await server.WaitPost(() =>
                {
                    var targets = droids.Where(d => entities.EntityExists(d) && mobs.IsAlive(d)).ToArray();
                    var standing = soldiers.Where(s => entities.EntityExists(s.Body) && mobs.IsAlive(s.Body)).ToArray();
                    done = targets.Length == 0 || standing.Length == 0;
                    if (done)
                        return;
                    foreach (var (body, rifle) in standing)
                    {
                        var from = transform.GetWorldPosition(body);
                        var target = targets.OrderBy(t => (transform.GetWorldPosition(t) - from).LengthSquared()).First();
                        guns.AttemptShoot(body, rifle, entities.GetComponent<GunComponent>(rifle),
                            entities.GetComponent<TransformComponent>(target).Coordinates, target);
                    }
                });
                if (done)
                    break;
                await pair.RunTicksSync(3);
                elapsed += 3;
            }
            // Bolts still in flight name their shooter, so the shooters outlive them.
            await pair.RunSeconds(2);
            await server.WaitPost(() =>
            {
                var droidsLeft = droids.Count(d => entities.EntityExists(d) && mobs.IsAlive(d));
                var soldiersLeft = soldiers.Count(s => entities.EntityExists(s.Body) && mobs.IsAlive(s.Body));
                if (droidsLeft == 0 && soldiersLeft > 0)
                {
                    wins++;
                    lossesInWins.Add(soldierCount - soldiersLeft);
                }
                var hurt = string.Join("/", soldiers.Select(q => entities.TryGetComponent<DamageableComponent>(q.Body, out var d) ? d.TotalDamage.Int() : -1));
                var aim = string.Join("/", soldiers.Select(q => !entities.EntityExists(q.Body) ? -1 : (int) (transform.GetWorldPosition(q.Body) -
                    transform.GetWorldPosition(campaign.Anchor(world, location, "capturePrimary"))).Length()));
                log.Add($"MODEL {soldierCount} soldiers, {droidCount} droids, attempt {attempt} {world}: droids left {droidsLeft}, soldiers left {soldiersLeft}, {elapsed / 30} s, soldier damage {hurt}, distance {aim}");
                foreach (var droid in droids.Where(entities.EntityExists))
                    entities.DeleteEntity(droid);
                foreach (var (body, rifle) in soldiers)
                {
                    if (entities.EntityExists(rifle))
                        entities.DeleteEntity(rifle);
                    if (entities.EntityExists(body))
                        entities.DeleteEntity(body);
                }
                foreach (var wreck in entities.EntityQuery<MetaDataComponent>().Where(m => m.EntityPrototype?.ID == "SWMobDroidBroken")
                             .Select(m => m.Owner).ToArray())
                    entities.DeleteEntity(wreck);
                attempts++;
            });
            await pair.RunTicksSync(5);
        }
        foreach (var line in log)
            TestContext.Out.WriteLine(line);
        TestContext.Out.WriteLine($"MODEL {soldierCount} soldiers, {droidCount} droids: wins {wins}/{attempts}; soldiers lost in wins: {string.Join(",", lossesInWins)}");
        Assert.That(attempts, Is.EqualTo(8));
        Assert.That(wins, Is.InRange(minWins, maxWins), "Soldiers taking the location out of 8 attempts.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CampaignDroidFiresOnStrangersWithoutATabletAndDoesNotDrainItsCell()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicHead");
        await campaign.DeclareWar(0);
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid droid = default;
        EntityUid stranger = default;
        EntityUid built = default;
        await server.WaitAssertion(() =>
        {
            var console = campaign.Anchor("SWGlobalWarR0", SWGlobalWarPlanetLocation.Orbit, "capturePrimary");
            var center = entities.GetComponent<TransformComponent>(console).Coordinates;
            // The factory path: a fabricated chassis takes its side from the chip the fabricator made.
            built = entities.SpawnEntity("SWMobDroidChassis", center.Offset(new Vector2(-1, -1)));
            var chassis = entities.GetComponent<Content.Shared.Silicons.Borgs.Components.BorgChassisComponent>(built);
            foreach (var module in new[] { "SWDroidAICoreModule", "SWDroidBlasterModule", "SWDroidFactionChipRepublic" })
                Assert.That(server.System<Robust.Shared.Containers.SharedContainerSystem>().Insert(
                    entities.SpawnEntity(module, center), chassis.ModuleContainer), Is.True, module);
            Assert.That(entities.GetComponent<SWDroidCommandableComponent>(built).Faction, Is.EqualTo("RepublicFaction"));
            droid = entities.SpawnEntity("SWGlobalWarDroidRepublic", center.Offset(new Vector2(0, -1)));
            stranger = entities.SpawnEntity("MobHuman", center.Offset(new Vector2(1, -1)));
            server.System<GodmodeSystem>().EnableGodmode(campaign.Actor(0).Body);
            campaign.StandBeside(campaign.Actor(0).Body, droid);
        });
        await pair.RunSeconds(6);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(stranger).TotalDamage.Int(), Is.GreaterThan(0),
                "It fires on a stranger with no operator.");
            Assert.That(entities.HasComponent<Content.Shared.PowerCell.Components.PowerCellDrawComponent>(droid), Is.False);
            Assert.That(entities.HasComponent<Content.Server._SW.Targeting.SWTargetingConfigComponent>(built), Is.True,
                "A hand-built droid guards too.");
            Assert.That(entities.GetComponent<Content.Shared.Silicons.Borgs.Components.BorgChassisComponent>(droid).Active, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
