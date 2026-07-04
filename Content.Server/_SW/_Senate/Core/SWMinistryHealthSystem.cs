using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryHealthSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string HealthMinistryId = "SWMinistryHealth";

    private const float TickInterval = 1f;

    public const int HospitalMaxLevel = 3;
    private const int BuildHours = 2;
    private const float HospitalUpkeepPerLevel = 40f; // per game-hour, per hospital level
    private const float TreatPerLevel = 0.8f;          // infection reduced per game-hour, per level
    private const float QuarantineBurn = 1.6f;         // infection reduced per game-hour while quarantined
    private const float BaseContagion = 0.9f;

    private const int AidCost = 1000;
    private const int EmergencyHours = 6;
    private const float SeedBaseChance = 0.04f; // per game-hour at neutral stats, before the stat scale

    private static readonly int[] FundingCostPerHour = { 0, 60, 140 };
    private static readonly float[] FundingResearchRate = { 0f, 6f, 13f }; // base research %/game-hour at Medicine 50

    private static readonly string[] PathogenNames =
    {
        "Чума Ракгулов", "Синяя Тень", "Лихорадка Кашиика", "Кровавая гниль",
        "Серая оспа", "Дрожь Криффа", "Споровый бич",
    };

    private static int BuildCost(int currentLevel) => 1500 + currentLevel * 1500;

    private readonly Dictionary<string, float> _infection = new();
    private readonly HashSet<string> _quarantine = new();
    private readonly Dictionary<string, int> _hospitals = new();
    private readonly Dictionary<string, int> _construction = new();
    private readonly Dictionary<string, int> _aidCooldown = new();
    private readonly List<string> _log = new();

    private bool _active;
    private string _name = string.Empty;
    private int _strain;
    private float _virulence = 1f;
    private float _lethality = 1f;

    private float _research;
    private int _vaccineStrain = -1; // strain the finished vaccine targets; -1 = none finished
    private int _funding;
    private float _immunity;

    private int _emergencyUntil = -1;
    private bool _emergencyPending;

    private string? _program;
    private float _progress;
    private int _hospitalsBuilt;

    private float _upkeepAccum;
    private int _day = -1;
    private float _accum;

    public int HospitalsBuilt => _hospitalsBuilt;
    public bool PathogenActive => _active;
    public string PathogenName => _name;
    public int PathogenStrain => _strain;
    public int Virulence => (int) MathF.Round(_virulence * 100f);
    public int Immunity => (int) MathF.Round(_immunity);
    public int Research => (int) MathF.Round(_research);
    public bool VaccineReady => _research >= 100f || _vaccineStrain >= 0;
    public bool VaccineStale => _vaccineStrain >= 0 && _vaccineStrain != _strain;
    public int ResearchFunding => _funding;
    public bool EmergencyActive => _emergencyUntil >= 0 && _time.CurrentHour < _emergencyUntil;
    public int EmergencyHoursLeft => EmergencyActive ? Math.Max(0, _emergencyUntil - _time.CurrentHour) : 0;
    public bool EmergencyPending => _emergencyPending;
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _infection.Clear();
        _quarantine.Clear();
        _hospitals.Clear();
        _construction.Clear();
        _aidCooldown.Clear();
        _log.Clear();
        _active = false;
        _name = string.Empty;
        _strain = 0;
        _virulence = 1f;
        _lethality = 1f;
        _research = 0f;
        _vaccineStrain = -1;
        _funding = 0;
        _immunity = 0f;
        _emergencyUntil = -1;
        _emergencyPending = false;
        _program = null;
        _progress = 0f;
        _hospitalsBuilt = 0;
        _upkeepAccum = 0f;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

    public float InfectionAvg()
    {
        var holders = _senate.SectorHolders.Keys.ToList();
        if (holders.Count == 0)
            return 0f;

        var sum = 0f;
        foreach (var sectorId in holders)
            sum += _infection.TryGetValue(sectorId, out var v) ? v : 0f;
        return sum / holders.Count;
    }

    public string PandemicStatus()
    {
        if (!_active)
            return "Нет вспышек";

        return InfectionAvg() switch
        {
            >= 60f => "Пандемия",
            >= 35f => "Эпидемия",
            >= 15f => "Вспышки",
            _ => "Очаги",
        };
    }

    public Color PandemicColor()
    {
        if (!_active)
            return Color.FromHex("#43D16A");

        return InfectionAvg() switch
        {
            >= 60f => Color.FromHex("#E0556B"),
            >= 35f => Color.FromHex("#E67E22"),
            >= 15f => Color.FromHex("#E0A23B"),
            _ => Color.FromHex("#5DADE2"),
        };
    }

    public string ResearchFundingLabel() => _funding switch
    {
        1 => "Базовое",
        2 => "Форсаж",
        _ => "Остановлено",
    };

    private static string SectorStatus(float infection) => infection switch
    {
        >= 70f => "Катастрофа",
        >= 40f => "Вспышка",
        >= 12f => "Очаг",
        _ => "Чисто",
    };

    private static Color SectorStatusColor(float infection) => infection switch
    {
        >= 70f => Color.FromHex("#E0556B"),
        >= 40f => Color.FromHex("#E67E22"),
        >= 12f => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#43D16A"),
    };

    public void SetFunding(int level) => _funding = Math.Clamp(level, 0, 2);

    public bool DeployVaccine()
    {
        if (_vaccineStrain < 0 && _research < 100f)
            return false;

        if (_research >= 100f && _vaccineStrain < 0)
            _vaccineStrain = _strain;

        var matched = _vaccineStrain == _strain;
        var efficacy = matched ? 1f : 0.5f;
        var crush = matched ? 0.35f : 0.6f;
        _immunity = Math.Clamp(_immunity + 55f * efficacy, 0f, 100f);

        foreach (var sectorId in _senate.SectorHolders.Keys.ToList())
        {
            if (_infection.TryGetValue(sectorId, out var inf))
                _infection[sectorId] = inf * crush;
        }

        AddLog(efficacy < 1f
            ? $"Развёрнута вакцина (штамм {_vaccineStrain + 1}) — против штамма {_strain + 1} вполсилы."
            : $"Массовая вакцинация завершена — иммунитет population подскочил.");

        _research = 0f;
        _vaccineStrain = -1;
        return true;
    }

    public bool SetQuarantine(string sectorId, bool on)
    {
        if (!_senate.IsClaimed(sectorId))
            return false;

        if (on)
            _quarantine.Add(sectorId);
        else
            _quarantine.Remove(sectorId);
        return true;
    }

    public bool BuildHospital(string sectorId)
    {
        if (!_senate.IsClaimed(sectorId))
            return false;
        if (_construction.ContainsKey(sectorId))
            return false;

        var level = _hospitals.TryGetValue(sectorId, out var l) ? l : 0;
        if (level >= HospitalMaxLevel)
            return false;

        if (!_dilemma.SpendMinistry(HealthMinistryId, BuildCost(level)))
            return false;

        _construction[sectorId] = _time.CurrentHour + BuildHours;
        return true;
    }

    public bool EmergencyAid(string sectorId)
    {
        if (!_senate.IsClaimed(sectorId))
            return false;
        if (_aidCooldown.TryGetValue(sectorId, out var ready) && _time.CurrentHour < ready)
            return false;
        if (!_dilemma.SpendMinistry(HealthMinistryId, AidCost))
            return false;

        if (_infection.TryGetValue(sectorId, out var inf))
            _infection[sectorId] = Math.Clamp(inf - 30f, 0f, 100f);
        _senate.AddSectorBias(sectorId, "SWGlobalHealthcare", 3f);
        _aidCooldown[sectorId] = _time.CurrentHour + 3;

        var name = _proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? sp.Name : sectorId;
        AddLog($"Экстренная помощь в {name}: очаг сбит.");
        return true;
    }

    public void DeclareEmergency()
    {
        if (EmergencyActive || _emergencyPending)
            return;

        if (_senate.OfficesVacant)
        {
            BeginEmergency();
            return;
        }

        _emergencyPending = true;
    }

    public bool ApproveEmergency()
    {
        if (!_emergencyPending)
            return false;

        _emergencyPending = false;
        BeginEmergency();
        return true;
    }

    public bool RejectEmergency()
    {
        if (!_emergencyPending)
            return false;

        _emergencyPending = false;
        return true;
    }

    private void BeginEmergency()
    {
        _emergencyUntil = _time.CurrentHour + EmergencyHours;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения",
            "Объявлено галактическое чрезвычайное положение по здоровью: всеобщий карантин.",
            SWSenateAnnouncementKind.Health));
        AddLog("Введён галактический карантин.");
    }

    public void SelectProgram(string programId)
    {
        if (!_proto.HasIndex<SWHealthProgramPrototype>(programId))
            return;

        _program = programId;
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

        if (_emergencyPending && _senate.OfficesVacant)
        {
            _emergencyPending = false;
            BeginEmergency();
        }

        if (_time.Day != _day)
        {
            if (_day != -1)
                ApplyProgram();
            _day = _time.Day;
            _program = null;
            _progress = 0f;
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        CompleteConstruction();
        PruneSectors();
        SeedPathogen(gameHours);

        if (_active)
        {
            SpreadInfection(gameHours);
            RollMutation(gameHours);
            ApplyMortality(gameHours);

            if (InfectionAvg() < 1f && _immunity > 40f)
            {
                _active = false;
                _research = 0f;
                _vaccineStrain = -1;
                AddLog($"Эпидемия «{_name}» подавлена.");
            }
        }

        AdvanceResearch(gameHours);
        TickUpkeep(gameHours);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void PruneSectors()
    {
        foreach (var sectorId in _infection.Keys.ToList())
            if (!_senate.IsClaimed(sectorId))
                _infection.Remove(sectorId);

        _quarantine.RemoveWhere(s => !_senate.IsClaimed(s));
    }

    private void CompleteConstruction()
    {
        foreach (var (sectorId, ready) in _construction.ToList())
        {
            if (_time.CurrentHour < ready)
                continue;

            _hospitals[sectorId] = Math.Min(HospitalMaxLevel, (_hospitals.TryGetValue(sectorId, out var l) ? l : 0) + 1);
            _construction.Remove(sectorId);
            _hospitalsBuilt++;
            var name = _proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? sp.Name : sectorId;
            AddLog($"Госпиталь в {name} достроен (ур. {_hospitals[sectorId]}).");
        }
    }

    // Outbreak frequency is governed by the Republic's health (and, lighter, its society):
    // a sickly, deprived galaxy breeds new plagues far more often than a well-run one.
    private float SeedChanceScale()
    {
        var hc = _senate.GetRepublicGlobal("SWGlobalHealthcare");
        var soc = _senate.GetRepublicGlobal("SWGlobalSociety");
        var gov = hc * 0.7f + soc * 0.3f;
        return Math.Clamp(50f / Math.Max(gov, 1f), 0.4f, 3.5f);
    }

    private void SeedPathogen(float gameHours)
    {
        if (_active)
            return;

        var holders = _senate.SectorHolders.Keys.ToList();
        if (holders.Count == 0)
            return;

        var chance = SeedBaseChance * SeedChanceScale() * gameHours;
        if (!_random.Prob(Math.Clamp(chance, 0f, 1f)))
            return;

        BeginPathogen(_random.Pick(holders));
    }

    private void BeginPathogen(string origin)
    {
        _active = true;
        _name = _random.Pick(PathogenNames);
        _strain = 0;
        // A weak healthcare system also lets the pathogen emerge nastier.
        var hc = _senate.GetRepublicGlobal("SWGlobalHealthcare");
        _virulence = Math.Clamp(1f + (50f - hc) / 120f, 0.85f, 1.5f);
        _lethality = 1f;
        _research = 0f;
        _vaccineStrain = -1;
        _immunity = Math.Max(0f, _immunity - 30f);
        _infection[origin] = 20f;

        var name = _proto.TryIndex<SWSectorPrototype>(origin, out var sp) ? sp.Name : origin;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения",
            $"Вспышка неизвестного патогена «{_name}» в секторе {name}.",
            SWSenateAnnouncementKind.Health));
        AddLog($"Новый патоген «{_name}» — очаг в {name} (вирулентность {(int) (_virulence * 100f)}%).");
    }

    private void SpreadInfection(float gameHours)
    {
        var holders = _senate.SectorHolders.Keys.ToList();
        var pressure = InfectionAvg();
        var spreadScale = _senate.StatPressureScale("SWGlobalHealthcare", 1f);

        foreach (var sectorId in holders)
        {
            var inf = _infection.TryGetValue(sectorId, out var v) ? v : 0f;
            var quarantined = _quarantine.Contains(sectorId) || EmergencyActive;
            var hospital = _hospitals.TryGetValue(sectorId, out var hl) ? hl : 0;

            if (!quarantined)
            {
                var gain = BaseContagion * (0.3f + pressure / 100f) * _virulence
                           * (1f - _immunity / 100f) * spreadScale * gameHours;
                inf += gain;
            }

            var treat = hospital * TreatPerLevel;
            if (quarantined)
                treat += QuarantineBurn;
            if (EmergencyActive)
                treat += 2.5f;
            inf -= treat * gameHours;

            _infection[sectorId] = Math.Clamp(inf, 0f, 100f);

            if (hospital > 0)
            {
                _senate.AddSectorBias(sectorId, "SWGlobalHealthcare", 0.25f * hospital * gameHours);
            }

            if (quarantined && !EmergencyActive)
            {
                _senate.AddSectorBias(sectorId, "SWGlobalEconomy", -0.3f * gameHours);
                _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.2f * gameHours);
            }
        }

        if (EmergencyActive)
        {
            foreach (var sectorId in holders)
            {
                _senate.AddSectorBias(sectorId, "SWGlobalEconomy", -0.6f * gameHours);
                _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.4f * gameHours);
            }
        }
    }

    private void RollMutation(float gameHours)
    {
        var chance = (0.015f + InfectionAvg() / 100f * 0.05f) * gameHours;
        if (_random.Prob(Math.Clamp(chance, 0f, 1f)))
            Mutate();
    }

    private void Mutate()
    {
        _strain++;
        _virulence = MathF.Min(_virulence * 1.15f, 3f);
        _lethality = MathF.Min(_lethality * 1.1f, 2.5f);
        _research *= 0.6f;
        _immunity *= 0.5f;
        AddLog($"Патоген мутировал — штамм {_strain + 1}, вирулентность выше.");
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство здравоохранения",
            $"Патоген «{_name}» мутировал в штамм {_strain + 1}.",
            SWSenateAnnouncementKind.Health));
    }

    private void ApplyMortality(float gameHours)
    {
        foreach (var (sectorId, inf) in _infection)
        {
            if (inf < 50f || !_senate.IsClaimed(sectorId))
                continue;

            var severity = (inf - 50f) / 50f * _lethality;
            _senate.AddSectorBias(sectorId, "SWGlobalHealthcare", -0.6f * severity * gameHours);
            _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.4f * severity * gameHours);
            _senate.AddSectorBias(sectorId, "SWGlobalEconomy", -0.3f * severity * gameHours);
            _senate.AddPolicy("SWStatWelfare", -0.25f * severity * gameHours);
        }
    }

    private void AdvanceResearch(float gameHours)
    {
        // No pathogen, no strain to target — a vaccine can't be developed in advance.
        if (!_active || _funding == 0 || _research >= 100f)
            return;

        var medicine = _senate.GetRepublicGlobal("SWGlobalHealthcare");
        var networkBonus = 1f + TotalHospitalLevels() * 0.05f;
        var rate = FundingResearchRate[_funding] * (medicine / 50f) * networkBonus;
        _research = Math.Clamp(_research + rate * gameHours, 0f, 100f);

        if (_research >= 100f && _vaccineStrain < 0)
        {
            _vaccineStrain = _strain;
            AddLog($"Вакцина против штамма {_strain + 1} готова к развёртыванию.");
        }
    }

    private int TotalHospitalLevels() => _hospitals.Values.Sum();

    private void TickUpkeep(float gameHours)
    {
        var levels = TotalHospitalLevels();
        if (levels == 0)
        {
            _upkeepAccum = 0f;
            return;
        }

        _upkeepAccum += levels * HospitalUpkeepPerLevel * gameHours;
        if (_upkeepAccum < 1f)
            return;

        var due = (int) _upkeepAccum;
        _upkeepAccum -= due;
        _dilemma.SpendMinistry(HealthMinistryId, due);
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWHealthProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.ResearchDelta != 0f && _research < 100f && _active)
            _research = Math.Clamp(_research + proto.ResearchDelta * scale, 0f, 100f);

        if (proto.SpreadDelta != 0f)
        {
            foreach (var sectorId in _infection.Keys.ToList())
                _infection[sectorId] = Math.Clamp(_infection[sectorId] + proto.SpreadDelta * scale, 0f, 100f);
        }
    }

    // Health ministry requests are eased where a hospital stands and the sector is not ravaged.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        if (sectorId == null)
            return (1f, 1f);

        var hospital = _hospitals.TryGetValue(sectorId, out var l) ? l : 0;
        var inf = _infection.TryGetValue(sectorId, out var v) ? v : 0f;

        var cost = 1f;
        var effect = 1f;

        if (hospital > 0)
        {
            cost *= 1f - 0.12f * hospital;
            effect *= 1f + 0.12f * hospital;
        }

        if (inf >= 50f)
        {
            cost *= 1.3f;
            effect *= 0.8f;
        }

        return (cost, effect);
    }

    // Resolving a sector's health request also relieves its outbreak.
    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null)
            return;

        if (_infection.TryGetValue(sectorId, out var inf))
            _infection[sectorId] = Math.Clamp(inf - 12f, 0f, 100f);
    }

    // --- Admin (swplague) ---

    public bool ForceSeed(string? sectorId)
    {
        string origin;
        if (sectorId != null)
        {
            if (!_senate.IsClaimed(sectorId))
                return false;
            origin = sectorId;
        }
        else
        {
            var holders = _senate.SectorHolders.Keys.ToList();
            if (holders.Count == 0)
                return false;
            origin = _random.Pick(holders);
        }

        if (_active)
        {
            _infection[origin] = Math.Clamp((_infection.TryGetValue(origin, out var v) ? v : 0f) + 25f, 0f, 100f);
            return true;
        }

        BeginPathogen(origin);
        return true;
    }

    public void ClearPlague()
    {
        _active = false;
        _infection.Clear();
        _research = 0f;
        _vaccineStrain = -1;
        AddLog("Эпидемия ликвидирована (адм.).");
    }

    public bool SetInfection(string? sectorId, float value)
    {
        var holders = _senate.SectorHolders.Keys.ToList();
        if (holders.Count == 0)
            return false;

        if (!_active && value > 0f)
            BeginPathogen(sectorId != null && _senate.IsClaimed(sectorId) ? sectorId : _random.Pick(holders));

        if (sectorId != null)
        {
            if (!_senate.IsClaimed(sectorId))
                return false;
            _infection[sectorId] = Math.Clamp(value, 0f, 100f);
        }
        else
        {
            foreach (var s in holders)
                _infection[s] = Math.Clamp(value, 0f, 100f);
        }

        return true;
    }

    public void AddInfectionAll(float delta)
    {
        foreach (var s in _senate.SectorHolders.Keys.ToList())
            _infection[s] = Math.Clamp((_infection.TryGetValue(s, out var v) ? v : 0f) + delta, 0f, 100f);
    }

    public float SectorInfection(string sectorId) => _infection.TryGetValue(sectorId, out var v) ? v : 0f;

    public bool IsQuarantined(string sectorId) => _quarantine.Contains(sectorId);

    public void AddInfection(string sectorId, float delta)
    {
        if (!_active || !_senate.IsClaimed(sectorId))
            return;

        _infection[sectorId] = Math.Clamp(SectorInfection(sectorId) + delta, 0f, 100f);
    }

    public bool ForceMutate()
    {
        if (!_active)
            return false;
        Mutate();
        return true;
    }

    public string AdminStatus()
    {
        if (!_active)
            return $"Эпидемий нет. Иммунитет {Immunity}/100.";

        return $"«{_name}» штамм {_strain + 1}, вирулентность {Virulence}%, среднее заражение {(int) InfectionAvg()}/100, вакцина {Research}%.";
    }

    public List<SWInfectionSectorEntry> BuildInfectionSectors()
    {
        var budget = _dilemma.GetMinistryBudget(HealthMinistryId);
        var list = new List<SWInfectionSectorEntry>();
        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var inf = _infection.TryGetValue(sectorId, out var v) ? (int) MathF.Round(v) : 0;
            var level = _hospitals.TryGetValue(sectorId, out var l) ? l : 0;
            var building = _construction.TryGetValue(sectorId, out var ready);
            var aidReady = !_aidCooldown.TryGetValue(sectorId, out var ar) || _time.CurrentHour >= ar;

            list.Add(new SWInfectionSectorEntry
            {
                Id = sectorId,
                Name = proto.Name,
                Color = proto.Color,
                Infection = inf,
                StatusLabel = SectorStatus(inf),
                StatusColor = SectorStatusColor(inf),
                Quarantined = _quarantine.Contains(sectorId),
                HospitalLevel = level,
                Building = building,
                BuildLeft = building ? Math.Max(0, ready - _time.CurrentHour) : 0,
                CanBuild = !building && level < HospitalMaxLevel && budget >= BuildCost(level),
                BuildCost = level < HospitalMaxLevel ? BuildCost(level) : 0,
                CanAid = aidReady && budget >= AidCost,
                AidCost = AidCost,
            });
        }

        list.Sort((a, b) => b.Infection.CompareTo(a.Infection));
        return list;
    }

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWHealthProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.ResearchDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Вакцина", Color = Color.FromHex("#48C9B0"), Delta = proto.ResearchDelta });
            if (proto.SpreadDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Заражение", Color = Color.FromHex("#E0556B"), Delta = proto.SpreadDelta });

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
