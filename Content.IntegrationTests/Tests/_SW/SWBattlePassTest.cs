#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._SW.BattlePass;
using Content.Server.Afk;
using Content.Server.Database;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.Cosmetics;
using Content.Shared._SW.TaskSystem;
using Content.Shared.CCVar;
using Content.Shared.Interaction.Events;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Everything a client can ask for is checked on the server: these tests ask for what must be refused.
/// </summary>
[TestFixture]
public sealed class SWBattlePassTest
{
    private const string Season = "SWSeasonColdWar";
    private const string CaseId = "SWCaseColdWar";

    private static async Task<(Pair.TestPair Pair, SWBattlePassSystem Pass, SWBattlePassSeasonPrototype Season, NetUserId User)> Setup(string now = "2026-12-20T12:00:00")
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        await server.WaitPost(() => cfg.SetCVar(SWVars.BattlePassNow, now));

        var proto = server.ProtoMan.Index<SWBattlePassSeasonPrototype>(Season);
        var pass = server.System<SWBattlePassSystem>();
        var user = server.ResolveDependency<IPlayerManager>().Sessions.First().UserId;
        return (pair, pass, proto, user);
    }

    /// <summary>
    /// The lines the design advisor checked against the picked costumes.
    /// </summary>
    [TestCase(3, SWBattlePassTrack.Paid, "SWNewYearCourierSuit", "SWNewYearCourierCap")]
    [TestCase(5, SWBattlePassTrack.Paid, "SWWinterRepublicCoverHelmet", "SWWinterImperialCoverHelmet")]
    [TestCase(7, SWBattlePassTrack.Paid, "SWNewYearCloakGreen")]
    [TestCase(9, SWBattlePassTrack.Paid, "SWWinterRepublicWorkCoat", "SWWinterImperialWorkCoat")]
    [TestCase(12, SWBattlePassTrack.Paid, "SWWinterRepublicParkaSuit", "SWWinterImperialParkaSuit")]
    [TestCase(14, SWBattlePassTrack.Paid, "SWNewYearDedMorozSuit")]
    [TestCase(17, SWBattlePassTrack.Paid, "SWWinterCloakFur")]
    [TestCase(22, SWBattlePassTrack.Paid, "SWWinterRepublicFrostSuit", "SWWinterImperialFrostSuit")]
    [TestCase(24, SWBattlePassTrack.Paid, "SWWinterRepublicFlightCoat", "SWWinterImperialFlightCoat")]
    [TestCase(27, SWBattlePassTrack.Paid, "SWWinterCloakPlaid")]
    [TestCase(29, SWBattlePassTrack.Paid, "SWWinterRepublicGreatcoat", "SWWinterImperialGreatcoat")]
    [TestCase(19, SWBattlePassTrack.Paid, "SWWinterRepublicOfficerSuit", "SWWinterImperialOfficerSuit")]
    [TestCase(20, SWBattlePassTrack.Free, "SWNewYearSnowmanSuit", "SWNewYearSnowmanHead")]
    [TestCase(10, SWBattlePassTrack.Free, "SWWinterCloakFrost")]
    [TestCase(10, SWBattlePassTrack.Paid, "SWWinterCloakDark")]
    [TestCase(15, SWBattlePassTrack.Paid, "SWCosmeticScarfBalmora", "SWNewYearSweater")]
    [TestCase(18, SWBattlePassTrack.Paid, "SWNewYearCloakRed", "SWNewYearCloakBlue")]
    [TestCase(20, SWBattlePassTrack.Paid, "SWWinterRepublicGarrisonSuit", "SWWinterImperialGarrisonSuit")]
    [TestCase(23, SWBattlePassTrack.Free, "SWWinterRepublicVeteranSuit", "SWWinterImperialVeteranSuit")]
    [TestCase(26, SWBattlePassTrack.Paid, "SWWinterCloakWool")]
    [TestCase(30, SWBattlePassTrack.Free, "SWWinterCloakLeather")]
    [TestCase(30, SWBattlePassTrack.Paid, "SWWinterRepublicColdWarSuit", "SWWinterRepublicSnowHelmet", "SWWinterImperialColdWarSuit", "SWWinterImperialSnowHelmet")]
    public async Task SeasonGivesThePickedCostumes(int tier, SWBattlePassTrack track, string first, params string[] also)
    {
        await using var pair = await PoolManager.GetServerClient();
        var protos = pair.Server.ProtoMan;
        var reward = SWBattlePassRules.GetReward(protos.Index<SWBattlePassSeasonPrototype>(Season), tier, track)!;

        Assert.That(reward.Cosmetic?.Id, Is.EqualTo(first));
        Assert.That(reward.Also.Select(a => a.Id), Is.EqualTo(also));
        foreach (var item in also.Append(first))
        {
            var effects = protos.Index<LoadoutPrototype>(item).Effects.OfType<SWCosmeticLoadoutEffect>();
            Assert.That(effects.Any(e => !e.Seasonal), $"{item} can be worn without owning it");
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneRewardOpensThePair()
    {
        var (pair, pass, season, user) = await Setup();
        var reward = season.Tiers[0].Free!;
        const string other = "SWCosmeticScarfCoal";

        await pair.Server.WaitAssertion(() =>
        {
            var data = pass.GetData(user)!;
            data.Xp = 100000;

            // Prototypes are shared with other tests in this pool, so the extra item goes away again.
            reward.Also.Add(other);
            try
            {
                Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out _), Is.True);
                Assert.That(pass.IsOwned(data, reward.Cosmetic!.Value), Is.True);
                Assert.That(pass.IsOwned(data, other), Is.True);
                Assert.That(data.State.Snowflakes, Is.EqualTo(0), "nothing here was owned before");
            }
            finally
            {
                reward.Also.Remove(other);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClaimsAreCheckedAgainstTheServer()
    {
        var (pair, pass, season, user) = await Setup();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var data = pass.GetData(user)!;
            Assert.That(data, Is.Not.Null);
            data.Xp = 0;

            // Tier not reached.
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out var refusal), Is.False);
            Assert.That(refusal?.Id, Is.EqualTo("sw-battlepass-refused-claim"));

            // A tier that does not exist, claimed by a modified client.
            data.Xp = 100000;
            Assert.That(pass.TryClaim(data, season, 31, SWBattlePassTrack.Free, out _), Is.False);
            Assert.That(pass.TryClaim(data, season, -1, SWBattlePassTrack.Free, out _), Is.False);

            // A tier with no reward on this track.
            Assert.That(pass.TryClaim(data, season, 3, SWBattlePassTrack.Free, out _), Is.False);

            // Paid refused, then granted, then fine.
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Paid, out refusal), Is.False);
            Assert.That(refusal?.Id, Is.EqualTo("sw-battlepass-refused-paid"));
            Assert.That(pass.IsOwned(data, "SWCosmeticScarfFrost"), Is.False);
            data.Paid = true;
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Paid, out _), Is.True);
            Assert.That(pass.IsOwned(data, "SWCosmeticScarfFrost"), Is.True);

            // Twice is refused and gives nothing more.
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out _), Is.True);
            Assert.That(pass.IsOwned(data, "SWCosmeticScarfWatch"), Is.True);
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeasonEndClosesXpAndLaterClaims()
    {
        var (pair, pass, season, user) = await Setup();
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var data = pass.GetData(user)!;
            var xp = SWBattlePassRules.XpForTier(season, 2);
            data.Xp = xp;
            data.Paid = false;

            cfg.SetCVar(SWVars.BattlePassNow, "2027-01-31T20:59:00");
            Assert.That(pass.IsXpOpen(season, data), Is.True, "the last Moscow day counts in full");

            cfg.SetCVar(SWVars.BattlePassNow, "2027-01-31T21:01:00");
            Assert.That(pass.IsXpOpen(season, data), Is.False);
            pass.AddXp(data, season, "test", 100);
            Assert.That(data.Xp, Is.EqualTo(xp), "no XP after the end");
            Assert.That(pass.AreClaimsOpen(season, data), Is.False, "the free track closes with the season");
            Assert.That(pass.TryClaim(data, season, 1, SWBattlePassTrack.Free, out var refusal), Is.False);
            Assert.That(refusal?.Id, Is.EqualTo("sw-battlepass-refused-closed"));

            // What was earned and not taken is handed over, not lost.
            pass.SendState(server.ResolveDependency<IPlayerManager>().Sessions.First());
            Assert.That(data.State.Claimed, Does.Contain("1f"));
            Assert.That(data.State.Claimed, Does.Contain("2f"));
            Assert.That(data.State.Cases[CaseId], Is.EqualTo(1));
            Assert.That(pass.IsOwned(data, "SWCosmeticScarfWatch"), Is.True);
            Assert.That(data.State.Claimed, Does.Not.Contain("1p"), "the paid track was not granted");

            // A paid track does not expire until it is finished.
            data.Paid = true;
            Assert.That(pass.IsXpOpen(season, data), Is.True);
            Assert.That(pass.AreClaimsOpen(season, data), Is.True);
            data.Xp = SWBattlePassRules.XpForTier(season, season.Tiers.Count);
            Assert.That(pass.IsXpOpen(season, data), Is.False);

            data.Paid = false;
            // Before the season nothing is open, whatever the client asks.
            cfg.SetCVar(SWVars.BattlePassPreseasonOpen, false);
            cfg.SetCVar(SWVars.BattlePassNow, "2026-10-01T00:00:00");
            Assert.That(pass.IsStarted(season), Is.False);
            Assert.That(pass.IsXpOpen(season, data), Is.False);
            cfg.SetCVar(SWVars.BattlePassNow, "2026-12-20T00:00:00");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaseOddsPityAndDuplicates()
    {
        var (pair, pass, _, user) = await Setup();
        var server = pair.Server;
        var proto = server.ProtoMan.Index<SWBattlePassCasePrototype>(CaseId);

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.Entries.Sum(e => e.Chance), Is.EqualTo(100f).Within(0.01f), "the printed odds add up");

            // The odds the window prints are the odds the server rolls.
            var counts = new int[proto.Entries.Count];
            const int rolls = 20000;
            for (var i = 0; i < rolls; i++)
            {
                counts[pass.RollEntry(proto, false)]++;
            }

            for (var i = 0; i < counts.Length; i++)
            {
                Assert.That(counts[i] * 100f / rolls, Is.EqualTo(proto.Entries[i].Chance).Within(1.5f), $"line {i}");
            }

            // Pity only rolls rare or better.
            for (var i = 0; i < 200; i++)
            {
                var entry = proto.Entries[pass.RollEntry(proto, true)];
                Assert.That(entry.Cosmetic, Is.Not.Null);
                Assert.That(entry.Rarity, Is.EqualTo(SWCosmeticRarity.Rare), "the guarantee is a rare, not better");
            }

            var data = pass.GetData(user)!;

            // No case, no roll.
            data.State.Cases.Clear();
            Assert.That(pass.TryOpenCase(data, CaseId, out _, out _, out _), Is.False);
            Assert.That(pass.TryOpenCase(data, "NotACase", out _, out _, out _), Is.False);

            // Eight empty cases, then the ninth gives a rare.
            data.State.Cases[CaseId] = 1;
            data.State.FirstCaseOpened = true;
            data.State.CasesSincePity = 8;
            Assert.That(pass.TryOpenCase(data, CaseId, out var index, out _, out _), Is.True);
            Assert.That(proto.Entries[index].Rarity, Is.EqualTo(SWCosmeticRarity.Rare));
            Assert.That(proto.Entries[index].Cosmetic, Is.Not.Null);
            Assert.That(data.State.CasesSincePity, Is.EqualTo(0));
            Assert.That(data.State.Cases.GetValueOrDefault(CaseId), Is.EqualTo(0), "the case is spent");

            // Every line owned: everything is a duplicate, paid in snowflakes.
            foreach (var entry in proto.Entries)
            {
                if (entry.Cosmetic != null)
                    data.Granted.Add(entry.Cosmetic.Value);
            }

            data.State.Snowflakes = 0;
            data.State.Cases[CaseId] = 200;
            var duplicates = 0;
            var expected = 0;
            for (var i = 0; i < 200; i++)
            {
                Assert.That(pass.TryOpenCase(data, CaseId, out index, out var duplicate, out var flakes), Is.True);
                var entry = proto.Entries[index];
                Assert.That(flakes, Is.EqualTo(entry.Cosmetic != null ? entry.DuplicateSnowflakes : entry.Snowflakes));
                Assert.That(duplicate, Is.EqualTo(entry.Cosmetic != null));
                expected += flakes;
                if (duplicate)
                    duplicates++;
            }

            Assert.That(duplicates, Is.GreaterThan(0));
            Assert.That(data.State.Snowflakes, Is.EqualTo(expected));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BuyingNeedsSnowflakes()
    {
        var (pair, pass, _, user) = await Setup();
        var server = pair.Server;
        var proto = server.ProtoMan.Index<SWBattlePassCasePrototype>(CaseId);
        var index = proto.Entries.FindIndex(e => e.Rarity == SWCosmeticRarity.Rare);

        await server.WaitAssertion(() =>
        {
            var data = pass.GetData(user)!;
            var price = proto.Entries[index].Price;
            var item = proto.Entries[index].Cosmetic!.Value;

            data.State.Snowflakes = price - 1;
            Assert.That(pass.TryBuyEntry(data, CaseId, index), Is.False);
            Assert.That(pass.IsOwned(data, item), Is.False);

            // Lines out of range from a modified client.
            Assert.That(pass.TryBuyEntry(data, CaseId, -1), Is.False);
            Assert.That(pass.TryBuyEntry(data, CaseId, 99), Is.False);
            Assert.That(pass.TryBuyEntry(data, CaseId, 0), Is.False, "snowflakes are not for sale for snowflakes");

            data.State.Snowflakes = price;
            Assert.That(pass.TryBuyEntry(data, CaseId, index), Is.True);
            Assert.That(pass.IsOwned(data, item), Is.True);
            Assert.That(data.State.Snowflakes, Is.EqualTo(0));

            // Owned already: no second purchase.
            data.State.Snowflakes = price;
            Assert.That(pass.TryBuyEntry(data, CaseId, index), Is.False);
            Assert.That(data.State.Snowflakes, Is.EqualTo(price));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaidTrackHoldsNoRandomBoxes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var season = pair.Server.ProtoMan.Index<SWBattlePassSeasonPrototype>(Season);

        Assert.Multiple(() =>
        {
            for (var i = 0; i < season.Tiers.Count; i++)
            {
                var paid = season.Tiers[i].Paid;
                if (paid == null)
                    continue;

                Assert.That(paid.Case, Is.Null, $"tier {i + 1}: a case on the paid track is a paid random box");
                Assert.That(paid.Snowflakes, Is.EqualTo(0), $"tier {i + 1}: snowflakes buy case items, so they cannot be paid for");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProgressSurvivesInTheDatabase()
    {
        var (pair, pass, season, user) = await Setup();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        await server.WaitPost(() =>
        {
            var data = pass.GetData(user)!;
            data.Xp = 4321;
            data.Paid = true;
            data.State.Snowflakes = 77;
            data.State.Cases[CaseId] = 3;
            data.State.Claimed.Add("1f");
            pass.AddXp(data, season, "test", 1);
        });

        await server.WaitPost(() => pass.SendState(server.ResolveDependency<IPlayerManager>().Sessions.First()));
        await pass.FlushAsync();
        await pair.RunTicksSync(5);

        var row = await db.GetSWBattlePassProgress(user.UserId, Season);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Xp, Is.EqualTo(4322));
        Assert.That(row.Paid, Is.True);

        var saved = SWBattlePassSavedState.Deserialize(row.State);
        Assert.That(saved.Snowflakes, Is.EqualTo(77));
        Assert.That(saved.Cases[CaseId], Is.EqualTo(3));
        Assert.That(saved.Claimed, Does.Contain("1f"));
        Assert.That(saved.XpBySource["test"], Is.EqualTo(1));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CatchUpBankCountsMissedDays()
    {
        var (pair, pass, season, user) = await Setup("2026-12-15T12:00:00");
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var sessions = server.ResolveDependency<IPlayerManager>();
            var data = pass.GetData(user)!;
            data.State = new SWBattlePassSavedState();

            // Long after the season's date, with no release day set: that date must not double anyone's XP.
            pass.SendState(sessions.Sessions.First());
            Assert.That(data.State.CatchUpBank, Is.EqualTo(0));

            // The day of the release: nothing missed yet.
            cfg.SetCVar(SWVars.BattlePassCatchUpFrom, "2026-12-15");
            pass.SendState(sessions.Sessions.First());
            Assert.That(data.State.CatchUpBank, Is.EqualTo(0));

            // Two days later: both are in the bank.
            cfg.SetCVar(SWVars.BattlePassNow, "2026-12-17T12:00:00");
            pass.SendState(sessions.Sessions.First());
            Assert.That(data.State.CatchUpBank, Is.EqualTo(season.CatchUpPerDay * 2));

            // Counted once.
            pass.SendState(sessions.Sessions.First());
            Assert.That(data.State.CatchUpBank, Is.EqualTo(season.CatchUpPerDay * 2));

            // Someone who comes late gets the same, counted from the release and not from the season's date.
            data.State = new SWBattlePassSavedState();
            pass.SendState(sessions.Sessions.First());
            Assert.That(data.State.CatchUpBank, Is.EqualTo(season.CatchUpPerDay * 2));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Time at the keyboard is not play: the minutes only run for moving, shooting or touching something.
    /// </summary>
    [Test]
    public async Task MinutesNeedMovementOrAction()
    {
        var (pair, pass, _, user) = await Setup();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var players = server.ResolveDependency<IPlayerManager>();
            var session = players.Sessions.First();
            var xform = server.System<SharedTransformSystem>();
            cfg.SetCVar(SWVars.BattlePassMinPlayers, 1);

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<SWFactionMemberComponent>(mob).Faction = "RepublicFaction";
            entMan.EnsureComponent<SWBattlePassPlayerComponent>(mob);
            players.SetAttachedEntity(session, mob, true);

            var tracker = new SWBattlePassRoundTracker();
            pass.InjectTrackerForTest(user, tracker);
            var timing = server.ResolveDependency<IGameTiming>();
            var afk = server.ResolveDependency<IAfkManager>();

            // The first sample of a new body counts as active: it has just spawned.
            afk.PlayerDidAction(session);
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(15));

            // Standing still past the grace, even with the keyboard in use.
            tracker.LastActed = timing.CurTime - TimeSpan.FromMinutes(2);
            for (var i = 0; i < 4; i++)
            {
                afk.PlayerDidAction(session);
                pass.Sample(15);
            }

            Assert.That(tracker.ActiveSeconds, Is.EqualTo(15), "standing still pays nothing");

            // Moving a little is not walking.
            tracker.LastActed = timing.CurTime - TimeSpan.FromMinutes(2);
            xform.SetWorldPosition(mob, xform.GetWorldPosition(mob) + new System.Numerics.Vector2(1, 0));
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(15), "one tile is not moving");

            // Walking pays.
            xform.SetWorldPosition(mob, xform.GetWorldPosition(mob) + new System.Numerics.Vector2(5, 0));
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(30));

            // Walking back and forth ends where it began, and it is still walking.
            tracker.LastActed = timing.CurTime - TimeSpan.FromMinutes(2);
            xform.SetWorldPosition(mob, xform.GetWorldPosition(mob) + new System.Numerics.Vector2(2, 0));
            pass.MeasureMovement();
            xform.SetWorldPosition(mob, xform.GetWorldPosition(mob) - new System.Numerics.Vector2(2, 0));
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(45), "the path counts, not the distance between samples");

            // What was walked before a sample does not carry over to the next one.
            tracker.LastActed = timing.CurTime - TimeSpan.FromMinutes(2);
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(45));

            // Standing and shooting pays, and so does using something.
            var shot = new ShotAttemptedEvent { User = mob };
            entMan.EventBus.RaiseLocalEvent(mob, ref shot);
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(60));

            tracker.LastActed = timing.CurTime - TimeSpan.FromMinutes(2);
            entMan.EventBus.RaiseLocalEvent(mob, new ContactInteractionEvent(mob));
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(75));

            // A dead body earns nothing, however busy.
            server.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
            afk.PlayerDidAction(session);
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(75));

            // Alone on the server the minutes do not count at all.
            cfg.SetCVar(SWVars.BattlePassMinPlayers, 2);
            server.System<MobStateSystem>().ChangeMobState(mob, MobState.Alive);
            afk.PlayerDidAction(session);
            tracker.LastActed = timing.CurTime;
            pass.Sample(15);
            Assert.That(tracker.ActiveSeconds, Is.EqualTo(75));
            cfg.SetCVar(SWVars.BattlePassMinPlayers, 2);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FirstCaseIsAlwaysTheAnnouncedItem()
    {
        var (pair, pass, _, user) = await Setup();
        var server = pair.Server;
        var proto = server.ProtoMan.Index<SWBattlePassCasePrototype>(CaseId);

        await server.WaitAssertion(() =>
        {
            var first = proto.FirstEntry!.Value;
            for (var i = 0; i < 50; i++)
            {
                var data = pass.GetData(user)!;
                data.State = new SWBattlePassSavedState();
                data.State.Cases[CaseId] = 2;
                data.State.CasesSincePity = 3;

                Assert.That(pass.TryOpenCase(data, CaseId, out var entry, out _, out _), Is.True);
                Assert.That(entry, Is.EqualTo(first));
                Assert.That(data.State.CasesSincePity, Is.EqualTo(3), "the first case stays out of the count");
                Assert.That(data.State.FirstCaseOpened, Is.True);
            }

            Assert.That(proto.Entries[first].Cosmetic, Is.Not.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShownOddsMatchAVeryLongRun()
    {
        var (pair, pass, _, _) = await Setup();
        var server = pair.Server;
        var proto = server.ProtoMan.Index<SWBattlePassCasePrototype>(CaseId);

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.Entries.Sum(e => e.Chance), Is.EqualTo(100f).Within(0.001f), "the base column adds up");

            var shown = SWBattlePassRules.ShownLongRunShares(proto);
            Assert.That(shown.Sum(), Is.EqualTo(100f).Within(0.01f), "the long run column adds up");

            const int rolls = 100000;
            var counts = new int[proto.Entries.Count];
            var sincePity = 0;
            var ninth = 0;
            for (var i = 0; i < rolls; i++)
            {
                var pity = sincePity >= proto.PityAfter;
                var entry = pass.RollEntry(proto, pity);
                if (pity)
                {
                    ninth++;
                    Assert.That(proto.Entries[entry].Rarity, Is.EqualTo(proto.PityRarity));
                }

                counts[entry]++;
                sincePity = SWBattlePassRules.ResetsPity(proto, proto.Entries[entry]) ? 0 : sincePity + 1;
            }

            Assert.That(ninth, Is.GreaterThan(0), "the guarantee fired");
            for (var i = 0; i < counts.Length; i++)
            {
                Assert.That(counts[i] * 100f / rolls, Is.EqualTo(shown[i]).Within(0.5f), $"line {i}");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PremiumPaysEveryTierAndFreeMaySkip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var season = pair.Server.ProtoMan.Index<SWBattlePassSeasonPrototype>(Season);
        var flakes = new[] { 5, 12, 17, 21, 24, 27 };

        Assert.Multiple(() =>
        {
            var emptyInARow = 0;
            var total = 0;
            for (var tier = 1; tier <= season.Tiers.Count; tier++)
            {
                Assert.That(SWBattlePassRules.GetReward(season, tier, SWBattlePassTrack.Paid), Is.Not.Null, $"premium tier {tier} is empty");

                var free = SWBattlePassRules.GetReward(season, tier, SWBattlePassTrack.Free);
                emptyInARow = free == null ? emptyInARow + 1 : 0;
                Assert.That(emptyInARow, Is.LessThanOrEqualTo(2), $"three free tiers in a row without a reward, up to {tier}");

                total += free?.Snowflakes ?? 0;
                if (flakes.Contains(tier))
                    Assert.That(free!.Snowflakes, Is.EqualTo(15), $"tier {tier}");
            }

            Assert.That(total, Is.EqualTo(90));
            Assert.That(SWBattlePassRules.GetReward(season, 3, SWBattlePassTrack.Free), Is.Null);

            var cases = Enumerable.Range(1, season.Tiers.Count)
                .Where(t => SWBattlePassRules.GetReward(season, t, SWBattlePassTrack.Free)?.Case != null);
            Assert.That(cases, Is.EquivalentTo(new[] { 2, 7, 11, 15, 18, 22, 28 }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WearKeepsWhatThePlayerChoseByHand()
    {
        var (pair, _, _, _) = await Setup();
        var server = pair.Server;
        const string group = "SWCosmeticsNeck";
        const string first = "SWCosmeticScarfWatch";
        const string second = "SWCosmeticScarfFrost";
        const string chosen = "SWCosmeticScarfCoal";
        const string chosenRole = "JobSWRepublicMedic";

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan;
            var profile = new HumanoidCharacterProfile();
            var entMan = server.EntMan;

            var worn = SWCosmeticWear.Wear(profile, first, group, null, entMan, proto);
            Assert.That(worn.Changed, Is.GreaterThan(10));
            profile = worn.Profile;
            Assert.That(profile.Loadouts.ContainsKey("JobSWJedi") && profile.Loadouts["JobSWJedi"].SelectedLoadouts.ContainsKey(group), Is.False,
                "a job with its own neck item is left alone");

            // The player picks something else for one job, by hand.
            var medic = profile.Loadouts[chosenRole].Clone();
            medic.SelectedLoadouts[group] = new List<Loadout> { new() { Prototype = chosen } };
            profile = profile.WithLoadout(medic);

            var again = SWCosmeticWear.Wear(profile, second, group, null, entMan, proto);
            Assert.That(again.KeptOther, Is.EqualTo(1));
            Assert.That(again.Changed, Is.EqualTo(worn.Changed - 1));
            Assert.That(again.Profile.Loadouts[chosenRole].SelectedLoadouts[group][0].Prototype.Id, Is.EqualTo(chosen));
            Assert.That(again.Profile.Loadouts["JobSWRepublicSoldier"].SelectedLoadouts[group][0].Prototype.Id, Is.EqualTo(second));

            var off = SWCosmeticWear.TakeOff(again.Profile, second, group, null, entMan, proto);
            Assert.That(off.Changed, Is.EqualTo(again.Changed));
            Assert.That(off.Profile.Loadouts[chosenRole].SelectedLoadouts[group][0].Prototype.Id, Is.EqualTo(chosen));
            Assert.That(off.Profile.Loadouts["JobSWRepublicSoldier"].SelectedLoadouts[group], Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KillsPayOnlyForEnemyPlayersWithinTheLimits()
    {
        var (pair, pass, season, _) = await Setup();
        var server = pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(SWVars.BattlePassCatchUpFrom, "2026-12-15"));
        var map = await pair.CreateTestMap();
        var dummies = await server.AddDummySessions(3);
        var killer = dummies[0];
        var enemy = dummies[1];
        var ally = dummies[2];

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var mobState = server.System<MobStateSystem>();
            var players = server.ResolveDependency<IPlayerManager>();
            pass.PretendRoundRunningForTest = true;

            EntityUid Body(ICommonSession? session, string faction)
            {
                var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
                entMan.EnsureComponent<SWFactionMemberComponent>(mob).Faction = faction;
                entMan.EnsureComponent<SWBattlePassPlayerComponent>(mob);
                if (session != null)
                    players.SetAttachedEntity(session, mob, true);
                return mob;
            }

            var killerMob = Body(killer, "RepublicFaction");
            var enemyMob = Body(enemy, "EmpireFaction");
            var allyMob = Body(ally, "RepublicFaction");
            var botMob = Body(null, "EmpireFaction");

            void Kill(EntityUid victim)
            {
                mobState.ChangeMobState(victim, MobState.Alive);
                mobState.ChangeMobState(victim, MobState.Dead, origin: killerMob);
            }

            int Paid() => pass.GetTracker(killer.UserId)?.KillsXp ?? 0;

            Kill(allyMob);
            Assert.That(Paid(), Is.EqualTo(0), "a teammate pays nothing");

            Kill(botMob);
            Assert.That(Paid(), Is.EqualTo(0), "a bot pays nothing");

            Kill(killerMob);
            Assert.That(Paid(), Is.EqualTo(0), "suicide pays nothing");

            Kill(enemyMob);
            Assert.That(Paid(), Is.EqualTo(season.Xp.Kill));
            Kill(enemyMob);
            Assert.That(Paid(), Is.EqualTo(season.Xp.Kill * season.Xp.KillsPerVictim));
            Kill(enemyMob);
            Assert.That(Paid(), Is.EqualTo(season.Xp.Kill * season.Xp.KillsPerVictim), "the same enemy pays only twice a round");
        });

        // A player without a record is paid into a new row, and nothing breaks.
        await server.WaitPost(() => pass.PayRound(true, TimeSpan.FromMinutes(30)));
        await pair.RunTicksSync(5);
        await server.WaitPost(() => { });
        await pass.FlushAsync();

        var db = server.ResolveDependency<IServerDbManager>();
        var row = await db.GetSWBattlePassProgress(killer.UserId.UserId, Season);
        Assert.That(row, Is.Not.Null);

        // Twice: a player new to a running season has a catch-up bank, which doubles the XP of a round.
        Assert.That(row!.Xp, Is.EqualTo(season.Xp.Kill * season.Xp.KillsPerVictim * 2));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundPaymentFollowsTheRules()
    {
        var (pair, pass, season, user) = await Setup("2026-12-15T12:00:00");
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var data = pass.GetData(user)!;
            data.State = new SWBattlePassSavedState();
            data.Xp = 0;

            // A round the server saw nothing of pays nothing.
            pass.PayRound(true, TimeSpan.FromMinutes(40));
            Assert.That(data.Xp, Is.EqualTo(0));

            // Minutes are paid at the rate the server tracked, and a restart pays no finishing bonus.
            var tracker = new SWBattlePassRoundTracker { ActiveSeconds = 600, MinutesXp = 50 };
            pass.InjectTrackerForTest(user, tracker);
            pass.PayRound(false, TimeSpan.Zero);
            Assert.That(data.Xp, Is.EqualTo(50));
            Assert.That(data.State.LastRound!.EndXp, Is.EqualTo(0));

            // A finished round adds the end bonus and the first round of the day.
            data.Xp = 0;
            tracker = new SWBattlePassRoundTracker { ActiveSeconds = 1200, MinutesXp = 100 };
            pass.InjectTrackerForTest(user, tracker);
            pass.PayRound(true, TimeSpan.FromMinutes(30));
            Assert.That(data.State.LastRound!.EndXp, Is.EqualTo(season.Xp.RoundEnd));
            Assert.That(data.State.LastRound!.FirstOfDayXp, Is.EqualTo(season.Xp.FirstRoundOfDay));
            Assert.That(data.Xp, Is.EqualTo(100 + season.Xp.RoundEnd + season.Xp.FirstRoundOfDay + data.State.LastRound!.QuestsXp));

            // The first round of the day is once a day, and the daily cap stops the minutes.
            data.Xp = 0;
            data.State.MinutesToday = season.Xp.MaxMinutesPerDay;
            tracker = new SWBattlePassRoundTracker { ActiveSeconds = 1200, MinutesXp = 100 };
            pass.InjectTrackerForTest(user, tracker);
            pass.PayRound(true, TimeSpan.FromMinutes(30));
            Assert.That(data.State.LastRound!.FirstOfDayXp, Is.EqualTo(0));
            Assert.That(data.State.LastRound!.MinutesXp, Is.EqualTo(0));
        });

        await pair.CleanReturnAsync();
    }
}
