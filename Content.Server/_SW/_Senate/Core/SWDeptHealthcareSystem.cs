using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWDeptHealthcareSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWMinistryHealthSystem _health = default!;

    public const string DeptId = "SWDepartmentHealthcare";

    private const float TickInterval = 1f;
    private const int TotalBrigades = 3;
    private const float CallInterval = 0.55f;
    private const int CallCap = 3;
    private const float CallLife = 2f;
    private const float MissionHours = 1f;
    private const float ReadinessRecovery = 4f;

    private readonly record struct CallOpt(string Label, string Desc, int Cost, float Supplies, bool Reinforced);
    private readonly record struct CallDef(string Name, string Desc, bool Epidemic, (string Name, float Delta)[] Win, (string Name, float Delta)[] Fail);

    private static readonly CallDef[] Calls =
    {
        new("Авария на верфи", "Разгерметизация дока: десятки рабочих с баротравмами.", false,
            new[] { ("Здравоохр. сектора", 3f), ("Мораль", 1f) },
            new[] { ("Здравоохр. сектора", -3f), ("Промышленность", -1f) }),
        new("Обрушение шахты", "Под завалами живые. Счёт на часы.", false,
            new[] { ("Здравоохр. сектора", 3f), ("Мораль", 2f) },
            new[] { ("Мораль", -3f) }),
        new("Вспышка инфекции", "Локальный очаг на орбитальной станции. Медики сектора не справляются.", true,
            new[] { ("Заражение", -12f), ("Здравоохр. сектора", 2f) },
            new[] { ("Заражение", 6f) }),
        new("Отравление на станции", "Сотни отравившихся дешёвым синтепайком из одной столовой.", false,
            new[] { ("Здравоохр. сектора", 2f), ("Мораль", 1f) },
            new[] { ("Здравоохр. сектора", -2f), ("Мораль", -1f) }),
        new("Роды в гиперпространстве", "Лайнер запрашивает бригаду: на борту начались сложные роды.", false,
            new[] { ("Мораль", 2f) },
            new[] { ("Мораль", -2f) }),
        new("Химический разлив", "Танкер дал течь над жилым куполом.", false,
            new[] { ("Здравоохр. сектора", 3f) },
            new[] { ("Здравоохр. сектора", -4f) }),
        new("Крушение шаттла", "Пассажирский шаттл упал в пустошах. Выжившие ждут эвакуации.", false,
            new[] { ("Мораль", 2f), ("Здравоохр. сектора", 2f) },
            new[] { ("Мораль", -3f) }),
        new("Эпизоотия нерфов", "Мор скота грозит голодом аграрному поясу сектора.", false,
            new[] { ("Экономика сектора", 2f), ("Мораль", 1f) },
            new[] { ("Экономика сектора", -3f) }),
    };

    private float _readiness = 80f;
    private float _supplies = 70f;
    private int _freeBrigades = TotalBrigades;

    private readonly List<ActiveCall> _calls = new();
    private int _nextCallId = 1;
    private float _callTimer;

    private readonly List<Mission> _missions = new();
    private readonly Dictionary<int, int> _opCooldowns = new();
    private readonly List<string> _log = new();

    private int _day = -1;
    private float _accum;
    private int _successfulMissions;

    public int Readiness => (int) MathF.Round(_readiness);
    public int Supplies => (int) MathF.Round(_supplies);
    public int FreeBrigades => _freeBrigades;
    public int ActiveMissions => _missions.Count;
    public int SuccessfulMissions => _successfulMissions;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _readiness = 80f;
        _supplies = 70f;
        _freeBrigades = TotalBrigades;
        _calls.Clear();
        _nextCallId = 1;
        _callTimer = 0f;
        _missions.Clear();
        _opCooldowns.Clear();
        _log.Clear();
        _successfulMissions = 0;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, $"[{_time.CurrentHour:00}:00] {text}");
        if (_log.Count > 8)
            _log.RemoveRange(8, _log.Count - 8);
    }

    public List<string> GetLog() => new(_log);

    private void AddReadiness(float d) => _readiness = Math.Clamp(_readiness + d, 0f, 100f);
    private void AddSupplies(float d) => _supplies = Math.Clamp(_supplies + d, 0f, 100f);

    public string ReadinessStatus() => _readiness switch
    {
        >= 70f => "Бригады рвутся в бой",
        >= 40f => "Устали, но держатся",
        _ => "На пределе",
    };

    public string SuppliesStatus() => _supplies switch
    {
        >= 60f => "Склады полны",
        >= 30f => "Запасы тают",
        _ => "Работаем на бинтах",
    };

    private string SectorName(string? sectorId) =>
        sectorId != null && _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Name : "дальний космос";

    public List<SWTransitEventEntry> BuildCalls()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWTransitEventEntry>();

        foreach (var call in _calls)
        {
            var def = Calls[call.DefIndex];
            var entry = new SWTransitEventEntry
            {
                Id = call.Id,
                Name = $"{def.Name} — {SectorName(call.SectorId)}",
                Description = def.Desc,
                TimeLabel = $"Вызов сгорит через ~{MathF.Max(0f, call.LifeHours):0.0} ч",
            };

            var canDispatch = _freeBrigades > 0 && _supplies >= 10f;
            entry.Options.Add(new SWBureauDilemmaOptionEntry
            {
                Label = "Отправить бригаду",
                Description = $"Запасы -10. Успех зависит от готовности и запасов (сейчас ~{SuccessChance(false)}%).",
                Affordable = canDispatch,
                Effects = ShowEffects(def.Win),
            });
            entry.Options.Add(new SWBureauDilemmaOptionEntry
            {
                Label = "Усиленный выезд",
                Description = "Запасы -15, гарантированный успех.",
                Cost = 500,
                Affordable = _freeBrigades > 0 && _supplies >= 15f && budget >= 500,
                Effects = ShowEffects(def.Win),
            });

            list.Add(entry);
        }

        return list;
    }

    private static List<SWBillEffectEntry> ShowEffects((string Name, float Delta)[] show)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var (name, delta) in show)
        {
            var inverted = name == "Заражение";
            var good = inverted ? delta < 0f : delta > 0f;
            list.Add(new SWBillEffectEntry
            {
                StatName = name,
                Color = good ? Color.FromHex("#43D16A") : Color.FromHex("#E0556B"),
                Delta = delta,
            });
        }

        return list;
    }

    private int SuccessChance(bool reinforced) =>
        reinforced ? 100 : Math.Clamp((int) (30 + _readiness * 0.5f + _supplies * 0.25f), 10, 95);

    public bool ResolveCall(int callId, int optionIndex)
    {
        var call = _calls.FirstOrDefault(c => c.Id == callId);
        if (call == null || _freeBrigades <= 0)
            return false;

        var reinforced = optionIndex == 1;
        var supplyCost = reinforced ? 15f : 10f;
        if (_supplies < supplyCost)
            return false;
        if (reinforced && !_dilemma.SpendMinistry(DeptId, 500))
            return false;

        _calls.Remove(call);
        _freeBrigades--;
        AddSupplies(-supplyCost);
        AddReadiness(-8f);

        _missions.Add(new Mission
        {
            DefIndex = call.DefIndex,
            SectorId = call.SectorId,
            HoursLeft = MissionHours,
            SuccessChance = SuccessChance(reinforced),
        });

        AddLog($"Бригада вылетела: {Calls[call.DefIndex].Name} ({SectorName(call.SectorId)}).");
        return true;
    }

    private void CompleteMission(Mission mission)
    {
        _freeBrigades = Math.Min(TotalBrigades, _freeBrigades + 1);
        var def = Calls[mission.DefIndex];
        var success = _random.Prob(mission.SuccessChance / 100f);

        ApplyOutcome(success ? def.Win : def.Fail, mission.SectorId);
        if (success)
        {
            _successfulMissions++;
            AddReadiness(3f);
            AddLog($"{def.Name}: бригада справилась ({SectorName(mission.SectorId)}).");
        }
        else
        {
            AddReadiness(-4f);
            AddLog($"{def.Name}: выезд прошёл тяжело, потери среди пострадавших.");
        }
    }

    private void ApplyOutcome((string Name, float Delta)[] effects, string? sectorId)
    {
        foreach (var (name, delta) in effects)
        {
            switch (name)
            {
                case "Мораль":
                    _senate.AddPolicy("SWStatMorale", delta);
                    break;
                case "Заражение":
                    if (sectorId != null && _health.PathogenActive)
                        _health.AddInfection(sectorId, delta);
                    break;
                case "Здравоохр. сектора":
                    if (sectorId != null && _senate.IsClaimed(sectorId))
                        _senate.AddSectorBias(sectorId, "SWGlobalHealthcare", delta);
                    break;
                case "Экономика сектора":
                    if (sectorId != null && _senate.IsClaimed(sectorId))
                        _senate.AddSectorBias(sectorId, "SWGlobalEconomy", delta);
                    break;
                case "Промышленность":
                    _senate.AddPolicy("SWStatManufacturing", delta);
                    break;
            }
        }
    }

    private void FailCall(ActiveCall call)
    {
        var def = Calls[call.DefIndex];
        ApplyOutcome(def.Fail, call.SectorId);
        _senate.AddPolicy("SWStatMorale", -1f);
        AddLog($"{def.Name}: вызов сгорел без ответа ({SectorName(call.SectorId)}).");
    }

    // --- Express operations ---

    private static readonly (string Name, string Desc, int Cost, int Cd)[] Ops =
    {
        ("Закупка медикаментов", "Пополнить полевые склады: запасы +30.", 800, 2),
        ("Ротация бригад", "Свежие смены из резерва: готовность +25.", 600, 3),
        ("Вакцинационный десант", "Развезти вакцину по секторам. Требует готовой вакцины Минздрава.", 1000, 6),
        ("Полевой госпиталь", "Развернуть модуль в самом больном секторе.", 1500, 6),
        ("Санитарная инспекция", "Проверка столовых и куполов в случайном секторе.", 500, 3),
    };

    public List<SWShipsOpEntry> BuildOps()
    {
        var budget = _dilemma.GetMinistryBudget(DeptId);
        var list = new List<SWShipsOpEntry>();
        for (var i = 0; i < Ops.Length; i++)
        {
            var (name, desc, cost, _) = Ops[i];
            var entry = new SWShipsOpEntry { Id = i, Name = name, Description = desc, Cost = cost };

            if (_opCooldowns.TryGetValue(i, out var ready) && _time.CurrentHour < ready)
                entry.Status = $"откат: {ready - _time.CurrentHour} ч";
            else if (i == 2 && !_health.VaccineReady)
                entry.Status = "вакцина не готова";
            else if (cost > budget)
                entry.Status = "не хватает бюджета";
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
        if (_opCooldowns.TryGetValue(idx, out var ready) && _time.CurrentHour < ready)
            return false;
        if (idx == 2 && !_health.VaccineReady)
            return false;

        var (_, _, cost, cd) = Ops[idx];
        if (cost > 0 && !_dilemma.SpendMinistry(DeptId, cost))
            return false;

        _opCooldowns[idx] = _time.CurrentHour + cd;

        switch (idx)
        {
            case 0:
                AddSupplies(30f);
                AddLog("Склады пополнены медикаментами.");
                break;
            case 1:
                AddReadiness(25f);
                AddLog("Бригады отдохнули и вернулись в строй.");
                break;
            case 2:
                if (_health.PathogenActive)
                    _health.AddInfectionAll(-6f);
                _senate.AddPolicy("SWStatMorale", 2f);
                AddLog("Вакцинационный десант прошёл по секторам.");
                break;
            case 3:
                DeployFieldHospital();
                break;
            case 4:
                var claimed = _senate.SectorHolders.Keys.ToList();
                if (claimed.Count > 0)
                {
                    var sector = _random.Pick(claimed);
                    _senate.AddSectorBias(sector, "SWGlobalHealthcare", 2f);
                    AddLog($"Санитарная инспекция прошла в {SectorName(sector)}.");
                }
                break;
        }

        return true;
    }

    private void DeployFieldHospital()
    {
        string? worst = null;
        var worstValue = float.MaxValue;
        foreach (var (sectorId, globals) in _senate.SectorGlobals)
        {
            if (!_senate.IsClaimed(sectorId) || !globals.TryGetValue("SWGlobalHealthcare", out var value))
                continue;
            if (value < worstValue)
            {
                worstValue = value;
                worst = sectorId;
            }
        }

        if (worst == null)
            return;

        _senate.AddSectorBias(worst, "SWGlobalHealthcare", 4f);
        if (_health.PathogenActive)
            _health.AddInfection(worst, -8f);
        AddLog($"Полевой госпиталь развёрнут в {SectorName(worst)}.");
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
            _calls.Clear();
            _missions.Clear();
            _freeBrigades = TotalBrigades;
            _opCooldowns.Clear();
            _callTimer = 0f;
        }

        if (!_time.DayActive || !_ministry.IsClaimed(DeptId))
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        AddReadiness(ReadinessRecovery * gameHours);

        for (var i = _missions.Count - 1; i >= 0; i--)
        {
            _missions[i].HoursLeft -= gameHours;
            if (_missions[i].HoursLeft > 0f)
                continue;

            var mission = _missions[i];
            _missions.RemoveAt(i);
            CompleteMission(mission);
        }

        for (var i = _calls.Count - 1; i >= 0; i--)
        {
            _calls[i].LifeHours -= gameHours;
            if (_calls[i].LifeHours > 0f)
                continue;

            var call = _calls[i];
            _calls.RemoveAt(i);
            FailCall(call);
        }

        _callTimer += gameHours;
        if (_callTimer < CallInterval || _calls.Count >= CallCap)
            return;

        _callTimer = 0f;
        SpawnCall();
    }

    private void SpawnCall()
    {
        var pool = Enumerable.Range(0, Calls.Length)
            .Where(i => (!Calls[i].Epidemic || _health.PathogenActive) && _calls.All(c => c.DefIndex != i))
            .ToList();
        if (pool.Count == 0)
            return;

        var claimed = _senate.SectorHolders.Keys.ToList();
        _calls.Add(new ActiveCall
        {
            Id = _nextCallId++,
            DefIndex = _random.Pick(pool),
            LifeHours = CallLife,
            SectorId = claimed.Count > 0 ? _random.Pick(claimed) : null,
        });
    }

    private sealed class ActiveCall
    {
        public int Id;
        public int DefIndex;
        public float LifeHours;
        public string? SectorId;
    }

    private sealed class Mission
    {
        public int DefIndex;
        public string? SectorId;
        public float HoursLeft;
        public int SuccessChance;
    }
}
