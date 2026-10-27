using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Content.Shared._SW._Senate.Bills;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWSenateStateSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateCardSystem _cards = default!;

    private readonly Dictionary<string, float> _republicSubStats = new();
    private readonly Dictionary<string, Dictionary<string, float>> _sectorGlobals = new();
    private readonly Dictionary<string, Dictionary<string, float>> _sectorBias = new();
    private readonly Dictionary<(string Sector, string Global), float> _supplyPenalties = new();
    private readonly Dictionary<(string Sector, string Global), float> _programEffects = new();
    private readonly Dictionary<string, Dictionary<string, float>> _inheritance = new();
    private readonly Dictionary<string, float> _sectorWeight = new();
    private readonly Dictionary<string, string> _sectorHolders = new();
    private readonly List<SWSenateBill> _bills = new();
    private readonly List<SWSenateSession> _sessions = new();
    private readonly HashSet<int> _announcedSessions = new();
    private int _nextBillId = 1;
    private int _nextSessionId = 1;
    private int _scheduleDay = 1;

    private const float NeutralStat = 50f;
    private const float DriftRate = 0.01f;
    // Carry-over decays more slowly; new decisions retain the normal response speed.
    public const float InheritanceHalfLifeSeconds = 28f * 60f;
    private const float UpdateInterval = 5f;

    private float _accumulator;

    public string? Chancellor { get; private set; }
    public string? Speaker { get; private set; }
    public string? ChancellorElect => _chancellorElect;

    private string? _chancellorElect;
    private bool _electionForced;
    private float _vacancyHours;
    private const float ElectionDelayHours = 1f;
    private const float EmergencySessionDelayHours = 2f;

    public IReadOnlyDictionary<string, Dictionary<string, float>> SectorGlobals => _sectorGlobals;
    public IReadOnlyDictionary<string, float> SubStats => _republicSubStats;
    public IReadOnlyDictionary<string, string> SectorHolders => _sectorHolders;
    public IReadOnlyList<SWSenateBill> Bills => _bills;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<SWSenateDayClosedEvent>(ev =>
        {
            if (!ev.FinalDay)
                ResolveExpiredSessions();
        });
        InitState();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Only actual sector statistics carry over. A new legislature starts from default policy.
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>())
            _republicSubStats[sub.ID] = Math.Clamp(sub.Default, sub.Min, sub.Max);
        foreach (var bias in _sectorBias.Values)
            bias.Clear();
        _supplyPenalties.Clear();
        _programEffects.Clear();
        ClearAllSituationalContributions();
        _inheritance.Clear();
        foreach (var (sector, globals) in _sectorGlobals)
            _inheritance[sector] = globals.ToDictionary(g => g.Key, g => g.Value - GetSectorTarget(sector, g.Key));
        _accumulator = 0f;
        _sectorHolders.Clear();
        Chancellor = null;
        Speaker = null;
        _barredChancellors.Clear();
        _sessions.Clear();
        _announcedSessions.Clear();
        _bills.Clear();
        _nextBillId = 1;
        _nextSessionId = 1;
        _scheduleDay = _time.Day;
        _chancellorElect = null;
        _electionForced = false;
        _vacancyHours = 0f;
    }

    private void InitState()
    {
        _republicSubStats.Clear();
        _sectorGlobals.Clear();
        _sectorBias.Clear();
        _supplyPenalties.Clear();
        _programEffects.Clear();
        ClearAllSituationalContributions();
        _inheritance.Clear();
        _sectorWeight.Clear();
        _sectorHolders.Clear();
        _bills.Clear();
        _sessions.Clear();
        _announcedSessions.Clear();
        _nextBillId = 1;
        _nextSessionId = 1;
        _scheduleDay = _time.Day;
        Chancellor = null;
        Speaker = null;
        _chancellorElect = null;
        _electionForced = false;
        _vacancyHours = 0f;

        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>())
            _republicSubStats[sub.ID] = Math.Clamp(sub.Default, sub.Min, sub.Max);

        foreach (var sector in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            var globals = new Dictionary<string, float>();
            var bias = new Dictionary<string, float>();
            foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
            {
                var start = sector.StartingStats.TryGetValue(global.ID, out var value)
                    ? Math.Clamp(value, global.Min, global.Max)
                    : NeutralStat;
                globals[global.ID] = start;
                bias[global.ID] = 0f;
            }

            _sectorGlobals[sector.ID] = globals;
            _sectorBias[sector.ID] = bias;
            _sectorWeight[sector.ID] = sector.Weight > 0 ? sector.Weight : 1f;
        }
    }

    private IEnumerable<SWStatPrototype> SubsOf(string globalId) =>
        _proto.EnumeratePrototypes<SWStatPrototype>().Where(s => s.Global == globalId);

    private Color SubColor(SWStatPrototype sub) =>
        _proto.TryIndex(sub.Global, out var global) ? global.Color : Color.White;

    public float GetPolicyScore(string globalId)
    {
        var sum = 0f;
        var weight = 0f;
        foreach (var sub in SubsOf(globalId))
        {
            if (!_republicSubStats.TryGetValue(sub.ID, out var value))
                continue;
            sum += value * sub.Weight;
            weight += sub.Weight;
        }

        return weight > 0 ? sum / weight : NeutralStat;
    }

    public Dictionary<string, float> GetPolicyScores()
    {
        var result = new Dictionary<string, float>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
            result[global.ID] = GetPolicyScore(global.ID);

        return result;
    }

    public float GetSectorTarget(string sectorId, string globalId)
    {
        if (!_proto.TryIndex<SWSectorPrototype>(sectorId, out var sector)
            || !_proto.TryIndex<SWGlobalStatPrototype>(globalId, out var global))
            return NeutralStat;

        var baseline = sector.StartingStats.TryGetValue(globalId, out var start) ? start : NeutralStat;
        var subs = SubsOf(globalId).ToList();
        var weight = subs.Sum(s => s.Weight);
        var defaultPolicy = weight > 0f
            ? subs.Sum(s => Math.Clamp(s.Default, s.Min, s.Max) * s.Weight) / weight
            : NeutralStat;
        var bias = _sectorBias.TryGetValue(sectorId, out var offsets) && offsets.TryGetValue(globalId, out var b) ? b : 0f;
        var supply = _supplyPenalties.GetValueOrDefault((sectorId, globalId));
        var programs = _programEffects.GetValueOrDefault((sectorId, globalId));
        return Math.Clamp(baseline + GetPolicyScore(globalId) - defaultPolicy + bias + supply + programs
            + GetSituationalGlobalDelta(sectorId, globalId), global.Min, global.Max);
    }

    /// <summary>Replace a temporary supply offset, including fulfilled-service benefits; never accumulate policy bias.</summary>
    public void SetSupplyPenalty(string sectorId, string globalId, float offset)
    {
        if (!float.IsFinite(offset) || !_sectorGlobals.ContainsKey(sectorId)
            || !_proto.HasIndex<SWGlobalStatPrototype>(globalId))
            return;
        var key = (sectorId, globalId);
        if (Math.Abs(offset) < .001f)
            _supplyPenalties.Remove(key);
        else
            _supplyPenalties[key] = Math.Clamp(offset, -20f, 6f);
    }

    public void ClearSupplyPenalties() => _supplyPenalties.Clear();

    /// <summary>Replace the contribution of completed projects; never accumulate it on each economy tick.</summary>
    public void SetProgramEffect(string sectorId, string globalId, float offset)
    {
        if (!float.IsFinite(offset) || !_sectorGlobals.ContainsKey(sectorId)
            || !_proto.HasIndex<SWGlobalStatPrototype>(globalId))
            return;
        var key = (sectorId, globalId);
        if (Math.Abs(offset) < .001f)
            _programEffects.Remove(key);
        else
            _programEffects[key] = Math.Clamp(offset, -100f, 100f);
    }

    public void ClearProgramEffects() => _programEffects.Clear();

    public float WeightedSectorAverage(Func<string, float> value)
    {
        var weight = _sectorWeight.Values.Sum();
        return weight > 0f ? _sectorWeight.Sum(s => value(s.Key) * s.Value) / weight : 0f;
    }

    public float GetRepublicTarget(string globalId)
    {
        var weight = _sectorWeight.Values.Sum();
        return weight > 0f
            ? _sectorWeight.Sum(s => GetSectorTarget(s.Key, globalId) * s.Value) / weight
            : NeutralStat;
    }

    public Dictionary<string, float> GetRepublicGlobals()
    {
        var result = new Dictionary<string, float>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
        {
            var sum = 0f;
            var weight = 0f;
            foreach (var (sectorId, globals) in _sectorGlobals)
            {
                if (!globals.TryGetValue(global.ID, out var value))
                    continue;
                var w = _sectorWeight.TryGetValue(sectorId, out var sw) ? sw : 1f;
                sum += value * w;
                weight += w;
            }

            result[global.ID] = weight > 0 ? sum / weight : NeutralStat;
        }

        return result;
    }

    public float GetRepublicGlobal(string globalId)
    {
        var sum = 0f;
        var weight = 0f;
        foreach (var (sectorId, globals) in _sectorGlobals)
        {
            if (!globals.TryGetValue(globalId, out var value))
                continue;
            var w = _sectorWeight.TryGetValue(sectorId, out var sw) ? sw : 1f;
            sum += value * w;
            weight += w;
        }

        return weight > 0 ? sum / weight : NeutralStat;
    }

    // Direction-aware multiplier for a per-game-hour accumulation, tied to a governing global's "факт".
    // Weak institutions can double worsening pressure, but do not penalize the base recovery rate.
    // Strong institutions still dampen pressure and improve recovery.
    public float StatPressureScale(string governingGlobalId, float delta)
    {
        var stat = GetRepublicGlobal(governingGlobalId);
        if (delta >= 0f)
            return Math.Clamp(NeutralStat / Math.Max(stat, 1f), 0.4f, 2f);

        return Math.Clamp(stat / NeutralStat, 1f, 1.6f);
    }

    public List<SWGlobalStatEntry> BuildGlobalEntries()
    {
        var actual = GetRepublicGlobals();
        var list = new List<SWGlobalStatEntry>();

        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            var entry = new SWGlobalStatEntry
            {
                Id = global.ID,
                Name = global.Name,
                ShortName = global.ShortName,
                Color = global.Color,
                Policy = GetRepublicTarget(global.ID),
                Actual = actual.TryGetValue(global.ID, out var a) ? a : NeutralStat,
                Min = global.Min,
                Max = global.Max,
            };

            foreach (var sub in SubsOf(global.ID).OrderBy(s => s.Order))
            {
                entry.Subs.Add(new SWStatEntry
                {
                    Id = sub.ID,
                    Name = sub.Name,
                    ShortName = sub.ShortName,
                    Color = global.Color,
                    Value = _republicSubStats.TryGetValue(sub.ID, out var v) ? v : sub.Default,
                    Min = sub.Min,
                    Max = sub.Max,
                });
            }

            list.Add(entry);
        }

        return list;
    }

    public List<SWSectorEntry> BuildSectorEntries(string? onlySector = null)
    {
        return _proto.EnumeratePrototypes<SWSectorPrototype>()
            .Where(p => _sectorGlobals.ContainsKey(p.ID) && (onlySector == null || onlySector == p.ID))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new SWSectorEntry
            {
                Id = p.ID, Name = p.Name, Color = p.Color,
                Claimed = IsClaimed(p.ID), Stats = BuildSectorGlobals(p.ID),
            }).ToList();
    }

    public List<SWStatEntry> BuildSectorGlobals(string sectorId)
    {
        var list = new List<SWStatEntry>();
        if (!_sectorGlobals.TryGetValue(sectorId, out var globals))
            return list;

        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (!globals.TryGetValue(global.ID, out var value))
                continue;

            list.Add(new SWStatEntry
            {
                Id = global.ID,
                Name = global.Name,
                ShortName = global.ShortName,
                Color = global.Color,
                Value = value,
                Min = global.Min,
                Max = global.Max,
            });
        }

        return list;
    }

    public void AddPolicy(string subId, float delta)
    {
        if (!_proto.TryIndex<SWStatPrototype>(subId, out var sub))
            return;

        var value = _republicSubStats.TryGetValue(subId, out var current) ? current : sub.Default;
        _republicSubStats[subId] = Math.Clamp(value + delta, sub.Min, sub.Max);
    }

    public void AddSectorBias(string sectorId, string globalId, float delta)
    {
        if (!_sectorBias.TryGetValue(sectorId, out var bias))
            return;

        var value = bias.TryGetValue(globalId, out var current) ? current : 0f;
        bias[globalId] = Math.Clamp(value + delta, -50f, 50f);
    }

    public void SetPolicy(string subId, float value)
    {
        if (!_proto.TryIndex<SWStatPrototype>(subId, out var sub))
            return;

        _republicSubStats[subId] = Math.Clamp(value, sub.Min, sub.Max);
    }

    public bool SetSectorGlobal(string sectorId, string globalId, float value)
    {
        if (!_sectorGlobals.TryGetValue(sectorId, out var globals) || !_proto.TryIndex<SWGlobalStatPrototype>(globalId, out var global))
            return false;

        globals[globalId] = Math.Clamp(value, global.Min, global.Max);
        return true;
    }

    /// <summary>
    /// Additive push on a sector global (used by the crisis engine for reversible pressure - the drift
    /// pulls the value back up once the pressure stops).
    /// </summary>
    public void NudgeSectorGlobal(string sectorId, string globalId, float delta)
    {
        if (!_sectorGlobals.TryGetValue(sectorId, out var globals)
            || !globals.TryGetValue(globalId, out var value)
            || !_proto.TryIndex<SWGlobalStatPrototype>(globalId, out var global))
            return;

        globals[globalId] = Math.Clamp(value + delta, global.Min, global.Max);
    }

    public void FillAll(float value)
    {
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>())
            _republicSubStats[sub.ID] = Math.Clamp(value, sub.Min, sub.Max);

        foreach (var (_, globals) in _sectorGlobals)
        {
            foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
            {
                if (globals.ContainsKey(global.ID))
                    globals[global.ID] = Math.Clamp(value, global.Min, global.Max);
            }
        }
    }

    public bool IsClaimed(string sectorId) => _sectorHolders.ContainsKey(sectorId);

    public string? GetSectorHolder(string sectorId) =>
        _sectorHolders.TryGetValue(sectorId, out var holder) ? holder : null;

    public bool TryClaim(string sectorId, string holder)
    {
        if (!_sectorGlobals.ContainsKey(sectorId) || _sectorHolders.ContainsKey(sectorId))
            return false;

        _sectorHolders[sectorId] = holder;
        return true;
    }

    public void Unclaim(string sectorId) => _sectorHolders.Remove(sectorId);

    private readonly HashSet<string> _barredChancellors = new();

    public bool TryTakeChancellor(string holder)
    {
        if (Chancellor != null || _barredChancellors.Contains(holder))
            return false;

        Chancellor = holder;
        _electionForced = false;
        return true;
    }

    public void ResignChancellor()
    {
        if (Chancellor is { } holder)
            _cards.RevokeHolder(holder, SWSenatePosition.Chancellor);
        Chancellor = null;
    }

    public bool IsBarredChancellor(string holder) => _barredChancellors.Contains(holder);

    public string? OustChancellor()
    {
        var name = Chancellor;
        ResignChancellor();
        if (name != null)
            _barredChancellors.Add(name);
        _electionForced = true;
        return name;
    }

    public bool TryTakeSpeaker(string holder)
    {
        if (Speaker != null)
            return false;

        Speaker = holder;
        return true;
    }

    public void ResignSpeaker()
    {
        if (Speaker is { } holder)
            _cards.RevokeHolder(holder, SWSenatePosition.Speaker);
        Speaker = null;
    }

    public bool HasActiveBill(string author) =>
        _bills.Any(b => (b.Stage == SWBillStage.Voting || b.Stage == SWBillStage.InCommittee || b.Stage == SWBillStage.Returned) && b.Author == author);

    public SWSenateBill SubmitBill(string name, string description, string author, Dictionary<string, float> effects)
    {
        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = name,
            Description = description,
            Author = author,
            Effects = effects,
            Stage = SWBillStage.Voting,
        };

        _bills.Add(bill);
        return bill;
    }

    public bool CanAmendLaw(int lawId) => _bills.Any(b => b.Id == lawId && IsActiveRootLaw(b));

    private static bool IsActiveRootLaw(SWSenateBill bill) => bill is
    {
        Stage: SWBillStage.Passed,
        Repealed: false,
        IsElection: false,
        SpecialMotionType: null,
        AmendsLawId: null,
    };

    private bool HasActiveAmendmentTarget(SWSenateBill bill) =>
        bill.AmendsLawId is not { } lawId || CanAmendLaw(lawId);

    private bool ValidAmendmentEffects(IReadOnlyDictionary<string, float> effects, string description) =>
        (effects.Any(e => e.Value != 0f) || !string.IsNullOrWhiteSpace(description))
        && effects.All(e => _proto.HasIndex<SWStatPrototype>(e.Key) && float.IsFinite(e.Value));

    private static string AmendmentName(SWSenateBill law, string name)
    {
        var prefix = $"Поправки к «{law.Name}»: ";
        name = name.Trim();
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name : prefix + name;
    }

    public SWSenateBill? SubmitAmendment(int lawId, string name, string description, string author,
        Dictionary<string, float> effects)
    {
        if (!_time.ModeActive || !_time.DayActive || !_sectorHolders.Values.Contains(author)
            || HasActiveBill(author) || string.IsNullOrWhiteSpace(name) || !ValidAmendmentEffects(effects, description))
            return null;

        var law = _bills.FirstOrDefault(b => b.Id == lawId && IsActiveRootLaw(b));
        if (law == null)
            return null;

        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = AmendmentName(law, name),
            Description = description.Trim(),
            Author = author,
            AmendsLawId = law.Id,
            Effects = effects.Where(e => e.Value != 0f).ToDictionary(e => e.Key, e => e.Value),
            Stage = SWBillStage.Voting,
        };
        _bills.Add(bill);
        return bill;
    }

    /// <summary>
    /// Consolidated enacted law, without modifying its original bill or any amendment's delta.
    /// Pending, vetoed and repealed amendments do not contribute.
    /// </summary>
    public Dictionary<string, float> GetEffectiveLawEffects(int lawId)
    {
        var law = _bills.FirstOrDefault(b => b.Id == lawId && IsActiveRootLaw(b));
        if (law == null)
            return new Dictionary<string, float>();

        var effects = new Dictionary<string, float>(law.Effects);
        foreach (var amendment in ActiveAmendments(lawId))
        {
            foreach (var (stat, delta) in amendment.Effects)
                effects[stat] = effects.GetValueOrDefault(stat) + delta;
        }
        return effects;
    }

    private IEnumerable<SWSenateBill> ActiveAmendments(int lawId) =>
        _bills.Where(b => b.AmendsLawId == lawId && b.Stage == SWBillStage.Passed && !b.Repealed);

    public SWSenateBill IssueDecree(string name, string description, string author, Dictionary<string, float> effects)
    {
        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = name,
            Description = description,
            Author = author,
            Effects = effects,
            Stage = SWBillStage.Passed,
        };

        _bills.Add(bill);
        ApplyBill(bill);
        return bill;
    }

    public SWSenateBill SubmitEmergencyBill(string name, string description, string author, Dictionary<string, float> effects, int crisisId, float relief)
    {
        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = $"СРОЧНО: {name}",
            Description = description,
            Author = author,
            Effects = effects,
            Stage = SWBillStage.Voting,
            Emergency = true,
            EmergencyCrisisId = crisisId,
            EmergencyRelief = relief,
        };

        _bills.Add(bill);
        return bill;
    }

    public SWSenateBill? SubmitEmergencyPowerMotion(SWMotionType type, int level, SWEmergencyPower power, string author, int requiredPercent)
    {
        if (type == SWMotionType.GrantPower && !Enum.IsDefined(power))
            return null;

        var motionLevel = Math.Clamp(level, 1, 3);
        var motionPower = type == SWMotionType.GrantPower ? power : SWEmergencyPower.Veto;
        if (_bills.Any(b =>
                b.Stage == SWBillStage.Voting
                && b.SpecialMotionType == type
                && b.SpecialMotionLevel == motionLevel
                && b.SpecialMotionPower == motionPower))
            return null;

        var threshold = Math.Clamp(requiredPercent, 1, 100);
        var bill = new SWSenateBill
        {
            Id = _nextBillId++,
            Name = EmergencyMotionName(type, motionLevel, motionPower),
            Description = EmergencyMotionDescription(type, motionLevel, motionPower, threshold),
            Author = author,
            Stage = SWBillStage.Voting,
            Emergency = type == SWMotionType.NoConfidence,
            SpecialMotionType = type,
            SpecialMotionLevel = motionLevel,
            SpecialMotionPower = motionPower,
            RequiredPercent = threshold,
        };

        _bills.Add(bill);
        return bill;
    }

    // --- Chancellor elections ---

    private void TickChancellorElection(float frameTime)
    {
        if (_chancellorElect != null)
        {
            if (Chancellor != null || !_sectorHolders.ContainsValue(_chancellorElect))
            {
                _chancellorElect = null;
            }
            else
            {
                _vacancyHours = 0f;
                return;
            }
        }

        var bill = _bills.FirstOrDefault(b => b.IsElection && b.Stage == SWBillStage.Voting);
        if (bill == null)
        {
            var vacant = _time.DayActive && Chancellor == null && (Speaker == null || _electionForced);
            _vacancyHours = vacant ? _vacancyHours + frameTime * (1f / SWSenateTimeSystem.RealSecondsPerGameHour) : 0f;

            if (!vacant || _vacancyHours < ElectionDelayHours || _sectorHolders.Count < 2)
                return;

            bill = new SWSenateBill
            {
                Id = _nextBillId++,
                Name = "Выборы Верховного Канцлера",
                Description = "Внеочередные выборы главы исполнительной власти Республики. Кандидатом может выдвинуть себя любой сенатор в ходе заседания.",
                Author = "Сенат",
                Stage = SWBillStage.Voting,
                IsElection = true,
            };
            _bills.Add(bill);
            _vacancyHours = 0f;
        }

        if (!_time.DayActive)
            return;
        if (_sessions.Any(s => s.ContainsBill(bill.Id) && s.Status != SWSessionStatus.Rejected))
            return;

        for (var hour = FirstSchedulableHour(); hour < SWSenateTimeSystem.EndHour; hour++)
        {
            if (!CanSchedule(hour, 2))
                continue;

            _sessions.Add(new SWSenateSession
            {
                Id = _nextSessionId++,
                BillId = bill.Id,
                StartHour = hour,
                Duration = 2,
                Status = SWSessionStatus.Confirmed,
            });
            return;
        }
    }

    private void TickElectionResolution()
    {
        var session = GetActiveSession();
        if (session == null)
            return;

        var bill = _bills.FirstOrDefault(b => b.Id == session.CurrentBillId);
        if (bill is not { IsElection: true, Stage: SWBillStage.Voting })
            return;

        var electorate = _sectorHolders.Count;
        if (electorate == 0 || bill.Candidates.Count == 0)
            return;

        string? leader = null;
        var leaderVotes = -1;
        var tied = false;
        foreach (var candidate in bill.Candidates)
        {
            var count = bill.ElectionVotes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == candidate);
            if (count > leaderVotes)
            {
                leaderVotes = count;
                leader = candidate;
                tied = false;
            }
            else if (count == leaderVotes)
            {
                tied = true;
            }
        }

        if (leader == null)
            return;

        var majority = electorate / 2 + 1;
        if (leaderVotes >= majority)
        {
            ResolveElection(bill, leader);
            return;
        }

        var castCount = bill.ElectionVotes.Keys.Count(sectorId => _sectorHolders.ContainsKey(sectorId));
        if (castCount >= electorate && !tied)
            ResolveElection(bill, leader);
    }

    private void TickEmergencySessions(float frameTime)
    {
        if (!_time.DayActive)
            return;

        var gameHours = frameTime * (1f / SWSenateTimeSystem.RealSecondsPerGameHour);
        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Voting || !bill.Emergency)
                continue;
            if (_sessions.Any(s => s.ContainsBill(bill.Id) && s.Status != SWSessionStatus.Rejected))
                continue;

            bill.EmergencyWaitHours += gameHours;
            if (bill.EmergencyWaitHours < (bill.EmergencyCrisisId > 0 ? 0.5f : EmergencySessionDelayHours))
                continue;

            var result = ScheduleEmergencySession(bill.Id);
            if (result != null)
            {
                bill.EmergencyWaitHours = 0f;
                RaiseLocalEvent(new SWSenateEmergencySessionScheduledEvent(bill.Name, result.StartHour, result.DisplacedBill));
            }
        }
    }

    private void ResolveElectionByPlurality(SWSenateBill bill)
    {
        string? winner = null;
        var best = 0;
        foreach (var candidate in bill.Candidates)
        {
            if (!_sectorHolders.ContainsValue(candidate))
                continue;

            var count = bill.ElectionVotes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == candidate);
            if (count > best)
            {
                best = count;
                winner = candidate;
            }
        }

        if (winner == null)
        {
            bill.Stage = SWBillStage.Rejected;
            return;
        }

        ResolveElection(bill, winner);
    }

    private void ResolveElection(SWSenateBill bill, string winner)
    {
        bill.Stage = SWBillStage.Passed;
        _chancellorElect = winner;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Сенат",
            Loc.GetString("sw-senate-election-result", ("name", winner))));
    }

    public bool NominateChancellor(string sectorId)
    {
        var bill = GetCurrentBill();
        if (bill is not { IsElection: true })
            return false;

        if (!_sectorHolders.TryGetValue(sectorId, out var name))
            return false;

        if (_barredChancellors.Contains(name) || bill.Candidates.Contains(name))
            return false;

        bill.Candidates.Add(name);
        return true;
    }

    public bool CastElectionVote(string sectorId, string candidate)
    {
        var bill = GetCurrentBill();
        if (bill is not { IsElection: true } || !_sectorHolders.ContainsKey(sectorId) || !bill.Candidates.Contains(candidate))
            return false;

        bill.ElectionVotes[sectorId] = candidate;
        return true;
    }

    public bool ConfirmChancellorElect(string name, string sectorId)
    {
        if (_chancellorElect == null || _chancellorElect != name || Chancellor != null)
            return false;

        Chancellor = name;
        _chancellorElect = null;
        _electionForced = false;
        Unclaim(sectorId);
        return true;
    }

    public SWSenateSession? GetActiveSession()
    {
        if (!_time.DayActive)
            return null;

        if (_time.ModeActive)
            ResolveExpiredSessions();
        return _sessions.FirstOrDefault(s => SessionIsActive(s));
    }

    private bool SessionIsActive(SWSenateSession session)
    {
        if (!_time.DayActive || session.IsAmendmentSession && !_time.ModeActive || session.Status != SWSessionStatus.Confirmed
            || !_bills.Any(b => b.Id == session.CurrentBillId && b.Stage == SWBillStage.Voting))
            return false;
        return session.ActiveEndHour is { } end
            ? _time.ActiveGameHours < end
            : _time.CalendarGameMinutes / 60d - _time.Day * 24 >= session.StartHour
              && _time.CalendarGameMinutes / 60d - _time.Day * 24 < session.EndHour;
    }

    public SWSenateBill? GetCurrentBill()
    {
        var session = GetActiveSession();
        if (session == null)
            return null;

        var bill = _bills.FirstOrDefault(b => b.Id == session.CurrentBillId);
        return bill is { Stage: SWBillStage.Voting } ? bill : null;
    }

    public bool CastVote(string sectorId, SWVoteChoice choice)
    {
        var bill = GetCurrentBill();
        if (bill == null || bill.AwaitingTiebreak || !_sectorHolders.ContainsKey(sectorId))
            return false;

        bill.Votes[sectorId] = choice;
        return true;
    }

    public bool CanEndCurrentVote()
    {
        var bill = GetCurrentBill();
        if (bill == null || bill.AwaitingTiebreak)
            return false;

        if (bill.SpecialMotionType != null)
            return CanEndSpecialMotionVote(bill);

        var electorate = _sectorHolders.Count;
        if (electorate == 0)
            return false;

        var remaining = electorate - bill.Votes.Count;
        if (remaining <= 0)
            return true;

        var votesFor = bill.Votes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == SWVoteChoice.Against);
        return Math.Abs(votesFor - votesAgainst) > remaining;
    }

    private bool CanEndSpecialMotionVote(SWSenateBill bill)
    {
        var electorate = _sectorHolders.Count;
        if (electorate == 0)
            return false;

        var votesFor = ValidVotesFor(bill);
        var remaining = Math.Max(0, electorate - ValidVotesCast(bill));
        return votesFor >= VoteNeeded(bill) || votesFor + remaining < VoteNeeded(bill) || remaining <= 0;
    }

    public bool EndVoteBySpeaker()
    {
        var bill = GetCurrentBill();
        if (bill == null || bill.AwaitingTiebreak || !CanEndCurrentVote())
            return false;

        ResolveBill(bill, true);
        AdvanceAmendmentAgenda(bill.Id);
        return true;
    }

    public bool Tiebreak(SWVoteChoice choice)
    {
        var bill = GetCurrentBill();
        if (bill == null || !bill.AwaitingTiebreak)
            return false;

        bill.AwaitingTiebreak = false;
        if (choice == SWVoteChoice.For)
        {
            // Passed the vote, but now awaits the Chancellor's formal signature before it becomes law.
            bill.Stage = SWBillStage.AwaitingSignature;
            RaiseVotesResolved(bill, true);
        }
        else
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
            RaiseVotesResolved(bill, false);
        }

        AdvanceAmendmentAgenda(bill.Id);
        return true;
    }

    // The Chancellor (or acting officer) ratifies a passed bill - only now does it apply and get announced.
    public bool SignBill(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.AwaitingSignature);
        if (bill == null)
            return false;

        if (!HasActiveAmendmentTarget(bill))
        {
            RejectOrphanedAmendment(bill);
            return false;
        }

        bill.Stage = SWBillStage.Passed;
        ApplyBill(bill);
        RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, true, bill.EmergencyCrisisId, bill.EmergencyRelief));
        return true;
    }

    // Emergency veto: kill a bill that passed the vote but awaits signature - it never becomes law.
    public bool VetoBill(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.AwaitingSignature);
        if (bill == null)
            return false;

        bill.Stage = SWBillStage.Rejected;
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Сенат",
            $"Канцлер наложил вето на закон «{bill.Name}».",
            SWSenateAnnouncementKind.Chancellor));
        return true;
    }

    public List<SWBillEntry> BuildBillsAwaitingSignature()
    {
        var list = new List<SWBillEntry>();
        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.AwaitingSignature)
                continue;

            list.Add(BuildBillEntry(bill));
        }

        return list;
    }

    private void RaiseVotesResolved(SWSenateBill bill, bool passed) =>
        RaiseLocalEvent(new SWSenateBillVotesResolvedEvent(bill.Id, bill.Name, passed, new Dictionary<string, SWVoteChoice>(bill.Votes)));

    private void ResolveBill(SWSenateBill bill, bool allowTiebreak)
    {
        if (!HasActiveAmendmentTarget(bill))
        {
            RejectOrphanedAmendment(bill);
            return;
        }

        if (bill.SpecialMotionType != null)
        {
            ResolveSpecialMotionBill(bill);
            return;
        }

        var votesFor = bill.Votes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Count(kv => _sectorHolders.ContainsKey(kv.Key) && kv.Value == SWVoteChoice.Against);

        if (votesFor > votesAgainst)
        {
            // Passed the vote, but now awaits the Chancellor's formal signature before it becomes law.
            bill.Stage = SWBillStage.AwaitingSignature;
            RaiseVotesResolved(bill, true);
        }
        else if (votesAgainst > votesFor)
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
            RaiseVotesResolved(bill, false);
        }
        else if (allowTiebreak)
        {
            bill.AwaitingTiebreak = true;
        }
        else
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
            RaiseVotesResolved(bill, false);
        }
    }

    private void ResolveSpecialMotionBill(SWSenateBill bill)
    {
        bill.AwaitingTiebreak = false;

        if (SpecialMotionPassed(bill))
        {
            bill.Stage = SWBillStage.Passed;
            RaiseVotesResolved(bill, true);
            RaiseLocalEvent(new SWSenateEmergencyMotionPassedEvent(
                bill.SpecialMotionType!.Value,
                bill.SpecialMotionLevel,
                bill.SpecialMotionPower));
            return;
        }

        bill.Stage = SWBillStage.Rejected;
        RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
        RaiseVotesResolved(bill, false);
    }

    public bool OfficesVacant => Speaker == null && Chancellor == null;

    private void AutopilotTick()
    {
        if (!_time.DayActive || !OfficesVacant)
            return;

        foreach (var session in _sessions)
        {
            if (session.Status == SWSessionStatus.Pending)
                ConfirmSession(session.Id);
        }

        // No Chancellor to ratify - passed bills are auto-signed into law.
        foreach (var id in _bills.Where(b => b.Stage == SWBillStage.AwaitingSignature && b.EmergencyCrisisId <= 0).Select(b => b.Id).ToList())
            SignBill(id);

        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Voting)
                continue;
            if (_sessions.Any(s => s.ContainsBill(bill.Id) && s.Status != SWSessionStatus.Rejected))
                continue;

            var free = BuildFreeStartHours();
            if (free.Count == 0)
                break;

            var session = TryScheduleSession(bill.Id, free[0], 1);
            if (session != null)
                session.Status = SWSessionStatus.Confirmed;
        }
    }

    private void AnnounceStartedSessions()
    {
        if (!_time.DayActive)
            return;

        var hour = _time.CurrentHour;
        foreach (var session in _sessions)
        {
            if (session.Status != SWSessionStatus.Confirmed)
                continue;
            if (!SessionIsActive(session))
                continue;
            if (!_announcedSessions.Add(session.Id))
                continue;

            RaiseLocalEvent(new SWSenateSessionStartedEvent(GetSessionName(session), session.StartHour, session.EndHour, session.Emergency));
        }
    }

    public List<SWBillEntry> BuildActiveLaws()
    {
        var laws = new List<SWBillEntry>();
        foreach (var bill in _bills)
        {
            if (!IsActiveRootLaw(bill))
                continue;

            laws.Add(BuildBillEntry(bill));
        }

        return laws;
    }

    private void ResolveExpiredSessions()
    {
        foreach (var session in _sessions.ToArray())
        {
            if (session.Status != SWSessionStatus.Confirmed)
                continue;

            if (session.IsAmendmentSession)
            {
                TickAmendmentSession(session);
                continue;
            }

            var expired = session.ActiveEndHour is { } end
                ? _time.ActiveGameHours >= end
                : !_time.DayActive || _time.CalendarGameMinutes / 60d - _time.Day * 24 >= session.EndHour;
            if (!expired)
                continue;

            var bill = _bills.FirstOrDefault(b => b.Id == session.BillId);
            if (bill is not { Stage: SWBillStage.Voting })
                continue;

            if (bill.IsElection)
            {
                ResolveElectionByPlurality(bill);
                continue;
            }

            bill.AwaitingTiebreak = false;
            ResolveBill(bill, false);
        }
    }

    private void AdvanceAmendmentAgenda(int billId)
    {
        var session = _sessions.FirstOrDefault(s => s.IsAmendmentSession && s.CurrentBillId == billId);
        if (session != null)
            TickAmendmentSession(session);
    }

    private void TickAmendmentSession(SWSenateSession session)
    {
        if (!_time.ModeActive)
            return;

        var now = _time.CalendarGameMinutes / 60d - _time.Day * 24;
        if (_time.DayActive && now < session.StartHour)
            return;

        // A delayed update must never reject the untouched remainder of the agenda.
        if (!_time.DayActive || now >= session.EndHour)
        {
            var current = _bills.FirstOrDefault(b => b.Id == session.CurrentBillId && b.Stage == SWBillStage.Voting);
            if (session.BallotEndHour != null && current != null)
            {
                current.AwaitingTiebreak = false;
                ResolveBill(current, false);
            }
            ReleaseAmendmentSession(session);
            return;
        }

        while (session.CurrentAmendmentIndex < session.AmendmentBillIds.Count)
        {
            var bill = _bills.FirstOrDefault(b => b.Id == session.CurrentBillId);
            if (bill is not { Stage: SWBillStage.Voting })
            {
                session.CurrentAmendmentIndex++;
                session.BallotEndHour = null;
                continue;
            }
            if (!HasActiveAmendmentTarget(bill))
            {
                RejectOrphanedAmendment(bill);
                return;
            }
            if (session.BallotEndHour == null)
            {
                var remaining = session.AmendmentBillIds.Skip(session.CurrentAmendmentIndex)
                    .Count(id => _bills.Any(b => b.Id == id && b.Stage == SWBillStage.Voting));
                bill.Votes.Clear();
                bill.AwaitingTiebreak = false;
                session.BallotEndHour = now + (session.EndHour - now) / remaining;
                return;
            }
            if (now < session.BallotEndHour)
                return;

            bill.AwaitingTiebreak = false;
            ResolveBill(bill, false);
            session.CurrentAmendmentIndex++;
            session.BallotEndHour = null;
        }
    }

    private void ReleaseAmendmentSession(SWSenateSession session)
    {
        if (session.IsAmendmentSession)
        {
            foreach (var bill in _bills.Where(b => session.ContainsBill(b.Id) && b.Stage == SWBillStage.Voting))
            {
                bill.Votes.Clear();
                bill.AwaitingTiebreak = false;
            }
        }
        _sessions.Remove(session);
        _announcedSessions.Remove(session.Id);
    }

    public string GetSessionName(SWSenateSession session)
    {
        if (!session.IsAmendmentSession)
            return GetBillName(session.BillId);

        var first = _bills.FirstOrDefault(b => b.Id == session.BillId);
        var name = first?.AmendsLawId is { } lawId ? LawName(lawId) : null;
        return $"Поправки к «{name ?? "закону"}» ({session.AmendmentBillIds.Count})";
    }

    public string GetSessionAgendaInfo(SWSenateSession session)
    {
        if (!session.IsAmendmentSession)
            return string.Empty;
        if (session.CurrentBillId == null)
            return "Повестка рассмотрена";

        var deadline = session.BallotEndHour is { } end ? $"\nГолосование до {SWSenateDuration.FormatClock(end)}" : string.Empty;
        return $"Поправка {session.CurrentAmendmentIndex + 1} из {session.AmendmentBillIds.Count}{deadline}";
    }

    public IReadOnlyList<SWSenateSession> Sessions => _sessions;

    public static bool IsReserved(int hour) => hour < 9 || hour == 14 || hour >= 20;

    public static string ReservedLabel(int hour)
    {
        if (hour == 14)
            return "Обед";
        if (hour == 8)
            return "Собрание Кабинета и комитетов";
        return "Личное время";
    }

    private int FirstSchedulableHour() => _time.CurrentMinute == 0 ? _time.CurrentHour : _time.CurrentHour + 1;

    private bool Overlaps(int startHour, int duration)
    {
        var end = startHour + duration;
        foreach (var session in _sessions)
        {
            if (session.Status == SWSessionStatus.Rejected)
                continue;
            if (startHour < session.EndHour && session.StartHour < end)
                return true;
        }

        return false;
    }

    public bool CanSchedule(int startHour, int duration)
    {
        if (duration < 1 || duration > 4)
            return false;
        if (startHour < FirstSchedulableHour())
            return false;

        for (var hour = startHour; hour < startHour + duration; hour++)
        {
            if (IsReserved(hour))
                return false;
        }

        return !Overlaps(startHour, duration);
    }

    public SWSenateSession? TryScheduleSession(int billId, int startHour, int duration)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId);
        if (bill is not { Stage: SWBillStage.Voting } || bill.Emergency || bill.IsElection
            || !HasActiveAmendmentTarget(bill) || !CanSchedule(startHour, duration))
            return null;

        if (_sessions.Any(s => s.ContainsBill(billId) && s.Status != SWSessionStatus.Rejected))
            return null;

        var session = new SWSenateSession
        {
            Id = _nextSessionId++,
            BillId = billId,
            StartHour = startHour,
            Duration = duration,
            Status = SWSessionStatus.Pending,
        };

        _sessions.Add(session);
        return session;
    }

    public SWSenateSession? TryScheduleAmendmentSession(List<int> billIds, int startHour, int duration)
    {
        if (!_time.ModeActive || !_time.DayActive || !CanSchedule(startHour, duration) || !CanGroupAmendments(billIds))
            return null;

        var session = new SWSenateSession
        {
            Id = _nextSessionId++,
            BillId = billIds[0],
            AmendmentBillIds = new List<int>(billIds),
            StartHour = startHour,
            Duration = duration,
        };
        _sessions.Add(session);
        return session;
    }

    private bool CanGroupAmendments(IReadOnlyList<int> billIds, int? existingSessionId = null)
    {
        if (billIds.Count < 2 || billIds.Distinct().Count() != billIds.Count)
            return false;

        int? lawId = null;
        foreach (var id in billIds)
        {
            var bill = _bills.FirstOrDefault(b => b.Id == id);
            if (bill is not { Stage: SWBillStage.Voting, AmendsLawId: not null, Emergency: false, IsElection: false,
                    SpecialMotionType: null, Repealed: false }
                || !HasActiveAmendmentTarget(bill) || lawId != null && lawId != bill.AmendsLawId
                || _sessions.Any(s => s.Id != existingSessionId && s.ContainsBill(id) && s.Status != SWSessionStatus.Rejected))
                return false;
            lawId = bill.AmendsLawId;
        }
        return true;
    }

    public bool ConfirmSession(int sessionId)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.Status == SWSessionStatus.Pending);
        if (session == null)
            return false;

        if (session.IsAmendmentSession && (!_time.ModeActive || !_time.DayActive
            || !CanGroupAmendments(session.AmendmentBillIds, session.Id)))
            return false;

        session.Status = SWSessionStatus.Confirmed;
        return true;
    }

    public bool RejectSession(int sessionId)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.Status == SWSessionStatus.Pending);
        if (session == null)
            return false;

        _sessions.Remove(session);
        return true;
    }

    public bool RemoveSession(int sessionId)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.Status == SWSessionStatus.Pending);
        if (session == null)
            return false;

        _sessions.Remove(session);
        return true;
    }

    public SWEmergencyScheduleResult? ScheduleEmergencySession(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId);
        if (bill is not { Stage: SWBillStage.Voting } || !bill.Emergency || !_time.DayActive)
            return null;

        if (_sessions.Any(s => s.ContainsBill(billId) && s.Status != SWSessionStatus.Rejected))
            return null;

        if (bill.EmergencyCrisisId > 0)
        {
            // An ongoing emergency vote has priority. Waiting bills remain visible in the queue.
            if (_sessions.Any(s => s.Emergency && s.Status != SWSessionStatus.Rejected
                && _bills.Any(b => b.Id == s.CurrentBillId && b.Stage == SWBillStage.Voting)
                && (s.ActiveEndHour is { } end ? _time.ActiveGameHours < end : s.EndHour > _time.CurrentHour)))
                return null;
            var now = _time.CalendarGameMinutes / 60d - _time.Day * 24;
            string? displacedCrisis = null;
            foreach (var occupied in _sessions.Where(s => !s.Emergency && s.Status != SWSessionStatus.Rejected
                && s.StartHour < now + 0.5d && now < s.EndHour).ToList())
            {
                displacedCrisis ??= GetSessionName(occupied);
                ReleaseAmendmentSession(occupied);
            }
            _sessions.Add(new SWSenateSession
            {
                Id = _nextSessionId++, BillId = billId, StartHour = now, Duration = 0.5d,
                ActiveEndHour = _time.ActiveGameHours + 0.5d,
                Status = SWSessionStatus.Confirmed, Emergency = true,
            });
            return new SWEmergencyScheduleResult { StartHour = now, DisplacedBill = displacedCrisis };
        }

        var targetHour = _time.CurrentHour + 1;
        while (targetHour < SWSenateTimeSystem.EndHour
               && _sessions.Any(s => s.Emergency && s.Status != SWSessionStatus.Rejected
                   && targetHour >= s.StartHour && targetHour < s.EndHour))
            targetHour++;

        if (targetHour < SWSenateTimeSystem.StartHour || targetHour >= SWSenateTimeSystem.EndHour)
            return null;

        string? displaced = null;
        foreach (var occupied in _sessions
                     .Where(s => s.Status != SWSessionStatus.Rejected && targetHour >= s.StartHour && targetHour < s.EndHour)
                     .ToList())
        {
            displaced ??= GetSessionName(occupied);
            ReleaseAmendmentSession(occupied);
        }

        _sessions.Add(new SWSenateSession
        {
            Id = _nextSessionId++,
            BillId = billId,
            StartHour = targetHour,
            Duration = 1,
            Status = SWSessionStatus.Confirmed,
            Emergency = true,
        });

        return new SWEmergencyScheduleResult { StartHour = targetHour, DisplacedBill = displaced };
    }

    public List<SWScheduleRow> BuildScheduleRows()
    {
        var rows = new List<SWScheduleRow>();
        var nowHour = _time.DayActive ? _time.CurrentHour : -1;

        for (var hour = SWSenateTimeSystem.StartHour; hour < SWSenateTimeSystem.EndHour; hour++)
        {
            var row = new SWScheduleRow
            {
                Hour = hour,
                TimeLabel = $"{hour:00}:00",
                IsNow = hour == nowHour,
            };

            var session = _sessions.FirstOrDefault(s =>
                s.Status != SWSessionStatus.Rejected && hour < s.EndHour && s.StartHour < hour + 1);

            if (session is { Emergency: true })
            {
                row.Text = hour == (int) Math.Floor(session.StartHour)
                    ? $"СРОЧНО: {GetSessionName(session)} ({session.TimeRange})"
                    : "   - продолжение";
                row.Color = Color.IndianRed;
            }
            else if (IsReserved(hour))
            {
                row.Text = ReservedLabel(hour);
                row.Color = Color.DarkGray;
            }
            else if (session == null)
            {
                row.Text = "Свободно";
                row.Color = Color.DimGray;
                row.Free = true;
            }
            else if (hour == (int) Math.Floor(session.StartHour))
            {
                var status = session.Status == SWSessionStatus.Confirmed ? "подтверждено" : "ожидает";
                row.Text = $"Заседание: {GetSessionName(session)} ({session.TimeRange}) [{status}]";
                row.Color = session.Status == SWSessionStatus.Confirmed ? Color.LightGreen : Color.Goldenrod;
            }
            else
            {
                row.Text = "   - продолжение";
                row.Color = session.Status == SWSessionStatus.Confirmed ? Color.LightGreen : Color.Goldenrod;
            }

            rows.Add(row);
        }

        return rows;
    }

    public List<SWBillOption> BuildSchedulableBills()
    {
        var options = new List<SWBillOption>();
        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Voting)
                continue;
            if (bill.IsElection)
                continue;
            if (_sessions.Any(s => s.ContainsBill(bill.Id) && s.Status != SWSessionStatus.Rejected))
                continue;

            options.Add(new SWBillOption
            {
                Id = bill.Id,
                Name = bill.Name,
                Emergency = bill.Emergency,
                AmendsLawId = bill.AmendsLawId,
                AmendsLawName = bill.AmendsLawId is { } lawId ? LawName(lawId) ?? string.Empty : string.Empty,
            });
        }

        options.Sort((a, b) => b.Emergency.CompareTo(a.Emergency));
        return options;
    }

    public List<int> BuildFreeStartHours()
    {
        var hours = new List<int>();
        var first = FirstSchedulableHour();
        for (var hour = SWSenateTimeSystem.StartHour; hour < SWSenateTimeSystem.EndHour; hour++)
        {
            if (!IsReserved(hour) && hour >= first && !Overlaps(hour, 1))
                hours.Add(hour);
        }

        return hours;
    }

    public List<SWSessionEntry> BuildPendingSessions()
    {
        var pending = new List<SWSessionEntry>();
        foreach (var session in _sessions)
        {
            if (session.Status != SWSessionStatus.Pending)
                continue;

            pending.Add(new SWSessionEntry
            {
                Id = session.Id,
                BillName = GetSessionName(session),
                BillNames = session.IsAmendmentSession
                    ? session.AmendmentBillIds.Select(GetBillName).ToList() : new List<string> { GetBillName(session.BillId) },
                TimeRange = $"{session.TimeRange}",
                Status = "ожидает",
            });
        }

        return pending;
    }

    public string GetBillName(int billId) =>
        _bills.FirstOrDefault(b => b.Id == billId)?.Name ?? "-";

    public List<SWBillEntry> BuildBillEntries()
    {
        var bills = new List<SWBillEntry>();
        foreach (var bill in _bills)
            bills.Add(BuildBillEntry(bill));

        return bills;
    }

    private SWBillEntry BuildBillEntry(SWSenateBill bill)
    {
        var activeRoot = IsActiveRootLaw(bill);
        var effects = activeRoot ? GetEffectiveLawEffects(bill.Id) : new Dictionary<string, float>(bill.Effects);
        return new SWBillEntry
        {
            Id = bill.Id,
            Name = bill.Name,
            Author = bill.Author,
            Description = bill.Description,
            Stage = BillStageLabel(bill),
            AmendsLawId = bill.AmendsLawId,
            AmendsLawName = bill.AmendsLawId is { } lawId ? LawName(lawId) : null,
            CanAmend = activeRoot,
            Effects = BuildEffects(effects),
            EffectValues = effects,
            Amendments = activeRoot ? ActiveAmendments(bill.Id).Select(BuildBillEntry).ToList() : new List<SWBillEntry>(),
        };
    }

    public void ReferToCommittee(SWSenateBill bill, string committeeId, string committeeName, int day)
    {
        if (bill.AmendsLawId != null && bill.Stage is not (SWBillStage.Voting or SWBillStage.InCommittee or SWBillStage.Returned))
            return;

        if (!HasActiveAmendmentTarget(bill))
        {
            RejectOrphanedAmendment(bill);
            return;
        }

        bill.Stage = SWBillStage.InCommittee;
        bill.CommitteeId = committeeId;
        bill.CommitteeName = committeeName;
        bill.CommitteeDay = day;
    }

    public void PlaceOnFloor(SWSenateBill bill)
    {
        if (bill.AmendsLawId != null && bill.Stage is not (SWBillStage.Voting or SWBillStage.InCommittee or SWBillStage.Returned))
            return;

        if (!HasActiveAmendmentTarget(bill))
        {
            RejectOrphanedAmendment(bill);
            return;
        }

        bill.Stage = SWBillStage.Voting;
        bill.CommitteeId = null;
        bill.CommitteeName = null;
    }

    // Bills held past their referral day are auto-discharged to the floor at the next committee hour.
    public void DischargeReferredBills(int currentDay)
    {
        foreach (var bill in _bills)
        {
            if (bill.Stage == SWBillStage.InCommittee && bill.CommitteeDay < currentDay)
                bill.Stage = SWBillStage.Voting;
        }
    }

    public List<SWSenateBill> GetInCommitteeBills(string committeeId) =>
        _bills.Where(b => b.Stage == SWBillStage.InCommittee && b.CommitteeId == committeeId).ToList();

    public bool IsInCommittee(int billId) =>
        _bills.Any(b => b.Id == billId && b.Stage == SWBillStage.InCommittee);

    public void AdvanceToFloor(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.InCommittee);
        if (bill == null)
            return;

        bill.Stage = SWBillStage.Voting;
        bill.CommitteeId = null;
        bill.CommitteeName = null;
    }

    public bool SendBackBill(int billId, string reason)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.InCommittee);
        if (bill == null || bill.SendBacks >= 2)
            return false;

        bill.Stage = SWBillStage.Returned;
        bill.SendBacks++;
        bill.ReturnReason = reason;
        return true;
    }

    public SWSenateBill? GetReturnedBill(int billId, string author) =>
        _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.Returned && b.Author == author);

    public void ReviseBill(SWSenateBill bill, string name, string description, Dictionary<string, float> effects)
    {
        if (bill.AmendsLawId is { } lawId)
        {
            var law = _bills.FirstOrDefault(b => b.Id == lawId && IsActiveRootLaw(b));
            if (law == null || bill.Stage != SWBillStage.Returned || string.IsNullOrWhiteSpace(name)
                || !ValidAmendmentEffects(effects, description))
                return;
            name = AmendmentName(law, name);
        }
        bill.Name = name;
        bill.Description = description;
        bill.Effects = effects.Where(e => e.Value != 0f).ToDictionary(e => e.Key, e => e.Value);
    }

    public List<SWReturnedBillEntry> BuildReturnedBills(string author)
    {
        var bills = new List<SWReturnedBillEntry>();
        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Returned || bill.Author != author)
                continue;

            bills.Add(new SWReturnedBillEntry
            {
                Id = bill.Id,
                AmendsLawId = bill.AmendsLawId,
                AmendsLawName = bill.AmendsLawId is { } lawId ? LawName(lawId) : null,
                Name = bill.Name,
                Description = bill.Description,
                Reason = bill.ReturnReason ?? string.Empty,
                SendBacks = bill.SendBacks,
                Effects = BuildBillEffects(bill),
                EffectValues = new Dictionary<string, float>(bill.Effects),
            });
        }

        return bills;
    }

    public List<SWBillEffectEntry> BuildBillEffects(SWSenateBill bill) => BuildEffects(bill.Effects);

    private List<SWBillEffectEntry> BuildEffects(IReadOnlyDictionary<string, float> values)
    {
        var effects = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!values.TryGetValue(sub.ID, out var delta))
                continue;

            effects.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = SubColor(sub), Delta = delta });
        }

        return effects;
    }

    public int VoteNeeded(SWSenateBill bill)
    {
        if (bill.RequiredPercent <= 0)
            return 0;

        var electorate = Math.Max(1, _sectorHolders.Count);
        return (int) MathF.Ceiling(bill.RequiredPercent / 100f * electorate);
    }

    private int ValidVotesCast(SWSenateBill bill) =>
        bill.Votes.Keys.Count(sectorId => _sectorHolders.ContainsKey(sectorId));

    private int ValidVotesFor(SWSenateBill bill) =>
        bill.Votes.Count(vote => _sectorHolders.ContainsKey(vote.Key) && vote.Value == SWVoteChoice.For);

    private bool SpecialMotionPassed(SWSenateBill bill) =>
        bill.RequiredPercent > 0 && ValidVotesFor(bill) >= VoteNeeded(bill);

    private static string EmergencyMotionName(SWMotionType type, int level, SWEmergencyPower power) => type switch
    {
        SWMotionType.GrantLevel => $"Чрезвычайные полномочия уровня {Roman(level)}",
        SWMotionType.GrantPower => $"Чрезвычайное полномочие: {SWEmergencyPowersSystem.PowerName(power)}",
        _ => "Вотум недоверия Канцлеру",
    };

    private static string EmergencyMotionDescription(SWMotionType type, int level, SWEmergencyPower power, int threshold) => type switch
    {
        SWMotionType.GrantLevel => $"Наделить Канцлера чрезвычайными полномочиями уровня {Roman(level)}. Для принятия нужно {threshold}% голосов ЗА.",
        SWMotionType.GrantPower => $"Наделить Канцлера отдельным чрезвычайным полномочием «{SWEmergencyPowersSystem.PowerName(power)}». Для принятия нужно {threshold}% голосов ЗА.",
        _ => $"Снять Канцлера с должности вотумом недоверия. Для принятия нужно {threshold}% голосов ЗА.",
    };

    private static string Roman(int n) => n switch { 3 => "III", 2 => "II", _ => "I" };

    public List<SWBillPresetEntry> BuildBillPresets()
    {
        var list = new List<SWBillPresetEntry>();
        foreach (var preset in _proto.EnumeratePrototypes<SWBillPresetPrototype>().OrderBy(p => p.Order))
        {
            var effects = new List<SWBillEffectEntry>();
            foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
            {
                if (!preset.Effects.TryGetValue(sub.ID, out var delta))
                    continue;

                effects.Add(new SWBillEffectEntry { StatName = sub.ShortName, Color = SubColor(sub), Delta = delta });
            }

            list.Add(new SWBillPresetEntry
            {
                Id = preset.ID,
                Name = preset.Name,
                Description = preset.Description,
                Effects = effects,
            });
        }

        return list;
    }

    public string BillStageLabel(SWSenateBill bill)
    {
        if (bill.Repealed)
            return "Отменён";

        if (bill.Stage == SWBillStage.InCommittee)
            return bill.CommitteeName ?? "На рассмотрении в комитете";
        if (bill.Stage == SWBillStage.Returned)
            return "Возвращён автору на доработку";
        if (bill.Stage == SWBillStage.Passed)
            return bill.IsElection ? Loc.GetString("sw-senate-bill-stage-election-done") : Loc.GetString("sw-senate-bill-stage-passed");
        if (bill.Stage == SWBillStage.AwaitingSignature)
            return Loc.GetString("sw-senate-bill-stage-signature");
        if (bill.Stage == SWBillStage.Rejected)
            return Loc.GetString("sw-senate-bill-stage-rejected");
        if (bill.AwaitingTiebreak)
            return Loc.GetString("sw-senate-bill-stage-tiebreak");

        var session = _sessions.FirstOrDefault(s => s.ContainsBill(bill.Id) && s.Status != SWSessionStatus.Rejected);
        if (session == null)
            return bill.EmergencyCrisisId > 0 ? "В очереди экстренного созыва" : Loc.GetString("sw-senate-bill-stage-awaiting");
        if (session.Status == SWSessionStatus.Pending)
            return Loc.GetString("sw-senate-bill-stage-pending");

        if (_time.DayActive && SessionIsActive(session) && session.CurrentBillId == bill.Id)
            return Loc.GetString("sw-senate-bill-stage-voting");

        return Loc.GetString("sw-senate-bill-stage-scheduled", ("time", SWSenateDuration.FormatClock(session.StartHour)));
    }

    private void ApplyBill(SWSenateBill bill)
    {
        foreach (var (subId, delta) in bill.Effects)
        {
            if (!_proto.TryIndex<SWStatPrototype>(subId, out var sub))
                continue;

            var value = _republicSubStats.TryGetValue(subId, out var current) ? current : sub.Default;
            _republicSubStats[subId] = Math.Clamp(value + delta, sub.Min, sub.Max);
        }
    }

    public bool RepealLaw(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId);
        if (bill is not { Stage: SWBillStage.Passed, SpecialMotionType: null, IsElection: false } || bill.Repealed)
            return false;

        // Undo the consolidated law once. Child amendments retain their own records and cannot be undone twice.
        var effects = bill.AmendsLawId == null ? GetEffectiveLawEffects(bill.Id) : bill.Effects;
        bill.Repealed = true;
        if (bill.AmendsLawId == null)
        {
            foreach (var amendment in _bills.Where(b => b.AmendsLawId == bill.Id))
            {
                if (amendment.Stage == SWBillStage.Passed)
                    amendment.Repealed = true;
                else if (amendment.Stage != SWBillStage.Rejected)
                    RejectOrphanedAmendment(amendment);
            }
        }

        foreach (var (subId, delta) in effects)
        {
            if (!_proto.TryIndex<SWStatPrototype>(subId, out var sub))
                continue;

            var value = _republicSubStats.TryGetValue(subId, out var current) ? current : sub.Default;
            _republicSubStats[subId] = Math.Clamp(value - delta, sub.Min, sub.Max);
        }

        return true;
    }

    private void RejectOrphanedAmendment(SWSenateBill bill)
    {
        if (bill.AmendsLawId == null || bill.Stage is SWBillStage.Passed or SWBillStage.Rejected)
            return;

        bill.Stage = SWBillStage.Rejected;
        bill.AwaitingTiebreak = false;
        bill.ReturnReason = "Закон, к которому предложены поправки, больше не действует.";
        foreach (var session in _sessions.Where(s => s.ContainsBill(bill.Id)).ToArray())
        {
            ReleaseAmendmentSession(session);
        }
        RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
    }

    public string? LawName(int billId) => _bills.FirstOrDefault(b => b.Id == billId)?.Name;

    public List<SWCourtLawOption> BuildRepealableLaws()
    {
        var list = new List<SWCourtLawOption>();
        foreach (var bill in _bills)
        {
            if (bill is { Stage: SWBillStage.Passed, SpecialMotionType: null, IsElection: false, Repealed: false })
                list.Add(new SWCourtLawOption { Id = bill.Id, Name = bill.Name });
        }

        return list;
    }

    public override void Update(float frameTime)
    {
        frameTime = _time.ActiveSeconds(frameTime);
        if (frameTime <= 0f)
            return;
        if (!_time.ModeActive)
            return;

        ResolveExpiredSessions();
        AnnounceStartedSessions();
        TickChancellorElection(frameTime);
        TickElectionResolution();
        TickEmergencySessions(frameTime);

        if (_time.Day != _scheduleDay)
        {
            _scheduleDay = _time.Day;
            _sessions.RemoveAll(s => s.ActiveEndHour == null
                || !_bills.Any(b => s.ContainsBill(b.Id) && b.Stage == SWBillStage.Voting));
            foreach (var session in _sessions)
            {
                session.StartHour = SWSenateTimeSystem.StartHour;
                session.Duration = Math.Max(0d, session.ActiveEndHour!.Value - _time.ActiveGameHours);
            }
            _announcedSessions.Clear();

            // Any bill the Chancellor left unsigned is auto-signed into law at the end of the day.
            foreach (var id in _bills.Where(b => b.Stage == SWBillStage.AwaitingSignature && b.EmergencyCrisisId <= 0).Select(b => b.Id).ToList())
                SignBill(id);
        }

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;
        var drift = 1f - MathF.Pow(1f - DriftRate, elapsed / UpdateInterval);
        var inheritanceDecay = MathF.Pow(0.5f, elapsed / InheritanceHalfLifeSeconds);

        AutopilotTick();

        foreach (var (sectorId, globals) in _sectorGlobals)
        {
            foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
            {
                if (!globals.TryGetValue(global.ID, out var value))
                    continue;

                var target = GetSectorTarget(sectorId, global.ID);
                var inherited = _inheritance.TryGetValue(sectorId, out var carry)
                    && carry.TryGetValue(global.ID, out var previous) ? previous : 0f;
                var currentRoundValue = value - inherited;
                var remaining = inherited * inheritanceDecay;
                if (carry != null)
                    carry[global.ID] = remaining;
                globals[global.ID] = Math.Clamp(currentRoundValue + (target - currentRoundValue) * drift + remaining,
                    global.Min, global.Max);
            }
        }
    }
}
