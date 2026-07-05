using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Content.Shared._SW._Senate.Bills;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateStateSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private readonly Dictionary<string, float> _republicSubStats = new();
    private readonly Dictionary<string, Dictionary<string, float>> _sectorGlobals = new();
    private readonly Dictionary<string, Dictionary<string, float>> _sectorBias = new();
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
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitState();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // World state (republic policy, sector globals/bias, passed laws) carries over between rounds.
        // Session state resets: every office and sector claim is vacated so the new round starts empty,
        // and in-progress bills/sessions are dropped (their authors are gone).
        _sectorHolders.Clear();
        Chancellor = null;
        Speaker = null;
        _barredChancellors.Clear();
        _sessions.Clear();
        _announcedSessions.Clear();
        _bills.RemoveAll(b => b.Stage != SWBillStage.Passed || b.SpecialMotionType != null || b.IsElection);
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
                bias[global.ID] = start - NeutralStat;
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
    // Worsening changes (delta > 0) bite several times harder when the stat is low and are dampened when
    // it is high; improving changes (delta < 0) are weaker at low stats and stronger at high stats.
    public float StatPressureScale(string governingGlobalId, float delta)
    {
        var stat = GetRepublicGlobal(governingGlobalId);
        if (delta >= 0f)
            return Math.Clamp(NeutralStat / Math.Max(stat, 1f), 0.4f, 3.5f);

        return Math.Clamp(stat / NeutralStat, 0.6f, 1.6f);
    }

    public List<SWGlobalStatEntry> BuildGlobalEntries()
    {
        var policy = GetPolicyScores();
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
                Policy = policy.TryGetValue(global.ID, out var p) ? p : NeutralStat,
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

    public void ResignChancellor() => Chancellor = null;

    public bool IsBarredChancellor(string holder) => _barredChancellors.Contains(holder);

    public string? OustChancellor()
    {
        var name = Chancellor;
        Chancellor = null;
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

    public void ResignSpeaker() => Speaker = null;

    public bool HasActiveBill(string author) =>
        _bills.Any(b => b.Stage == SWBillStage.Voting && b.Author == author);

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
            _vacancyHours = 0f;
            return;
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
        if (_sessions.Any(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected))
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

        var bill = _bills.FirstOrDefault(b => b.Id == session.BillId);
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
            var count = bill.ElectionVotes.Values.Count(v => v == candidate);
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
            if (_sessions.Any(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected))
                continue;

            bill.EmergencyWaitHours += gameHours;
            if (bill.EmergencyWaitHours < EmergencySessionDelayHours)
                continue;

            var result = ScheduleEmergencySession(bill.Id);
            if (result != null)
                RaiseLocalEvent(new SWSenateEmergencySessionScheduledEvent(bill.Name, result.StartHour, result.DisplacedBill));
        }
    }

    private void ResolveElectionByPlurality(SWSenateBill bill)
    {
        string? winner = null;
        var best = -1;
        foreach (var candidate in bill.Candidates)
        {
            var count = bill.ElectionVotes.Values.Count(v => v == candidate);
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

        var hour = _time.CurrentHour;
        return _sessions.FirstOrDefault(s =>
            s.Status == SWSessionStatus.Confirmed && hour >= s.StartHour && hour < s.EndHour);
    }

    public SWSenateBill? GetCurrentBill()
    {
        var session = GetActiveSession();
        if (session == null)
            return null;

        var bill = _bills.FirstOrDefault(b => b.Id == session.BillId);
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

        var votesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);
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

        return true;
    }

    // The Chancellor (or acting officer) ratifies a passed bill - only now does it apply and get announced.
    public bool SignBill(int billId)
    {
        var bill = _bills.FirstOrDefault(b => b.Id == billId && b.Stage == SWBillStage.AwaitingSignature);
        if (bill == null)
            return false;

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

            list.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Description = bill.Description,
                Stage = Loc.GetString("sw-senate-bill-stage-signature"),
                Effects = BuildBillEffects(bill),
            });
        }

        return list;
    }

    private void RaiseVotesResolved(SWSenateBill bill, bool passed) =>
        RaiseLocalEvent(new SWSenateBillVotesResolvedEvent(bill.Id, bill.Name, passed, new Dictionary<string, SWVoteChoice>(bill.Votes)));

    private void ResolveBill(SWSenateBill bill, bool allowTiebreak)
    {
        if (bill.SpecialMotionType != null)
        {
            ResolveSpecialMotionBill(bill);
            return;
        }

        var votesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);

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
                session.Status = SWSessionStatus.Confirmed;
        }

        // No Chancellor to ratify - passed bills are auto-signed into law.
        foreach (var id in _bills.Where(b => b.Stage == SWBillStage.AwaitingSignature).Select(b => b.Id).ToList())
            SignBill(id);

        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Voting)
                continue;
            if (_sessions.Any(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected))
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
            if (hour < session.StartHour || hour >= session.EndHour)
                continue;
            if (!_announcedSessions.Add(session.Id))
                continue;

            RaiseLocalEvent(new SWSenateSessionStartedEvent(GetBillName(session.BillId), session.StartHour, session.EndHour, session.Emergency));
        }
    }

    public List<SWBillEntry> BuildActiveLaws()
    {
        var laws = new List<SWBillEntry>();
        foreach (var bill in _bills)
        {
            if (bill.Stage != SWBillStage.Passed)
                continue;
            if (bill.SpecialMotionType != null)
                continue;
            if (bill.IsElection)
                continue;

            laws.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Description = bill.Description,
                Stage = Loc.GetString("sw-senate-bill-stage-passed"),
                Effects = BuildBillEffects(bill),
            });
        }

        return laws;
    }

    private void ResolveExpiredSessions()
    {
        foreach (var session in _sessions)
        {
            if (session.Status != SWSessionStatus.Confirmed)
                continue;

            var expired = !_time.DayActive || _time.CurrentHour >= session.EndHour;
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

    public IReadOnlyList<SWSenateSession> Sessions => _sessions;

    public static bool IsReserved(int hour) => hour < 9 || hour == 14 || hour >= 20;

    public static string ReservedLabel(int hour)
    {
        if (hour == 14)
            return "Обед";
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
        if (bill == null || bill.Emergency || bill.IsElection || !CanSchedule(startHour, duration))
            return null;

        if (_sessions.Any(s => s.BillId == billId && s.Status != SWSessionStatus.Rejected))
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

    public bool ConfirmSession(int sessionId)
    {
        var session = _sessions.FirstOrDefault(s => s.Id == sessionId && s.Status == SWSessionStatus.Pending);
        if (session == null)
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

        if (_sessions.Any(s => s.BillId == billId && s.Status != SWSessionStatus.Rejected))
            return null;

        var targetHour = _time.CurrentHour + 1;
        if (targetHour < SWSenateTimeSystem.StartHour || targetHour >= SWSenateTimeSystem.EndHour)
            return null;

        string? displaced = null;
        foreach (var occupied in _sessions
                     .Where(s => s.Status != SWSessionStatus.Rejected && targetHour >= s.StartHour && targetHour < s.EndHour)
                     .ToList())
        {
            displaced ??= GetBillName(occupied.BillId);
            _sessions.Remove(occupied);
            _announcedSessions.Remove(occupied.Id);
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
                s.Status != SWSessionStatus.Rejected && hour >= s.StartHour && hour < s.EndHour);

            if (session is { Emergency: true })
            {
                row.Text = hour == session.StartHour
                    ? $"СРОЧНО: {GetBillName(session.BillId)} ({session.StartHour:00}:00-{session.EndHour:00}:00)"
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
            else if (hour == session.StartHour)
            {
                var status = session.Status == SWSessionStatus.Confirmed ? "подтверждено" : "ожидает";
                row.Text = $"Заседание: {GetBillName(session.BillId)} ({session.StartHour:00}:00-{session.EndHour:00}:00) [{status}]";
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
            if (_sessions.Any(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected))
                continue;

            options.Add(new SWBillOption { Id = bill.Id, Name = bill.Name, Emergency = bill.Emergency });
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
                BillName = GetBillName(session.BillId),
                TimeRange = $"{session.StartHour:00}:00-{session.EndHour:00}:00",
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
        {
            bills.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Description = bill.Description,
                Stage = BillStageLabel(bill),
                Effects = BuildBillEffects(bill),
            });
        }

        return bills;
    }

    public List<SWBillEffectEntry> BuildBillEffects(SWSenateBill bill)
    {
        var effects = new List<SWBillEffectEntry>();
        foreach (var sub in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!bill.Effects.TryGetValue(sub.ID, out var delta))
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
        if (bill.Stage == SWBillStage.Passed)
            return bill.IsElection ? Loc.GetString("sw-senate-bill-stage-election-done") : Loc.GetString("sw-senate-bill-stage-passed");
        if (bill.Stage == SWBillStage.AwaitingSignature)
            return Loc.GetString("sw-senate-bill-stage-signature");
        if (bill.Stage == SWBillStage.Rejected)
            return Loc.GetString("sw-senate-bill-stage-rejected");
        if (bill.AwaitingTiebreak)
            return Loc.GetString("sw-senate-bill-stage-tiebreak");

        var session = _sessions.FirstOrDefault(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected);
        if (session == null)
            return Loc.GetString("sw-senate-bill-stage-awaiting");
        if (session.Status == SWSessionStatus.Pending)
            return Loc.GetString("sw-senate-bill-stage-pending");

        if (_time.DayActive && _time.CurrentHour >= session.StartHour && _time.CurrentHour < session.EndHour)
            return Loc.GetString("sw-senate-bill-stage-voting");

        return Loc.GetString("sw-senate-bill-stage-scheduled", ("time", $"{session.StartHour:00}:00"));
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

    public override void Update(float frameTime)
    {
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
            _sessions.Clear();
            _announcedSessions.Clear();

            // Any bill the Chancellor left unsigned is auto-signed into law at the end of the day.
            foreach (var id in _bills.Where(b => b.Stage == SWBillStage.AwaitingSignature).Select(b => b.Id).ToList())
                SignBill(id);
        }

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

        AutopilotTick();

        var policy = GetPolicyScores();
        foreach (var (sectorId, globals) in _sectorGlobals)
        {
            if (!_sectorBias.TryGetValue(sectorId, out var bias))
                continue;

            foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>())
            {
                if (!globals.TryGetValue(global.ID, out var value))
                    continue;

                var policyScore = policy.TryGetValue(global.ID, out var p) ? p : NeutralStat;
                var offset = bias.TryGetValue(global.ID, out var b) ? b : 0f;
                var target = Math.Clamp(policyScore + offset, global.Min, global.Max);
                globals[global.ID] = value + (target - value) * DriftRate;
            }
        }
    }
}
