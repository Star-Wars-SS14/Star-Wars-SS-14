using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared.GameTicking;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

// The Chancellor's emergency powers: a tiered grant (I/II/III) plus any individually-vested powers, with a
// renewable expiry, and the Authority(tyranny)/Legitimacy axis that the whole arc balances on.
public sealed class SWEmergencyPowersSystem : EntitySystem
{
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly Robust.Shared.Random.IRobustRandom _random = default!;

    private const float TickInterval = 1f;
    private const int DurationDays = 2;          // a grant lasts this many days unless renewed
    private const float TyrannyDecay = 1.5f;     // authority/tyranny eases per game-hour with no abuse
    private const float LegitRecover = 1.0f;     // legitimacy heals per game-hour when powers are dormant
    public const int LowLegitimacy = 40;         // below this, a no-confidence vote needs only 2/3

    private int _level;
    private readonly HashSet<SWEmergencyPower> _granted = new();
    private int _expiryDay = -1;
    private float _authority;
    private float _legitimacy = 100f;
    private bool _barredChancellor;              // set when a Chancellor is ousted by no-confidence

    private int _day = -1;
    private float _accum;

    public int Level => _level;
    public int Authority => (int) MathF.Round(_authority);
    public int Legitimacy => (int) MathF.Round(_legitimacy);
    public bool Active => (_level > 0 || _granted.Count > 0) && !Expired;
    public bool Expired => _expiryDay >= 0 && _time.Day > _expiryDay;
    public int ExpiryDay => _expiryDay;
    public int DaysLeft => _expiryDay < 0 ? 0 : Math.Max(0, _expiryDay - _time.Day);
    public bool ChancellorBarred => _barredChancellor;

    public static int RequiredLevel(SWEmergencyPower power) => power switch
    {
        SWEmergencyPower.Veto or SWEmergencyPower.Command => 1,
        SWEmergencyPower.EmergencyBudget or SWEmergencyPower.MartialLaw
            or SWEmergencyPower.Mobilization or SWEmergencyPower.Censorship => 2,
        _ => 3,
    };

    public static string PowerName(SWEmergencyPower power) => power switch
    {
        SWEmergencyPower.Veto => "Вето",
        SWEmergencyPower.Command => "Командование министерствами",
        SWEmergencyPower.EmergencyBudget => "Чрезвычайный бюджет",
        SWEmergencyPower.MartialLaw => "Военное положение",
        SWEmergencyPower.Mobilization => "Мобилизация",
        SWEmergencyPower.Censorship => "Цензура",
        SWEmergencyPower.Purge => "Чистки",
        SWEmergencyPower.Decree => "Декреты",
        _ => "Контроль над министрами",
    };

    public bool HasPower(SWEmergencyPower power) =>
        Active && (RequiredLevel(power) <= _level || _granted.Contains(power));

    public string LevelName() => _level switch
    {
        3 => "Уровень III — чрезвычайный",
        2 => "Уровень II — расширенный",
        1 => "Уровень I — ограниченный",
        _ => _granted.Count > 0 ? "Точечные полномочия" : "Нет полномочий",
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateEmergencyMotionPassedEvent>(OnEmergencyMotionPassed);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _level = 0;
        _granted.Clear();
        _expiryDay = -1;
        _authority = 0f;
        _legitimacy = 100f;
        _barredChancellor = false;
        _day = -1;
    }

    // --- Grants ---

    public void GrantLevel(int level)
    {
        _level = Math.Clamp(Math.Max(_level, level), 0, 3);
        Renew();
        Announce($"Сенат наделил Канцлера чрезвычайными полномочиями: {LevelName()}.");
    }

    public void GrantPower(SWEmergencyPower power)
    {
        _granted.Add(power);
        Renew();
        Announce($"Сенат наделил Канцлера полномочием: {PowerName(power)}.");
    }

    public void Renew() => _expiryDay = _time.Day + DurationDays;

    public void Relinquish(bool announce = true)
    {
        if (!Active && _level == 0 && _granted.Count == 0)
            return;

        _level = 0;
        _granted.Clear();
        _expiryDay = -1;
        if (announce)
            Announce("Канцлер сложил чрезвычайные полномочия.");
    }

    // --- Authority / Legitimacy ---

    // Every emergency action stains the Chancellor; abuse (acting without a real crisis, against the calm)
    // costs far more legitimacy and openly hits the Republic's rights and morale.
    public void RegisterUse(float authority, float legitimacy, bool abuse)
    {
        _authority = Math.Clamp(_authority + authority, 0f, 100f);
        _legitimacy = Math.Clamp(_legitimacy - legitimacy, 0f, 100f);

        if (abuse)
        {
            _legitimacy = Math.Clamp(_legitimacy - legitimacy, 0f, 100f);
            _senate.AddPolicy("SWStatRights", -1.5f);
            _senate.AddPolicy("SWStatMorale", -1f);
        }
    }

    public int NoConfidenceThreshold() => _legitimacy < LowLegitimacy ? 67 : 75; // percent of votes needed

    public void OustChancellor()
    {
        _barredChancellor = true;
        Relinquish(false);
    }

    public void ClearBar() => _barredChancellor = false;

    // --- Senate-session emergency motions ---

    public int MotionThreshold(SWMotionType type) => type == SWMotionType.NoConfidence ? NoConfidenceThreshold() : 67;

    private void OnEmergencyMotionPassed(SWSenateEmergencyMotionPassedEvent ev)
    {
        ApplyPassedMotion(ev.Type, ev.Level, ev.Power);
    }

    private void ApplyPassedMotion(SWMotionType type, int level, SWEmergencyPower power)
    {
        switch (type)
        {
            case SWMotionType.GrantLevel:
                GrantLevel(level);
                break;
            case SWMotionType.GrantPower:
                GrantPower(power);
                break;
            case SWMotionType.NoConfidence:
                var name = _senate.OustChancellor();
                Relinquish(false);
                Announce($"Вотум недоверия принят: Канцлер {name ?? ""} снят с должности. Обязанности исполняет Спикер.");
                break;
        }
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

        if (_time.Day != _day)
        {
            _day = _time.Day;
            if (Expired)
                Relinquish();
        }

        if (!_time.DayActive)
            return;

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);

        // Tyranny eases over time; legitimacy heals only while powers are dormant.
        _authority = Math.Clamp(_authority - TyrannyDecay * gameHours, 0f, 100f);
        if (!Active)
            _legitimacy = Math.Clamp(_legitimacy + LegitRecover * gameHours, 0f, 100f);

        TickTyrannyConsequences(gameHours);
        TickAutoOffer(gameHours);
    }

    // High tyranny breeds unrest, and — paired with collapsed legitimacy — the risk of a coup that topples
    // the Chancellor outright.
    private void TickTyrannyConsequences(float gameHours)
    {
        if (_authority < 70f)
            return;

        _senate.AddPolicy("SWStatMorale", -0.3f * gameHours);
        _senate.AddPolicy("SWStatRights", -0.2f * gameHours);
        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, "SWGlobalSociety", -0.2f * gameHours);

        if (_senate.Chancellor == null)
            return;

        var coupChance = (_authority - 70f) / 30f * (1f - _legitimacy / 100f) * 0.03f * gameHours;
        if (_random.Prob(Math.Clamp(coupChance, 0f, 1f)))
        {
            var name = _senate.OustChancellor();
            Relinquish(false);
            Announce($"Переворот! Канцлер {name} свергнут на волне тирании. Обязанности исполняет Спикер.");
        }
    }

    private bool _autoOffered;

    private bool IsCatastrophe() =>
        _senate.GetRepublicGlobal("SWGlobalDefense") < 25f
        || _senate.GetRepublicGlobal("SWGlobalSecurity") < 25f
        || _senate.GetRepublicGlobal("SWGlobalHealthcare") < 25f
        || _senate.GetRepublicGlobal("SWGlobalSociety") < 25f;

    private void TickAutoOffer(float gameHours)
    {
        if (!IsCatastrophe())
        {
            _autoOffered = false;
            return;
        }

        // During a catastrophe, the Senate is offered an emergency act automatically (once per crisis).
        if (_autoOffered || Active || _senate.SectorHolders.Count == 0)
            return;

        _autoOffered = true;
        _senate.SubmitEmergencyPowerMotion(SWMotionType.GrantLevel, 1, SWEmergencyPower.Veto, "Кризис", MotionThreshold(SWMotionType.GrantLevel));
    }

    private void Announce(string text) =>
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Сенат", text));

    // --- Admin (swpowers) ---

    public void AdminSetLevel(int level)
    {
        if (level <= 0)
            Relinquish(false);
        else
            GrantLevel(level);
    }

    public void AdminSetAuthority(float v) => _authority = Math.Clamp(v, 0f, 100f);
    public void AdminSetLegitimacy(float v) => _legitimacy = Math.Clamp(v, 0f, 100f);

    public string AdminStatus() =>
        $"Полномочия: {LevelName()} (точечно: {_granted.Count}). Авторитет {Authority}/100, легитимность {Legitimacy}/100, дней осталось {DaysLeft}.";
}
