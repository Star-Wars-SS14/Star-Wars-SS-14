using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Ghost;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Station.Systems;
using Content.Shared._SW.Tutorial;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._SW.Tutorial;

/// <summary>
/// Runs the new player tutorial: a private map per student, in the same process as the real round. The student gets
/// a body through a path of our own, so no job slot is spent and <see cref="PlayerSpawnCompleteEvent"/> is never
/// raised. Autobalance, the elimination check and the faction arrival radio do not see the student for that reason.
/// </summary>
public sealed partial class SWTutorialSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    private const string SpawnPointName = "spawn";
    private const string StudentSpecies = "Human";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);

    private readonly Dictionary<NetUserId, Student> _students = new();
    private readonly HashSet<ICommonSession> _pendingOffers = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeNetworkEvent<SWTutorialChoiceEvent>(OnChoice);
        SubscribeNetworkEvent<SWTutorialExitEvent>(OnExitRequest);

        InitializeSteps();
        InitializeFinish();

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownSteps();
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public bool IsStudent(NetUserId user)
    {
        return _students.ContainsKey(user);
    }

    /// <summary>
    /// The step a student is on, or null for anyone else.
    /// </summary>
    public string? GetStudentStep(NetUserId user)
    {
        return _students.TryGetValue(user, out var student) ? CurrentStep(student).ID : null;
    }

    /// <summary>
    /// The scenario for a mode, if there is one yet.
    /// </summary>
    public SWTutorialScenarioPrototype? GetScenario(SWTutorialChoice mode)
    {
        return _proto.EnumeratePrototypes<SWTutorialScenarioPrototype>().FirstOrDefault(s => s.Mode == mode);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingOffers.Count > 0)
            ProcessPendingOffers();

        if (_students.Count == 0)
            return;

        // Before the timed part: GameTicker joins a returning student to the game again, and the round can start
        // on the very next tick.
        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby)
        {
            foreach (var user in _students.Keys)
            {
                if (_player.TryGetSessionById(user, out var session))
                    KeepOutOfRoundStart(session);
            }
        }

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        CleanUpAbsentStudents();
        UpdateSteps();
    }

    /// <summary>
    /// Takes a player from the lobby into a new private tutorial map.
    /// </summary>
    /// <param name="session">The player.</param>
    /// <param name="mode">The chosen mode.</param>
    /// <param name="error">Why the player could not start.</param>
    public bool TryStart(ICommonSession session, SWTutorialChoice mode, out LocId? error)
    {
        error = null;
        if (IsStudent(session.UserId))
        {
            error = "sw-tutorial-error-already-started";
            return false;
        }

        // Anyone else is a part of the real round, and leaving the tutorial wipes the mind.
        if (_ticker.PlayerGameStatuses.GetValueOrDefault(session.UserId) == PlayerGameStatus.JoinedGame
            || session.AttachedEntity != null
            || _mind.TryGetMind(session.UserId, out _, out _))
        {
            error = "sw-tutorial-error-not-in-lobby";
            return false;
        }

        if (!_userDb.IsLoadComplete(session))
        {
            error = "sw-tutorial-error-not-loaded";
            return false;
        }

        if (GetScenario(mode) is not { } scenario)
        {
            error = "sw-tutorial-error-mode-not-ready";
            return false;
        }

        if (_students.Count >= _cfg.GetCVar(SWVars.TutorialMaxStudents))
        {
            error = "sw-tutorial-error-full";
            return false;
        }

        var mapPath = new ResPath(_cfg.GetCVar(SWVars.TutorialMap));
        var options = DeserializationOptions.Default with { InitializeMaps = true };
        if (!_loader.TryLoadMap(mapPath, out var map, out _, options))
        {
            Log.Error($"Failed to load the tutorial map {mapPath}");
            error = "sw-tutorial-error-map";
            return false;
        }

        var mapUid = map.Value.Owner;
        var points = CollectPoints(mapUid);
        if (!HasAllPoints(mapPath, scenario, points))
        {
            QueueDel(mapUid);
            error = "sw-tutorial-error-map";
            return false;
        }

        SetFlag(session, SWTutorialTrackers.Seen);

        var student = new Student(mapUid, scenario, points, ++_runCounter, _timing.CurTime);
        _students[session.UserId] = student;

        // Before PlayerJoinGame: the client has to know about the tutorial when it loads the game screen.
        student.StepIndex = 0;
        SendState(session, student, FirstStep(student));
        _ticker.PlayerJoinGame(session, silent: true);
        KeepOutOfRoundStart(session);

        var profile = _ticker.GetPlayerProfile(session);
        var mind = _mind.CreateMind(session.UserId, profile.Name);
        _mind.SetUserId(mind, session.UserId);

        // A random first character can be a species the gear does not fit, and pockets then spill on the floor.
        var body = profile.Species == StudentSpecies
            ? profile
            : HumanoidCharacterProfile.DefaultWithSpecies(StudentSpecies).WithName(profile.Name);
        var mob = _stationSpawning.SpawnPlayerMob(points[SpawnPointName][0], null, body, null);
        EnsureComp<SWTutorialPlayerComponent>(mob);
        _stationSpawning.EquipStartingGear(mob, _proto.Index(scenario.Gear), raiseEvent: false);
        student.Body = mob;
        _mind.TransferTo(mind, mob);

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(mob):player} started the tutorial in mode {mode}.");
        Funnel(student, $"start mode={mode}");
        StartStep(session, student, 0);
        return true;
    }

    /// <summary>
    /// Sends a student back to the lobby and removes the private map.
    /// </summary>
    public bool TryExit(ICommonSession session)
    {
        if (!_students.TryGetValue(session.UserId, out var student))
            return false;

        FunnelLeft(student, "exit");
        Leave(session, student);
        return true;
    }

    private void Leave(ICommonSession session, Student student)
    {
        _students.Remove(session.UserId);
        RaiseNetworkEvent(new SWTutorialStateEvent(false), session);

        // Respawn writes its own admin log line, this says that it was not a real respawn.
        _adminLog.Add(LogType.Action, LogImpact.Low, $"Player {session.Name} left the tutorial.");
        _mind.TryGetMind(session.UserId, out var mindId, out _);
        _ticker.Respawn(session);
        CleanUp(student, mindId);
    }

    /// <summary>
    /// A wiped mind stays as an entity, and the round end summary would list it as a played character.
    /// </summary>
    private void CleanUp(Student student, EntityUid? mindId)
    {
        QueueDel(mindId);
        QueueDel(student.Map);
    }

    private Dictionary<string, List<EntityCoordinates>> CollectPoints(EntityUid map)
    {
        var points = new Dictionary<string, List<EntityCoordinates>>();
        var query = EntityQueryEnumerator<SWTutorialPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var point, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            if (!points.TryGetValue(point.Point, out var list))
                points[point.Point] = list = new List<EntityCoordinates>();

            list.Add(xform.Coordinates);
        }

        return points;
    }

    /// <summary>
    /// A map that lacks a point the scenario needs should say so at the door, not on step 8.
    /// </summary>
    private bool HasAllPoints(
        ResPath mapPath,
        SWTutorialScenarioPrototype scenario,
        Dictionary<string, List<EntityCoordinates>> points)
    {
        var missing = RequiredPoints(scenario).Where(p => !points.ContainsKey(p)).ToList();
        if (missing.Count == 0)
            return true;

        Log.Error($"The tutorial map {mapPath} has no points: {string.Join(", ", missing)}");
        return false;
    }

    /// <summary>
    /// Before the round starts, a player with the joined status counts as ready in
    /// <see cref="GameTicker.ReadyPlayerCount"/> but does not get spawned by <see cref="GameTicker.StartRound"/>. That
    /// fails an assert there in debug builds and inflates the player count for picking a preset in any build.
    /// </summary>
    private void KeepOutOfRoundStart(ICommonSession session)
    {
        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby && _ticker.UserHasJoinedGame(session))
            _ticker.ToggleReady(session, false);
    }

    /// <summary>
    /// The student's status can say not joined, so the console commands for joining still reach the spawn.
    /// </summary>
    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (IsStudent(ev.Player.UserId))
            ev.Handled = true;
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        // On first connection the play times are still loading from the database at this moment.
        _pendingOffers.Add(ev.PlayerSession);
    }

    private void ProcessPendingOffers()
    {
        foreach (var session in _pendingOffers.ToArray())
        {
            if (session.Status != SessionStatus.InGame)
            {
                _pendingOffers.Remove(session);
                continue;
            }

            if (!_userDb.IsLoadComplete(session))
                continue;

            _pendingOffers.Remove(session);

            if (ShouldOffer(session))
                RaiseNetworkEvent(new SWTutorialOfferEvent(), session);
            else if (_playTime.TryGetTrackerTime(session, SWTutorialTrackers.Seen, out _))
                RaiseNetworkEvent(new SWTutorialSeenEvent(), session);
        }
    }

    private bool ShouldOffer(ICommonSession session)
    {
        if (IsStudent(session.UserId) || !_ticker.LobbyEnabled)
            return false;

        return !_playTime.TryGetTrackerTime(session, SWTutorialTrackers.Seen, out _)
               && _playTime.GetOverallPlaytime(session) < TimeSpan.FromMinutes(_cfg.GetCVar(SWVars.TutorialOfferMinutes));
    }

    private void SetFlag(ICommonSession session, string tracker)
    {
        if (_playTime.TryGetTrackerTime(session, tracker, out _))
            return;

        _playTime.AddTimeToTracker(session, tracker, TimeSpan.FromSeconds(1));
        _playTime.SaveSession(session);
    }

    private void OnChoice(SWTutorialChoiceEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        switch (ev.Choice)
        {
            case SWTutorialChoice.Skip:
                if (_userDb.IsLoadComplete(session))
                    SetFlag(session, SWTutorialTrackers.Seen);
                break;
            case SWTutorialChoice.NewToGame:
            case SWTutorialChoice.NewToServer:
                if (!TryStart(session, ev.Choice, out var error) && error != null)
                    _chat.DispatchServerMessage(session, Loc.GetString(error.Value));
                break;
        }
    }

    private void OnExitRequest(SWTutorialExitEvent ev, EntitySessionEventArgs args)
    {
        TryExit(args.SenderSession);
    }

    /// <summary>
    /// A ghost could warp to the real round, so giving up the body here means leaving the tutorial. A dead body moves
    /// its player into a ghost on any key press, and that must not end the death scene.
    /// </summary>
    private void OnGhostAttempt(GhostAttemptHandleEvent ev)
    {
        if (ev.Handled || ev.Mind.UserId is not { } user || !_students.TryGetValue(user, out var student))
            return;

        if (!_player.TryGetSessionById(user, out var session))
            return;

        ev.Handled = true;
        ev.Result = !_mobState.IsDead(student.Body) && TryExit(session);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (!_students.TryGetValue(args.Session.UserId, out var student))
            return;

        switch (args.NewStatus)
        {
            case SessionStatus.Disconnected:
                student.DisconnectedAt = _timing.CurTime;
                break;
            case SessionStatus.InGame:
                // GameTicker puts the player back into the body, the client has lost the prompt.
                student.DisconnectedAt = null;
                SendState(args.Session, student);
                break;
        }
    }

    private void CleanUpAbsentStudents()
    {
        var timeout = TimeSpan.FromSeconds(_cfg.GetCVar(SWVars.TutorialDisconnectTimeout));
        foreach (var (user, student) in _students.ToArray())
        {
            if (student.DisconnectedAt is not { } disconnectedAt || _timing.CurTime - disconnectedAt < timeout)
                continue;

            FunnelLeft(student, "disconnect");
            _students.Remove(user);
            _mind.TryGetMind(user, out var mindId, out var mind);
            _mind.WipeMind(mindId, mind);
            CleanUp(student, mindId);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // GameTicker has already sent everyone to the lobby and is about to delete all entities, the map included.
        foreach (var (user, student) in _students)
        {
            FunnelLeft(student, "restart");
            if (_player.TryGetSessionById(user, out var session) && session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(new SWTutorialStateEvent(false), session);
        }

        _students.Clear();
        _graduates.Clear();
    }

    private sealed class Student(
        EntityUid map,
        SWTutorialScenarioPrototype scenario,
        Dictionary<string, List<EntityCoordinates>> points,
        int run,
        TimeSpan startedAt)
    {
        public readonly EntityUid Map = map;
        public readonly SWTutorialScenarioPrototype Scenario = scenario;
        public readonly Dictionary<string, List<EntityCoordinates>> Points = points;

        /// <summary>
        /// A counter with no link to the player, for the funnel log.
        /// </summary>
        public readonly int Run = run;

        public readonly TimeSpan StartedAt = startedAt;
        public readonly Dictionary<string, List<EntityUid>> Tagged = new();
        public readonly List<EntityUid> UntilStepEnd = new();

        public EntityUid Body;
        public TimeSpan? DisconnectedAt;

        public int StepIndex;
        public TimeSpan StepStartedAt;
        public EntityCoordinates StepStartPosition;
        public int HintsFired;
        public int MistakesMade;

        public LocId? Prompt;
        public bool Big;
        public SWTutorialHighlight Highlight;
        public LocId? Note;

        /// <summary>
        /// The item this step needs is not in the hands.
        /// </summary>
        public bool MissingItem;

        public float DamageMark;
        public string? LastChannel;
        public bool ReturnPressed;
    }
}
