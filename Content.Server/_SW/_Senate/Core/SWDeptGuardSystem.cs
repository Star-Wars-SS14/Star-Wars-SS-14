using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWDeptGuardSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryJusticeSystem _justice = default!;
    [Dependency] private readonly SWMinistryInternalSecuritySystem _security = default!;

    public const string DeptId = "SWDepartmentJustice";

    private const float TickInterval = 1f;
    private const int MaxSquads = 6;
    private const int PostCap = 2;
    private const float WritInterval = 0.7f;
    private const int WritCap = 3;
    private const float WritLife = 2f;
    private const float WritHours = 1f;

    private static readonly (string Name, string Effect)[] Posts =
    {
        ("Зал заседаний", "порядок на трибуне: Мораль"),
        ("Коридоры Сената", "патрули: Полиция, гасят волнения"),
        ("Доки станции", "досмотр грузов: Преступность вниз"),
        ("Архив и серверные", "охрана тайн: Контрразведка"),
    };

    private readonly record struct WritDef(string Name, string Desc, float Relief, int Fine);

    private static readonly WritDef[] Writs =
    {
        new("Конвой заключённого", "Доставить обвиняемого из блока временного содержания в зал суда.", 10f, 300),
        new("Охрана слушания", "Обеспечить порядок на громком процессе: публика на взводе.", 8f, 200),
        new("Доставка свидетеля", "Свидетель боится за жизнь - нужен эскорт до трибуны.", 12f, 300),
        new("Обыск в доках", "Судебный ордер на досмотр подозрительного склада.", 8f, 500),
        new("Усмирение дебоша", "Драка в приёмной Сената переросла в погром.", 6f, 200),
        new("Эскорт сенатора", "Сенатор получил угрозы и требует охрану на день.", 6f, 400),
        new("Изъятие архива", "Изъять документы по ордеру, пока их не сожгли.", 10f, 400),
    };

    private float _discipline = 60f;
    private int _totalSquads = 4;
    private readonly int[] _posted = new int[4];

    private readonly List<ActiveWrit> _writs = new();
    private int _nextWritId = 1;
    private float _writTimer;

    private readonly List<RunningWrit> _running = new();
    private readonly Dictionary<int, int> _opCooldowns = new();
    private readonly List<string> _log = new();

    private int _day = -1;
    private float _accum;
    private int _writsExecuted;

    public int Discipline => (int) MathF.Round(_discipline);
    public int TotalSquads => _totalSquads;
    public int PostedTotal => _posted.Sum();
    public int ReserveSquads => Math.Max(0, _totalSquads - PostedTotal - _running.Sum(r => r.Squads));
    public int WritsExecuted => _writsExecuted;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _discipline = 60f;
        _totalSquads = 4;
        Array.Clear(_posted, 0, _posted.Length);
        _writs.Clear();
        _nextWritId = 1;
        _writTimer = 0f;
        _running.Clear();
        _opCooldowns.Clear();
        _log.Clear();
        _writsExecuted = 0;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, $"[{_time.CurrentHour:00}:00] {text}");
        if (_log.Count > 8)
            _log.RemoveRange(8, _log.Count - 8);
    }

    public List<string> GetLog() => new(_log);

    private void AddDiscipline(float d) => _discipline = Math.Clamp(_discipline + d, 0f, 100f);

    public string DisciplineStatus() => _discipline switch
    {
        >= 75f => "Образцовый строй",
        >= 50f => "Служба идёт",
        >= 30f => "Разболтались",
        _ => "Гвардия в разброде",
    };

    // --- Posts ---

    public bool SetPost(int post, int delta)
    {
        if (post is < 0 or >= 4)
            return false;

        var target = Math.Clamp(_posted[post] + delta, 0, PostCap);
        if (target == _posted[post])
            return false;
        if (target > _posted[post] && ReserveSquads <= 0)
            return false;

        _posted[post] = target;
        return true;
    }

    public List<SWGuardPostEntry> BuildPosts()
    {
        var list = new List<SWGuardPostEntry>();
        for (var i = 0; i < Posts.Length; i++)
        {
            list.Add(new SWGuardPostEntry
            {
                Index = i,
                Name = Posts[i].Name,
                Assigned = _posted[i],
                Max = PostCap,
                EffectLabel = Posts[i].Effect,
            });
        }

        return list;
    }

    // --- Writs ---

    public List<SWTransitEventEntry> BuildWrits()
    {
        var list = new List<SWTransitEventEntry>();
        foreach (var writ in _writs)
        {
            var def = Writs[writ.DefIndex];
            var entry = new SWTransitEventEntry
            {
                Id = writ.Id,
                Name = def.Name,
                Description = def.Desc,
                TimeLabel = $"Ордер сгорит через ~{MathF.Max(0f, writ.LifeHours):0.0} ч",
            };

            entry.Options.Add(new SWBureauDilemmaOptionEntry
            {
                Label = "Направить 1 отряд",
                Description = $"Успех ~{SuccessChance(1)}%.",
                Affordable = ReserveSquads >= 1,
            });
            entry.Options.Add(new SWBureauDilemmaOptionEntry
            {
                Label = "Направить 2 отряда",
                Description = $"Успех ~{SuccessChance(2)}%.",
                Affordable = ReserveSquads >= 2,
            });

            list.Add(entry);
        }

        return list;
    }

    private int SuccessChance(int squads) =>
        Math.Clamp((int) (25 + squads * 25 + _discipline * 0.4f), 10, 95);

    public bool ResolveWrit(int writId, int optionIndex)
    {
        var writ = _writs.FirstOrDefault(w => w.Id == writId);
        if (writ == null)
            return false;

        var squads = optionIndex == 1 ? 2 : 1;
        if (ReserveSquads < squads)
            return false;

        _writs.Remove(writ);
        _running.Add(new RunningWrit
        {
            DefIndex = writ.DefIndex,
            Squads = squads,
            HoursLeft = WritHours,
            SuccessChance = SuccessChance(squads),
        });

        AddLog($"Гвардия выдвинулась: {Writs[writ.DefIndex].Name} ({squads} отр.).");
        return true;
    }

    private void CompleteWrit(RunningWrit writ)
    {
        var def = Writs[writ.DefIndex];
        if (_random.Prob(writ.SuccessChance / 100f))
        {
            _writsExecuted++;
            _justice.RelieveBacklog(def.Relief);
            _dilemma.SetMinistryBudget(DeptId, _dilemma.GetMinistryBudget(DeptId) + def.Fine);
            AddDiscipline(2f);
            AddLog($"{def.Name}: исполнено. Суды разгружены, взыскано {def.Fine} кр.");
        }
        else
        {
            AddDiscipline(-4f);
            _senate.AddPolicy("SWStatMorale", -1f);
            AddLog($"{def.Name}: провал. Гвардия опозорилась.");
        }
    }

    private void FailWrit(ActiveWrit writ)
    {
        AddDiscipline(-3f);
        _senate.AddPolicy("SWStatMorale", -1f);
        AddLog($"{Writs[writ.DefIndex].Name}: ордер просрочен без исполнения.");
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Строевые учения", "Муштра в ангаре: дисциплина +15.", 600, 3),
        ("Набор рекрутов", "Сформировать новый отряд гвардии (до 6).", 1200, 6),
        ("Парад гвардии", "Алые плащи на площади: Мораль +2, дисциплина слегка просядет.", 800, 5),
        ("Ночное усиление", "Сверхурочные посты: разово гасит накал волнений.", 700, 4),
        ("Чистка рядов", "Уволить разгильдяев: дисциплина +10, отряд может уйти.", 400, 5),
    };

    public List<SWShipsOpEntry> BuildOps()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWShipsOpEntry>();
        for (var i = 0; i < Ops.Length; i++)
        {
            var (name, desc, cost, _) = Ops[i];
            var entry = new SWShipsOpEntry { Id = i, Name = name, Description = desc, Cost = cost };

            if (_opCooldowns.TryGetValue(i, out var ready) && _time.AbsoluteHour < ready)
                entry.Status = $"готовность через {ready - _time.AbsoluteHour} ч";
            else if (i == 1 && _totalSquads >= MaxSquads)
                entry.Status = "штат укомплектован";
            else if (cost > budget)
                entry.Status = "недостаточно бюджета";
            else
                entry.Available = true;

            list.Add(entry);
        }

        return list;
    }

    public bool LaunchOp(int idx)
    {
        if (idx < 0 || idx >= Ops.Length)
            return false;
        if (_opCooldowns.TryGetValue(idx, out var ready) && _time.AbsoluteHour < ready)
            return false;
        if (idx == 1 && _totalSquads >= MaxSquads)
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(DeptId, cost))
            return false;

        _opCooldowns[idx] = _time.AbsoluteHour + cd;

        switch (idx)
        {
            case 0:
                AddDiscipline(15f);
                AddLog("Учения завершены: строй как на параде.");
                break;
            case 1:
                _totalSquads++;
                AddLog("Новый отряд гвардии принял присягу.");
                break;
            case 2:
                _senate.AddPolicy("SWStatMorale", 2f);
                AddDiscipline(-5f);
                AddLog("Парад гвардии прошёл на главной площади.");
                break;
            case 3:
                _security.AddUnrest(-6f);
                AddDiscipline(-3f);
                AddLog("Ночное усиление: улицы вокруг Сената притихли.");
                break;
            case 4:
                AddDiscipline(10f);
                if (_totalSquads > 2 && _random.Prob(0.4f))
                {
                    _totalSquads--;
                    AddLog("Чистка рядов: отряд расформирован за нарушения.");
                }
                else
                {
                    AddLog("Чистка рядов: разгильдяи наказаны, строй подтянулся.");
                }
                break;
        }

        return true;
    }

    // --- Tick ---

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_time.Day != _day)
        {
            _day = _time.Day;
            _opCooldowns.Clear();
            _writTimer = 0f;
        }

        if (!_time.DayActive || !_ministry.IsClaimed(DeptId))
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        TickPosts(gameHours);

        for (var i = _running.Count - 1; i >= 0; i--)
        {
            _running[i].HoursLeft -= gameHours;
            if (_running[i].HoursLeft > 0f)
                continue;

            var writ = _running[i];
            _running.RemoveAt(i);
            CompleteWrit(writ);
        }

        for (var i = _writs.Count - 1; i >= 0; i--)
        {
            _writs[i].LifeHours -= gameHours;
            if (_writs[i].LifeHours > 0f)
                continue;

            var writ = _writs[i];
            _writs.RemoveAt(i);
            FailWrit(writ);
        }

        _writTimer += gameHours;
        if (_writTimer >= WritInterval && _writs.Count < WritCap)
        {
            _writTimer = 0f;
            var pool = Enumerable.Range(0, Writs.Length).Where(i => _writs.All(w => w.DefIndex != i)).ToList();
            if (pool.Count > 0)
                _writs.Add(new ActiveWrit { Id = _nextWritId++, DefIndex = _random.Pick(pool), LifeHours = WritLife });
        }
    }

    private void TickPosts(float gameHours)
    {
        var scale = 0.5f + _discipline / 100f;

        if (_posted[0] > 0)
            _senate.AddPolicy("SWStatMorale", 0.04f * _posted[0] * scale * gameHours);

        if (_posted[1] > 0)
        {
            _senate.AddPolicy("SWStatPolice", 0.05f * _posted[1] * scale * gameHours);
            if (_security.Unrest >= 55)
                _security.AddUnrest(-0.15f * _posted[1] * scale * gameHours);
        }

        if (_posted[2] > 0)
            _senate.AddPolicy("SWStatCrime", -0.05f * _posted[2] * scale * gameHours);

        if (_posted[3] > 0)
            _senate.AddPolicy("SWStatCounter", 0.04f * _posted[3] * scale * gameHours);
    }

    private sealed class ActiveWrit
    {
        public int Id;
        public int DefIndex;
        public float LifeHours;
    }

    private sealed class RunningWrit
    {
        public int DefIndex;
        public int Squads;
        public float HoursLeft;
        public int SuccessChance;
    }
}
