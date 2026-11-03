#nullable enable
using System;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Explosion;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

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
        });
        await pair.CleanReturnAsync();
    }
}
