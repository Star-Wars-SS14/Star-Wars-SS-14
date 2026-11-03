#nullable enable
using System.Linq;
using Content.Server._SW.GlobalWar.Identity;
using Content.Shared.Explosion;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

public sealed partial class GlobalWarMechanicsTest
{
    /// <summary>
    /// A body taken by controlmob is outside the campaign and fights as a mindless NPC does, while the campaign body
    /// of the same mind stays bound by peace.
    /// </summary>
    [Test]
    public async Task ABodyTakenOverByControlmobFightsOutsideTheCampaign()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
        });
        // The client session is the host admin in tests; the dummy soldier is an ordinary player.
        var campaign = await Start(pair, "SWGlobalWarRepublicCitizen", "SWGlobalWarRepublicSoldier");
        var server = pair.Server;
        var entities = server.EntMan;
        var identity = server.System<SWGlobalWarIdentitySystem>();
        var minds = server.System<SharedMindSystem>();
        var transform = server.System<SharedTransformSystem>();

        bool Cancelled(EntityUid user)
        {
            var attempt = new SWExplosionAttemptEvent(transform.GetMapCoordinates(user), null, user);
            entities.EventBus.RaiseEvent(EventSource.Local, ref attempt);
            return attempt.Cancelled;
        }

        await server.WaitAssertion(() =>
        {
            var soldier = campaign.Actor(1);
            Assert.That(identity.TryGetIdentity(soldier.Body, out _, out _), Is.True);
            Assert.That(Cancelled(soldier.Body), Is.True, "Peace closes combat for the campaign body.");

            var puppet = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(soldier.Body).Coordinates);
            minds.TransferTo(soldier.Mind, puppet);
            Assert.That(identity.TryGetIdentity(puppet, out _, out _), Is.False);
            Assert.That(entities.HasComponent<Content.Shared._SW.TaskSystem.SWFactionMemberComponent>(puppet), Is.False);
            Assert.That(Cancelled(puppet), Is.False, "The taken body blows things up like any NPC.");
            Assert.That(campaign.Sessions[1].AttachedEntity, Is.EqualTo(puppet));
        });
        await pair.CleanReturnAsync();
    }
}
