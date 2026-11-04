using System.Linq;
using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Server._SW.TaskSystem;
using Content.Shared.CCVar;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared._SW.BattlePass;
using Content.Shared._SW.TaskSystem;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.BattlePass;

public sealed partial class SWBattlePassSystem
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often the walked path is measured. The sample alone misses a player who walks back and forth.
    /// </summary>
    private static readonly TimeSpan MoveCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Standing still for longer than this stops the minutes, well before the game calls the player AFK.
    /// </summary>
    private static readonly TimeSpan IdleGrace = TimeSpan.FromSeconds(60);

    private const float MoveTiles = 3f;
    private const int OneLifeMinutes = 15;

    private readonly Dictionary<NetUserId, SWBattlePassRoundTracker> _trackers = new();
    private readonly HashSet<EntityUid> _countedTasks = new();
    private TimeSpan _nextSample;
    private TimeSpan _nextMoveCheck;

    private void InitializeRound()
    {
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<SWBattlePassPlayerComponent, ContactInteractionEvent>(OnContact);
        SubscribeLocalEvent<SWBattlePassPlayerComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<SWBattlePassPlayerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWBattlePassPlayerComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<SWTaskComponent, SWOnTaskCompleteEvent>(OnTaskComplete);
    }

    /// <summary>
    /// Test pools run a dummy ticker, whose round never starts.
    /// </summary>
    public bool PretendRoundRunningForTest { get; set; }

    private bool RoundRunning => PretendRoundRunningForTest || _ticker.RunLevel == GameRunLevel.InRound;

    public SWBattlePassRoundTracker? GetTracker(NetUserId user)
    {
        return _trackers.GetValueOrDefault(user);
    }

    /// <summary>
    /// Sets what the server has seen of a player this round. Tests of the payment use this instead of playing it out.
    /// </summary>
    public void InjectTrackerForTest(NetUserId user, SWBattlePassRoundTracker tracker)
    {
        _trackers[user] = tracker;
    }

    private void UpdateRound()
    {
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (_timing.CurTime >= _nextMoveCheck)
        {
            _nextMoveCheck = _timing.CurTime + MoveCheckInterval;
            MeasureMovement();
        }

        if (_timing.CurTime >= _nextSample)
        {
            _nextSample = _timing.CurTime + SampleInterval;
            Sample((int) SampleInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// Adds what everyone has walked since the last check. Called on a timer; public for tests.
    /// </summary>
    public void MeasureMovement()
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } mob && _trackers.TryGetValue(session.UserId, out var tracker))
                AddMovement(tracker, _transform.GetMapCoordinates(mob));
        }
    }

    private static void AddMovement(SWBattlePassRoundTracker tracker, MapCoordinates position)
    {
        if (tracker.LastPosition is { } last)
        {
            tracker.MovedTiles += last.MapId != position.MapId
                ? MoveTiles
                : (last.Position - position.Position).Length();
        }

        tracker.LastPosition = position;
    }

    /// <summary>
    /// Credits active time to everyone who is really playing. Called on a timer; public for tests.
    /// </summary>
    /// <param name="seconds">How much time this sample stands for.</param>
    public void Sample(int seconds)
    {
        if (CurrentSeason is not { } season || !IsStarted(season) || !_cfg.GetCVar(SWVars.BattlePassEnabled))
            return;

        var players = _player.PlayerCount;
        if (players < _cfg.GetCVar(SWVars.BattlePassMinPlayers))
            return;

        var rules = season.Xp;
        var seed = players < rules.SeedBelowPlayers ? rules.SeedMultiplier : 1f;

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame
                || session.AttachedEntity is not { } mob
                || _tutorial.IsStudent(session.UserId)
                || !_mobState.IsAlive(mob)
                || !TryGetRate(session, mob, season, out var rate, out var faction))
            {
                continue;
            }

            var tracker = GetOrCreateTracker(session.UserId);
            AddMovement(tracker, _transform.GetMapCoordinates(mob));

            // A new body starts as active: the spawn is an action, and it may take a moment to walk off.
            if (tracker.LastMob != mob || tracker.MovedTiles >= MoveTiles)
                tracker.LastActed = _timing.CurTime;

            tracker.LastMob = mob;
            tracker.MovedTiles = 0;

            if (_afk.IsAfk(session) || _timing.CurTime - tracker.LastActed > IdleGrace)
                continue;

            tracker.ActiveSeconds += seconds;
            tracker.MinutesXp += seconds / 60f * rate * seed;
            tracker.LifeSeconds += seconds;
            if (faction != null)
                tracker.SecondsByFaction[faction] = tracker.SecondsByFaction.GetValueOrDefault(faction) + seconds;

            if (!tracker.OneLifeCounted && tracker.LifeSeconds >= OneLifeMinutes * 60)
            {
                tracker.OneLifeCounted = true;
                AdvanceQuests(session.UserId, season, SWBattlePassQuestKind.MinutesOneLife, tracker);
            }
        }
    }

    private SWBattlePassRoundTracker GetOrCreateTracker(NetUserId user)
    {
        if (!_trackers.TryGetValue(user, out var tracker))
        {
            tracker = new SWBattlePassRoundTracker { LastActed = _timing.CurTime };
            _trackers[user] = tracker;
        }

        return tracker;
    }

    /// <summary>
    /// What a minute pays this player. Mobs without a faction or a civilian job earn nothing: ghost roles, parasites.
    /// </summary>
    private bool TryGetRate(
        ICommonSession session,
        EntityUid mob,
        SWBattlePassSeasonPrototype season,
        out int rate,
        out string? faction)
    {
        rate = 0;
        faction = null;

        if (TryComp<SWFactionMemberComponent>(mob, out var member))
        {
            rate = season.Xp.FighterPerMinute;
            faction = member.Faction;
            return true;
        }

        if (session.AttachedEntity != null
            && _mind.TryGetMind(session.UserId, out var mind, out _)
            && _jobs.MindTryGetJobId(mind, out var job)
            && job != null
            && season.CivilianJobs.Contains(job.Value))
        {
            rate = season.Xp.CivilianPerMinute;
            return true;
        }

        return false;
    }

    private void OnContact(Entity<SWBattlePassPlayerComponent> ent, ref ContactInteractionEvent args)
    {
        MarkActed(ent);
    }

    private void OnShotAttempted(Entity<SWBattlePassPlayerComponent> ent, ref ShotAttemptedEvent args)
    {
        MarkActed(ent);
    }

    private void MarkActed(EntityUid mob)
    {
        if (TryComp<ActorComponent>(mob, out var actor) && _trackers.TryGetValue(actor.PlayerSession.UserId, out var tracker))
            tracker.LastActed = _timing.CurTime;
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (CurrentSeason is not { } season || !IsStarted(season))
            return;

        // Only a body a player spawned into takes part: no ghost role, no bot.
        EnsureComp<SWBattlePassPlayerComponent>(ev.Mob);

        var tracker = GetOrCreateTracker(ev.Player.UserId);
        tracker.LifeSeconds = 0;
        tracker.OneLifeCounted = false;

        if (tracker.DiedThisLife)
        {
            tracker.DiedThisLife = false;
            AdvanceQuests(ev.Player.UserId, season, SWBattlePassQuestKind.ReturnsAfterDeath, tracker);
        }
    }

    private void OnMobStateChanged(Entity<SWBattlePassPlayerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead
            || !TryComp<ActorComponent>(ent, out var victimActor)
            || CurrentSeason is not { } season
            || !IsStarted(season)
            || !RoundRunning)
        {
            return;
        }

        var victim = victimActor.PlayerSession.UserId;
        if (_trackers.TryGetValue(victim, out var victimTracker))
            victimTracker.DiedThisLife = true;

        // A bot has no actor, a teammate has the same faction, a civilian has none. None of them pays.
        if (args.Origin is not { } origin
            || origin == ent.Owner
            || !TryComp<SWFactionMemberComponent>(ent, out var victimMember)
            || !TryComp<ActorComponent>(origin, out var killerActor)
            || !TryComp<SWFactionMemberComponent>(origin, out var killerMember)
            || killerMember.Faction == victimMember.Faction
            || killerActor.PlayerSession.UserId == victim)
        {
            return;
        }

        var killer = killerActor.PlayerSession.UserId;
        var tracker = GetOrCreateTracker(killer);
        var rules = season.Xp;

        var kills = tracker.KillsByVictim.GetValueOrDefault(victim);
        if (kills >= rules.KillsPerVictim)
            return;

        tracker.KillsByVictim[victim] = kills + 1;
        tracker.KillsXp = Math.Min(rules.MaxKillXpPerRound, tracker.KillsXp + rules.Kill);
        AdvanceQuests(killer, season, SWBattlePassQuestKind.EnemyKills, tracker);
    }

    private void OnDamageChanged(Entity<SWBattlePassPlayerComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased
            || args.DamageDelta == null
            || args.Origin is not { } origin
            || !TryComp<ActorComponent>(origin, out var actor)
            || !TryComp<SWFactionMemberComponent>(origin, out var member)
            || !TryComp<SWFactionMemberComponent>(ent, out var patient)
            || member.Faction != patient.Faction
            || args.DamageDelta.GetTotal() >= 0
            || CurrentSeason is not { } season
            || !RoundRunning)
        {
            return;
        }

        AdvanceQuests(actor.PlayerSession.UserId, season, SWBattlePassQuestKind.Heals, GetOrCreateTracker(actor.PlayerSession.UserId));
    }

    private void OnTaskComplete(Entity<SWTaskComponent> ent, ref SWOnTaskCompleteEvent args)
    {
        // The rule fires this again at round end, and admin test completions must not pay.
        if (ent.Comp.ParentTask != null
            || HasComp<SWForceCompletedTaskComponent>(ent)
            || CurrentSeason is not { } season
            || !IsStarted(season)
            || !RoundRunning
            || !_countedTasks.Add(ent))
        {
            return;
        }

        var rules = season.Xp;
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } mob
                || !_trackers.TryGetValue(session.UserId, out var tracker)
                || tracker.ActiveSeconds < rules.FactionTaskMinMinutes * 60
                || !_mobState.IsAlive(mob)
                || !TryComp<SWFactionMemberComponent>(mob, out var member)
                || member.Faction != ent.Comp.OwningFaction)
            {
                continue;
            }

            tracker.TasksXp += rules.FactionTask;
            AdvanceQuests(session.UserId, season, SWBattlePassQuestKind.FactionTasksAlive, tracker);
        }
    }

    /// <summary>
    /// Moves every current quest of this kind one step, and pays those it finishes.
    /// </summary>
    private void AdvanceQuests(
        NetUserId user,
        SWBattlePassSeasonPrototype season,
        SWBattlePassQuestKind kind,
        SWBattlePassRoundTracker? tracker)
    {
        if (!_players.TryGetValue(user, out var data) || !_player.TryGetSessionById(user, out var session))
            return;

        Refresh(session, data, season);
        var state = data.State;
        var finished = false;

        foreach (var quest in season.DailyQuests)
        {
            if (quest.Kind == kind && state.DailyActive.Contains(quest.Id) && !state.DailyDone.Contains(quest.Id))
                finished |= Advance(data, season, quest, state.DailyDone, session, tracker);
        }

        foreach (var quest in season.WeeklyQuests)
        {
            if (quest.Kind == kind && !state.WeeklyDone.Contains(quest.Id))
                finished |= Advance(data, season, quest, state.WeeklyDone, session, tracker);
        }

        if (finished)
            SendState(session);
    }

    private bool Advance(
        SWBattlePassData data,
        SWBattlePassSeasonPrototype season,
        SWBattlePassQuest quest,
        HashSet<string> done,
        ICommonSession session,
        SWBattlePassRoundTracker? tracker)
    {
        var state = data.State;
        var progress = state.QuestProgress.GetValueOrDefault(quest.Id) + 1;
        state.QuestProgress[quest.Id] = progress;
        MarkDirty(data);

        if (progress < quest.Target)
            return false;

        done.Add(quest.Id);
        AddXp(data, season, "quest", quest.Xp);
        if (tracker != null)
            tracker.QuestsXp += quest.Xp;

        // The one line a fight is interrupted with.
        _chat.DispatchServerMessage(session,
            Loc.GetString("sw-battlepass-quest-done", ("name", Loc.GetString(quest.Name)), ("xp", quest.Xp)));
        return true;
    }

    private void OnRoundEnded(RoundEndedEvent ev)
    {
        PayRound(true, ev.RoundDuration);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // A restart without a round end: minutes are still owed, the bonuses for finishing are not.
        PayRound(false, TimeSpan.Zero);
    }

    /// <summary>
    /// Pays everyone what the server saw them do this round, once, and forgets the round.
    /// </summary>
    /// <param name="finished">Whether the round reached its end.</param>
    /// <param name="duration">How long it lasted.</param>
    public void PayRound(bool finished, TimeSpan duration)
    {
        if (_trackers.Count == 0)
        {
            _countedTasks.Clear();
            return;
        }

        var trackers = _trackers.ToArray();
        _trackers.Clear();
        _countedTasks.Clear();

        if (CurrentSeason is not { } season || !IsStarted(season) || !_cfg.GetCVar(SWVars.BattlePassEnabled))
            return;

        var winners = new HashSet<string>();
        if (finished)
        {
            var winning = _tasks.GetWinners();
            foreach (var (id, faction) in _factions.GetFactions())
            {
                if (faction != null && winning.Contains(faction.Value))
                    winners.Add(id);
            }
        }

        foreach (var (user, tracker) in trackers)
        {
            // Offline players get what they played, without the bonuses for being there at the end.
            var online = _player.TryGetSessionById(user, out var session) && session!.Status == SessionStatus.InGame;
            var stayed = finished && online;
            _ = PayPlayer(user, session, tracker, season, stayed, duration, winners);
        }
    }

    private async Task PayPlayer(
        NetUserId user,
        ICommonSession? session,
        SWBattlePassRoundTracker tracker,
        SWBattlePassSeasonPrototype season,
        bool stayed,
        TimeSpan duration,
        HashSet<string> winners)
    {
        try
        {
            await ModifyAsync(user, data => ApplyRound(data, session, tracker, season, stayed, duration, winners));
        }
        catch (Exception e)
        {
            Log.Error($"Paying the battle pass round of {user} failed: {e}");
        }
    }

    private void ApplyRound(
        SWBattlePassData data,
        ICommonSession? session,
        SWBattlePassRoundTracker tracker,
        SWBattlePassSeasonPrototype season,
        bool stayed,
        TimeSpan duration,
        HashSet<string> winners)
    {
        Refresh(session, data, season);

        var rules = season.Xp;
        var state = data.State;
        var now = Now;
        var today = SWBattlePassRules.DayKey(now);
        var summary = new SWBattlePassRoundSummary();

        if (state.MinutesDay != today)
        {
            state.MinutesDay = today;
            state.MinutesToday = 0;
        }

        var minutes = tracker.ActiveSeconds / 60;
        var paidMinutes = Math.Min(minutes, Math.Max(0, rules.MaxMinutesPerDay - state.MinutesToday));
        state.MinutesToday += paidMinutes;

        summary.Minutes = paidMinutes;
        summary.MinutesXp = minutes == 0 ? 0 : (int) (tracker.MinutesXp * paidMinutes / (tracker.ActiveSeconds / 60f));
        summary.TasksXp = tracker.TasksXp;
        summary.KillsXp = tracker.KillsXp;

        var reachedEnd = stayed
            && minutes >= rules.RoundEndMinMinutes
            && duration.TotalMinutes >= rules.RoundEndMinRoundMinutes;

        if (reachedEnd)
        {
            summary.EndXp = rules.RoundEnd;

            // The side the player spent most of the round on, so that swapping at the end pays nothing.
            if (tracker.SecondsByFaction.Count > 0)
            {
                var side = tracker.SecondsByFaction.MaxBy(pair => pair.Value).Key;
                if (winners.Contains(side))
                {
                    summary.WinXp = rules.Win;
                    AdvanceQuests(data.User, season, SWBattlePassQuestKind.Wins, tracker);
                }
            }

            AdvanceQuests(data.User, season, SWBattlePassQuestKind.RoundsFinished, tracker);
        }

        var roundXp = summary.MinutesXp + summary.TasksXp + summary.KillsXp + summary.EndXp + summary.WinXp;

        if (roundXp > 0 && SWBattlePassRules.IsEvening(rules, now))
            summary.EveningXp = (int) (roundXp * (rules.EveningMultiplier - 1f));

        roundXp += summary.EveningXp;

        // While the bank holds anything, the XP from a round counts twice and drains the bank.
        summary.CatchUpXp = Math.Min(state.CatchUpBank, roundXp);
        state.CatchUpBank -= summary.CatchUpXp;

        if (state.FirstRoundDay != today && (reachedEnd || minutes >= rules.FirstRoundOfDayMinMinutes))
        {
            state.FirstRoundDay = today;
            summary.FirstOfDayXp = rules.FirstRoundOfDay;
        }

        AddXp(data, season, "minutes", summary.MinutesXp);
        AddXp(data, season, "tasks", summary.TasksXp);
        AddXp(data, season, "kills", summary.KillsXp);
        AddXp(data, season, "end", summary.EndXp);
        AddXp(data, season, "win", summary.WinXp);
        AddXp(data, season, "evening", summary.EveningXp);
        AddXp(data, season, "catchup", summary.CatchUpXp);
        AddXp(data, season, "first", summary.FirstOfDayXp);

        summary.QuestsXp = tracker.QuestsXp;
        summary.Total = roundXp + summary.CatchUpXp + summary.FirstOfDayXp + summary.QuestsXp;

        if (minutes > 0)
        {
            state.RoundsPlayed++;
            state.LastPlayedDay = today;
        }

        state.LastRound = summary;
        MarkDirty(data);

        if (session is not { Status: SessionStatus.InGame })
            return;

        if (summary.Total > 0)
        {
            var tier = SWBattlePassRules.TierOf(season, data.Xp);
            var left = SWBattlePassRules.XpForTier(season, Math.Min(tier + 1, season.Tiers.Count)) - data.Xp;
            _chat.DispatchServerMessage(session,
                Loc.GetString("sw-battlepass-round-summary",
                    ("xp", summary.Total),
                    ("tier", tier),
                    ("left", Math.Max(0, left))));
        }

        SendState(session);
    }
}
