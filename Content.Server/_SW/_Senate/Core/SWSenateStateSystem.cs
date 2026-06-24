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
        _sessions.Clear();
        _announcedSessions.Clear();
        _bills.RemoveAll(b => b.Stage != SWBillStage.Passed);
        _nextSessionId = 1;
        _scheduleDay = _time.Day;
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
    /// Additive push on a sector global (used by the crisis engine for reversible pressure — the drift
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

    public bool TryTakeChancellor(string holder)
    {
        if (Chancellor != null)
            return false;

        Chancellor = holder;
        return true;
    }

    public void ResignChancellor() => Chancellor = null;

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
            bill.Stage = SWBillStage.Passed;
            ApplyBill(bill);
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, true, bill.EmergencyCrisisId, bill.EmergencyRelief));
        }
        else
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
        }

        return true;
    }

    private void ResolveBill(SWSenateBill bill, bool allowTiebreak)
    {
        var votesFor = bill.Votes.Values.Count(v => v == SWVoteChoice.For);
        var votesAgainst = bill.Votes.Values.Count(v => v == SWVoteChoice.Against);

        if (votesFor > votesAgainst)
        {
            bill.Stage = SWBillStage.Passed;
            ApplyBill(bill);
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, true, bill.EmergencyCrisisId, bill.EmergencyRelief));
        }
        else if (votesAgainst > votesFor)
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
        }
        else if (allowTiebreak)
        {
            bill.AwaitingTiebreak = true;
        }
        else
        {
            bill.Stage = SWBillStage.Rejected;
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, false));
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
            if (bill is { Stage: SWBillStage.Voting })
            {
                bill.AwaitingTiebreak = false;
                ResolveBill(bill, false);
            }
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
        if (_bills.All(b => b.Id != billId) || !CanSchedule(startHour, duration))
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

    /// <summary>
    /// Speaker's emergency power: book an emergency bill into the next game hour, overriding whatever was
    /// there (a displaced bill session is dropped so it returns to the schedulable pool). Auto-confirmed —
    /// no Chancellor sign-off. Only works on emergency ("СРОЧНО:") bills.
    /// </summary>
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

            // Emergency sessions take priority even over reserved hours (lunch/personal time).
            if (session is { Emergency: true })
            {
                row.Text = hour == session.StartHour
                    ? $"СРОЧНО: {GetBillName(session.BillId)} ({session.StartHour:00}:00–{session.EndHour:00}:00)"
                    : "   — продолжение";
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
            }
            else if (hour == session.StartHour)
            {
                var status = session.Status == SWSessionStatus.Confirmed ? "подтверждено" : "ожидает";
                row.Text = $"Заседание: {GetBillName(session.BillId)} ({session.StartHour:00}:00–{session.EndHour:00}:00) [{status}]";
                row.Color = session.Status == SWSessionStatus.Confirmed ? Color.LightGreen : Color.Goldenrod;
            }
            else
            {
                row.Text = "   — продолжение";
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
            if (_sessions.Any(s => s.BillId == bill.Id && s.Status != SWSessionStatus.Rejected))
                continue;

            options.Add(new SWBillOption { Id = bill.Id, Name = bill.Name, Emergency = bill.Emergency });
        }

        // Emergency measures float to the top so the Speaker sees them first.
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
                TimeRange = $"{session.StartHour:00}:00–{session.EndHour:00}:00",
                Status = "ожидает",
            });
        }

        return pending;
    }

    public string GetBillName(int billId) =>
        _bills.FirstOrDefault(b => b.Id == billId)?.Name ?? "—";

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
            return Loc.GetString("sw-senate-bill-stage-passed");
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
        ResolveExpiredSessions();
        AnnounceStartedSessions();

        if (_time.Day != _scheduleDay)
        {
            _scheduleDay = _time.Day;
            _sessions.Clear();
            _announcedSessions.Clear();
        }

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator = 0f;

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
