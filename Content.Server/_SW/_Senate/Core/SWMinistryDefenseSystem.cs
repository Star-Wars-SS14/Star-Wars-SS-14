using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryDefenseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;

    public const string DefenseMinistryId = "SWMinistryDefense";

    private const float TickInterval = 1f;
    private const float BorderStart = 30f;

    // Escalation to this level or higher needs the Chancellor's ratification.
    private const SWDefenseStance ApprovalThreshold = SWDefenseStance.Mobilization;

    private SWDefenseStance _stance = SWDefenseStance.Alert;
    private SWDefenseStance? _pending;
    private float _border = BorderStart;
    private float _weariness;
    private string? _program;
    private float _progress;

    private int _day = -1;
    private float _accum;

    public SWDefenseStance Stance => _stance;
    public SWDefenseStance? PendingStance => _pending;
    public bool WarPending => _pending != null;
    public int BorderPressure => (int) MathF.Round(_border);
    public int WarWeariness => (int) MathF.Round(_weariness);
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _stance = SWDefenseStance.Alert;
        _pending = null;
        _border = BorderStart;
        _weariness = 0f;
        _program = null;
        _progress = 0f;
        _day = -1;
    }

    public static int DefconLevel(SWDefenseStance stance) => 5 - (byte) stance;

    public static string StanceName(SWDefenseStance stance) => stance switch
    {
        SWDefenseStance.Peace => "Мир",
        SWDefenseStance.Vigilance => "Бдительность",
        SWDefenseStance.Alert => "Тревога",
        SWDefenseStance.Mobilization => "Мобилизация",
        _ => "Война",
    };

    public static Color StanceColor(SWDefenseStance stance) => stance switch
    {
        SWDefenseStance.Peace => Color.FromHex("#43D16A"),
        SWDefenseStance.Vigilance => Color.FromHex("#5DADE2"),
        SWDefenseStance.Alert => Color.FromHex("#E0A23B"),
        SWDefenseStance.Mobilization => Color.FromHex("#E67E22"),
        _ => Color.FromHex("#E0556B"),
    };

    public string BorderStatus()
    {
        return _border switch
        {
            >= 95f => "На грани войны",
            >= 75f => "Провокации и вторжения",
            >= 50f => "Напряжённо",
            _ => "Спокойно",
        };
    }

    // (border rise per game-hour, war-weariness per game-hour, sector nudges per game-hour)
    private static (float Border, float Weariness, (string Stat, float Delta)[] Nudges) Config(SWDefenseStance stance) => stance switch
    {
        SWDefenseStance.Peace => (2.25f, -2f, new[]
        {
            ("SWGlobalEconomy", 0.2f), ("SWGlobalSociety", 0.2f), ("SWGlobalDefense", -0.1f),
        }),
        SWDefenseStance.Vigilance => (1.0f, -2f, new[]
        {
            ("SWGlobalDefense", 0.1f), ("SWGlobalSecurity", 0.1f), ("SWGlobalEconomy", 0.1f),
        }),
        SWDefenseStance.Alert => (1.5f, -1f, new[]
        {
            ("SWGlobalDefense", 0.1f), ("SWGlobalSociety", -0.1f),
        }),
        SWDefenseStance.Mobilization => (-1.5f, 2f, new[]
        {
            ("SWGlobalDefense", 0.25f), ("SWGlobalEconomy", -0.2f), ("SWGlobalSociety", -0.2f),
        }),
        _ => (-3f, 3f, new[]
        {
            ("SWGlobalDefense", 0.35f), ("SWGlobalEconomy", -0.35f), ("SWGlobalSociety", -0.3f),
        }),
    };

    public void SetStance(SWDefenseStance target)
    {
        if (target == _stance)
        {
            _pending = null;
            return;
        }

        var escalating = (byte) target > (byte) _stance;
        if (escalating && target >= ApprovalThreshold && !_senate.OfficesVacant)
        {
            _pending = target;
            return;
        }

        _pending = null;
        ChangeStance(target);
    }

    public bool ApproveWar()
    {
        if (_pending is not { } target)
            return false;

        _pending = null;
        ChangeStance(target);
        return true;
    }

    public bool RejectWar()
    {
        if (_pending == null)
            return false;

        _pending = null;
        return true;
    }

    private void ChangeStance(SWDefenseStance stance)
    {
        if (stance == _stance)
            return;

        _stance = stance;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Штаб обороны",
            $"Уровень боеготовности: DEFCON {DefconLevel(stance)} — {StanceName(stance)}."));
    }

    public void SelectProgram(string programId)
    {
        if (!_proto.HasIndex<SWDefenseProgramPrototype>(programId))
            return;

        _program = programId;
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_pending is { } pending && _senate.OfficesVacant)
        {
            _pending = null;
            ChangeStance(pending);
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
        var cfg = Config(_stance);

        _border = Math.Clamp(_border + cfg.Border * gameHours, 0f, 100f);
        if (_border >= 75f)
        {
            NudgeSectors("SWGlobalDefense", -0.2f * gameHours);
            NudgeSectors("SWGlobalSociety", -0.2f * gameHours);
        }
        if (_border >= 95f)
        {
            NudgeSectors("SWGlobalDefense", -0.3f * gameHours);
            NudgeSectors("SWGlobalSociety", -0.3f * gameHours);
        }

        foreach (var (stat, delta) in cfg.Nudges)
            NudgeSectors(stat, delta * gameHours);

        _weariness = Math.Clamp(_weariness + cfg.Weariness * gameHours, 0f, 100f);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWDefenseProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.BorderDelta != 0f)
            _border = Math.Clamp(_border + proto.BorderDelta * scale, 0f, 100f);
    }

    private void NudgeSectors(string globalId, float delta)
    {
        if (delta == 0f)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, globalId, delta);
    }

    public List<SWBillEffectEntry> BuildStanceEffects(SWDefenseStance stance)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var (stat, delta) in Config(stance).Nudges)
        {
            var name = stat;
            var color = Color.White;
            if (_proto.TryIndex<SWGlobalStatPrototype>(stat, out var global))
            {
                name = global.ShortName;
                color = global.Color;
            }

            list.Add(new SWBillEffectEntry { StatName = name, Color = color, Delta = delta });
        }

        list.Add(new SWBillEffectEntry { StatName = "Граница", Color = Color.FromHex("#5DADE2"), Delta = Config(stance).Border });
        return list;
    }

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWDefenseProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.BorderDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Граница", Color = Color.FromHex("#5DADE2"), Delta = proto.BorderDelta });

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
