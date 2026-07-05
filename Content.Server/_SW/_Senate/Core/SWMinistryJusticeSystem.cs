using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryJusticeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string JusticeMinistryId = "SWMinistryJustice";

    private const float TickInterval = 1f;

    private const int MaxActiveCases = 10;
    private const int BacklogComfort = 4;
    private const float ArrivalBase = 0.14f;
    private const float OverdueAge = 28f;        // game-hours before a case is dropped
    private const int InvestigateCost = 200;
    private const int TribunalHours = 6;

    private static readonly SWCaseType[] CaseTypes =
    {
        SWCaseType.Criminal, SWCaseType.Corruption, SWCaseType.Separatism, SWCaseType.Civil, SWCaseType.Political,
    };

    private readonly List<SWCourtCase> _cases = new();
    private readonly List<SWCourtClosed> _archive = new();
    private int _convictJust, _convictWrong, _acquitJust, _acquitWrong, _dismissed, _overdue;
    private int _nextCaseId = 1;
    private float _backlog = 20f;
    private int _capacity;
    private SWJusticeStance _stance = SWJusticeStance.Balanced;

    private int _tribunalUntil = -1;
    private bool _tribunalPending;

    private string? _program;
    private float _progress;

    private readonly List<string> _log = new();
    private int _day = -1;
    private float _accum;

    public SWJusticeStance Stance => _stance;
    public int Backlog => (int) MathF.Round(_backlog);
    public int Capacity => _capacity;
    public int CapacityMax => CapacityFor(_stance) + (TribunalActive ? 10 : 0);
    public int CaseCount => _cases.Count;
    public bool TribunalActive => _tribunalUntil >= 0 && _time.AbsoluteHour < _tribunalUntil;
    public int TribunalHoursLeft => TribunalActive ? Math.Max(0, _tribunalUntil - _time.AbsoluteHour) : 0;
    public bool TribunalPending => _tribunalPending;
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);

    public void RelieveBacklog(float amount) => _backlog = Math.Clamp(_backlog - amount, 0f, 100f);

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _cases.Clear();
        _archive.Clear();
        _convictJust = _convictWrong = _acquitJust = _acquitWrong = _dismissed = _overdue = 0;
        _nextCaseId = 1;
        _backlog = 20f;
        _capacity = CapacityFor(SWJusticeStance.Balanced);
        _regenAccum = 0f;
        _overtimeToday = 0;
        _stance = SWJusticeStance.Balanced;
        _tribunalUntil = -1;
        _tribunalPending = false;
        _program = null;
        _progress = 0f;
        _log.Clear();
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

    private const float RegenHours = 4f;       // courts recover 1 capacity every this many game-hours
    private const int OvertimeGain = 3;
    private const int OvertimeBaseCost = 1500;
    private const int OvertimeStep = 1000;      // each extra overtime session this day costs this much more

    private float _regenAccum;
    private int _overtimeToday;

    private static int CapacityFor(SWJusticeStance stance) => stance switch
    {
        SWJusticeStance.Lenient => 6,
        SWJusticeStance.Harsh => 9,
        _ => 7,
    };

    public int OvertimeCost() => OvertimeBaseCost + _overtimeToday * OvertimeStep;
    public bool CanBuyOvertime() => _dilemma.GetMinistryBudget(JusticeMinistryId) >= OvertimeCost();

    public bool BuyOvertime()
    {
        var cost = OvertimeCost();
        if (!_dilemma.SpendMinistry(JusticeMinistryId, cost))
            return false;

        _capacity += OvertimeGain;
        _overtimeToday++;
        AddLog($"Назначены сверхурочные слушания: +{OvertimeGain} к ресурсу судов.");
        return true;
    }

    public static string StanceName(SWJusticeStance stance) => stance switch
    {
        SWJusticeStance.Lenient => "Мягкая",
        SWJusticeStance.Harsh => "Жёсткая",
        _ => "Сбалансированная",
    };

    public static Color StanceColor(SWJusticeStance stance) => stance switch
    {
        SWJusticeStance.Lenient => Color.FromHex("#43D16A"),
        SWJusticeStance.Harsh => Color.FromHex("#E0556B"),
        _ => Color.FromHex("#5DADE2"),
    };

    public string BacklogStatus() => _backlog switch
    {
        >= 80f => "Коллапс судов",
        >= 60f => "Суды перегружены",
        >= 35f => "Напряжённо",
        _ => "В норме",
    };

    private static string TypeName(SWCaseType type) => type switch
    {
        SWCaseType.Criminal => "Уголовное",
        SWCaseType.Corruption => "Коррупция",
        SWCaseType.Separatism => "Сепаратизм",
        SWCaseType.Civil => "Гражданское",
        _ => "Политическое",
    };

    private static Color TypeColor(SWCaseType type) => type switch
    {
        SWCaseType.Criminal => Color.FromHex("#E0556B"),
        SWCaseType.Corruption => Color.FromHex("#B7950B"),
        SWCaseType.Separatism => Color.FromHex("#E67E22"),
        SWCaseType.Civil => Color.FromHex("#5DADE2"),
        _ => Color.FromHex("#9B59B6"),
    };

    private static float GuiltProb(SWCaseType type) => type switch
    {
        SWCaseType.Criminal => 0.7f,
        SWCaseType.Corruption => 0.65f,
        SWCaseType.Separatism => 0.6f,
        SWCaseType.Civil => 0.45f,
        _ => 0.38f,
    };

    private static string EvidenceLabel(float e) => e switch
    {
        >= 75f => "неопровержимая",
        >= 55f => "весомая",
        >= 35f => "спорная",
        _ => "слабая",
    };

    private static string SeverityLabel(int s) => s switch
    {
        3 => "особо тяжкое",
        2 => "тяжкое",
        _ => "мелкое",
    };

    public void SetStance(SWJusticeStance stance) => _stance = stance;

    public void SelectProgram(string programId)
    {
        if (_proto.HasIndex<SWJusticeProgramPrototype>(programId))
            _program = programId;
    }

    // --- Tribunal (Chancellor-ratified) ---

    public void DeclareTribunal()
    {
        if (_tribunalPending || TribunalActive)
            return;

        if (_senate.OfficesVacant)
        {
            BeginTribunal();
            return;
        }

        _tribunalPending = true;
    }

    public bool ApproveTribunal()
    {
        if (!_tribunalPending)
            return false;

        _tribunalPending = false;
        BeginTribunal();
        return true;
    }

    public bool RejectTribunal()
    {
        if (!_tribunalPending)
            return false;

        _tribunalPending = false;
        return true;
    }

    private void BeginTribunal()
    {
        _tribunalUntil = _time.AbsoluteHour + TribunalHours;
        _capacity += 10;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство юстиции",
            "Учреждены чрезвычайные трибуналы: ускоренные процессы по всей Республике.",
            SWSenateAnnouncementKind.Justice));
        AddLog("Чрезвычайные трибуналы запущены.");
    }

    // --- Case actions ---

    private SWCourtCase? Find(int id) => _cases.FirstOrDefault(c => c.Id == id);

    public bool Investigate(int caseId)
    {
        var c = Find(caseId);
        if (c == null || c.Stage != SWCaseStage.Investigation || _capacity < 1)
            return false;
        if (!_dilemma.SpendMinistry(JusticeMinistryId, InvestigateCost))
            return false;

        _capacity--;
        var target = c.Guilt ? 88f : 28f;
        c.Evidence = Math.Clamp(c.Evidence + (target - c.Evidence) * 0.5f + _random.NextFloat(-8f, 8f), 0f, 100f);
        return true;
    }

    public bool SendToCourt(int caseId)
    {
        var c = Find(caseId);
        if (c == null || c.Stage != SWCaseStage.Investigation)
            return false;

        c.Stage = SWCaseStage.Hearing;
        return true;
    }

    public bool HoldHearing(int caseId)
    {
        var c = Find(caseId);
        if (c == null || c.Stage != SWCaseStage.Hearing || _capacity < 1)
            return false;

        _capacity--;
        c.Stage = SWCaseStage.Verdict;
        return true;
    }

    public bool Rule(int caseId, SWVerdict verdict)
    {
        var c = Find(caseId);
        if (c == null || c.Stage != SWCaseStage.Verdict)
            return false;

        ApplyVerdict(c, verdict);
        _cases.Remove(c);
        _backlog = Math.Clamp(_backlog - 6f, 0f, 100f);
        return true;
    }

    private void ApplyVerdict(SWCourtCase c, SWVerdict verdict)
    {
        var sev = c.Severity;
        var harsh = _stance == SWJusticeStance.Harsh;
        var lenient = _stance == SWJusticeStance.Lenient;

        void Sector(string global, float delta)
        {
            if (c.SectorId != null && _senate.IsClaimed(c.SectorId))
                _senate.AddSectorBias(c.SectorId, global, delta);
        }

        switch (verdict)
        {
            case SWVerdict.Convict when c.Guilt:
            {
                var order = sev * (harsh ? 1.4f : lenient ? 0.7f : 1f);
                _senate.AddPolicy("SWStatPolice", 1.2f * order);
                _senate.AddPolicy("SWStatCrime", 1.0f * order);
                _senate.AddPolicy("SWStatRights", -0.4f * sev * (harsh ? 1.3f : 1f));
                Sector("SWGlobalSecurity", 1.5f * order);
                _convictJust++;
                Archive(c, "Справедливый приговор", 0);
                break;
            }
            case SWVerdict.Convict:
            {
                _senate.AddPolicy("SWStatRights", -2.0f * sev * (harsh ? 1.2f : 1f));
                _senate.AddPolicy("SWStatMorale", -1.2f * sev);
                _senate.AddPolicy("SWStatPolice", -0.3f * sev);
                _backlog = Math.Clamp(_backlog + 4f, 0f, 100f);
                AddLog($"Судебная ошибка по делу #{c.Id} ({TypeName(c.Type)}): осуждён невиновный.");
                _convictWrong++;
                Archive(c, "Судебная ошибка", 1);
                break;
            }
            case SWVerdict.Acquit when !c.Guilt:
            {
                _senate.AddPolicy("SWStatRights", 1.2f * sev * (lenient ? 1.3f : 1f));
                _acquitJust++;
                Archive(c, "Оправдан невиновный", 0);
                break;
            }
            case SWVerdict.Acquit:
            {
                _senate.AddPolicy("SWStatCrime", -1.2f * sev);
                _senate.AddPolicy("SWStatPolice", -1.0f * sev);
                Sector("SWGlobalSecurity", -1.0f * sev);
                AddLog($"Виновный по делу #{c.Id} ({TypeName(c.Type)}) оправдан.");
                _acquitWrong++;
                Archive(c, "Виновный оправдан", 1);
                break;
            }
            default:
            {
                _senate.AddPolicy("SWStatPolice", -0.4f * sev);
                _dismissed++;
                Archive(c, "Дело прекращено", 2);
                break;
            }
        }
    }

    private void Archive(SWCourtCase c, string result, int tier)
    {
        _archive.Insert(0, new SWCourtClosed { Id = c.Id, Type = c.Type, Result = result, Tier = tier });
        if (_archive.Count > 25)
            _archive.RemoveRange(25, _archive.Count - 25);
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_tribunalPending && _senate.OfficesVacant)
        {
            _tribunalPending = false;
            BeginTribunal();
        }

        if (_time.Day != _day)
        {
            if (_day != -1)
                ApplyProgram();
            _day = _time.Day;
            _program = null;
            _progress = 0f;
            _capacity = CapacityFor(_stance) + (TribunalActive ? 10 : 0);
            _regenAccum = 0f;
            _overtimeToday = 0;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        RegenCapacity(gameHours);
        SpawnCases(gameHours);
        AgeCases(gameHours);
        TickBacklog(gameHours);
        TickStance(gameHours);
        TickTribunal(gameHours);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    // Courts slowly recover throughput through the day, up to their normal ceiling.
    private void RegenCapacity(float gameHours)
    {
        var ceiling = CapacityFor(_stance) + (TribunalActive ? 10 : 0);
        if (_capacity >= ceiling)
            return;

        _regenAccum += gameHours;
        while (_regenAccum >= RegenHours && _capacity < ceiling)
        {
            _capacity++;
            _regenAccum -= RegenHours;
        }
    }

    private void SpawnCases(float gameHours)
    {
        if (_cases.Count >= MaxActiveCases)
            return;

        var crimeControl = _senate.SubStats.TryGetValue("SWStatCrime", out var c) ? c : 40f;
        var rate = (ArrivalBase + (50f - crimeControl) / 100f * 0.15f + _backlog / 100f * 0.1f) * gameHours;
        if (!_random.Prob(Math.Clamp(rate, 0f, 1f)))
            return;

        CreateCase(_random.Pick(CaseTypes));
    }

    private void CreateCase(SWCaseType type)
    {
        var holders = _senate.SectorHolders.Keys.ToList();
        _cases.Add(new SWCourtCase
        {
            Id = _nextCaseId++,
            Type = type,
            Stage = SWCaseStage.Investigation,
            Guilt = _random.Prob(GuiltProb(type)),
            Evidence = _random.NextFloat(35f, 60f),
            Severity = _random.Next(1, 4),
            SectorId = holders.Count > 0 && _random.Prob(0.5f) ? _random.Pick(holders) : null,
            Age = 0f,
        });
    }

    // --- Admin (swcase) ---

    public bool AdminAddCase(SWCaseType? type)
    {
        if (_cases.Count >= MaxActiveCases)
            return false;

        CreateCase(type ?? _random.Pick(CaseTypes));
        return true;
    }

    public void ClearCases() => _cases.Clear();

    public void SetBacklog(float value) => _backlog = Math.Clamp(value, 0f, 100f);

    public string AdminStatus() =>
        $"Дел: {_cases.Count}/{MaxActiveCases}, нагрузка {Backlog}/100 ({BacklogStatus()}), ресурс {_capacity}/{CapacityMax}, политика {StanceName(_stance)}.";

    private void AgeCases(float gameHours)
    {
        for (var i = _cases.Count - 1; i >= 0; i--)
        {
            var c = _cases[i];
            c.Age += gameHours;
            if (c.Age < OverdueAge)
                continue;

            // Overdue: dropped without justice - order and trust suffer.
            _senate.AddPolicy("SWStatPolice", -0.5f * c.Severity);
            _senate.AddPolicy("SWStatRights", -0.3f * c.Severity);
            _backlog = Math.Clamp(_backlog + 3f, 0f, 100f);
            AddLog($"Дело #{c.Id} ({TypeName(c.Type)}) просрочено и прекращено.");
            _overdue++;
            Archive(c, "Просрочено", 1);
            _cases.RemoveAt(i);
        }
    }

    private void TickBacklog(float gameHours)
    {
        var over = Math.Max(0, _cases.Count - BacklogComfort);
        var scale = _senate.StatPressureScale("SWGlobalSecurity", 1f);

        if (over > 0)
            _backlog = Math.Clamp(_backlog + over * 0.4f * scale * gameHours, 0f, 100f);
        else
            _backlog = Math.Clamp(_backlog - 0.6f * gameHours, 0f, 100f);

        if (_backlog >= 70f)
        {
            _senate.AddPolicy("SWStatPolice", -0.2f * gameHours);
            _senate.AddPolicy("SWStatRights", -0.15f * gameHours);
        }
    }

    private void TickStance(float gameHours)
    {
        switch (_stance)
        {
            case SWJusticeStance.Lenient:
                _senate.AddPolicy("SWStatRights", 0.15f * gameHours);
                _senate.AddPolicy("SWStatPolice", -0.1f * gameHours);
                break;
            case SWJusticeStance.Harsh:
                _senate.AddPolicy("SWStatPolice", 0.15f * gameHours);
                _senate.AddPolicy("SWStatCrime", 0.1f * gameHours);
                _senate.AddPolicy("SWStatRights", -0.15f * gameHours);
                break;
        }
    }

    private void TickTribunal(float gameHours)
    {
        if (!TribunalActive)
            return;

        _backlog = Math.Clamp(_backlog - 4f * gameHours, 0f, 100f);
        _senate.AddPolicy("SWStatPolice", 0.3f * gameHours);
        _senate.AddPolicy("SWStatRights", -0.5f * gameHours);
        _senate.AddPolicy("SWStatMorale", -0.2f * gameHours);
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWJusticeProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.BacklogDelta != 0f)
            _backlog = Math.Clamp(_backlog + proto.BacklogDelta * scale, 0f, 100f);
    }

    // Justice requests ease when courts are orderly and bite when the docket is swamped.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var cost = 1f;
        var effect = 1f;

        if (_backlog >= 60f)
        {
            cost *= 1.25f;
            effect *= 0.85f;
        }
        else if (_backlog <= 25f)
        {
            effect *= 1.15f;
        }

        return (cost, effect);
    }

    public void OnSectorEngagement(string? sectorId)
    {
        _backlog = Math.Clamp(_backlog - 4f, 0f, 100f);
    }

    public int ConvictJust => _convictJust;
    public int ConvictWrong => _convictWrong;
    public int AcquitJust => _acquitJust;
    public int AcquitWrong => _acquitWrong;
    public int Dismissed => _dismissed;
    public int Overdue => _overdue;
    public int Resolved => _convictJust + _convictWrong + _acquitJust + _acquitWrong + _dismissed + _overdue;

    public int Accuracy()
    {
        var judged = _convictJust + _convictWrong + _acquitJust + _acquitWrong;
        return judged > 0 ? (int) MathF.Round((_convictJust + _acquitJust) / (float) judged * 100f) : 0;
    }

    public List<SWClosedCaseEntry> BuildArchive()
    {
        var list = new List<SWClosedCaseEntry>();
        foreach (var closed in _archive)
        {
            list.Add(new SWClosedCaseEntry
            {
                Id = closed.Id,
                TypeLabel = TypeName(closed.Type),
                TypeColor = TypeColor(closed.Type),
                ResultLabel = closed.Result,
                ResultColor = closed.Tier switch
                {
                    0 => Color.FromHex("#43D16A"),
                    1 => Color.FromHex("#E0556B"),
                    _ => Color.FromHex("#888888"),
                },
            });
        }

        return list;
    }

    public List<SWCaseEntry> BuildCases()
    {
        var budget = _dilemma.GetMinistryBudget(JusticeMinistryId);
        var list = new List<SWCaseEntry>();
        foreach (var c in _cases)
        {
            var sectorName = c.SectorId != null && _proto.TryIndex<SWSectorPrototype>(c.SectorId, out var sp) ? sp.Name : string.Empty;
            list.Add(new SWCaseEntry
            {
                Id = c.Id,
                Stage = c.Stage,
                TypeLabel = TypeName(c.Type),
                TypeColor = TypeColor(c.Type),
                StageLabel = c.Stage switch
                {
                    SWCaseStage.Investigation => "Расследование",
                    SWCaseStage.Hearing => "Слушание",
                    _ => "Приговор",
                },
                Evidence = (int) MathF.Round(c.Evidence),
                EvidenceLabel = EvidenceLabel(c.Evidence),
                SeverityLabel = SeverityLabel(c.Severity),
                AgeLabel = $"{(int) MathF.Floor(c.Age)} ч в реестре",
                SectorName = sectorName,
                CanInvestigate = c.Stage == SWCaseStage.Investigation && _capacity >= 1 && budget >= InvestigateCost,
                CanSendToCourt = c.Stage == SWCaseStage.Investigation,
                CanHold = c.Stage == SWCaseStage.Hearing && _capacity >= 1,
                AtVerdict = c.Stage == SWCaseStage.Verdict,
            });
        }

        // Verdict-ready first, then by age.
        list.Sort((a, b) =>
        {
            if (a.AtVerdict != b.AtVerdict)
                return a.AtVerdict ? -1 : 1;
            return b.Id.CompareTo(a.Id);
        });
        return list;
    }

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWJusticeProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.BacklogDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Нагрузка", Color = Color.FromHex("#E0A23B"), Delta = proto.BacklogDelta });

            list.Add(entry);
        }

        return list;
    }

    private List<SWBillEffectEntry> PolicyEffectEntries(Dictionary<ProtoId<SWStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!effects.TryGetValue(sub.ID, out var delta))
                continue;

            var color = _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;
            list.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = color, Delta = delta });
        }

        return list;
    }
}

public sealed class SWCourtCase
{
    public int Id;
    public SWCaseType Type;
    public SWCaseStage Stage;
    public bool Guilt;
    public float Evidence;
    public int Severity;
    public string? SectorId;
    public float Age;
}

public sealed class SWCourtClosed
{
    public int Id;
    public SWCaseType Type;
    public string Result = string.Empty;
    public int Tier;
}
