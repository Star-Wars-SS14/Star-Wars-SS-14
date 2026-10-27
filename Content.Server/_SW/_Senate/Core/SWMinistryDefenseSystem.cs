using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string DefenseMinistryId = "SWMinistryDefense";

    private const float TickInterval = 1f;
    private const float BorderStart = 30f;

    private const int FleetCap = 5;
    private const float UpkeepPerHour = 100f;       // per deployed group, per game-hour
    private const float BorderReliefPerGroup = 1.5f; // border pressure eased per game-hour, per border group
    private const float GarrisonDefense = 0.4f;
    private const float GarrisonSecurity = 0.2f;

    private const int YardRepairCost = 1500;

    private readonly List<SWFleetGroup> _groups = new();
    private int _nextGroupId = 1;
    private float _upkeepAccum;

    private readonly List<string> _log = new();

    // Escalation to this level or higher needs the Chancellor's ratification.
    private const SWDefenseStance ApprovalThreshold = SWDefenseStance.Mobilization;

    private SWDefenseStance _stance = SWDefenseStance.Alert;
    private SWDefenseStance? _pending;
    private float _border = BorderStart;
    private float _weariness;
    private float _accum;

    public SWDefenseStance Stance => _stance;
    public SWDefenseStance? PendingStance => _pending;
    public bool WarPending => _pending != null;
    public int BorderPressure => (int) MathF.Round(_border);
    public int WarWeariness => (int) MathF.Round(_weariness);
    public string? SelectedProgram => null;
    public int ProgramProgress => 0;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateDayClosedEvent>(ev =>
        {
            if (ev.FinalDay)
                return;
            TickStrategic(0f);
        });
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _accum = 0f;
        _stance = SWDefenseStance.Alert;
        _pending = null;
        _border = BorderStart;
        _weariness = 0f;
        _groups.Clear();
        _nextGroupId = 1;
        _upkeepAccum = 0f;
        _log.Clear();
        ResetStrategic();
    }

    public void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

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

    public void SetBorder(float value) => _border = Math.Clamp(value, 0f, 100f);

    public void AddBorder(float delta) => _border = Math.Clamp(_border + delta, 0f, 100f);

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
        SWDefenseStance.Peace => (3.5f, -2.5f, new[]
        {
            ("SWGlobalEconomy", 0.3f), ("SWGlobalSociety", 0.3f), ("SWGlobalDefense", -0.2f),
        }),
        SWDefenseStance.Vigilance => (1.8f, -2.5f, new[]
        {
            ("SWGlobalDefense", 0.2f), ("SWGlobalSecurity", 0.15f), ("SWGlobalEconomy", 0.15f),
        }),
        SWDefenseStance.Alert => (2.5f, -1.5f, new[]
        {
            ("SWGlobalDefense", 0.2f), ("SWGlobalSociety", -0.15f),
        }),
        SWDefenseStance.Mobilization => (-2f, 2.5f, new[]
        {
            ("SWGlobalDefense", 0.4f), ("SWGlobalEconomy", -0.3f), ("SWGlobalSociety", -0.3f),
        }),
        _ => (-3.5f, 3.5f, new[]
        {
            ("SWGlobalDefense", 0.5f), ("SWGlobalEconomy", -0.5f), ("SWGlobalSociety", -0.45f),
        }),
    };

    public void SetStance(SWDefenseStance target)
    {
        if (!Enum.IsDefined(target))
            return;
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
            $"Уровень боеготовности: DEFCON {DefconLevel(stance)} - {StanceName(stance)}.",
            SWSenateAnnouncementKind.Defense));
    }

    public List<SWBillEffectEntry> BuildStanceEffects(SWDefenseStance stance)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var (global, delta) in Config(stance).Nudges)
        {
            var id = DoctrineStat(global);
            var name = id;
            var color = Color.White;
            if (_proto.TryIndex<SWStatPrototype>(id, out var stat))
            {
                name = stat.ShortName;
                color = _proto.TryIndex<SWGlobalStatPrototype>(stat.Global, out var group) ? group.Color : Color.White;
            }
            list.Add(new SWBillEffectEntry { StatName = name, Color = color, Delta = delta * 6f });
        }
        list.Add(new SWBillEffectEntry { StatName = "Граница", Color = Color.FromHex("#5DADE2"), Delta = Config(stance).Border, PerHour = true });
        list.Add(new SWBillEffectEntry { StatName = "Усталость", Color = Color.FromHex("#E0A23B"), Delta = Config(stance).Weariness, PerHour = true });
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
