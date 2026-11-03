using System;
using System.Linq;
using Content.Server._SW.GlobalWar;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server.GameTicking;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Mind;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._SW.GlobalWar;

[TestFixture]
// Client windows render text, and the client font cache is not safe for two clients at once.
[NonParallelizable]
public sealed class GlobalWarIdentityTest
{
    [Test]
    public async Task FactionLockSurvivesMindReplacementAndRejectsBodyLabelsAndVisits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var ticker = server.System<GameTicker>();
            var minds = server.System<SharedMindSystem>();
            var identity = server.System<SWGlobalWarIdentitySystem>();
            var session = server.PlayerMan.Sessions.Single();
            Assert.That(ticker.StartGameRule("SWGlobalWarCampaign", out var rule), Is.True);
            var generation = entities.GetComponent<SWGlobalWarGameruleComponent>(rule).Generation;
            var body = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var forged = entities.AddComponent<SWFactionMemberComponent>(body);
            forged.Faction = "EmpireFaction";
            forged.IsLeader = true;
            var mind = minds.CreateMind(session.UserId);
            minds.TransferTo(mind, body);
            Assert.That(identity.TryGetActor(session, body, out _, out _), Is.False);
            Assert.That(identity.TryEnroll(mind, SWGlobalWarFaction.Republic, Guid.NewGuid()), Is.False);
            Assert.That(identity.TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);
            Assert.That(forged.Faction, Is.EqualTo("RepublicFaction"));
            Assert.That(forged.IsLeader, Is.False);
            Assert.That(identity.TryGetActor(session, body, out var actualMind, out var faction), Is.True);
            Assert.That(actualMind, Is.EqualTo(mind.Owner));
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));

            // Neither a forged label nor a visited entity changes the authoritative faction.
            forged.Faction = "EmpireFaction";
            forged.IsLeader = true;
            Assert.That(identity.TryGetIdentity(body, out _, out faction), Is.True);
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));
            var visited = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            minds.Visit(mind, visited);
            Assert.That(identity.TryGetActor(session, visited, out _, out _), Is.False);
            Assert.That(identity.TryGetIdentity(body, out _, out _), Is.False);
            minds.UnVisit(mind);
            var replacementBody = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            minds.TransferTo(mind, replacementBody);
            Assert.That(forged.IsLeader, Is.False);
            // A body entered outside the campaign spawn, such as by controlmob, carries no side until enrolled.
            Assert.That(identity.TryGetActor(session, replacementBody, out _, out _), Is.False);
            Assert.That(identity.TryGetIdentity(body, out _, out _), Is.False);
            Assert.That(identity.TryEnroll(mind, SWGlobalWarFaction.Republic, generation), Is.True);
            Assert.That(identity.TryGetActor(session, replacementBody, out _, out faction), Is.True);
            Assert.That(faction, Is.EqualTo(SWGlobalWarFaction.Republic));

            minds.SetUserId(mind, null);
            var replacementMind = minds.CreateMind(session.UserId);
            minds.TransferTo(replacementMind, replacementBody);
            Assert.That(identity.CanJoin(session.UserId, SWGlobalWarFaction.Empire), Is.False);
            Assert.That(identity.TryEnroll(replacementMind, SWGlobalWarFaction.Empire, generation), Is.False);
            Assert.That(identity.TryEnroll(replacementMind, SWGlobalWarFaction.Republic, generation), Is.True);

            // Restarting a rule is not a new round and must not reset a user's lock.
            ticker.EndGameRule(rule);
            Assert.That(identity.TryGetIdentity(replacementBody, out _, out _), Is.False);
            Assert.That(ticker.StartGameRule("SWGlobalWarCampaign", out var nextRule), Is.True);
            var nextGeneration = entities.GetComponent<SWGlobalWarGameruleComponent>(nextRule).Generation;
            Assert.That(identity.TryEnroll(replacementMind, SWGlobalWarFaction.Republic, generation), Is.False);
            Assert.That(identity.TryEnroll(replacementMind, SWGlobalWarFaction.Empire, nextGeneration), Is.False);
            Assert.That(identity.TryEnroll(replacementMind, SWGlobalWarFaction.Republic, nextGeneration), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
