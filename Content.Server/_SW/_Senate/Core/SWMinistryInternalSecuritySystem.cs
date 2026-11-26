using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryInternalSecuritySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWSenateTransientSituationSystem _transient = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    public const string SecurityMinistryId = "SWMinistryInternalSecurity";
    private const float TickInterval = 1f;
    private const float ThreatStart = 30f;
    private const float UnrestStart = 20f;
    private const int CellCap = 5;
    private const SWSecurityPosture ApprovalThreshold = SWSecurityPosture.Emergency;
    private SWSecurityPosture _posture = SWSecurityPosture.Watch;
    private SWSecurityPosture? _pending;
    private float _threat = ThreatStart;
    private float _unrest = UnrestStart;
    private int _plotsBusted;
    private float _accum;
    private readonly List<string> _log = new();
    public SWSecurityPosture Posture => _posture;
    public SWSecurityPosture? PendingPosture => _pending;
    public bool EmergencyPending => _pending != null;
    public int Threat => (int) MathF.Round(_threat);
    public int Unrest => (int) MathF.Round(_unrest);
    public int PlotsBusted => _plotsBusted;
    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _accum = 0; _posture = SWSecurityPosture.Watch; _pending = null;
            _threat = ThreatStart; _unrest = UnrestStart; _plotsBusted = 0; _log.Clear();
            ResetSecurityMap();
        });
    }
    public List<string> GetLog() => new(_log);
    public void SetThreat(float value) => _threat = Math.Clamp(value, 0f, 100f);
    public void AddThreat(float delta) => _threat = Math.Clamp(_threat + delta, 0f, 100f);
    public void SetUnrest(float value) => _unrest = Math.Clamp(value, 0f, 100f);
    public void AddUnrest(float delta) => _unrest = Math.Clamp(_unrest + delta, 0f, 100f);
    private int CellsOn(string sectorId) => _securityCells.Values.Count(c => c.Deployed && !c.Burned
        && _securityWorlds.TryGetValue(c.WorldId, out var world) && world.SectorId == sectorId);
    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 10)
            _log.RemoveRange(10, _log.Count - 10);
    }

    public static string PostureName(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => "Гражданский",
        SWSecurityPosture.Watch => "Наблюдение",
        SWSecurityPosture.Heightened => "Усиленный",
        _ => "Чрезвычайное положение",
    };

    public static Color PostureColor(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => Color.FromHex("#43D16A"),
        SWSecurityPosture.Watch => Color.FromHex("#5DADE2"),
        SWSecurityPosture.Heightened => Color.FromHex("#E0A23B"),
        _ => Color.FromHex("#E0556B"),
    };

    public string ThreatStatus() => _threat switch
    {
        >= 80f => "Критическая",
        >= 55f => "Высокая",
        >= 30f => "Умеренная",
        _ => "Низкая",
    };

    public string UnrestStatus() => _unrest switch
    {
        >= 80f => "Бунты",
        >= 55f => "Волнения",
        >= 30f => "Напряжение",
        _ => "Спокойно",
    };

    private static (float Threat, float Unrest, (string Stat, float Delta)[] Nudges) Config(SWSecurityPosture posture) => posture switch
    {
        SWSecurityPosture.Civil => (3.0f, -2.5f, new[]
        {
            ("SWGlobalSociety", 0.3f), ("SWGlobalSecurity", -0.25f),
        }),
        SWSecurityPosture.Watch => (1.5f, -0.5f, new[]
        {
            ("SWGlobalSecurity", 0.1f),
        }),
        SWSecurityPosture.Heightened => (-1.5f, 2.0f, new[]
        {
            ("SWGlobalSecurity", 0.3f), ("SWGlobalSociety", -0.2f),
        }),
        _ => (-3.5f, 4.0f, new[]
        {
            ("SWGlobalSecurity", 0.5f), ("SWGlobalSociety", -0.5f), ("SWGlobalEconomy", -0.2f),
        }),
    };

    public void SetPosture(SWSecurityPosture target)
    {
        if (!Enum.IsDefined(typeof(SWSecurityPosture), target)) return;
        if (target == _posture)
        {
            _pending = null;
            return;
        }

        var escalating = (byte) target > (byte) _posture;
        if (escalating && target >= ApprovalThreshold && !_senate.OfficesVacant)
        {
            _pending = target;
            return;
        }

        _pending = null;
        ChangePosture(target);
    }

    public bool ApproveEmergency()
    {
        if (_pending is not { } target)
            return false;

        _pending = null;
        ChangePosture(target);
        return true;
    }

    public bool RejectEmergency()
    {
        if (_pending == null)
            return false;

        _pending = null;
        return true;
    }

    private void ChangePosture(SWSecurityPosture posture)
    {
        if (posture == _posture)
            return;

        _posture = posture;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство ВБ",
            $"Режим безопасности: {PostureName(posture)}.",
            SWSenateAnnouncementKind.Security));
    }

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_pending is { } pending && _senate.OfficesVacant)
        {
            _pending = null;
            ChangePosture(pending);
        }

        EnsureSecurityMap();
        var gameHours = dt / SWSenateTimeSystem.RealSecondsPerGameHour;
        var cfg = Config(_posture);
        if (MinisterActive)
        {
            _threat = Math.Clamp(_threat + cfg.Threat * _senate.StatPressureScale("SWGlobalSecurity", cfg.Threat) * gameHours, 0f, 100f);
            _unrest = Math.Clamp(_unrest + cfg.Unrest * _senate.StatPressureScale("SWGlobalSociety", cfg.Unrest) * gameHours, 0f, 100f);
            UnrestFeedback(gameHours);
        }
        TickSecurityMap();
        PublishSecurityConditions();
    }

    private void UnrestFeedback(float gameHours)
    {
        if (!_senate.SubStats.TryGetValue("SWStatRights", out var rights))
            rights = 50f;
        if (!_senate.SubStats.TryGetValue("SWStatMorale", out var morale))
            morale = 50f;

        // Below ~40 average, freedoms/morale starvation stokes unrest; above it, soothes.
        var pressure = (40f - (rights + morale) / 2f) * 0.05f;
        _unrest = Math.Clamp(_unrest + pressure * gameHours, 0f, 100f);
    }

    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var covered = sectorId != null && CellsOn(sectorId) > 0;

        var cost = 1f;
        var effect = 1f;

        if (covered)
        {
            cost *= 0.7f;
            effect *= 1.2f;
        }

        if (_posture >= SWSecurityPosture.Emergency)
            effect *= 1.15f;

        if (_posture == SWSecurityPosture.Civil && !covered)
        {
            cost *= 1.2f;
            effect *= 0.85f;
        }

        return (cost, effect);
    }
    public void DampPlots(string sectorId, float delta)
    {
        if (delta >= 0 || string.IsNullOrWhiteSpace(sectorId)) return;
        _securityMitigation[sectorId] = (Math.Clamp(-delta / 12f, 0f, 1f), _time.ActiveGameHours + 6);
    }
    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null) return;
        _transient.Refresh("ministry.security.requests", sectorId, "SWStatPolice", 2f);
    }
    public List<SWBillEffectEntry> BuildPostureEffects(SWSecurityPosture posture)
    {
        var cfg = Config(posture);
        return new List<SWBillEffectEntry>
        {
            new() { StatName = "Угроза", Color = Color.FromHex("#E0556B"), Delta = cfg.Threat },
            new() { StatName = "Накал", Color = Color.FromHex("#E0A23B"), Delta = cfg.Unrest },
        };
    }
}
