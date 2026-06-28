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

    private SWDefenseStance _stance = SWDefenseStance.Alert;
    private bool _warPending;
    private float _border = BorderStart;
    private float _weariness;
    private string? _program;
    private float _progress;

    private int _day = -1;
    private float _accum;

    public SWDefenseStance Stance => _stance;
    public bool WarPending => _warPending;
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
        _warPending = false;
        _border = BorderStart;
        _weariness = 0f;
        _program = null;
        _progress = 0f;
        _day = -1;
    }

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

    // Peace and de-escalation are unilateral; War must be ratified by the Chancellor (or acting / autopilot).
    public void SetStance(SWDefenseStance stance)
    {
        if (stance == SWDefenseStance.War)
        {
            if (_stance == SWDefenseStance.War)
                return;
            _warPending = true;
            return;
        }

        _warPending = false;
        _stance = stance;
    }

    public bool ApproveWar()
    {
        if (!_warPending)
            return false;

        _warPending = false;
        _stance = SWDefenseStance.War;
        return true;
    }

    public bool RejectWar()
    {
        if (!_warPending)
            return false;

        _warPending = false;
        return true;
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

        if (_warPending && _senate.OfficesVacant)
        {
            _warPending = false;
            _stance = SWDefenseStance.War;
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

        TickBorder(gameHours);
        TickStance(gameHours);
        TickProgram(gameHours);
    }

    private void TickBorder(float gameHours)
    {
        var rise = _stance switch
        {
            SWDefenseStance.Peace => 2.25f,
            SWDefenseStance.War => -3f,
            _ => 1.5f,
        };

        _border = Math.Clamp(_border + rise * gameHours, 0f, 100f);

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
    }

    private void TickStance(float gameHours)
    {
        switch (_stance)
        {
            case SWDefenseStance.War:
                NudgeSectors("SWGlobalDefense", 0.3f * gameHours);
                NudgeSectors("SWGlobalEconomy", -0.3f * gameHours);
                NudgeSectors("SWGlobalSociety", (_weariness >= 60f ? -0.4f : -0.2f) * gameHours);
                _weariness = Math.Clamp(_weariness + 3f * gameHours, 0f, 100f);
                break;
            case SWDefenseStance.Peace:
                NudgeSectors("SWGlobalEconomy", 0.2f * gameHours);
                NudgeSectors("SWGlobalSociety", 0.2f * gameHours);
                NudgeSectors("SWGlobalDefense", -0.1f * gameHours);
                _weariness = Math.Clamp(_weariness - 2f * gameHours, 0f, 100f);
                break;
            default:
                _weariness = Math.Clamp(_weariness - 2f * gameHours, 0f, 100f);
                break;
        }
    }

    private void TickProgram(float gameHours)
    {
        if (_program == null || _progress >= 100f)
            return;

        var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
        _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
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
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Граница", Color = Color.FromHex("#2980B9"), Delta = proto.BorderDelta });

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
