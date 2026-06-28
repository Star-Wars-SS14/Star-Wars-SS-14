using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryDefenseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;

    public const string DefenseMinistryId = "SWMinistryDefense";

    private const float TickInterval = 1f;
    private const float BorderStart = 30f;

    private const int FleetCap = 5;
    private const float UpkeepPerHour = 100f;       // per deployed group, per game-hour
    private const float BorderReliefPerGroup = 1.5f; // border pressure eased per game-hour, per border group
    private const float GarrisonDefense = 0.4f;
    private const float GarrisonSecurity = 0.2f;

    private readonly List<SWFleetGroup> _groups = new();
    private int _nextGroupId = 1;
    private float _upkeepAccum;

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
        _groups.Clear();
        _nextGroupId = 1;
        _upkeepAccum = 0f;
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
        SWDefenseStance.Peace => (2.5f, -2.5f, new[]
        {
            ("SWGlobalEconomy", 0.3f), ("SWGlobalSociety", 0.3f), ("SWGlobalDefense", -0.2f),
        }),
        SWDefenseStance.Vigilance => (1.0f, -2.5f, new[]
        {
            ("SWGlobalDefense", 0.2f), ("SWGlobalSecurity", 0.15f), ("SWGlobalEconomy", 0.15f),
        }),
        SWDefenseStance.Alert => (1.5f, -1.5f, new[]
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

        EnsureGroups();
        PruneGarrisons();

        var borderGroups = _groups.Count(g => g.State == SWFleetState.Border);
        var deployed = _groups.Count(g => g.State != SWFleetState.Reserve);

        _border = Math.Clamp(_border + (cfg.Border - BorderReliefPerGroup * borderGroups) * gameHours, 0f, 100f);
        if (_border >= 75f)
        {
            NudgeSectors("SWGlobalDefense", -0.3f * gameHours);
            NudgeSectors("SWGlobalSociety", -0.3f * gameHours);
        }
        if (_border >= 95f)
        {
            NudgeSectors("SWGlobalDefense", -0.4f * gameHours);
            NudgeSectors("SWGlobalSociety", -0.4f * gameHours);
        }

        foreach (var (stat, delta) in cfg.Nudges)
            NudgeSectors(stat, delta * gameHours);

        foreach (var group in _groups)
        {
            if (group.State != SWFleetState.Sector || group.SectorId == null)
                continue;

            _senate.AddSectorBias(group.SectorId, "SWGlobalDefense", GarrisonDefense * gameHours);
            _senate.AddSectorBias(group.SectorId, "SWGlobalSecurity", GarrisonSecurity * gameHours);
        }

        TickUpkeep(deployed, gameHours);

        _weariness = Math.Clamp(_weariness + cfg.Weariness * gameHours, 0f, 100f);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void EnsureGroups()
    {
        var target = Math.Clamp(2 + _senate.SectorHolders.Count / 6, 2, FleetCap);
        while (_groups.Count < target)
            _groups.Add(new SWFleetGroup { Id = _nextGroupId++, State = SWFleetState.Reserve });
    }

    // A garrison whose sector was vacated falls back to reserve.
    private void PruneGarrisons()
    {
        foreach (var group in _groups)
        {
            if (group.State == SWFleetState.Sector && (group.SectorId == null || !_senate.IsClaimed(group.SectorId)))
            {
                group.State = SWFleetState.Reserve;
                group.SectorId = null;
            }
        }
    }

    private void TickUpkeep(int deployed, float gameHours)
    {
        if (deployed == 0)
        {
            _upkeepAccum = 0f;
            return;
        }

        _upkeepAccum += deployed * UpkeepPerHour * gameHours;
        if (_upkeepAccum < 1f)
            return;

        var due = (int) _upkeepAccum;
        _upkeepAccum -= due;

        if (_dilemma.SpendMinistry(DefenseMinistryId, due))
            return;

        // Out of funds — fleet stands down.
        foreach (var group in _groups)
        {
            group.State = SWFleetState.Reserve;
            group.SectorId = null;
        }
        _upkeepAccum = 0f;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Штаб обороны",
            "Флот отозван в резерв: нет средств на содержание."));
    }

    public bool DeployGroup(int groupId, string target)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null)
            return false;

        switch (target)
        {
            case "":
            case "reserve":
                group.State = SWFleetState.Reserve;
                group.SectorId = null;
                return true;
            case "border":
                group.State = SWFleetState.Border;
                group.SectorId = null;
                return true;
            default:
                if (!_senate.IsClaimed(target))
                    return false;
                group.State = SWFleetState.Sector;
                group.SectorId = target;
                return true;
        }
    }

    public int FleetUpkeep => (int) MathF.Round(_groups.Count(g => g.State != SWFleetState.Reserve) * UpkeepPerHour);

    public List<SWFleetGroupEntry> BuildFleet()
    {
        EnsureGroups();
        var list = new List<SWFleetGroupEntry>();
        foreach (var group in _groups)
        {
            var label = group.State switch
            {
                SWFleetState.Border => "Граница",
                SWFleetState.Sector => group.SectorId != null && _proto.TryIndex<SWSectorPrototype>(group.SectorId, out var sp) ? sp.Name : "Сектор",
                _ => "Резерв",
            };

            list.Add(new SWFleetGroupEntry
            {
                Id = group.Id,
                State = group.State,
                LocationLabel = label,
                SectorId = group.SectorId,
            });
        }

        return list;
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

public sealed class SWFleetGroup
{
    public int Id;
    public SWFleetState State;
    public string? SectorId;
}
