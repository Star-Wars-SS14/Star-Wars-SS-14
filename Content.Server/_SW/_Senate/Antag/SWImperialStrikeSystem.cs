using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.RoundEnd;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Director;
using Content.Server._SW._Senate.GameTicking;
using Content.Shared.Echo.CaptureConsole;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._SW._Senate.Antag;

public enum SWStrikeTask : byte
{
    Kidnap,
    Kill,
    Capture,
}

public sealed class SWImperialStrikeSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryDefenseSystem _defense = default!;
    [Dependency] private readonly SWSenateCourtSystem _court = default!;
    [Dependency] private readonly SWDirectorSystem _director = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;

    private const int WindowOpenHour = 10;
    private const int WindowCloseHour = 18;
    private const float BreachChance = 0.04f;
    private const float BreachChanceHot = 0.08f;
    private const int HotBorder = 75;
    private const float EvacHoldSeconds = 60f;
    private const int Operatives = 2;
    private static readonly TimeSpan ConsoleTimeout = TimeSpan.FromSeconds(15);

    private const string CommanderSpawner = "SWSpawnPointImperialStrikeCommander";
    private const string OperativeSpawner = "SWSpawnPointImperialStrikeOperative";
    private const string RepublicLabel = "Галактическая Республика";
    private const string EmpireLabel = "Империя Ситов";

    public bool Launched { get; private set; }
    private SWStrikeTask? _task;
    private string? _targetName;
    private bool _succeeded;
    private bool _empireControls;
    private float _holdTime;
    private int _lastProcHour = -1;
    private bool _wasDay = true;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        SubscribeLocalEvent<SWImperialStrikeMemberComponent, PlayerAttachedEvent>(OnMemberAttached);
        SubscribeLocalEvent<SWSenateCaptureConsoleComponent, CaptureConsoleButtonPressedMessage>(OnCaptureButton);
        SubscribeLocalEvent<SWSenateCaptureConsoleComponent, BeforeActivatableUIOpenEvent>(OnConsoleOpen);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Launched = false;
        _task = null;
        _targetName = null;
        _succeeded = false;
        _empireControls = false;
        _holdTime = 0f;
        _lastProcHour = -1;
        _wasDay = true;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        var nightStarted = _wasDay && !_time.DayActive;
        _wasDay = _time.DayActive;

        TickConsoles();

        if (!Launched)
        {
            TickBreach();
            if (nightStarted)
                BreachRoll(2f);
        }
        else
        {
            TickExtraction(frameTime);
        }
    }

    private void TickBreach()
    {
        if (!_time.DayActive)
            return;

        var hour = _time.AbsoluteHour;
        if (hour == _lastProcHour)
            return;

        _lastProcHour = hour;
        BreachRoll(1f);
    }

    private void BreachRoll(float scale)
    {
        if (!_director.AutoAntagsAllowed)
            return;

        if (!InWindow(_time.AbsoluteHour))
            return;

        if (_defense.HasBorderFleet)
            return;

        var chance = (_defense.BorderPressure >= HotBorder ? BreachChanceHot : BreachChance) * scale;
        if (!_random.Prob(Math.Min(chance, 1f)))
            return;

        TryLaunch(null, null, out _);
    }

    private bool InWindow(int hour)
    {
        var startDay = _time.Day;
        var roundDays = 3;
        var ruleQuery = EntityQueryEnumerator<SWSenateRuleComponent>();
        if (ruleQuery.MoveNext(out var rule))
        {
            startDay = rule.StartDay;
            roundDays = rule.RoundDays;
        }

        return hour >= startDay * 24 + WindowOpenHour
            && hour <= (startDay + roundDays - 1) * 24 + WindowCloseHour;
    }

    private void TickExtraction(float frameTime)
    {
        if (_succeeded || _targetName == null || _task is not (SWStrikeTask.Kidnap or SWStrikeTask.Kill))
            return;

        var wantDead = _task == SWStrikeTask.Kill;
        var target = FindTarget(_targetName, out var dead);
        if (target == null || dead != wantDead || !InEvacZone(target.Value))
        {
            _holdTime = 0f;
            return;
        }

        _holdTime += frameTime;
        if (_holdTime < EvacHoldSeconds)
            return;

        _succeeded = true;
        Announce(wantDead
            ? $"Имперский диверсионный отряд вывез тело — {_targetName}. Челнок противника покинул атмосферу."
            : $"{_targetName} похищен имперским диверсионным отрядом! Челнок противника покинул атмосферу.");

        var query = EntityQueryEnumerator<SWImperialStrikeMemberComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (InEvacZone(uid))
                QueueDel(uid);
        }

        QueueDel(target.Value);
    }

    public bool TryLaunch(SWStrikeTask? forcedTask, string? forcedTarget, out string error)
    {
        error = string.Empty;
        if (Launched)
        {
            error = "Диверсионный отряд уже был запущен в этом раунде.";
            return false;
        }

        var spawns = new List<EntityCoordinates>();
        var spawnQuery = EntityQueryEnumerator<SWImperialStrikeSpawnComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out _, out _, out var xform))
            spawns.Add(xform.Coordinates);

        if (spawns.Count == 0)
        {
            error = "На карте нет маркеров высадки (SWImperialStrikeSpawn).";
            return false;
        }

        var hasEvac = EntityQueryEnumerator<SWImperialEvacPointComponent>().MoveNext(out _);
        var hasConsole = EntityQueryEnumerator<SWSenateCaptureConsoleComponent>().MoveNext(out _);

        string? target = null;
        if (forcedTarget != null)
        {
            if (FindAlive(forcedTarget) == null)
            {
                error = $"Цель не найдена или мертва: {forcedTarget}";
                return false;
            }

            target = forcedTarget;
        }

        SWStrikeTask task;
        if (forcedTask is { } forced)
        {
            task = forced;
            if (task == SWStrikeTask.Capture && !hasConsole)
            {
                error = "На карте нет консоли контроля Сената.";
                return false;
            }

            if (task is SWStrikeTask.Kidnap or SWStrikeTask.Kill && !hasEvac)
            {
                error = "На карте нет точки эвакуации (SWImperialEvacPoint).";
                return false;
            }

            if (task is SWStrikeTask.Kidnap or SWStrikeTask.Kill && target == null)
            {
                var pool = OfficialPool();
                if (pool.Count == 0)
                {
                    error = "Нет живых должностных лиц для выбора цели.";
                    return false;
                }

                target = _random.Pick(pool);
            }
        }
        else
        {
            var pool = OfficialPool();
            var options = new List<SWStrikeTask>();
            if (pool.Count > 0 && hasEvac)
            {
                options.Add(SWStrikeTask.Kill);
                options.Add(SWStrikeTask.Kidnap);
            }
            if (hasConsole)
                options.Add(SWStrikeTask.Capture);

            if (options.Count == 0)
            {
                error = "Нет валидной задачи: на карте нет ни целей, ни консоли контроля.";
                return false;
            }

            task = _random.Pick(options);
            if (task != SWStrikeTask.Capture)
                target = _random.Pick(pool);
        }

        _random.Shuffle(spawns);
        Spawn(CommanderSpawner, spawns[0]);
        for (var i = 0; i < Operatives; i++)
            Spawn(OperativeSpawner, spawns[(i + 1) % spawns.Count]);

        Launched = true;
        _task = task;
        _targetName = target;
        _succeeded = false;
        _holdTime = 0f;

        _defense.AddLog("Потерян контакт с постом раннего обнаружения: неопознанное судно вошло в атмосферу.");
        return true;
    }

    public string DescribeTask() => _task switch
    {
        SWStrikeTask.Kidnap => $"похищение — {_targetName}",
        SWStrikeTask.Kill => $"ликвидация — {_targetName}",
        SWStrikeTask.Capture => "захват здания Сената",
        _ => "-",
    };

    private void OnMemberAttached(EntityUid uid, SWImperialStrikeMemberComponent comp, PlayerAttachedEvent args)
    {
        if (_task is not { } task)
            return;

        var role = comp.Commander ? "командир" : "оперативник";
        var order = task switch
        {
            SWStrikeTask.Kidnap =>
                $"Задача отряда: похитить должностное лицо — {_targetName}. Доставьте цель живой в точку эвакуации и удерживайте её там {(int) EvacHoldSeconds} секунд: челнок заберёт группу вместе с целью.",
            SWStrikeTask.Kill =>
                $"Задача отряда: ликвидировать должностное лицо — {_targetName}. Доставьте тело в точку эвакуации и удерживайте его там {(int) EvacHoldSeconds} секунд: челнок заберёт группу вместе с телом.",
            _ =>
                "Задача отряда: захватить здание Сената. Активируйте консоль контроля Сената и не позволяйте республиканцам прервать перехват до его завершения.",
        };

        _chat.DispatchServerMessage(args.Player,
            $"Вы — {role} имперского диверсионного отряда. {order} У вас поддельные пропуска Сената; слушайте командира и не раскрывайтесь раньше времени.");
    }

    private void TickConsoles()
    {
        var query = EntityQueryEnumerator<SWSenateCaptureConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IsTimeout && _timing.CurTime - comp.TimeoutStart > ConsoleTimeout)
            {
                comp.IsTimeout = false;
                UpdateConsole(uid, comp, null);
            }

            if (!comp.Capturing || _timing.CurTime - comp.StartTime < comp.CaptureDuration)
                continue;

            comp.Capturing = false;
            _empireControls = comp.ByEmpire;

            if (comp.ByEmpire)
            {
                if (_task == SWStrikeTask.Capture)
                    _succeeded = true;
                Announce("Здание Сената под контролем имперских сил!");
                UpdateConsole(uid, comp, null);
                EndRoundCaptured();
                return;
            }

            Announce("Контроль над зданием Сената возвращён Республике.");
            UpdateConsole(uid, comp, null);
        }
    }

    private void OnCaptureButton(EntityUid uid, SWSenateCaptureConsoleComponent comp, CaptureConsoleButtonPressedMessage args)
    {
        if (!_time.ModeActive || comp.IsTimeout)
            return;

        if (comp.Capturing)
        {
            comp.Capturing = false;
            comp.IsTimeout = true;
            comp.TimeoutStart = _timing.CurTime;
            Announce(comp.ByEmpire
                ? "Перехват управления Сенатом прерван."
                : "Восстановление контроля над Сенатом прервано.");
        }
        else
        {
            var member = HasComp<SWImperialStrikeMemberComponent>(args.Actor);
            if (member == _empireControls)
            {
                UpdateConsole(uid, comp, args.Actor);
                return;
            }

            comp.Capturing = true;
            comp.ByEmpire = member;
            comp.StartTime = _timing.CurTime;
            comp.IsTimeout = true;
            comp.TimeoutStart = _timing.CurTime;
            Announce(member
                ? "Тревога! Неизвестные перехватывают управление зданием Сената!"
                : "Республиканские силы восстанавливают контроль над зданием Сената.");
        }

        UpdateConsole(uid, comp, args.Actor);
    }

    private void OnConsoleOpen(EntityUid uid, SWSenateCaptureConsoleComponent comp, BeforeActivatableUIOpenEvent args)
    {
        UpdateConsole(uid, comp, args.User);
    }

    private void UpdateConsole(EntityUid uid, SWSenateCaptureConsoleComponent comp, EntityUid? user)
    {
        var member = user != null && HasComp<SWImperialStrikeMemberComponent>(user.Value);
        var remain = comp.Capturing ? comp.CaptureDuration - (_timing.CurTime - comp.StartTime) : TimeSpan.Zero;
        var state = new CaptureConsoleUpdateState(
            comp.Capturing,
            comp.StartTime,
            remain,
            comp.CaptureDuration,
            _empireControls ? EmpireLabel : RepublicLabel,
            member ? EmpireLabel : RepublicLabel,
            "Сенат Галактической Республики",
            true,
            comp.IsTimeout);

        _ui.SetUiState(uid, CaptureConsoleUi.Key, state);
    }

    private void EndRoundCaptured()
    {
        var query = EntityQueryEnumerator<SWSenateRuleComponent>();
        while (query.MoveNext(out var rule))
        {
            if (rule.EndTriggered)
                return;

            rule.EndTriggered = true;
        }

        _time.ModeActive = false;
        _chat.DispatchServerAnnouncement(
            "Здание Сената захвачено силами Империи. Работа созыва прервана - подводятся итоги.",
            Color.FromHex("#E0556B"));
        _roundEnd.EndRound();
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        if (!Launched || _task is not { } task)
            return;

        var result = task switch
        {
            SWStrikeTask.Kidnap => _succeeded
                ? $"[color=red]задание выполнено — {_targetName} похищен(а) Империей[/color]"
                : $"[color=green]задание провалено — похищение ({_targetName}) не удалось[/color]",
            SWStrikeTask.Kill => _succeeded
                ? $"[color=red]задание выполнено — {_targetName} ликвидирован(а), тело вывезено Империей[/color]"
                : $"[color=green]задание провалено — тело ({_targetName}) не было вывезено[/color]",
            _ => _succeeded
                ? "[color=red]задание выполнено — здание Сената было захвачено[/color]"
                : "[color=green]задание провалено — Сенат устоял[/color]",
        };

        args.AddLine(string.Empty);
        if (_empireControls)
            args.AddLine("[color=red]Здание Сената захвачено Империей - победа диверсионного отряда.[/color]");
        args.AddLine($"Имперский диверсионный отряд: {result}.");
    }

    private List<string> OfficialPool()
    {
        var names = new HashSet<string>();
        foreach (var (_, holder) in _senate.SectorHolders)
            names.Add(holder);
        if (_senate.Chancellor != null)
            names.Add(_senate.Chancellor);
        if (_senate.Speaker != null)
            names.Add(_senate.Speaker);
        foreach (var (_, holder) in _ministry.Holders)
            names.Add(holder);
        if (_court.Chief != null)
            names.Add(_court.Chief);
        foreach (var judge in _court.Judges)
            names.Add(judge);
        if (_court.Prosecutor != null)
            names.Add(_court.Prosecutor);
        if (_court.Defender != null)
            names.Add(_court.Defender);

        return names.Where(name => FindAlive(name) != null).ToList();
    }

    private EntityUid? FindAlive(string name)
    {
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var mob, out var meta))
        {
            if (mob.CurrentState != MobState.Dead && meta.EntityName == name)
                return uid;
        }

        return null;
    }

    private EntityUid? FindTarget(string name, out bool dead)
    {
        dead = false;
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var mob, out var meta))
        {
            if (meta.EntityName != name)
                continue;

            dead = mob.CurrentState == MobState.Dead;
            return uid;
        }

        return null;
    }

    private bool InEvacZone(EntityUid uid)
    {
        var xform = Transform(uid);
        var pos = _transform.GetWorldPosition(uid);
        var query = EntityQueryEnumerator<SWImperialEvacPointComponent, TransformComponent>();
        while (query.MoveNext(out var evacUid, out var evac, out var evacXform))
        {
            if (evacXform.MapID != xform.MapID)
                continue;

            if ((_transform.GetWorldPosition(evacUid) - pos).Length() <= evac.Range)
                return true;
        }

        return false;
    }

    private void Announce(string text)
    {
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Служба безопасности Сената", text, SWSenateAnnouncementKind.Security));
    }
}
