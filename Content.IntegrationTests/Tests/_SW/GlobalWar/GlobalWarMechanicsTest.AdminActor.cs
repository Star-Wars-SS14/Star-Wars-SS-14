#nullable enable
using System;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Electrocution;
using Content.Shared.Explosion;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMechanicsTest
{
    /// <summary>
    /// An active admin debugs the campaign: an aghost acts as the head of the side that owns the location, a body
    /// outside the campaign ignores peace, and entering again over a living character gives up the old office.
    /// </summary>
    [Test]
    public async Task AnActiveAdminActsAsTheLocalHeadAndMayEnterAgain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        // The client session is the host admin in tests.
        var campaign = await Start(pair, "SWGlobalWarRepublicSoldier");
        var server = pair.Server;
        var entities = server.EntMan;
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var ledger = server.System<SWGlobalWarLedgerSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var minds = server.System<SharedMindSystem>();
        var transform = server.System<SharedTransformSystem>();
        var session = campaign.Sessions[0];

        await server.WaitAssertion(() =>
        {
            Assert.That(server.ResolveDependency<IAdminManager>().IsAdmin(session), Is.True);
            var soldier = campaign.Actor(0);
            Assert.That(soldier.Role, Is.EqualTo(SWGlobalWarRole.Soldier), "In their own body the admin is the soldier.");

            // In their own campaign body an admin plays by the rules: peace closes combat.
            bool Cancelled(EntityUid user)
            {
                var attempt = new SWExplosionAttemptEvent(transform.GetMapCoordinates(user), null, user);
                entities.EventBus.RaiseEvent(EventSource.Local, ref attempt);
                return attempt.Cancelled;
            }
            Assert.That(Cancelled(soldier.Body), Is.True);

            // An aghost on a Republic world acts as the Republic head with every office.
            var ghost = entities.SpawnEntity("AdminObserver", entities.GetComponent<TransformComponent>(soldier.Body).Coordinates);
            // Upstream ElectrocutionHUDVisualizerSystem logs an error for every CMWindoor in view when this HUD leaves
            // the client's entity, which fails this test or the next one on the pooled pair.
            entities.RemoveComponent<ShowElectrocutionHUDComponent>(ghost);
            minds.Visit(soldier.Mind, ghost);
            Assert.That(assignments.TryGetAssignment(session.UserId, out var head), Is.True);
            Assert.That(head.Role, Is.EqualTo(SWGlobalWarRole.Head));
            Assert.That(head.Faction, Is.EqualTo(SWGlobalWarFaction.Republic));
            Assert.That(head.Body, Is.EqualTo(ghost));
            Assert.That(assignments.TryGetActor(session, campaign.Generation, head.Revision, out var actor), Is.True);
            Assert.That(assignments.GetAuthority(actor), Is.EqualTo(Enum.GetValues<SWGlobalWarAuthority>()
                .Aggregate(SWGlobalWarAuthority.None, (all, one) => all | one)));
            var finance = ledger.GetFinanceView(session, SWGlobalWarFaction.Republic);
            Assert.That(finance.Availability, Is.EqualTo(SWGlobalWarViewAvailability.Ready));
            Assert.That(finance.Budgets, Is.Not.Empty);
            Assert.That(ledger.GetFinanceView(session, SWGlobalWarFaction.Empire).Availability,
                Is.EqualTo(SWGlobalWarViewAvailability.Forbidden), "An Empire console on a Republic world stays closed.");
            // The office is not written to the registry: the side still has no head.
            Assert.That(assignments.TryGetRegistry(out var registry), Is.True);
            Assert.That(registry.Comp.Assignments.Values.Any(a => a.Role == SWGlobalWarRole.Head), Is.False);

            // The aghost may buy on the market, and may pick the other side to capture or command there.
            Assert.That(server.System<SWGlobalWarPoliticsSystem>().CanAcquireMarketGoods(ghost, campaign.Generation), Is.True);
            assignments.SetAdminSide(session.UserId, SWGlobalWarFaction.Empire);
            Assert.That(assignments.TryGetAssignment(session.UserId, out var empire), Is.True);
            Assert.That(empire.Faction, Is.EqualTo(SWGlobalWarFaction.Empire));
            assignments.SetAdminSide(session.UserId, null);

            // On a neutral world there is nobody to act for.
            Assert.That(planets.TryEnsureLocation(campaign.Planet("SWGlobalWarN0"), SWGlobalWarPlanetLocation.Surface,
                out var neutral, out _), Is.True);
            transform.SetCoordinates(ghost, new Robust.Shared.Map.EntityCoordinates(neutral!.Value, default));
            Assert.That(assignments.TryGetActor(session, campaign.Generation, head.Revision, out _), Is.False);

            minds.UnVisit(soldier.Mind);
            Assert.That(campaign.Actor(0), Is.EqualTo(soldier));

            // Entering again over the living soldier gives up the office, so the entry has a place.
            Assert.That(server.System<SWGlobalWarSpawnSystem>().TryResolveSpawn(session, "SWGlobalWarRepublicSoldier",
                out _, out var reason), Is.True, reason);
            Assert.That(assignments.TryReserveEntry(campaign.Generation, session.UserId, "SWGlobalWarRepublicSoldier",
                out var reservation), Is.True);
            Assert.That(registry.Comp.Assignments.Keys.Any(user => user == session.UserId), Is.False);
            assignments.ReleaseEntry(campaign.Generation, reservation.Id);

            // A humanoid spawned and taken over by the admin is outside the campaign: it fights in peace and acts
            // as the local head at the consoles.
            var puppet = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(soldier.Body).Coordinates);
            minds.TransferTo(soldier.Mind, puppet);
            Assert.That(Cancelled(puppet), Is.False);
            Assert.That(assignments.TryGetAssignment(session.UserId, out var puppetHead), Is.True);
            Assert.That(puppetHead.Role, Is.EqualTo(SWGlobalWarRole.Head));
            Assert.That(puppetHead.Body, Is.EqualTo(puppet));

            // The pair goes back to the pool: leave no admin ghost or puppet for the next test's client.
            minds.TransferTo(soldier.Mind, soldier.Body);
            entities.DeleteEntity(puppet);
            entities.DeleteEntity(ghost);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The aghost of an active admin works the campaign as the head of its side: it appoints a player, reads the
    /// character panel, declares war and captures a world for the side it picked.
    /// </summary>
    [Test]
    public async Task AnAdminGhostAppointsDeclaresWarAndCaptures()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicSoldier", "SWGlobalWarRepublicCitizen");
        var server = pair.Server;
        var entities = server.EntMan;
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var politics = server.System<SWGlobalWarPoliticsSystem>();
        var planets = server.System<SWGlobalWarPlanetSystem>();
        var minds = server.System<SharedMindSystem>();
        var admin = campaign.Sessions[0];
        var citizen = campaign.Sessions[1];
        var ghost = EntityUid.Invalid;
        var world = EntityUid.Invalid;
        var mind = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var soldier = campaign.Actor(0);
            ghost = entities.SpawnEntity("AdminObserver", entities.GetComponent<TransformComponent>(soldier.Body).Coordinates);
            // Upstream ElectrocutionHUDVisualizerSystem logs an error for every CMWindoor in view when this HUD leaves
            // the client's entity, which fails this test or the next one on the pooled pair.
            entities.RemoveComponent<ShowElectrocutionHUDComponent>(ghost);
            mind = soldier.Mind;
            minds.Visit(mind, ghost);
            Assert.That(planets.TryGetPhysicalLocation(ghost, out world, out _), Is.True);
            Assert.That(assignments.TryGetAssignment(admin.UserId, out var head), Is.True);
            Assert.That(head.Faction, Is.EqualTo(SWGlobalWarFaction.Republic));

            // The character panel opens for the aghost with the head's choices.
            var view = assignments.GetCharacterView(admin, entities.GetNetEntity(ghost));
            Assert.That(view.Availability, Is.EqualTo(SWGlobalWarViewAvailability.Ready));
            Assert.That(view.Role, Is.EqualTo(SWGlobalWarRole.Head));
            Assert.That(view.Living, Is.True);
            Assert.That(view.Roles, Is.Not.Empty);
            Assert.That(view.Targets.Any(t => t.User == citizen.UserId), Is.True);

            // The aghost offers the citizen a soldier's place, and the citizen takes it.
            var target = campaign.Actor(1);
            Assert.That(assignments.TryExecute(admin, new SWGlobalWarAppointmentCommand(campaign.Generation, Guid.NewGuid(),
                SWGlobalWarAppointmentAction.Offer, head.Revision, citizen.UserId, target.Revision,
                "SWGlobalWarRepublicSoldier"), out var offer, out var failure), Is.True, failure.ToString());
            Assert.That(assignments.TryExecute(citizen, new SWGlobalWarAppointmentCommand(campaign.Generation,
                Guid.NewGuid(), SWGlobalWarAppointmentAction.Accept, target.Revision, citizen.UserId, target.Revision,
                Offer: offer), out _, out failure), Is.True, failure.ToString());
            Assert.That(campaign.Actor(1).Role, Is.EqualTo(SWGlobalWarRole.Soldier));
        });

        // War opens after the peace period; the aghost declares it as the head.
        var timing = server.ResolveDependency<IGameTiming>();
        var clock = server.System<SWGlobalWarCampaignSystem>();
        await server.WaitPost(() =>
        {
            Assert.That(clock.TryGetCampaign(out var state), Is.True);
            timing.TimeBase = (timing.TimeBase.Item1 + TimeSpan.FromMinutes(61) - clock.Elapsed(state), timing.TimeBase.Item2);
        });
        await pair.RunTicksSync(45);
        await server.WaitAssertion(() =>
        {
            Assert.That(politics.TryGetPolitics(out var state), Is.True);
            Assert.That(assignments.TryGetAssignment(admin.UserId, out var head), Is.True);
            if (state.Comp.Relations != SWGlobalWarRelations.War)
            {
                Assert.That(politics.Execute(admin, new SWGlobalWarPoliticalCommand(campaign.Generation, Guid.NewGuid(),
                    head.Revision, SWGlobalWarPoliticalAction.DeclareWar, state.Comp.RelationsRevision)).Success, Is.True);
            }
            Assert.That(state.Comp.Relations, Is.EqualTo(SWGlobalWarRelations.War));

            // Its own side cannot take its own world; picked as the Empire, the aghost may take its orbit.
            Assert.That(politics.CanCapture(ghost, world, SWGlobalWarPlanetLocation.Orbit, campaign.Generation), Is.False);
            assignments.SetAdminSide(admin.UserId, SWGlobalWarFaction.Empire);
            Assert.That(politics.CanCapture(ghost, world, SWGlobalWarPlanetLocation.Orbit, campaign.Generation), Is.True);
            Assert.That(politics.CanConductHostility(ghost, world, campaign.Generation), Is.True);
            assignments.SetAdminSide(admin.UserId, null);

            // The pair goes back to the pool: leave no admin ghost for the next test's client.
            minds.UnVisit(mind);
            entities.DeleteEntity(ghost);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An admin who is a Force user may enter again in another role: the Force place of the old body is given up, so
    /// it no longer blocks the entry.
    /// </summary>
    [Test]
    public async Task AnAdminForceUserMayEnterAgain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        var campaign = await Start(pair, "SWGlobalWarRepublicForceUser");
        var server = pair.Server;
        var assignments = server.System<SWGlobalWarAssignmentsSystem>();
        var session = campaign.Sessions[0];

        await server.WaitAssertion(() =>
        {
            Assert.That(assignments.TryGetForceCarrier(session.UserId, out _), Is.True);
            Assert.That(server.System<SWGlobalWarSpawnSystem>().TryResolveSpawn(session, "SWGlobalWarRepublicSoldier",
                out _, out var reason), Is.True, reason);
            Assert.That(assignments.TryReserveEntry(campaign.Generation, session.UserId, "SWGlobalWarRepublicSoldier",
                out var reservation), Is.True);
            Assert.That(assignments.TryGetForceCarrier(session.UserId, out _), Is.False);
            assignments.ReleaseEntry(campaign.Generation, reservation.Id);
        });
        await pair.CleanReturnAsync();
    }
}
