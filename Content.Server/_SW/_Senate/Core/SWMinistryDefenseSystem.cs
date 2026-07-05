using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryDefenseSystem : EntitySystem
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

    private const float DamageChancePerHour = 0.25f; // per border group while the border is hot (>=75)
    private const int RepairHours = 3;
    private const int YardRepairCost = 1500;
    private const int ExpressRepairCost = 3500;

    private const int MaxActiveOps = 2;

    private readonly List<SWFleetGroup> _groups = new();
    private int _nextGroupId = 1;
    private float _upkeepAccum;

    private readonly List<SWActiveOperation> _activeOps = new();
    private readonly Dictionary<string, int> _cooldowns = new();
    private readonly List<string> _log = new();

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
        _activeOps.Clear();
        _cooldowns.Clear();
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

    public void SelectProgram(string programId)
    {
        if (!_proto.HasIndex<SWDefenseProgramPrototype>(programId))
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
            _activeOps.Clear();
            _cooldowns.Clear();
        }

        if (!_time.DayActive)
            return;

        CompleteOperations();

        var gameHours = dt * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        var cfg = Config(_stance);

        EnsureGroups();
        PruneGarrisons();
        CompleteRepairs();

        var borderRelief = 0f;
        foreach (var g in _groups)
        {
            if (g.State == SWFleetState.Border)
                borderRelief += g.Damaged ? BorderReliefPerGroup * 0.5f : BorderReliefPerGroup;
        }

        var deployed = _groups.Count(g => g.State is SWFleetState.Border or SWFleetState.Sector);

        // Imperial pressure builds faster the weaker the Republic's defences; fleet relief is unscaled.
        var borderRise = cfg.Border * _senate.StatPressureScale("SWGlobalDefense", cfg.Border);
        _border = Math.Clamp(_border + (borderRise - borderRelief) * gameHours, 0f, 100f);
        if (_border >= 75f)
        {
            NudgeSectors("SWGlobalDefense", -0.3f * gameHours);
            NudgeSectors("SWGlobalSociety", -0.3f * gameHours);
            RollBorderDamage(gameHours, _border >= 95f ? 0.5f : DamageChancePerHour);
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

            var scale = group.Damaged ? 0.5f : 1f;
            _senate.AddSectorBias(group.SectorId, "SWGlobalDefense", GarrisonDefense * scale * gameHours);
            _senate.AddSectorBias(group.SectorId, "SWGlobalSecurity", GarrisonSecurity * scale * gameHours);
        }

        TickUpkeep(deployed, gameHours);

        var wearinessScale = _senate.StatPressureScale("SWGlobalSociety", cfg.Weariness);
        _weariness = Math.Clamp(_weariness + cfg.Weariness * wearinessScale * gameHours, 0f, 100f);

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

        // Out of funds - fleet stands down.
        foreach (var group in _groups)
        {
            group.State = SWFleetState.Reserve;
            group.SectorId = null;
        }
        _upkeepAccum = 0f;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Штаб обороны",
            "Флот отозван в резерв: нет средств на содержание.",
            SWSenateAnnouncementKind.Defense));
    }

    private void RollBorderDamage(float gameHours, float chancePerHour)
    {
        foreach (var group in _groups)
        {
            if (group.State == SWFleetState.Border && !group.Damaged && _random.Prob(chancePerHour * gameHours))
            {
                group.Damaged = true;
                AddLog($"{group.Id}-й Флот повреждён в стычке на границе.");
            }
        }
    }

    private void CompleteRepairs()
    {
        foreach (var group in _groups)
        {
            if (group.State == SWFleetState.Repairing && _time.CurrentHour >= group.RepairUntil)
            {
                group.Damaged = false;
                group.RepairUntil = 0;
                group.State = SWFleetState.Reserve;
                group.SectorId = null;
            }
        }
    }

    public bool DeployGroup(int groupId, string target)
    {
        var group = _groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null || group.State == SWFleetState.Repairing)
            return false;

        if (target == "express")
        {
            if (!group.Damaged || !_dilemma.SpendMinistry(DefenseMinistryId, ExpressRepairCost))
                return false;
            group.Damaged = false;
            return true;
        }

        if (target.StartsWith("repair:"))
        {
            var yard = target["repair:".Length..];
            if (!group.Damaged
                || !_senate.IsClaimed(yard)
                || !_proto.TryIndex<SWSectorPrototype>(yard, out var yp) || !yp.Shipyard
                || !_dilemma.SpendMinistry(DefenseMinistryId, YardRepairCost))
                return false;

            group.State = SWFleetState.Repairing;
            group.SectorId = null;
            group.RepairUntil = _time.CurrentHour + RepairHours;
            return true;
        }

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
    public int FleetSize => _groups.Count;
    public int FleetActive => _groups.Count(g => g.State == SWFleetState.Sector);

    public List<SWFleetGroupEntry> BuildFleet()
    {
        EnsureGroups();
        var list = new List<SWFleetGroupEntry>();
        foreach (var group in _groups)
        {
            var label = group.State switch
            {
                SWFleetState.Border => "Граница",
                SWFleetState.Repairing => "Ремонт",
                SWFleetState.Sector => group.SectorId != null && _proto.TryIndex<SWSectorPrototype>(group.SectorId, out var sp) ? sp.Name : "Сектор",
                _ => "Резерв",
            };

            list.Add(new SWFleetGroupEntry
            {
                Id = group.Id,
                State = group.State,
                LocationLabel = label,
                SectorId = group.SectorId,
                Damaged = group.Damaged,
                RepairLeft = group.State == SWFleetState.Repairing ? Math.Max(0, group.RepairUntil - _time.CurrentHour) : 0,
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

    // Defense ministry requests are modulated by readiness and whether a healthy garrison holds the sector.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var garrison = sectorId != null && _groups.Any(g => g.State == SWFleetState.Sector && g.SectorId == sectorId && !g.Damaged);

        var cost = 1f;
        var effect = 1f;

        if (garrison)
        {
            cost *= 0.6f;
            effect *= 1.25f;
        }

        if (_stance >= SWDefenseStance.Mobilization)
            effect *= 1.2f;

        if (_stance == SWDefenseStance.Peace && !garrison)
        {
            cost *= 1.3f;
            effect *= 0.8f;
        }

        return (cost, effect);
    }

    // Resolving a hot military request can bloody the garrison that responded.
    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null)
            return;

        var garrison = _groups.FirstOrDefault(g => g.State == SWFleetState.Sector && g.SectorId == sectorId && !g.Damaged);
        if (garrison != null && _random.Prob(0.3f))
        {
            garrison.Damaged = true;
            var name = _proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? sp.Name : sectorId;
            AddLog($"{garrison.Id}-й Флот повреждён при отражении кризиса в {name}.");
        }
    }

    public bool LaunchOperation(string opId, string target)
    {
        if (!_proto.TryIndex<SWDefenseOperationPrototype>(opId, out var op))
            return false;
        if (_stance < op.RequireStance)
            return false;
        if (op.RequireBorderFleet && _groups.All(g => g.State != SWFleetState.Border))
            return false;
        if (_cooldowns.TryGetValue(opId, out var ready) && _time.CurrentHour < ready)
            return false;

        string? sectorId = null;
        if (op.Target == SWOpTarget.Sector)
        {
            if (!_senate.IsClaimed(target))
                return false;
            sectorId = target;
        }

        if (op.Duration > 0 && _activeOps.Count >= MaxActiveOps)
            return false;

        if (_dilemma.GetMinistryAdmin(DefenseMinistryId) < op.AdminCost
            || _dilemma.GetMinistryBudget(DefenseMinistryId) < op.Cost)
            return false;

        _dilemma.SpendMinistryAdmin(DefenseMinistryId, op.AdminCost);
        _dilemma.SpendMinistry(DefenseMinistryId, op.Cost);
        _cooldowns[opId] = _time.CurrentHour + op.Cooldown;

        if (op.Duration <= 0)
            ApplyOp(op, sectorId);
        else
            _activeOps.Add(new SWActiveOperation { OpId = opId, SectorId = sectorId, CompleteHour = _time.CurrentHour + op.Duration });

        return true;
    }

    private void ApplyOp(SWDefenseOperationPrototype op, string? sectorId)
    {
        if (op.BorderDelta != 0f)
            _border = Math.Clamp(_border + op.BorderDelta, 0f, 100f);

        foreach (var (sub, delta) in op.PolicyEffects)
            _senate.AddPolicy(sub, delta);

        if (sectorId != null && _senate.IsClaimed(sectorId))
        {
            foreach (var (global, delta) in op.SectorEffects)
                _senate.AddSectorBias(sectorId, global, delta);
        }
    }

    private void CompleteOperations()
    {
        for (var i = _activeOps.Count - 1; i >= 0; i--)
        {
            var active = _activeOps[i];
            if (_time.CurrentHour < active.CompleteHour)
                continue;

            if (_proto.TryIndex<SWDefenseOperationPrototype>(active.OpId, out var op))
                ApplyOp(op, active.SectorId);

            _activeOps.RemoveAt(i);
        }
    }

    public int ActiveOpSlots => MaxActiveOps;
    public int ActiveOpUsed => _activeOps.Count;

    public List<SWDefenseOperationEntry> BuildOperations()
    {
        var admin = _dilemma.GetMinistryAdmin(DefenseMinistryId);
        var budget = _dilemma.GetMinistryBudget(DefenseMinistryId);
        var borderFleet = _groups.Any(g => g.State == SWFleetState.Border);
        var slotsFree = _activeOps.Count < MaxActiveOps;

        var list = new List<SWDefenseOperationEntry>();
        foreach (var op in _proto.EnumeratePrototypes<SWDefenseOperationPrototype>().OrderBy(o => o.Order))
        {
            var entry = new SWDefenseOperationEntry
            {
                Id = op.ID,
                Name = op.Name,
                Description = op.Description,
                Cost = op.Cost,
                AdminCost = op.AdminCost,
                Duration = op.Duration,
                Target = op.Target,
                Effects = OpEffects(op),
            };

            if (_cooldowns.TryGetValue(op.ID, out var ready) && _time.CurrentHour < ready)
                entry.StatusLabel = $"откат: {ready - _time.CurrentHour} ч";
            else if (_stance < op.RequireStance)
                entry.StatusLabel = $"нужна готовность: {StanceName(op.RequireStance)}";
            else if (op.RequireBorderFleet && !borderFleet)
                entry.StatusLabel = "нужен флот на границе";
            else if (op.Duration > 0 && !slotsFree)
                entry.StatusLabel = "нет свободных слотов";
            else if (admin < op.AdminCost)
                entry.StatusLabel = "нет админ-ресурса";
            else if (budget < op.Cost)
                entry.StatusLabel = "не хватает бюджета";
            else
                entry.Available = true;

            list.Add(entry);
        }

        return list;
    }

    public List<SWActiveOpEntry> BuildActiveOps()
    {
        var list = new List<SWActiveOpEntry>();
        foreach (var active in _activeOps)
        {
            var name = _proto.TryIndex<SWDefenseOperationPrototype>(active.OpId, out var op) ? op.Name : active.OpId;
            var detail = active.SectorId != null && _proto.TryIndex<SWSectorPrototype>(active.SectorId, out var sp) ? sp.Name : "-";
            list.Add(new SWActiveOpEntry { Name = name, Detail = detail, HoursLeft = Math.Max(0, active.CompleteHour - _time.CurrentHour) });
        }

        return list;
    }

    private List<SWBillEffectEntry> OpEffects(SWDefenseOperationPrototype op)
    {
        var list = PolicyEffectEntries(op.PolicyEffects);
        foreach (var (global, delta) in op.SectorEffects)
        {
            var name = global.Id;
            var color = Color.White;
            if (_proto.TryIndex<SWGlobalStatPrototype>(global, out var gp))
            {
                name = gp.ShortName;
                color = gp.Color;
            }

            list.Add(new SWBillEffectEntry { StatName = name, Color = color, Delta = delta });
        }

        if (op.BorderDelta != 0f)
            list.Add(new SWBillEffectEntry { StatName = "Граница", Color = Color.FromHex("#5DADE2"), Delta = op.BorderDelta });

        return list;
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
    public bool Damaged;
    public int RepairUntil;
}

public sealed class SWActiveOperation
{
    public string OpId = string.Empty;
    public string? SectorId;
    public int CompleteHour;
}
