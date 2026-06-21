using System.Linq;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate;

public sealed class SWSenateStateSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;

    private readonly Dictionary<string, Dictionary<string, float>> _sectorStats = new();
    private readonly Dictionary<string, string> _sectorHolders = new();
    private readonly List<SWSenateBill> _bills = new();
    private readonly List<SWSenateSession> _sessions = new();
    private readonly HashSet<int> _announcedSessions = new();
    private int _nextBillId = 1;
    private int _nextSessionId = 1;
    private int _scheduleDay = 1;

    private const float DefaultStat = 50f;
    private const float DriftRate = 0.01f;
    private const float UpdateInterval = 5f;

    private float _accumulator;

    public string? Chancellor { get; private set; }
    public string? Speaker { get; private set; }

    public IReadOnlyDictionary<string, Dictionary<string, float>> SectorStats => _sectorStats;
    public IReadOnlyDictionary<string, string> SectorHolders => _sectorHolders;
    public IReadOnlyList<SWSenateBill> Bills => _bills;

    public override void Initialize()
    {
        InitState();
    }

    private void InitState()
    {
        _sectorStats.Clear();
        _sectorHolders.Clear();
        _bills.Clear();
        _sessions.Clear();
        _announcedSessions.Clear();
        _nextBillId = 1;
        _nextSessionId = 1;
        _scheduleDay = _time.Day;
        Chancellor = null;
        Speaker = null;

        foreach (var sector in _proto.EnumeratePrototypes<SWSectorPrototype>())
        {
            var stats = new Dictionary<string, float>();
            foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>())
            {
                stats[stat.ID] = sector.StartingStats.TryGetValue(stat.ID, out var value)
                    ? Math.Clamp(value, stat.Min, stat.Max)
                    : DefaultStat;
            }

            _sectorStats[sector.ID] = stats;
        }
    }

    public Dictionary<string, float> GetRepublicStats()
    {
        var result = new Dictionary<string, float>();
        if (_sectorStats.Count == 0)
            return result;

        foreach (var stat in _proto.EnumeratePrototypes<SWStatPrototype>())
        {
            var sum = 0f;
            var count = 0;
            foreach (var (_, stats) in _sectorStats)
            {
                if (!stats.TryGetValue(stat.ID, out var value))
                    continue;
                sum += value;
                count++;
            }

            result[stat.ID] = count > 0 ? sum / count : 0f;
        }

        return result;
    }

    public bool IsClaimed(string sectorId) => _sectorHolders.ContainsKey(sectorId);

    public string? GetSectorHolder(string sectorId) =>
        _sectorHolders.TryGetValue(sectorId, out var holder) ? holder : null;

    public bool TryClaim(string sectorId, string holder)
    {
        if (!_sectorStats.ContainsKey(sectorId) || _sectorHolders.ContainsKey(sectorId))
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
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, true));
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
            RaiseLocalEvent(new SWSenateBillResolvedEvent(bill.Name, true));
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

            RaiseLocalEvent(new SWSenateSessionStartedEvent(GetBillName(session.BillId), session.StartHour, session.EndHour));
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

            if (IsReserved(hour))
            {
                row.Text = ReservedLabel(hour);
                row.Color = Color.DarkGray;
            }
            else
            {
                var session = _sessions.FirstOrDefault(s =>
                    s.Status != SWSessionStatus.Rejected && hour >= s.StartHour && hour < s.EndHour);

                if (session == null)
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

            options.Add(new SWBillOption { Id = bill.Id, Name = bill.Name });
        }

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
            var effects = new List<SWBillEffectEntry>();
            foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
            {
                if (!bill.Effects.TryGetValue(proto.ID, out var delta))
                    continue;

                effects.Add(new SWBillEffectEntry { StatName = proto.ShortName, Color = proto.Color, Delta = delta });
            }

            bills.Add(new SWBillEntry
            {
                Id = bill.Id,
                Name = bill.Name,
                Author = bill.Author,
                Description = bill.Description,
                Stage = BillStageLabel(bill),
                Effects = effects,
            });
        }

        return bills;
    }

    public List<SWBillEffectEntry> BuildBillEffects(SWSenateBill bill)
    {
        var effects = new List<SWBillEffectEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWStatPrototype>().OrderBy(s => s.Order))
        {
            if (!bill.Effects.TryGetValue(proto.ID, out var delta))
                continue;

            effects.Add(new SWBillEffectEntry { StatName = proto.ShortName, Color = proto.Color, Delta = delta });
        }

        return effects;
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
        foreach (var (statId, delta) in bill.Effects)
        {
            if (!_proto.TryIndex<SWStatPrototype>(statId, out var statProto))
                continue;

            foreach (var (_, stats) in _sectorStats)
            {
                if (!stats.TryGetValue(statId, out var value))
                    continue;

                stats[statId] = Math.Clamp(value + delta, statProto.Min, statProto.Max);
            }
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

        var republic = GetRepublicStats();
        foreach (var (_, stats) in _sectorStats)
        {
            foreach (var statId in stats.Keys.ToList())
            {
                if (!republic.TryGetValue(statId, out var target))
                    continue;

                stats[statId] += (target - stats[statId]) * DriftRate;
            }
        }
    }
}
