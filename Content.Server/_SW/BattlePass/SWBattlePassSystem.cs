using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Afk;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Content.Server._SW.Cosmetics;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.Tutorial;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.Cosmetics;
using Content.Shared._SW.Tutorial;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._SW.BattlePass;

/// <summary>
/// The server side of the battle pass: keeps each player's season, checks every claim and case, and saves. Round play
/// is watched in the other part of this class. Nothing here touches a mob: the pass cannot change a fight.
/// </summary>
public sealed partial class SWBattlePassSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IAfkManager _afk = default!;
    [Dependency] private readonly IDependencyCollection _deps = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly ITaskManager _task = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SWCosmeticSystem _cosmetics = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SWTasksGameRuleSystem _tasks = default!;
    [Dependency] private readonly SWTutorialSystem _tutorial = default!;

    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(0.1);

    private readonly Dictionary<NetUserId, SWBattlePassData> _players = new();
    private readonly HashSet<Task> _saves = new();
    private TimeSpan _nextSave;

    public override void Initialize()
    {
        base.Initialize();

        _userDb.AddOnLoadPlayer(LoadPlayer);
        _userDb.AddOnFinishLoad(FinishLoad);
        _userDb.AddOnPlayerDisconnect(PlayerDisconnected);

        SubscribeNetworkEvent<SWBattlePassRequestStateEvent>(OnRequestState);
        SubscribeNetworkEvent<SWBattlePassClaimEvent>(OnClaim);
        SubscribeNetworkEvent<SWBattlePassClaimAllEvent>(OnClaimAll);
        SubscribeNetworkEvent<SWBattlePassClaimOvertimeEvent>(OnClaimOvertime);
        SubscribeNetworkEvent<SWBattlePassOpenCaseEvent>(OnOpenCase);
        SubscribeNetworkEvent<SWBattlePassBuyEntryEvent>(OnBuyEntry);

        InitializeRound();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        // A shutdown between two save ticks must not lose a claim.
        SaveDirty();
        _task.BlockWaitOnTask(Task.WhenAll(_saves));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateRound();

        if (_timing.RealTime < _nextSave)
            return;

        _nextSave = _timing.RealTime + SaveInterval;
        SaveDirty();
        SendUnlocks();
    }

    /// <summary>
    /// The button stays hidden during a new player's tutorial choice, and nothing else tells them when that is over.
    /// </summary>
    private void SendUnlocks()
    {
        foreach (var session in _player.Sessions)
        {
            if (_players.TryGetValue(session.UserId, out var data) && !data.UnlockSent && IsButtonUnlocked(session, data))
                SendState(session);
        }
    }

    /// <summary>
    /// Now, or the time set by <c>sw.battlepass_now</c> for tests of season dates.
    /// </summary>
    public DateTime Now
    {
        get
        {
            var fake = _cfg.GetCVar(SWVars.BattlePassNow);
            return fake != string.Empty
                && DateTime.TryParse(fake, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The season of the moment: the newest one that has started, or the next one to start.
    /// </summary>
    public SWBattlePassSeasonPrototype? CurrentSeason
    {
        get
        {
            var now = Now;
            SWBattlePassSeasonPrototype? started = null;
            SWBattlePassSeasonPrototype? upcoming = null;

            foreach (var season in _proto.EnumeratePrototypes<SWBattlePassSeasonPrototype>())
            {
                if (season.StartUtc <= now)
                {
                    if (started == null || season.StartUtc > started.StartUtc)
                        started = season;
                }
                else if (upcoming == null || season.StartUtc < upcoming.StartUtc)
                {
                    upcoming = season;
                }
            }

            return started ?? upcoming;
        }
    }

    public bool IsStarted(SWBattlePassSeasonPrototype season)
    {
        return Now >= season.StartUtc || _cfg.GetCVar(SWVars.BattlePassPreseasonOpen);
    }

    /// <summary>
    /// A bought paid track does not expire: its owner keeps earning XP until the last tier.
    /// </summary>
    public bool IsXpOpen(SWBattlePassSeasonPrototype season, SWBattlePassData data)
    {
        if (!IsStarted(season))
            return false;

        return Now < season.EndUtc || data.Paid && SWBattlePassRules.TierOf(season, data.Xp) < season.Tiers.Count;
    }

    public bool AreClaimsOpen(SWBattlePassSeasonPrototype season, SWBattlePassData data)
    {
        if (!IsStarted(season))
            return false;

        return Now < season.EndUtc || data.Paid;
    }

    public SWBattlePassData? GetData(NetUserId user)
    {
        return _players.GetValueOrDefault(user);
    }

    /// <summary>
    /// Whether the player is alive in the round. Cases do not open in a fight.
    /// </summary>
    public bool IsInCombat(ICommonSession session)
    {
        return _ticker.RunLevel == GameRunLevel.InRound
            && session.AttachedEntity is { } mob
            && _mobState.IsAlive(mob);
    }

    #region Loading and saving

    private async Task LoadPlayer(ICommonSession session, CancellationToken cancel)
    {
        if (CurrentSeason is not { } season)
            return;

        var persist = ServerPreferencesManager.ShouldStorePrefs(session.Channel.AuthType);

        // Their last changes may still be on the way to the database; this copy is newer than any row.
        if (_players.TryGetValue(session.UserId, out var kept))
        {
            kept.Connected = true;
            return;
        }

        var data = persist
            ? FromRow(session.UserId, season, await _db.GetSWBattlePassProgress(session.UserId, season.ID, cancel))
            : new SWBattlePassData(session.UserId, season.ID);

        cancel.ThrowIfCancellationRequested();

        data.Connected = true;
        data.Persist = persist;
        _players[session.UserId] = data;
    }

    private void FinishLoad(ICommonSession session)
    {
        SendState(session);
    }

    private void PlayerDisconnected(ICommonSession session)
    {
        if (!_players.TryGetValue(session.UserId, out var data))
            return;

        data.Connected = false;
        if (!data.Dirty && !data.Saving)
            _players.Remove(session.UserId);
    }

    private static SWBattlePassData FromRow(NetUserId user, SWBattlePassSeasonPrototype season, SWBattlePassProgress? row)
    {
        var data = new SWBattlePassData(user, season.ID);
        if (row == null)
            return data;

        data.Xp = row.Xp;
        data.Paid = row.Paid;
        data.State = SWBattlePassSavedState.Deserialize(row.State);
        return data;
    }

    /// <summary>
    /// Changes a player's season whether they are online or not, which admin commands need. Online it runs at once.
    /// </summary>
    /// <returns>False if there is no season.</returns>
    public async Task<bool> ModifyAsync(NetUserId user, Action<SWBattlePassData> action)
    {
        if (CurrentSeason is not { } season)
            return false;

        if (!_players.TryGetValue(user, out var data))
        {
            var row = await _db.GetSWBattlePassProgress(user.UserId, season.ID);

            // They may have connected during the wait.
            if (!_players.TryGetValue(user, out data))
            {
                data = FromRow(user, season, row);
                _players[user] = data;
            }
        }

        action(data);
        MarkDirty(data);
        return true;
    }

    private void MarkDirty(SWBattlePassData data)
    {
        data.Dirty = true;
    }

    private void SaveDirty()
    {
        foreach (var (user, data) in _players.ToArray())
        {
            if (data.Saving)
                continue;

            if (!data.Dirty || !data.Persist)
            {
                if (!data.Connected)
                    _players.Remove(user);

                continue;
            }

            data.Dirty = false;
            data.Saving = true;
            _saves.Add(Save(data, data.State.Serialize()));
        }

        _saves.RemoveWhere(task => task.IsCompleted);
    }

    private async Task Save(SWBattlePassData data, string state)
    {
        try
        {
            await _db.SaveSWBattlePassProgress(data.User, data.Season, data.Xp, data.Paid, state);
        }
        catch (Exception e)
        {
            // Stays dirty, so the next tick tries again.
            data.Dirty = true;
            Log.Error($"Saving the battle pass of {data.User} failed: {e}");
        }
        finally
        {
            data.Saving = false;
        }
    }

    /// <summary>
    /// Waits until everything changed so far is in the database. For tests and admin commands.
    /// </summary>
    public async Task FlushAsync()
    {
        SaveDirty();
        await Task.WhenAll(_saves.ToArray());
    }

    #endregion

    #region State

    private SWBattlePassState BuildState(ICommonSession session, SWBattlePassData data, SWBattlePassSeasonPrototype season)
    {
        Refresh(session, data, season);

        var state = data.State;
        var result = new SWBattlePassState
        {
            Season = season.ID,
            Started = IsStarted(season),
            Ended = Now >= season.EndUtc,
            XpOpen = IsXpOpen(season, data),
            ClaimsOpen = AreClaimsOpen(season, data),
            Xp = data.Xp,
            Paid = data.Paid,
            Claimed = new HashSet<string>(state.Claimed),
            Cases = new Dictionary<string, int>(state.Cases),
            Titles = new HashSet<string>(state.Titles),
            Snowflakes = state.Snowflakes,
            CasesSincePity = state.CasesSincePity,
            FirstCaseOpened = state.FirstCaseOpened,
            OvertimeClaimed = state.OvertimeClaimed,
            CatchUpBank = state.CatchUpBank,
            Evening = SWBattlePassRules.IsEvening(season.Xp, Now),
            FirstOpenGift = data.GiftJustGiven,
            ButtonUnlocked = data.UnlockSent = IsButtonUnlocked(session, data),
            InCombat = IsInCombat(session),
            LastRound = state.LastRound,
        };

        data.GiftJustGiven = false;

        foreach (var id in state.DailyActive)
        {
            result.Quests.Add(new SWBattlePassQuestState
            {
                Id = id,
                Progress = state.QuestProgress.GetValueOrDefault(id),
                Done = state.DailyDone.Contains(id),
            });
        }

        foreach (var quest in season.WeeklyQuests)
        {
            result.Quests.Add(new SWBattlePassQuestState
            {
                Id = quest.Id,
                Weekly = true,
                Progress = state.QuestProgress.GetValueOrDefault(quest.Id),
                Done = state.WeeklyDone.Contains(quest.Id),
            });
        }

        return result;
    }

    /// <summary>
    /// Sends a player their own numbers.
    /// </summary>
    public void SendState(ICommonSession session)
    {
        if (CurrentSeason is not { } season || !_players.TryGetValue(session.UserId, out var data))
            return;

        RaiseNetworkEvent(new SWBattlePassStateEvent(BuildState(session, data, season)), session);
    }

    /// <summary>
    /// The tutorial hides the pass from a new player until they have seen it or played a round.
    /// </summary>
    private bool IsButtonUnlocked(ICommonSession session, SWBattlePassData data)
    {
        if (data.Xp > 0 || data.State.RoundsPlayed > 0)
            return true;

        if (_tutorial.IsStudent(session.UserId) || !_playTime.TryGetTrackerTimes(session, out var times))
            return false;

        return times.ContainsKey(SWTutorialTrackers.Seen)
            || times.ContainsKey(SWTutorialTrackers.Done)
            || _playTime.GetOverallPlaytime(session).TotalMinutes >= _cfg.GetCVar(SWVars.TutorialOfferMinutes);
    }

    /// <summary>
    /// Brings the dated parts of a player's season up to today: catch-up bank, quest lists, the tutorial reward.
    /// </summary>
    private void Refresh(ICommonSession? session, SWBattlePassData data, SWBattlePassSeasonPrototype season)
    {
        if (!IsStarted(season))
            return;

        var now = Now;
        var state = data.State;

        RefreshCatchUp(data, season, now);

        // Nothing earned is lost when the season closes: it is handed over.
        if (now >= season.EndUtc)
            HandOverUnclaimed(data, season);

        var today = SWBattlePassRules.DayKey(now);
        if (state.DailyDay != today)
        {
            // What is unfinished stays, so a bad day does not burn it. Only what is done makes room for new ones.
            foreach (var done in state.DailyDone)
            {
                state.DailyActive.Remove(done);
                state.QuestProgress.Remove(done);
            }

            state.DailyDone.Clear();
            state.DailyDay = today;
            FillDailyQuests(data, season, today);
            MarkDirty(data);
        }

        var week = SWBattlePassRules.WeekKey(now);
        if (state.WeeklyKey != week)
        {
            foreach (var quest in season.WeeklyQuests)
            {
                state.QuestProgress.Remove(quest.Id);
            }

            state.WeeklyDone.Clear();
            state.WeeklyKey = week;
            MarkDirty(data);
        }

        if (session != null
            && !state.TutorialPaid
            && _playTime.TryGetTrackerTimes(session, out var times)
            && times.ContainsKey(SWTutorialTrackers.Done))
        {
            state.TutorialPaid = true;
            AddXp(data, season, "tutorial", season.Xp.Tutorial);
        }
    }

    private void RefreshCatchUp(SWBattlePassData data, SWBattlePassSeasonPrototype season, DateTime now)
    {
        if (!DateTime.TryParseExact(_cfg.GetCVar(SWVars.BattlePassCatchUpFrom), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            return;

        var state = data.State;
        var seasonFirst = SWBattlePassRules.MoscowDay(season.StartUtc);
        if (from > seasonFirst)
            seasonFirst = from;

        var seasonLast = SWBattlePassRules.MoscowDay(season.EndUtc).AddDays(-1);
        var yesterday = SWBattlePassRules.MoscowDay(now).AddDays(-1);
        if (yesterday > seasonLast)
            yesterday = seasonLast;

        var day = state.CatchUpThrough == null
            ? seasonFirst
            : DateTime.ParseExact(state.CatchUpThrough, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1);
        if (day < seasonFirst)
            day = seasonFirst;

        var changed = false;
        for (; day <= yesterday; day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (key != state.LastPlayedDay)
                state.CatchUpBank += season.CatchUpPerDay;

            state.CatchUpThrough = key;
            changed = true;
        }

        if (changed)
            MarkDirty(data);
    }

    private void FillDailyQuests(SWBattlePassData data, SWBattlePassSeasonPrototype season, string today)
    {
        var state = data.State;

        // The same list for the same player on the same day, whenever it is computed.
        var seed = HashCode.Combine(data.User.UserId, today);
        var rand = new System.Random(seed);
        var free = season.DailyQuests.Where(q => !state.DailyActive.Contains(q.Id)).ToList();
        while (state.DailyActive.Count < season.DailyQuestCount && free.Count > 0)
        {
            var index = rand.Next(free.Count);
            state.DailyActive.Add(free[index].Id);
            free.RemoveAt(index);
        }
    }

    /// <summary>
    /// The one way XP is added.
    /// </summary>
    /// <param name="data">The player.</param>
    /// <param name="season">The season.</param>
    /// <param name="source">Where it came from, for the season's numbers.</param>
    /// <param name="amount">How much.</param>
    public void AddXp(SWBattlePassData data, SWBattlePassSeasonPrototype season, string source, int amount)
    {
        if (amount <= 0 || !IsXpOpen(season, data))
            return;

        data.Xp += amount;
        data.State.XpBySource[source] = data.State.XpBySource.GetValueOrDefault(source) + amount;
        MarkDirty(data);
    }

    #endregion

    /// <summary>
    /// Takes revoked items out of the cached characters at once. The cosmetic system's spawn check would catch them too,
    /// but the player's window should not go on showing them as worn.
    /// </summary>
    public void DropInvalidCosmetics(NetUserId user)
    {
        if (!_player.TryGetSessionById(user, out var session) || !_prefs.TryGetCachedPreferences(user, out var prefs))
            return;

        foreach (var profile in prefs.Characters.Values)
        {
            profile.EnsureValid(session, _deps, []);
        }
    }

    #region Requests

    private bool TryGetRequester(
        EntitySessionEventArgs args,
        out ICommonSession session,
        out SWBattlePassData data,
        out SWBattlePassSeasonPrototype season)
    {
        session = args.SenderSession;
        data = default!;
        season = default!;

        if (!_players.TryGetValue(session.UserId, out var found) || CurrentSeason is not { } current)
            return false;

        data = found;
        season = current;

        if (_timing.RealTime < data.NextRequest)
            return false;

        data.NextRequest = _timing.RealTime + RequestDelay;
        return true;
    }

    private void OnRequestState(SWBattlePassRequestStateEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out var season))
            return;

        if (ev.Opening && !_tutorial.IsStudent(session.UserId))
            GiveFirstOpenGift(data, season);

        SendState(session);
    }

    /// <summary>
    /// The arrival gift: the first tier and with it the first scarf, on the first opening of the window.
    /// </summary>
    public void GiveFirstOpenGift(SWBattlePassData data, SWBattlePassSeasonPrototype season)
    {
        if (data.State.FirstOpened || !IsXpOpen(season, data))
            return;

        data.State.FirstOpened = true;
        data.GiftJustGiven = true;
        AddXp(data, season, "gift", season.FirstOpenXp);
    }

    private void OnClaim(SWBattlePassClaimEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out var season))
            return;

        if (!TryClaim(data, season, ev.Tier, ev.Track, out var refusal))
            Refuse(session, refusal);

        SendState(session);
    }

    private void OnClaimAll(SWBattlePassClaimAllEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out var season))
            return;

        for (var tier = 1; tier <= season.Tiers.Count; tier++)
        {
            TryClaim(data, season, tier, SWBattlePassTrack.Free, out _);
            TryClaim(data, season, tier, SWBattlePassTrack.Paid, out _);
        }

        SendState(session);
    }

    private void OnClaimOvertime(SWBattlePassClaimOvertimeEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out var season))
            return;

        TryClaimOvertime(data, season);
        SendState(session);
    }

    private void Refuse(ICommonSession session, LocId? reason)
    {
        if (reason != null)
            RaiseNetworkEvent(new SWBattlePassRefusedEvent(Loc.GetString(reason.Value)), session);
    }

    /// <summary>
    /// Checks a claim against the server's own numbers and pays it. Anything the client claims beyond them is refused.
    /// </summary>
    public bool TryClaim(
        SWBattlePassData data,
        SWBattlePassSeasonPrototype season,
        int tier,
        SWBattlePassTrack track,
        out LocId? refusal)
    {
        refusal = null;

        if (!AreClaimsOpen(season, data))
        {
            refusal = "sw-battlepass-refused-closed";
            return false;
        }

        if (track == SWBattlePassTrack.Paid && !data.Paid)
        {
            refusal = "sw-battlepass-refused-paid";
            return false;
        }

        if (!SWBattlePassRules.CanClaim(season, data.Xp, data.Paid, data.State.Claimed, tier, track))
        {
            refusal = "sw-battlepass-refused-claim";
            return false;
        }

        PayClaim(data, season, tier, track);
        return true;
    }

    /// <summary>
    /// Gives everything earned and not yet taken, once the season has closed.
    /// </summary>
    private void HandOverUnclaimed(SWBattlePassData data, SWBattlePassSeasonPrototype season)
    {
        for (var tier = 1; tier <= season.Tiers.Count; tier++)
        {
            if (SWBattlePassRules.CanClaim(season, data.Xp, data.Paid, data.State.Claimed, tier, SWBattlePassTrack.Free))
                PayClaim(data, season, tier, SWBattlePassTrack.Free);
            if (SWBattlePassRules.CanClaim(season, data.Xp, data.Paid, data.State.Claimed, tier, SWBattlePassTrack.Paid))
                PayClaim(data, season, tier, SWBattlePassTrack.Paid);
        }

        var overtime = SWBattlePassRules.OvertimeEarned(season, data.Xp) - data.State.OvertimeClaimed;
        if (overtime > 0 && season.OvertimeCase is { } caseId)
        {
            data.State.OvertimeClaimed += overtime;
            data.State.Cases[caseId] = data.State.Cases.GetValueOrDefault(caseId) + overtime;
            MarkDirty(data);
        }
    }

    private void PayClaim(SWBattlePassData data, SWBattlePassSeasonPrototype season, int tier, SWBattlePassTrack track)
    {
        var reward = SWBattlePassRules.GetReward(season, tier, track)!;
        var key = SWBattlePassRules.RewardKey(tier, track);
        data.State.Claimed.Add(key);

        if (reward.Cosmetic is { } cosmetic)
        {
            // Snowflakes only when the whole reward was owned already: half a pair is still a new thing to wear.
            if (IsOwned(data, cosmetic) && reward.Also.All(also => IsOwned(data, also)))
                data.State.Snowflakes += season.DuplicateRewardSnowflakes;

            foreach (var item in reward.Also.Append(cosmetic))
            {
                if (!IsOwned(data, item))
                    Grant(data, item);
            }
        }

        if (reward.Case is { } caseId)
            data.State.Cases[caseId] = data.State.Cases.GetValueOrDefault(caseId) + 1;

        if (reward.Title is { } title)
            data.State.Titles.Add(title);

        data.State.Snowflakes += reward.Snowflakes;

        MarkDirty(data);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Battle pass: {data.User} claimed {key} of {season.ID}.");
    }

    /// <summary>
    /// Whether a player owns a cosmetic right now, counting what was given a moment ago.
    /// </summary>
    public bool IsOwned(SWBattlePassData data, ProtoId<LoadoutPrototype> cosmetic)
    {
        if (data.Granted.Contains(cosmetic))
            return true;

        return _player.TryGetSessionById(data.User, out var session)
            && _playTime.TryGetTrackerTimes(session, out var times)
            && SWCosmeticTrackers.IsOwned(times, cosmetic);
    }

    private void Grant(SWBattlePassData data, ProtoId<LoadoutPrototype> cosmetic)
    {
        data.Granted.Add(cosmetic);
        RunGrant(data.User, cosmetic);
    }

    private async void RunGrant(NetUserId user, ProtoId<LoadoutPrototype> cosmetic)
    {
        try
        {
            var name = _player.TryGetSessionById(user, out var session) ? session.Name : user.ToString();
            await _cosmetics.SetOwned(user, name, cosmetic, true, "battlepass");
        }
        catch (Exception e)
        {
            Log.Error($"Granting {cosmetic} to {user} failed: {e}");
        }
    }

    /// <summary>
    /// Pays the cases earned past the last tier.
    /// </summary>
    public int TryClaimOvertime(SWBattlePassData data, SWBattlePassSeasonPrototype season)
    {
        if (season.OvertimeCase is not { } caseId || !AreClaimsOpen(season, data))
            return 0;

        var count = SWBattlePassRules.OvertimeEarned(season, data.Xp) - data.State.OvertimeClaimed;
        if (count <= 0)
            return 0;

        data.State.OvertimeClaimed += count;
        data.State.Cases[caseId] = data.State.Cases.GetValueOrDefault(caseId) + count;
        MarkDirty(data);
        return count;
    }

    private void OnOpenCase(SWBattlePassOpenCaseEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out _))
            return;

        if (IsInCombat(session))
        {
            Refuse(session, "sw-battlepass-refused-combat");
            return;
        }

        if (!TryOpenCase(data, ev.Case, out var entry, out var duplicate, out var snowflakes))
        {
            Refuse(session, "sw-battlepass-refused-no-case");
            SendState(session);
            return;
        }

        SendState(session);
        RaiseNetworkEvent(new SWBattlePassCaseResultEvent(ev.Case, entry, duplicate, snowflakes), session);
    }

    /// <summary>
    /// Opens one case of a player's. The roll happens here and nowhere else.
    /// </summary>
    public bool TryOpenCase(
        SWBattlePassData data,
        string caseId,
        out int entryIndex,
        out bool duplicate,
        out int snowflakes)
    {
        entryIndex = -1;
        duplicate = false;
        snowflakes = 0;

        if (!_proto.TryIndex<SWBattlePassCasePrototype>(caseId, out var proto))
            return false;

        var state = data.State;
        var count = state.Cases.GetValueOrDefault(caseId);
        if (count <= 0)
            return false;

        if (count == 1)
            state.Cases.Remove(caseId);
        else
            state.Cases[caseId] = count - 1;

        // The first case of a player is announced in the window and stays out of the guarantee's count.
        if (!state.FirstCaseOpened && proto.FirstEntry is { } first)
        {
            state.FirstCaseOpened = true;
            entryIndex = first;
        }
        else
        {
            state.FirstCaseOpened = true;
            entryIndex = RollEntry(proto, state.CasesSincePity >= proto.PityAfter);
            state.CasesSincePity = SWBattlePassRules.ResetsPity(proto, proto.Entries[entryIndex]) ? 0 : state.CasesSincePity + 1;
        }

        var entry = proto.Entries[entryIndex];
        if (entry.Cosmetic is { } cosmetic)
        {
            if (IsOwned(data, cosmetic))
            {
                duplicate = true;
                snowflakes = entry.DuplicateSnowflakes;
            }
            else
            {
                Grant(data, cosmetic);
            }
        }
        else
        {
            snowflakes = entry.Snowflakes;
        }

        state.Snowflakes += snowflakes;
        MarkDirty(data);

        _adminLog.Add(LogType.Action,
            LogImpact.Low,
            $"Battle pass: {data.User} opened {caseId}, entry {entryIndex}, duplicate {duplicate}, snowflakes {snowflakes}.");
        return true;
    }

    /// <summary>
    /// Rolls a line of a case with the odds printed in the window. With pity on, only lines of exactly the promised
    /// rarity are in the roll.
    /// </summary>
    public int RollEntry(SWBattlePassCasePrototype proto, bool pity)
    {
        var total = 0f;
        for (var i = 0; i < proto.Entries.Count; i++)
        {
            if (!pity || SWBattlePassRules.IsPityEntry(proto, proto.Entries[i]))
                total += proto.Entries[i].Chance;
        }

        var roll = _random.NextFloat() * total;
        var last = 0;
        for (var i = 0; i < proto.Entries.Count; i++)
        {
            if (pity && !SWBattlePassRules.IsPityEntry(proto, proto.Entries[i]))
                continue;

            last = i;
            roll -= proto.Entries[i].Chance;
            if (roll < 0)
                return i;
        }

        return last;
    }

    private void OnBuyEntry(SWBattlePassBuyEntryEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetRequester(args, out var session, out var data, out _))
            return;

        if (!TryBuyEntry(data, ev.Case, ev.Entry))
            Refuse(session, "sw-battlepass-refused-buy");

        SendState(session);
    }

    /// <summary>
    /// Buys a line of a case with snowflakes. Snowflakes only come from play, so this never involves money.
    /// </summary>
    public bool TryBuyEntry(SWBattlePassData data, string caseId, int entryIndex)
    {
        if (!_proto.TryIndex<SWBattlePassCasePrototype>(caseId, out var proto)
            || entryIndex < 0
            || entryIndex >= proto.Entries.Count)
        {
            return false;
        }

        var entry = proto.Entries[entryIndex];
        if (entry.Cosmetic is not { } cosmetic
            || entry.Price <= 0
            || IsOwned(data, cosmetic)
            || data.State.Snowflakes < entry.Price)
        {
            return false;
        }

        data.State.Snowflakes -= entry.Price;
        Grant(data, cosmetic);
        MarkDirty(data);

        _adminLog.Add(LogType.Action, LogImpact.Low, $"Battle pass: {data.User} bought {cosmetic} for {entry.Price} snowflakes.");
        return true;
    }

    #endregion
}
