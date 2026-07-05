using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Crises;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateCrisisSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    // All rates below are per GAME MINUTE - crises pace the same at any game speed and freeze overnight.
    private const float TickGameMinutes = 1f;
    private const float SpawnChance = 0.25f;
    // Natural wind-down once the root cause is fixed - deliberately slow (≈ a third of the growth rate)
    // so the acute phase lingers and solutions/aid/laws stay worth applying.
    private const float SelfDecay = 1.5f;
    // Pressure → stat-depression scaling. Equilibrium drag ≈ weight * (severity/100) * PressureToDrag.
    private const float PressureScale = 0.2f;
    private const float PressureToDrag = 20f;
    private const int LogLimit = 8;

    // Escalation: a crisis sitting at/above this severity for SpreadAfterTicks pulls in another sector.
    private const float EscalateThreshold = 70f;
    private const int SpreadAfterTicks = 3;
    // Spill pressure onto non-affected sectors once a crisis is inter-/republic-wide.
    private const float SpillInter = 0.25f;
    private const float SpillRepublic = 0.5f;
    // Aid sent by an outside sector's senator from their own budget.
    private const int AidCost = 2500;
    private const float AidRelief = 12f;

    private readonly List<SWActiveCrisis> _crises = new();
    private int _nextId = 1;
    private float _gameAccum;

    public IReadOnlyList<SWActiveCrisis> Crises => _crises;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateBillResolvedEvent>(OnBillResolved);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _crises.Clear();
        _nextId = 1;
    }

    private void OnBillResolved(SWSenateBillResolvedEvent ev)
    {
        if (!ev.Passed || ev.EmergencyCrisisId <= 0)
            return;

        var crisis = _crises.FirstOrDefault(c => c.Id == ev.EmergencyCrisisId);
        if (crisis == null)
            return;

        crisis.Severity = Math.Max(0f, crisis.Severity - ev.EmergencyRelief);
        Log(crisis, "Принят экстренный закон - тяжесть снижена.");
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        // Advance on game time only (frozen during the overnight gap), so pacing is speed-independent.
        if (!_time.DayActive)
            return;

        _gameAccum += frameTime * (60f / SWSenateTimeSystem.RealSecondsPerGameHour);
        if (_gameAccum < TickGameMinutes)
            return;

        var elapsed = _gameAccum;
        _gameAccum = 0f;

        TrySpawn();
        Tick(elapsed);
    }

    private void TrySpawn()
    {
        foreach (var proto in _proto.EnumeratePrototypes<SWCrisisPrototype>())
        {
            if (proto.Conditions.Count == 0)
                continue;
            // One crisis per prototype at a time; further sectors join the existing one via escalation.
            if (_crises.Any(c => c.CrisisId == proto.ID))
                continue;

            if (proto.Republic)
            {
                if (!ConditionsMet(proto, string.Empty) || !_random.Prob(SpawnChance))
                    continue;
                SpawnRepublic(proto);
                continue;
            }

            foreach (var (sectorId, _) in _senate.SectorHolders)
            {
                if (!ConditionsMet(proto, sectorId) || !_random.Prob(SpawnChance))
                    continue;

                Spawn(proto, sectorId);
                break;
            }
        }
    }

    private void Spawn(SWCrisisPrototype proto, string sectorId)
    {
        var crisis = new SWActiveCrisis
        {
            Id = _nextId++,
            CrisisId = proto.ID,
            Type = SWCrisisType.Sectoral,
            OriginSector = sectorId,
            Severity = proto.StartSeverity,
        };
        crisis.AffectedSectors.Add(sectorId);
        Log(crisis, $"Кризис разразился в секторе {SectorName(sectorId)}.");
        _crises.Add(crisis);
    }

    private void SpawnRepublic(SWCrisisPrototype proto)
    {
        var crisis = new SWActiveCrisis
        {
            Id = _nextId++,
            CrisisId = proto.ID,
            Type = SWCrisisType.Republic,
            OriginSector = string.Empty,
            Severity = proto.StartSeverity,
        };
        foreach (var (sectorId, _) in _senate.SectorHolders)
            crisis.AffectedSectors.Add(sectorId);

        Log(crisis, "Кризис разразился на уровне Республики.");
        _crises.Add(crisis);
    }

    private bool RootActive(SWActiveCrisis crisis, SWCrisisPrototype proto) =>
        proto.Republic
            ? ConditionsMet(proto, string.Empty)
            : crisis.AffectedSectors.Any(s => ConditionsMet(proto, s));

    private void Tick(float minutes)
    {
        for (var i = _crises.Count - 1; i >= 0; i--)
        {
            var crisis = _crises[i];
            if (!_proto.TryIndex<SWCrisisPrototype>(crisis.CrisisId, out var proto))
            {
                _crises.RemoveAt(i);
                continue;
            }

            var rootActive = RootActive(crisis, proto);
            if (rootActive)
                crisis.Severity = Math.Min(100f, crisis.Severity + proto.SeverityGain * minutes);
            else
                crisis.Severity = Math.Max(0f, crisis.Severity - SelfDecay * minutes);

            Escalate(crisis, proto);
            ApplyPressure(crisis, proto, minutes);

            // Exit requires both: the acute phase is over (severity 0) AND the root cause is fixed.
            if (crisis.Severity <= 0f && !rootActive)
            {
                Log(crisis, "Кризис преодолён.");
                _crises.RemoveAt(i);
            }
        }
    }

    private void Escalate(SWActiveCrisis crisis, SWCrisisPrototype proto)
    {
        var active = _senate.SectorHolders.Keys.ToList();

        // (b) natural merge: other active sectors that independently hit the conditions are pulled in.
        foreach (var sectorId in active)
        {
            if (crisis.AffectedSectors.Contains(sectorId))
                continue;
            if (ConditionsMet(proto, sectorId))
            {
                crisis.AffectedSectors.Add(sectorId);
                Log(crisis, $"Кризис охватил сектор {SectorName(sectorId)}.");
            }
        }

        // (a) forced spread: high severity sustained for a while spills into one more active sector.
        if (crisis.Severity >= EscalateThreshold)
        {
            if (++crisis.HighTicks >= SpreadAfterTicks)
            {
                crisis.HighTicks = 0;
                var candidates = active.Where(s => !crisis.AffectedSectors.Contains(s)).ToList();
                if (candidates.Count > 0)
                {
                    var target = _random.Pick(candidates);
                    crisis.AffectedSectors.Add(target);
                    Log(crisis, $"Кризис распространился на сектор {SectorName(target)}.");
                }
            }
        }
        else
        {
            crisis.HighTicks = 0;
        }

        // Type follows coverage: >50% of active sectors = Republican, 2+ = Inter, else Sectoral.
        var activeCount = Math.Max(1, active.Count);
        var coverage = (float) crisis.AffectedSectors.Count / activeCount;
        var newType = coverage > 0.5f
            ? SWCrisisType.Republic
            : crisis.AffectedSectors.Count >= 2
                ? SWCrisisType.Inter
                : SWCrisisType.Sectoral;

        if (newType != crisis.Type)
        {
            crisis.Type = newType;
            Log(crisis, $"Эскалация: кризис стал {TypeLabel(newType).ToLowerInvariant()}.");
        }
    }

    private void ApplyPressure(SWActiveCrisis crisis, SWCrisisPrototype proto, float minutes)
    {
        if (crisis.Severity <= 0f)
            return;

        var factor = crisis.Severity / 100f * PressureScale * minutes;
        var spill = crisis.Type switch
        {
            SWCrisisType.Inter => SpillInter,
            SWCrisisType.Republic => SpillRepublic,
            _ => 0f,
        };

        foreach (var sectorId in _senate.SectorGlobals.Keys)
        {
            var affected = crisis.AffectedSectors.Contains(sectorId);
            if (!affected && spill <= 0f)
                continue;

            var mult = affected ? 1f : spill;
            foreach (var (global, weight) in proto.Pressure)
                _senate.NudgeSectorGlobal(sectorId, global, -weight * factor * mult);
        }
    }

    public bool AidCrisis(int crisisId, string helperSector)
    {
        var crisis = _crises.FirstOrDefault(c => c.Id == crisisId);
        if (crisis == null
            || helperSector == crisis.OriginSector
            || crisis.AffectedSectors.Contains(helperSector)
            || !_senate.SectorGlobals.ContainsKey(helperSector))
            return false;

        if (!_budget.Spend(helperSector, AidCost))
            return false;

        crisis.Severity = Math.Max(0f, crisis.Severity - AidRelief);
        var holder = _senate.GetSectorHolder(helperSector) ?? SectorName(helperSector);
        Log(crisis, $"{holder} ({SectorName(helperSector)}): помощь пострадавшим.");
        return true;
    }

    public bool ApplySolution(int crisisId, int index, string sectorId)
    {
        var crisis = _crises.FirstOrDefault(c => c.Id == crisisId);
        if (crisis == null || !crisis.AffectedSectors.Contains(sectorId))
            return false;
        if (!_proto.TryIndex<SWCrisisPrototype>(crisis.CrisisId, out var proto)
            || index < 0
            || index >= proto.Solutions.Count)
            return false;

        var solution = proto.Solutions[index];
        if (solution.Cost > 0 && !_budget.Spend(sectorId, solution.Cost))
            return false;

        crisis.Severity = Math.Max(0f, crisis.Severity - solution.Relief);
        foreach (var (global, delta) in solution.SectorEffects)
            _senate.AddSectorBias(sectorId, global, delta);

        var holder = _senate.GetSectorHolder(sectorId) ?? SectorName(sectorId);
        Log(crisis, $"{holder}: {solution.Label}.");
        return true;
    }

    public bool AdminSpawn(string? sectorId, string? crisisId)
    {
        SWCrisisPrototype? proto;
        if (crisisId != null)
        {
            if (!_proto.TryIndex(crisisId, out proto))
                return false;
        }
        else
        {
            var pool = _proto.EnumeratePrototypes<SWCrisisPrototype>().ToList();
            if (pool.Count == 0)
                return false;
            proto = _random.Pick(pool);
        }

        // Republic crises need no sector - they cover the whole Republic.
        if (proto.Republic)
        {
            SpawnRepublic(proto);
            return true;
        }

        // Sectoral: an explicit sector must be valid; if none was given, pick an active one for testing.
        var sector = sectorId;
        if (sector != null && !_senate.SectorGlobals.ContainsKey(sector))
            return false;

        if (sector == null)
        {
            var pool = _senate.SectorHolders.Keys.ToList();
            if (pool.Count == 0)
                pool = _senate.SectorGlobals.Keys.ToList();
            if (pool.Count == 0)
                return false;
            sector = _random.Pick(pool);
        }

        Spawn(proto, sector);
        return true;
    }

    public List<SWCrisisEntry> BuildCrises(string? viewerSector, bool viewerChancellor)
    {
        var list = new List<SWCrisisEntry>();
        foreach (var crisis in _crises)
        {
            if (!_proto.TryIndex<SWCrisisPrototype>(crisis.CrisisId, out var proto))
                continue;

            var rootActive = RootActive(crisis, proto);
            var canResolve = viewerSector != null && crisis.AffectedSectors.Contains(viewerSector);
            var canAid = viewerSector != null
                && viewerSector != crisis.OriginSector
                && !crisis.AffectedSectors.Contains(viewerSector);
            var canPropose = proto.EmergencyLaws.Count > 0
                && (viewerChancellor || (viewerSector != null && crisis.AffectedSectors.Contains(viewerSector)));

            var entry = new SWCrisisEntry
            {
                Id = crisis.Id,
                Name = proto.Name,
                Description = proto.Description,
                TypeLabel = TypeLabel(crisis.Type),
                AffectedLabel = string.Join(", ", crisis.AffectedSectors.Select(SectorName)),
                Severity = crisis.Severity,
                SeverityLabel = $"Тяжесть: {crisis.Severity:0}/100 ({(rootActive ? "растёт" : "спадает")})",
                Impact = BuildImpact(crisis, proto),
                EscalationThreat = EscalationThreat(crisis),
                ExitProgress = $"Выход: тяжесть {crisis.Severity:0} → 0; первопричина {(rootActive ? "ещё активна" : "устранена")}.",
                CanResolve = canResolve,
                CanAid = canAid,
                AidCost = AidCost,
                AidAffordable = viewerSector != null && _budget.Available(viewerSector) >= AidCost,
                CanPropose = canPropose,
                Log = crisis.Log.ToList(),
            };

            for (var i = 0; i < proto.EmergencyLaws.Count; i++)
            {
                var law = proto.EmergencyLaws[i];
                entry.Laws.Add(new SWCrisisLawEntry
                {
                    Index = i,
                    Name = law.Name,
                    Description = law.Description,
                    Relief = law.Relief,
                    Effects = PolicyEffectEntries(law.PolicyEffects),
                });
            }

            var available = viewerSector != null ? _budget.Available(viewerSector) : 0;
            foreach (var solution in proto.Solutions)
            {
                entry.Solutions.Add(new SWCrisisSolutionEntry
                {
                    Label = solution.Label,
                    Description = solution.Description,
                    Cost = solution.Cost,
                    Relief = solution.Relief,
                    Affordable = solution.Cost <= 0 || available >= solution.Cost,
                    Effects = BiasEffects(solution.SectorEffects),
                });
            }

            list.Add(entry);
        }

        list.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return list;
    }

    private List<SWBillEffectEntry> BuildImpact(SWActiveCrisis crisis, SWCrisisPrototype proto)
    {
        var list = new List<SWBillEffectEntry>();
        var drag = crisis.Severity / 100f * PressureToDrag;
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (proto.Pressure.TryGetValue(global.ID, out var weight))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = -weight * drag });
        }

        return list;
    }

    private List<SWBillEffectEntry> BiasEffects(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta });
        }

        return list;
    }

    private string EscalationThreat(SWActiveCrisis crisis) => crisis.Type switch
    {
        SWCrisisType.Sectoral => crisis.Severity >= EscalateThreshold
            ? "Высокая тяжесть - кризис может распространиться на другие сектора."
            : "Локализован в одном секторе; рост тяжести грозит распространением.",
        SWCrisisType.Inter => "Межсекторальный: при охвате больше половины секторов станет республиканским.",
        SWCrisisType.Republic => "Республиканский масштаб - давит на все сектора Республики.",
        _ => string.Empty,
    };

    private bool ConditionsMet(SWCrisisPrototype proto, string sectorId)
    {
        if (proto.Conditions.Count == 0)
            return false;

        foreach (var cond in proto.Conditions)
        {
            var value = proto.Republic ? RepublicGlobal(cond.Stat) : SectorGlobal(sectorId, cond.Stat);
            var ok = cond.Comparison == SWCrisisComparison.Below
                ? value < cond.Threshold
                : value > cond.Threshold;
            if (!ok)
                return false;
        }

        return true;
    }

    public bool ProposeLaw(int crisisId, int lawIndex, string author, string? sectorOwner, bool isChancellor)
    {
        var crisis = _crises.FirstOrDefault(c => c.Id == crisisId);
        if (crisis == null
            || !_proto.TryIndex<SWCrisisPrototype>(crisis.CrisisId, out var proto)
            || lawIndex < 0
            || lawIndex >= proto.EmergencyLaws.Count)
            return false;

        var allowed = isChancellor || (sectorOwner != null && crisis.AffectedSectors.Contains(sectorOwner));
        if (!allowed || _senate.HasActiveBill(author))
            return false;

        var law = proto.EmergencyLaws[lawIndex];
        var effects = new Dictionary<string, float>();
        foreach (var (sub, delta) in law.PolicyEffects)
        {
            if (_proto.HasIndex<SWStatPrototype>(sub))
                effects[sub] = delta;
        }

        if (effects.Count == 0)
            return false;

        _senate.SubmitEmergencyBill(law.Name, law.Description, author, effects, crisis.Id, law.Relief);
        Log(crisis, $"{author}: внесён экстренный закон «{law.Name}».");
        return true;
    }

    private float RepublicGlobal(string stat) =>
        _senate.GetRepublicGlobals().TryGetValue(stat, out var value) ? value : 50f;

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

    private float SectorGlobal(string sectorId, string stat) =>
        _senate.SectorGlobals.TryGetValue(sectorId, out var globals) && globals.TryGetValue(stat, out var value)
            ? value
            : 50f;

    private void Log(SWActiveCrisis crisis, string line)
    {
        crisis.Log.Add(line);
        if (crisis.Log.Count > LogLimit)
            crisis.Log.RemoveAt(0);
    }

    private string SectorName(string id) =>
        _proto.TryIndex<SWSectorPrototype>(id, out var proto) ? proto.Name : id;

    private static string TypeLabel(SWCrisisType type) => type switch
    {
        SWCrisisType.Sectoral => "Секторальный",
        SWCrisisType.Inter => "Межсекторальный",
        SWCrisisType.Republic => "Республиканский",
        _ => "-",
    };
}

public sealed class SWActiveCrisis
{
    public int Id;
    public string CrisisId = string.Empty;
    public SWCrisisType Type;
    public string OriginSector = string.Empty;
    public readonly HashSet<string> AffectedSectors = new();
    public float Severity;
    public int HighTicks;
    public readonly List<string> Log = new();
}
