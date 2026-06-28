using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryEconomySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistryFinanceSystem _finance = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string EconomyMinistryId = "SWMinistryEconomy";

    private const float TickInterval = 1f;

    private const int MaxProjects = 2;
    private const int MaxRoutes = 4;
    private const int RouteSetupCost = 2000;
    private const float RouteUpkeepPerHour = 60f;
    private const float RouteEconomyBonus = 0.4f;  // per game-hour to each endpoint
    private const float RouteIndustryBonus = 0.2f;
    private const float RouteSecurityFloor = 30f;  // below this an endpoint route is disrupted

    private readonly List<SWActiveMegaproject> _projects = new();
    private readonly List<SWTradeRoute> _routes = new();
    private int _nextRouteId = 1;
    private readonly List<string> _log = new();
    private float _routeUpkeepAccum;

    private float _growth = 1f;
    private float _overheat = 25f;
    private int _policy = 1; // 0 austerity / 1 neutral / 2 stimulus
    private bool _stimulusPending;

    private string? _program;
    private float _progress;

    private int _day = -1;
    private float _accum;

    public SWEconomyPhase Phase => DerivePhase();
    public float Growth => _growth;
    public int Overheat => (int) MathF.Round(_overheat);
    public int Policy => _policy;
    public bool StimulusPending => _stimulusPending;
    public string? SelectedProgram => _program;
    public int ProgramProgress => (int) MathF.Round(_progress);
    public int ProjectSlots => MaxProjects;
    public int ProjectUsed => _projects.Count;
    public int RouteCap => MaxRoutes;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _projects.Clear();
        _routes.Clear();
        _nextRouteId = 1;
        _log.Clear();
        _routeUpkeepAccum = 0f;
        _growth = 1f;
        _overheat = 25f;
        _policy = 1;
        _stimulusPending = false;
        _program = null;
        _progress = 0f;
        _day = -1;
    }

    private void AddLog(string text)
    {
        _log.Insert(0, text);
        if (_log.Count > 6)
            _log.RemoveRange(6, _log.Count - 6);
    }

    public List<string> GetLog() => new(_log);

    private SWEconomyPhase DerivePhase()
    {
        if (_growth < 0f)
            return SWEconomyPhase.Recession;
        if (_overheat >= 70f)
            return SWEconomyPhase.Overheat;
        if (_growth >= 3f)
            return SWEconomyPhase.Growth;
        return SWEconomyPhase.Recovery;
    }

    public static string PhaseName(SWEconomyPhase phase) => phase switch
    {
        SWEconomyPhase.Recession => "Рецессия",
        SWEconomyPhase.Recovery => "Восстановление",
        SWEconomyPhase.Growth => "Рост",
        _ => "Перегрев",
    };

    public static Color PhaseColor(SWEconomyPhase phase) => phase switch
    {
        SWEconomyPhase.Recession => Color.FromHex("#E0556B"),
        SWEconomyPhase.Recovery => Color.FromHex("#5DADE2"),
        SWEconomyPhase.Growth => Color.FromHex("#43D16A"),
        _ => Color.FromHex("#E0A23B"),
    };

    public string OverheatStatus() => _overheat switch
    {
        >= 90f => "Пузырь вот-вот лопнет",
        >= 70f => "Перегрев",
        >= 45f => "Напряжённо",
        _ => "Здоровый",
    };

    public string PolicyLabel() => _policy switch
    {
        0 => "Сдерживание",
        2 => "Стимулирование",
        _ => "Нейтрально",
    };

    public void SetPolicy(int level) => _policy = Math.Clamp(level, 0, 2);

    public void SelectProgram(string programId)
    {
        if (_proto.HasIndex<SWEconomyProgramPrototype>(programId))
            _program = programId;
    }

    // (growth push, overheat push) per game-hour.
    private static (float Growth, float Overheat) PolicyConfig(int policy) => policy switch
    {
        0 => (-1.0f, -3.0f),
        2 => (1.5f, 2.5f),
        _ => (0.3f, -0.5f),
    };

    public void DeclareStimulus()
    {
        if (_stimulusPending)
            return;

        if (_senate.OfficesVacant)
        {
            BeginStimulus();
            return;
        }

        _stimulusPending = true;
    }

    public bool ApproveStimulus()
    {
        if (!_stimulusPending)
            return false;

        _stimulusPending = false;
        BeginStimulus();
        return true;
    }

    public bool RejectStimulus()
    {
        if (!_stimulusPending)
            return false;

        _stimulusPending = false;
        return true;
    }

    private void BeginStimulus()
    {
        _growth = Math.Clamp(_growth + 4f, -4f, 6f);
        _overheat = Math.Clamp(_overheat + 30f, 0f, 100f);
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство экономики",
            "Запущен большой стимул: экономика резко разогнана."));
        AddLog("Большой стимул: рост разогнан, перегрев подскочил.");
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < TickInterval)
            return;

        var dt = _accum;
        _accum = 0f;

        if (_stimulusPending && _senate.OfficesVacant)
        {
            _stimulusPending = false;
            BeginStimulus();
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

        PruneProjects();
        PruneRoutes();

        TickCycle(gameHours);
        TickProjects(gameHours);
        TickRoutes(gameHours);

        if (_program != null && _progress < 100f)
        {
            var perHour = 100f / (SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour);
            _progress = Math.Clamp(_progress + perHour * gameHours, 0f, 100f);
        }
    }

    private void TickCycle(float gameHours)
    {
        var cfg = PolicyConfig(_policy);

        var overheatDrag = _overheat >= 70f ? -(_overheat - 70f) / 10f : 0f;
        // The Finance ministry's key rate also steers growth (high rate cools, cheap rate fans).
        var rateDrag = _finance.RateDrag();
        _growth = Math.Clamp(_growth + (cfg.Growth + overheatDrag - rateDrag) * gameHours, -4f, 6f);

        var rise = cfg.Overheat + MathF.Max(0f, _growth - 2f) * 0.5f;
        if (rise > 0f)
            rise *= _senate.StatPressureScale("SWGlobalEconomy", 1f);
        _overheat = Math.Clamp(_overheat + rise * gameHours, 0f, 100f);

        if (_overheat >= 95f)
        {
            _growth = -3f;
            _overheat = 40f;
            RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство экономики",
                "Экономика перегрелась — обвал! Республика входит в рецессию."));
            AddLog("Пузырь лопнул: обвал и рецессия.");
        }

        // Growth (or its absence) compounds onto the sectors.
        NudgeSectors("SWGlobalEconomy", _growth * 0.15f * gameHours);
        NudgeSectors("SWGlobalIndustry", _growth * 0.12f * gameHours);
        _senate.AddPolicy("SWStatEmployment", _growth * 0.05f * gameHours);
    }

    private void NudgeSectors(string globalId, float delta)
    {
        if (delta == 0f)
            return;

        foreach (var (sectorId, _) in _senate.SectorHolders)
            _senate.AddSectorBias(sectorId, globalId, delta);
    }

    // --- Megaprojects ---

    public bool LaunchProject(string projectId, string target)
    {
        if (!_proto.TryIndex<SWMegaprojectPrototype>(projectId, out var proto))
            return false;
        if (_projects.Count >= MaxProjects)
            return false;

        string? sectorId = null;
        if (proto.Target == SWOpTarget.Sector)
        {
            if (!_senate.IsClaimed(target))
                return false;
            sectorId = target;
        }

        if (_dilemma.GetMinistryBudget(EconomyMinistryId) < proto.CostPerPhase)
            return false;

        _dilemma.SpendMinistry(EconomyMinistryId, proto.CostPerPhase);
        _projects.Add(new SWActiveMegaproject
        {
            ProtoId = projectId,
            SectorId = sectorId,
            Phase = 0,
            PhaseLeft = proto.PhaseHours,
            Stalled = false,
        });

        AddLog($"Заложен мегапроект «{proto.Name}».");
        return true;
    }

    public bool CancelProject(int index)
    {
        if (index < 0 || index >= _projects.Count)
            return false;

        var name = _proto.TryIndex<SWMegaprojectPrototype>(_projects[index].ProtoId, out var p) ? p.Name : "проект";
        _projects.RemoveAt(index);
        AddLog($"Мегапроект «{name}» свёрнут.");
        return true;
    }

    private void TickProjects(float gameHours)
    {
        for (var i = _projects.Count - 1; i >= 0; i--)
        {
            var project = _projects[i];
            if (!_proto.TryIndex<SWMegaprojectPrototype>(project.ProtoId, out var proto))
            {
                _projects.RemoveAt(i);
                continue;
            }

            if (project.Stalled)
            {
                // Waiting on funds to start the next phase.
                if (_dilemma.GetMinistryBudget(EconomyMinistryId) >= proto.CostPerPhase
                    && _dilemma.SpendMinistry(EconomyMinistryId, proto.CostPerPhase))
                {
                    project.Stalled = false;
                    project.PhaseLeft = proto.PhaseHours;
                }
                continue;
            }

            project.PhaseLeft -= gameHours;
            if (project.PhaseLeft > 0f)
                continue;

            project.Phase++;
            if (project.Phase >= proto.Phases)
            {
                CompleteProject(proto, project.SectorId);
                _projects.RemoveAt(i);
                continue;
            }

            // Fund the next phase, else stall until the budget recovers.
            if (_dilemma.GetMinistryBudget(EconomyMinistryId) >= proto.CostPerPhase
                && _dilemma.SpendMinistry(EconomyMinistryId, proto.CostPerPhase))
                project.PhaseLeft = proto.PhaseHours;
            else
                project.Stalled = true;
        }
    }

    private void CompleteProject(SWMegaprojectPrototype proto, string? sectorId)
    {
        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta);

        if (sectorId != null && _senate.IsClaimed(sectorId))
        {
            foreach (var (global, delta) in proto.SectorEffects)
                _senate.AddSectorBias(sectorId, global, delta);
        }

        if (proto.GrowthDelta != 0f)
            _growth = Math.Clamp(_growth + proto.GrowthDelta, -4f, 6f);
        if (proto.OverheatDelta != 0f)
            _overheat = Math.Clamp(_overheat + proto.OverheatDelta, 0f, 100f);

        var where = sectorId != null && _proto.TryIndex<SWSectorPrototype>(sectorId, out var sp) ? sp.Name : "Республики";
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Министерство экономики",
            $"Мегапроект «{proto.Name}» завершён в интересах {where}."));
        AddLog($"Мегапроект «{proto.Name}» завершён.");
    }

    private void PruneProjects()
    {
        for (var i = _projects.Count - 1; i >= 0; i--)
        {
            var p = _projects[i];
            if (p.SectorId != null && !_senate.IsClaimed(p.SectorId))
            {
                var name = _proto.TryIndex<SWMegaprojectPrototype>(p.ProtoId, out var proto) ? proto.Name : "проект";
                _projects.RemoveAt(i);
                AddLog($"Мегапроект «{name}» отменён: сектор оставлен.");
            }
        }
    }

    // --- Trade routes ---

    public bool AddRoute(string aId, string bId)
    {
        if (aId == bId || !_senate.IsClaimed(aId) || !_senate.IsClaimed(bId))
            return false;
        if (_routes.Count >= MaxRoutes)
            return false;
        if (_routes.Any(r => (r.A == aId && r.B == bId) || (r.A == bId && r.B == aId)))
            return false;
        if (!_dilemma.SpendMinistry(EconomyMinistryId, RouteSetupCost))
            return false;

        _routes.Add(new SWTradeRoute { Id = _nextRouteId++, A = aId, B = bId });
        var an = _proto.TryIndex<SWSectorPrototype>(aId, out var ap) ? ap.Name : aId;
        var bn = _proto.TryIndex<SWSectorPrototype>(bId, out var bp) ? bp.Name : bId;
        AddLog($"Открыт гиперпуть {an} — {bn}.");
        return true;
    }

    public bool RemoveRoute(int routeId)
    {
        var route = _routes.FirstOrDefault(r => r.Id == routeId);
        if (route == null)
            return false;

        _routes.Remove(route);
        return true;
    }

    private bool RouteDisrupted(SWTradeRoute route) =>
        SectorSecurity(route.A) < RouteSecurityFloor || SectorSecurity(route.B) < RouteSecurityFloor;

    private float SectorSecurity(string sectorId) =>
        _senate.SectorGlobals.TryGetValue(sectorId, out var g) && g.TryGetValue("SWGlobalSecurity", out var s) ? s : 50f;

    private void TickRoutes(float gameHours)
    {
        if (_routes.Count == 0)
        {
            _routeUpkeepAccum = 0f;
            return;
        }

        foreach (var route in _routes)
        {
            if (RouteDisrupted(route))
                continue;

            _senate.AddSectorBias(route.A, "SWGlobalEconomy", RouteEconomyBonus * gameHours);
            _senate.AddSectorBias(route.B, "SWGlobalEconomy", RouteEconomyBonus * gameHours);
            _senate.AddSectorBias(route.A, "SWGlobalIndustry", RouteIndustryBonus * gameHours);
            _senate.AddSectorBias(route.B, "SWGlobalIndustry", RouteIndustryBonus * gameHours);
        }

        _routeUpkeepAccum += _routes.Count * RouteUpkeepPerHour * gameHours;
        if (_routeUpkeepAccum < 1f)
            return;

        var due = (int) _routeUpkeepAccum;
        _routeUpkeepAccum -= due;
        _dilemma.SpendMinistry(EconomyMinistryId, due);
    }

    private void PruneRoutes()
    {
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            if (!_senate.IsClaimed(_routes[i].A) || !_senate.IsClaimed(_routes[i].B))
                _routes.RemoveAt(i);
        }
    }

    private void ApplyProgram()
    {
        if (_program == null || !_proto.TryIndex<SWEconomyProgramPrototype>(_program, out var proto))
            return;

        var scale = _progress / 100f;
        if (scale <= 0f)
            return;

        foreach (var (sub, delta) in proto.PolicyEffects)
            _senate.AddPolicy(sub, delta * scale);

        if (proto.GrowthDelta != 0f)
            _growth = Math.Clamp(_growth + proto.GrowthDelta * scale, -4f, 6f);
        if (proto.OverheatDelta != 0f)
            _overheat = Math.Clamp(_overheat + proto.OverheatDelta * scale, 0f, 100f);
    }

    // Economy requests ease in industrial, growing sectors and bite during recession.
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var cost = 1f;
        var effect = 1f;

        if (sectorId != null && _senate.SectorGlobals.TryGetValue(sectorId, out var g)
            && g.TryGetValue("SWGlobalIndustry", out var ind) && ind >= 60f)
        {
            cost *= 0.85f;
            effect *= 1.15f;
        }

        if (Phase == SWEconomyPhase.Recession)
        {
            cost *= 1.25f;
            effect *= 0.85f;
        }
        else if (Phase == SWEconomyPhase.Growth)
        {
            effect *= 1.1f;
        }

        return (cost, effect);
    }

    public void OnSectorEngagement(string? sectorId)
    {
        if (sectorId == null)
            return;

        _senate.AddSectorBias(sectorId, "SWGlobalEconomy", 2f);
    }

    // --- UI builders ---

    public List<SWEconSectorEntry> BuildSectors()
    {
        var list = new List<SWEconSectorEntry>();
        foreach (var (sectorId, _) in _senate.SectorHolders)
        {
            if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var proto))
                continue;

            var econ = _senate.SectorGlobals.TryGetValue(sectorId, out var g) && g.TryGetValue("SWGlobalEconomy", out var e) ? (int) MathF.Round(e) : 50;
            var ind = g != null && g.TryGetValue("SWGlobalIndustry", out var iv) ? (int) MathF.Round(iv) : 50;

            list.Add(new SWEconSectorEntry { Id = sectorId, Name = proto.Name, Color = proto.Color, Economy = econ, Industry = ind });
        }

        list.Sort((a, b) => a.Economy.CompareTo(b.Economy));
        return list;
    }

    public List<SWMegaprojectEntry> BuildProjects()
    {
        var budget = _dilemma.GetMinistryBudget(EconomyMinistryId);
        var slotsFree = _projects.Count < MaxProjects;

        var list = new List<SWMegaprojectEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWMegaprojectPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWMegaprojectEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Phases = proto.Phases,
                CostPerPhase = proto.CostPerPhase,
                PhaseHours = proto.PhaseHours,
                Target = proto.Target,
                Effects = ProjectEffects(proto),
            };

            if (!slotsFree)
                entry.StatusLabel = "нет свободных слотов";
            else if (budget < proto.CostPerPhase)
                entry.StatusLabel = "не хватает бюджета на фазу";
            else
                entry.Available = true;

            list.Add(entry);
        }

        return list;
    }

    public List<SWActiveMegaprojectEntry> BuildActiveProjects()
    {
        var list = new List<SWActiveMegaprojectEntry>();
        for (var i = 0; i < _projects.Count; i++)
        {
            var p = _projects[i];
            var name = _proto.TryIndex<SWMegaprojectPrototype>(p.ProtoId, out var proto) ? proto.Name : p.ProtoId;
            var detail = p.SectorId != null && _proto.TryIndex<SWSectorPrototype>(p.SectorId, out var sp) ? sp.Name : "Республика";
            list.Add(new SWActiveMegaprojectEntry
            {
                Index = i,
                Name = name,
                Detail = detail,
                Phase = Math.Min(p.Phase + 1, proto?.Phases ?? p.Phase + 1),
                TotalPhases = proto?.Phases ?? 1,
                PhaseLeft = (int) MathF.Ceiling(MathF.Max(0f, p.PhaseLeft)),
                Stalled = p.Stalled,
            });
        }

        return list;
    }

    public List<SWTradeRouteEntry> BuildRoutes()
    {
        var list = new List<SWTradeRouteEntry>();
        foreach (var route in _routes)
        {
            var an = _proto.TryIndex<SWSectorPrototype>(route.A, out var ap) ? ap.Name : route.A;
            var bn = _proto.TryIndex<SWSectorPrototype>(route.B, out var bp) ? bp.Name : route.B;
            var disrupted = RouteDisrupted(route);
            list.Add(new SWTradeRouteEntry
            {
                Id = route.Id,
                AName = an,
                BName = bn,
                Active = !disrupted,
                StatusLabel = disrupted ? "под угрозой пиратов" : $"+{RouteEconomyBonus:0.0} торговли/ч",
            });
        }

        return list;
    }

    public List<SWDefenseProgramEntry> BuildPrograms()
    {
        var list = new List<SWDefenseProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWEconomyProgramPrototype>().OrderBy(p => p.Order))
        {
            var entry = new SWDefenseProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Selected = proto.ID == _program,
                Effects = PolicyEffectEntries(proto.PolicyEffects),
            };

            if (proto.GrowthDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Рост", Color = Color.FromHex("#43D16A"), Delta = proto.GrowthDelta });
            if (proto.OverheatDelta != 0f)
                entry.Effects.Add(new SWBillEffectEntry { StatName = "Перегрев", Color = Color.FromHex("#E0A23B"), Delta = proto.OverheatDelta });

            list.Add(entry);
        }

        return list;
    }

    private List<SWBillEffectEntry> ProjectEffects(SWMegaprojectPrototype proto)
    {
        var list = PolicyEffectEntries(proto.PolicyEffects);
        foreach (var (global, delta) in proto.SectorEffects)
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

        if (proto.GrowthDelta != 0f)
            list.Add(new SWBillEffectEntry { StatName = "Рост", Color = Color.FromHex("#43D16A"), Delta = proto.GrowthDelta });
        if (proto.OverheatDelta != 0f)
            list.Add(new SWBillEffectEntry { StatName = "Перегрев", Color = Color.FromHex("#E0A23B"), Delta = proto.OverheatDelta });

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

public sealed class SWActiveMegaproject
{
    public string ProtoId = string.Empty;
    public string? SectorId;
    public int Phase;
    public float PhaseLeft;
    public bool Stalled;
}

public sealed class SWTradeRoute
{
    public int Id;
    public string A = string.Empty;
    public string B = string.Empty;
}
