using System.Linq;
using Content.Server.GameTicking;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Court;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed class SWSenateCourtSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;

    public const int MorningStart = 9;
    public const int MorningEnd = 13;
    public const int MorningDeadline = 14;
    public const int MorningRetryMinute = 30;
    public const int EveningCard = 14;
    public const int EveningStart = 15;
    public const int EveningEnd = 19;
    public const int EveningDeadline = 20;
    public const int NominationOpenHour = 10;
    public const float ConfirmHours = 2f;
    public const int ReviewHours = 2;
    public const int OfficialCorruptionThreshold = 45;

    public string? Chief { get; private set; }
    private readonly List<string> _judges = new();
    public IReadOnlyList<string> Judges => _judges;
    public string? Prosecutor { get; private set; }
    public string? Defender { get; private set; }

    private sealed class Nomination
    {
        public string Nominee = string.Empty;
        public bool ForChief;
        public bool Accepted;
        public readonly Dictionary<string, bool> Votes = new();
        public int DeadlineAbs;
    }

    private Nomination? _nomination;

    private sealed class ActiveCase
    {
        public int Id;
        public string ProtoId = string.Empty;
        public int Day;
        public int Slot;
        public string PlaintiffLabel = string.Empty;
        public string DefendantLabel = string.Empty;
        public string PlaintiffSector = string.Empty;
        public string DefendantSector = string.Empty;
        public string OfficialName = string.Empty;
        public string Presiding = string.Empty;
        public readonly Dictionary<string, bool> Votes = new();
        public bool Closed;
        public bool Unresolved;
        public bool PlaintiffWon;
    }

    private readonly List<ActiveCase> _cases = new();
    private readonly HashSet<string> _usedProtos = new();
    private readonly HashSet<string> _prosecuted = new();
    private int _nextCaseNumber = 1;

    private sealed class Review
    {
        public int LawId;
        public string LawName = string.Empty;
        public int DeadlineAbs;
        public readonly Dictionary<string, bool> Votes = new();
    }

    private Review? _review;

    private readonly Dictionary<string, List<string>> _notices = new();
    private float _accumulator;
    private int _morningAttemptDay;
    private bool _morningInitialAttempted;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        Chief = null;
        _judges.Clear();
        Prosecutor = null;
        Defender = null;
        _nomination = null;
        _cases.Clear();
        _usedProtos.Clear();
        _prosecuted.Clear();
        _nextCaseNumber = 1;
        _review = null;
        _notices.Clear();
        _morningAttemptDay = 0;
        _morningInitialAttempted = false;
    }

    public bool TakeOffice(SWSenatePosition position, string name)
    {
        if (!CanTakeOffice(position, name))
            return false;

        switch (position)
        {
            case SWSenatePosition.ChiefJustice:
                if (Chief != null)
                    return false;
                Chief = name;
                return true;
            case SWSenatePosition.Judge:
                if (_judges.Count >= 2)
                    return false;
                _judges.Add(name);
                return true;
            case SWSenatePosition.Prosecutor:
                if (Prosecutor != null)
                    return false;
                Prosecutor = name;
                return true;
            case SWSenatePosition.Defender:
                if (Defender != null)
                    return false;
                Defender = name;
                return true;
            default:
                return false;
        }
    }

    public bool CanTakeOffice(SWSenatePosition position, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || IsJudge(name) || Prosecutor == name || Defender == name)
            return false;

        return SeatFree(position);
    }

    public bool SeatFree(SWSenatePosition position) => position switch
    {
        SWSenatePosition.ChiefJustice => Chief == null,
        SWSenatePosition.Judge => _judges.Count < 2,
        SWSenatePosition.Prosecutor => Prosecutor == null,
        SWSenatePosition.Defender => Defender == null,
        _ => false,
    };

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive)
            return;

        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        _accumulator = 0f;

        if (!_time.DayActive)
            return;

        if (ShouldAttemptMorningCase())
            GenerateCase(0, false);
        if (_time.CurrentHour >= EveningCard)
            GenerateCase(1, false);

        foreach (var c in _cases)
        {
            if (!c.Closed && c.Day == _time.Day && _time.AbsoluteHour >= DeadlineAbs(c))
                ResolveCase(c);
        }

        if (_nomination is { Accepted: true } nom && _time.AbsoluteHour >= nom.DeadlineAbs)
            ResolveNomination();

        if (_review is { } review && _time.AbsoluteHour >= review.DeadlineAbs)
            ResolveReview();
    }

    // --- bench ---

    public bool IsJudge(string name) => Chief == name || _judges.Contains(name);
    public bool MustPromoteChief => Chief == null && _judges.Count > 0;
    public int SeatsTaken => _judges.Count + (Chief != null ? 1 : 0);

    public string BenchLabel()
    {
        if (SeatsTaken == 0)
            return "Состав Верховного Суда не сформирован";

        var chief = Chief != null ? $"Верховный Судья {Chief}" : "Верховный Судья не назначен";
        var judges = _judges.Count > 0 ? $"судьи: {string.Join(", ", _judges)}" : "республиканские судьи не назначены";
        return $"{chief}; {judges}";
    }

    public List<SWCourtJudgeEntry> BuildJudges()
    {
        var list = new List<SWCourtJudgeEntry>();
        if (Chief != null)
            list.Add(new SWCourtJudgeEntry { Name = Chief, IsChief = true });
        foreach (var judge in _judges)
            list.Add(new SWCourtJudgeEntry { Name = judge });
        return list;
    }

    public bool Resign(string name)
    {
        if (Chief == name)
        {
            Chief = null;
            Announce($"Верховный Судья {name} сложил полномочия.");
            return true;
        }

        if (_judges.Remove(name))
        {
            Announce($"Республиканский Судья {name} сложил полномочия.");
            return true;
        }

        if (Prosecutor == name)
        {
            Prosecutor = null;
            return true;
        }

        if (Defender == name)
        {
            Defender = null;
            return true;
        }

        return false;
    }

    // --- nomination ---

    public bool NominationsOpen => _time.Day > 1 || _time.CurrentHour >= NominationOpenHour;
    public string? PendingNominee => _nomination?.Nominee;
    public bool PendingForChief => _nomination?.ForChief ?? false;
    public bool PendingAccepted => _nomination?.Accepted ?? false;

    public string PendingLabel()
    {
        if (_nomination == null)
            return string.Empty;

        var seat = _nomination.ForChief ? "Верховного Судьи" : "Республиканского Судьи";
        return _nomination.Accepted
            ? $"Сенат голосует по кандидатуре {_nomination.Nominee} на пост {seat}."
            : $"Ожидается ответ {_nomination.Nominee} на предложение поста {seat}.";
    }

    public List<string> BuildCandidates()
    {
        var list = new List<string>();
        foreach (var (_, holder) in _senate.SectorHolders)
            if (!list.Contains(holder))
                list.Add(holder);
        foreach (var (_, holder) in _ministry.Holders)
            if (!list.Contains(holder))
                list.Add(holder);
        if (_senate.Speaker != null && !list.Contains(_senate.Speaker))
            list.Add(_senate.Speaker);
        if (Prosecutor != null && !list.Contains(Prosecutor))
            list.Add(Prosecutor);
        if (Defender != null && !list.Contains(Defender))
            list.Add(Defender);

        list.RemoveAll(name => name == _senate.Chancellor || IsJudge(name));
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public bool Nominate(string name, bool forChief)
    {
        if (!NominationsOpen || _nomination != null || string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (name == _senate.Chancellor)
            return false;

        if (forChief)
        {
            if (Chief != null || !_judges.Contains(name))
                return false;
        }
        else
        {
            if (MustPromoteChief || SeatsTaken >= 3 || IsJudge(name))
                return false;
        }

        _nomination = new Nomination { Nominee = name, ForChief = forChief };
        AddNotice(name, forChief
            ? "Канцлер предлагает вам пост Верховного Судьи. Ответьте в рабочей консоли."
            : "Канцлер предлагает вам пост Республиканского Судьи. Ответьте в рабочей консоли.");
        return true;
    }

    public bool IsNominee(string name) => _nomination is { Accepted: false } nom && nom.Nominee == name;

    public string NominationLabel(string name)
    {
        if (!IsNominee(name))
            return string.Empty;

        return _nomination!.ForChief
            ? "Канцлер предлагает вам пост Верховного Судьи Республики."
            : "Канцлер предлагает вам пост Республиканского Судьи.";
    }

    public bool RespondNomination(string name, bool accept)
    {
        if (!IsNominee(name))
            return false;

        if (!accept)
        {
            if (_senate.Chancellor is { } chancellor)
                AddNotice(chancellor, $"{name} отклонил предложение судейского поста.");
            _nomination = null;
            return true;
        }

        _nomination!.Accepted = true;
        _nomination.DeadlineAbs = _time.AbsoluteHour + (int) ConfirmHours;
        Announce($"Сенат рассматривает кандидатуру {name} на пост {(_nomination.ForChief ? "Верховного Судьи" : "Республиканского Судьи")}. Голосование в консоли голосования.");
        return true;
    }

    public bool HasConfirmationVote => _nomination is { Accepted: true };

    public string ConfirmationLabel() =>
        _nomination is { Accepted: true } nom
            ? $"Утверждение: {nom.Nominee} - {(nom.ForChief ? "Верховный Судья" : "Республиканский Судья")}"
            : string.Empty;

    public (int yes, int no) ConfirmationTally()
    {
        if (_nomination is not { Accepted: true } nom)
            return (0, 0);

        var yes = nom.Votes.Count(kv => _senate.SectorHolders.ContainsKey(kv.Key) && kv.Value);
        var no = nom.Votes.Count(kv => _senate.SectorHolders.ContainsKey(kv.Key) && !kv.Value);
        return (yes, no);
    }

    public int ConfirmationVoteOf(string sectorId) =>
        _nomination is { Accepted: true } nom && nom.Votes.TryGetValue(sectorId, out var v) ? (v ? 1 : 2) : 0;

    public bool CastConfirmationVote(string sectorId, bool approve)
    {
        if (_nomination is not { Accepted: true } nom || !_senate.SectorHolders.ContainsKey(sectorId))
            return false;

        nom.Votes[sectorId] = approve;

        var electorate = _senate.SectorHolders.Count;
        var (yes, no) = ConfirmationTally();
        var majority = electorate / 2 + 1;
        if (yes >= majority || yes + no >= electorate)
            ResolveNomination();

        return true;
    }

    private void ResolveNomination()
    {
        if (_nomination is not { Accepted: true } nom)
            return;

        var electorate = Math.Max(1, _senate.SectorHolders.Count);
        var (yes, _) = ConfirmationTally();
        var confirmed = yes >= electorate / 2 + 1;
        _nomination = null;

        if (!confirmed)
        {
            Announce($"Сенат отклонил кандидатуру {nom.Nominee}.");
            if (_senate.Chancellor is { } chancellor)
                AddNotice(chancellor, $"Сенат отклонил кандидатуру {nom.Nominee}. Предложите другого кандидата.");
            return;
        }

        Appoint(nom.Nominee, nom.ForChief);
    }

    private void Appoint(string name, bool forChief)
    {
        foreach (var (sectorId, holder) in _senate.SectorHolders.ToList())
        {
            if (holder == name)
                _senate.Unclaim(sectorId);
        }

        foreach (var (ministryId, holder) in _ministry.Holders.ToList())
        {
            if (holder == name)
                _ministry.Unclaim(ministryId);
        }

        if (_senate.Speaker == name)
            _senate.ResignSpeaker();
        if (Prosecutor == name)
            Prosecutor = null;
        if (Defender == name)
            Defender = null;

        if (forChief)
        {
            _judges.Remove(name);
            Chief = name;
            Announce($"Сенат утвердил {name} в должности Верховного Судьи Республики.");
        }
        else
        {
            if (!_judges.Contains(name))
                _judges.Add(name);
            Announce($"Сенат утвердил {name} в должности Республиканского Судьи.");
        }

        AddNotice(name, "Назначение утверждено. Перерегистрируйте карту в судебной рабочей консоли.");
    }

    // --- cases ---

    private static int HearingStart(int slot) => slot == 0 ? MorningStart : EveningStart;
    private static int HearingEnd(int slot) => slot == 0 ? MorningEnd : EveningEnd;
    private static int Deadline(int slot) => slot == 0 ? MorningDeadline : EveningDeadline;
    private int DeadlineAbs(ActiveCase c) => c.Day * 24 + Deadline(c.Slot);

    private bool ShouldAttemptMorningCase()
    {
        if (_morningAttemptDay != _time.Day)
        {
            _morningAttemptDay = _time.Day;
            _morningInitialAttempted = false;
        }

        if (_time.CurrentHour >= MorningStart)
            return false;

        if (!_morningInitialAttempted && _time.CurrentHour >= SWSenateTimeSystem.StartHour)
        {
            _morningInitialAttempted = true;
            return true;
        }

        return _time.CurrentHour > SWSenateTimeSystem.StartHour
            || (_time.CurrentHour == SWSenateTimeSystem.StartHour && _time.CurrentMinute >= MorningRetryMinute);
    }

    public bool AdminGenerateCase(string? protoId)
    {
        var slot = !_time.DayActive || _time.CurrentHour < EveningCard ? 0 : 1;
        return GenerateCase(slot, true, protoId);
    }

    private bool GenerateCase(int slot, bool force, string? protoId = null)
    {
        if (_cases.Any(c => c.Day == _time.Day && c.Slot == slot))
            return false;

        // No bench - no card. The morning slot gets a delayed 07:30 retry;
        // regular generation never creates a case after the verdict deadline.
        if (!force && SeatsTaken == 0)
            return false;
        if (!force && _time.AbsoluteHour >= _time.Day * 24 + Deadline(slot))
            return false;

        var sectors = _senate.SectorGlobals.Keys.ToList();
        var official = FindOfficialFigurant();

        var pool = _proto.EnumeratePrototypes<SWCourtCasePrototype>()
            .Where(p => force || !_usedProtos.Contains(p.ID))
            .ToList();
        _random.Shuffle(pool);

        SWCourtCasePrototype? pick = null;
        if (protoId != null)
        {
            if (!_proto.TryIndex<SWCourtCasePrototype>(protoId, out pick))
                return false;
            if (!force && _usedProtos.Contains(pick.ID))
                return false;
            if (!CanGenerateCase(pick, sectors.Count, official))
                return false;
        }

        if (pick == null && official != null)
            pick = pool.FirstOrDefault(p => p.CaseType == SWCourtCaseType.RepublicVsOfficial);

        if (pick == null)
        {
            foreach (var proto in pool)
            {
                if (CanGenerateCase(proto, sectors.Count, official))
                {
                    pick = proto;
                    break;
                }
            }
        }

        if (pick == null)
            return false;

        _usedProtos.Add(pick.ID);

        var c = new ActiveCase
        {
            Id = _nextCaseNumber++,
            ProtoId = pick.ID,
            Day = _time.Day,
            Slot = slot,
        };

        switch (pick.CaseType)
        {
            case SWCourtCaseType.SectorVsRepublic:
                c.PlaintiffSector = _random.Pick(sectors);
                c.PlaintiffLabel = SectorName(c.PlaintiffSector);
                c.DefendantLabel = "Галактическая Республика";
                break;
            case SWCourtCaseType.RepublicVsSector:
                c.DefendantSector = _random.Pick(sectors);
                c.PlaintiffLabel = "Галактическая Республика";
                c.DefendantLabel = SectorName(c.DefendantSector);
                break;
            case SWCourtCaseType.SectorVsSector:
                var pair = sectors.OrderBy(_ => _random.Next()).Take(2).ToList();
                c.PlaintiffSector = pair[0];
                c.DefendantSector = pair[1];
                c.PlaintiffLabel = SectorName(c.PlaintiffSector);
                c.DefendantLabel = SectorName(c.DefendantSector);
                break;
            case SWCourtCaseType.CitizenVsSector:
                c.DefendantSector = _random.Pick(sectors);
                c.PlaintiffLabel = pick.PlaintiffName.Length > 0 ? pick.PlaintiffName : "Гражданский истец";
                c.DefendantLabel = SectorName(c.DefendantSector);
                break;
            case SWCourtCaseType.RepublicVsOfficial:
                c.OfficialName = official!;
                _prosecuted.Add(official!);
                c.PlaintiffLabel = "Галактическая Республика";
                c.DefendantLabel = official!;
                break;
        }

        c.Presiding = Chief ?? (_judges.Count > 0 ? _random.Pick(_judges) : string.Empty);
        _cases.Add(c);

        foreach (var judge in BuildJudges())
            AddNotice(judge.Name, $"Дело №{c.Id} «{Fill(pick.Name, c)}» назначено на {HearingStart(c.Slot):00}:00.");
        if (Prosecutor != null)
            AddNotice(Prosecutor, $"Получены материалы дела №{c.Id}: «{Fill(pick.Name, c)}».");
        if (Defender != null)
            AddNotice(Defender, $"Получены материалы дела №{c.Id}: «{Fill(pick.Name, c)}».");

        return true;
    }

    private static bool CanGenerateCase(SWCourtCasePrototype proto, int sectors, string? official) => proto.CaseType switch
    {
        SWCourtCaseType.SectorVsSector => sectors >= 2,
        SWCourtCaseType.RepublicVsOfficial => official != null,
        _ => sectors >= 1,
    };

    private string? FindOfficialFigurant()
    {
        foreach (var (name, value) in _corruption.Persons.OrderByDescending(kv => kv.Value))
        {
            if (value < OfficialCorruptionThreshold || _prosecuted.Contains(name) || IsJudge(name))
                continue;
            if (name == Prosecutor || name == Defender)
                continue;
            if (!HoldsOffice(name))
                continue;

            return name;
        }

        return null;
    }

    private bool HoldsOffice(string name) =>
        _senate.SectorHolders.Values.Contains(name)
        || _ministry.Holders.Values.Contains(name)
        || _senate.Speaker == name;

    public bool CastVerdictVote(string judge, int caseId, bool forPlaintiff)
    {
        if (!IsJudge(judge))
            return false;

        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (c == null || c.Closed || c.Day != _time.Day || !_time.DayActive)
            return false;

        if (_time.CurrentHour < HearingStart(c.Slot) || _time.AbsoluteHour >= DeadlineAbs(c))
            return false;

        c.Votes[judge] = forPlaintiff;
        return true;
    }

    public bool CloseVerdictVote(string judge, int caseId)
    {
        if (!CanCloseVerdictVote(judge, caseId))
            return false;

        var c = _cases.First(x => x.Id == caseId);
        ResolveCase(c);
        return true;
    }

    private bool CanCloseVerdictVote(string judge, int caseId)
    {
        if (!IsJudge(judge))
            return false;

        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (c == null || c.Closed || c.Day != _time.Day || !_time.DayActive)
            return false;

        if (_time.CurrentHour < HearingEnd(c.Slot) || _time.AbsoluteHour >= DeadlineAbs(c))
            return false;

        return Chief == judge || c.Presiding == judge;
    }

    private void ResolveCase(ActiveCase c)
    {
        if (c.Closed)
            return;

        c.Closed = true;

        var bench = BuildJudges().Select(j => j.Name).ToList();
        var plaintiff = c.Votes.Count(kv => bench.Contains(kv.Key) && kv.Value);
        var defendant = c.Votes.Count(kv => bench.Contains(kv.Key) && !kv.Value);

        if (plaintiff + defendant == 0)
        {
            c.Unresolved = true;
            return;
        }

        bool plaintiffWon;
        if (plaintiff != defendant)
        {
            plaintiffWon = plaintiff > defendant;
        }
        else
        {
            var decider = Chief ?? c.Presiding;
            if (decider.Length == 0 || !c.Votes.TryGetValue(decider, out var deciderVote))
            {
                c.Unresolved = true;
                return;
            }

            plaintiffWon = deciderVote;
        }

        c.PlaintiffWon = plaintiffWon;
        if (!_proto.TryIndex<SWCourtCasePrototype>(c.ProtoId, out var proto))
            return;

        var outcome = plaintiffWon ? proto.PlaintiffWin : proto.DefendantWin;
        ApplyOutcome(c, proto, outcome);
    }

    private void ApplyOutcome(ActiveCase c, SWCourtCasePrototype proto, SWCourtOutcome outcome)
    {
        foreach (var (subId, delta) in outcome.PolicyEffects)
            _senate.AddPolicy(subId, delta);

        if (c.PlaintiffSector.Length > 0)
        {
            foreach (var (globalId, delta) in outcome.PlaintiffSectorEffects)
                _senate.AddSectorBias(c.PlaintiffSector, globalId, delta);
            _budget.Grant(c.PlaintiffSector, outcome.PlaintiffBudget);
        }

        if (c.DefendantSector.Length > 0)
        {
            foreach (var (globalId, delta) in outcome.DefendantSectorEffects)
                _senate.AddSectorBias(c.DefendantSector, globalId, delta);
            _budget.Grant(c.DefendantSector, outcome.DefendantBudget);
        }

        if (proto.CaseType == SWCourtCaseType.RepublicVsOfficial && c.PlaintiffWon && c.OfficialName.Length > 0)
        {
            foreach (var (sectorId, holder) in _senate.SectorHolders.ToList())
            {
                if (holder == c.OfficialName)
                    _senate.Unclaim(sectorId);
            }

            foreach (var (ministryId, holder) in _ministry.Holders.ToList())
            {
                if (holder == c.OfficialName)
                    _ministry.Unclaim(ministryId);
            }

            if (_senate.Speaker == c.OfficialName)
                _senate.ResignSpeaker();
        }
    }

    // --- constitutional review ---

    public bool StartReview(string name, int lawId)
    {
        if (Chief != name || _review != null)
            return false;

        var lawName = _senate.LawName(lawId);
        if (lawName == null || !_senate.BuildRepealableLaws().Any(l => l.Id == lawId))
            return false;

        _review = new Review { LawId = lawId, LawName = lawName, DeadlineAbs = _time.AbsoluteHour + ReviewHours };
        Announce($"Верховный Суд начал проверку закона «{lawName}» на конституционность.");
        return true;
    }

    public bool CastReviewVote(string judge, bool unconstitutional)
    {
        if (_review == null || !IsJudge(judge))
            return false;

        if (_time.AbsoluteHour >= _review.DeadlineAbs)
        {
            ResolveReview();
            return false;
        }

        _review.Votes[judge] = unconstitutional;

        var bench = BuildJudges();
        if (bench.Count > 0 && bench.All(j => _review.Votes.ContainsKey(j.Name)))
            ResolveReview();

        return true;
    }

    private void ResolveReview()
    {
        if (_review == null)
            return;

        var review = _review;
        _review = null;

        var bench = BuildJudges().Select(j => j.Name).ToList();
        var yes = review.Votes.Count(kv => bench.Contains(kv.Key) && kv.Value);
        var no = review.Votes.Count(kv => bench.Contains(kv.Key) && !kv.Value);

        bool unconstitutional;
        if (yes != no)
            unconstitutional = yes > no;
        else if (Chief != null && review.Votes.TryGetValue(Chief, out var chiefVote))
            unconstitutional = chiefVote;
        else
            unconstitutional = false;

        if (unconstitutional && _senate.RepealLaw(review.LawId))
        {
            LawsRepealed++;
        }
    }

    public SWCourtReviewEntry? BuildReview(string? viewer)
    {
        if (_review == null)
            return null;

        var bench = BuildJudges().Select(j => j.Name).ToList();
        return new SWCourtReviewEntry
        {
            LawId = _review.LawId,
            LawName = _review.LawName,
            VotesFor = _review.Votes.Count(kv => bench.Contains(kv.Key) && kv.Value),
            VotesAgainst = _review.Votes.Count(kv => bench.Contains(kv.Key) && !kv.Value),
            MyVote = viewer != null && _review.Votes.TryGetValue(viewer, out var v) ? (v ? 1 : 2) : 0,
            CanVote = viewer != null && IsJudge(viewer) && _time.AbsoluteHour < _review.DeadlineAbs,
        };
    }

    // --- builders ---

    public SWCourtCaseEntry? BuildCurrentCase(string? viewer, int hintSide)
    {
        var slot = !_time.DayActive || _time.CurrentHour < EveningCard ? 0 : 1;
        var c = _cases.FirstOrDefault(x => x.Day == _time.Day && x.Slot == slot)
            ?? _cases.FirstOrDefault(x => x.Day == _time.Day && !x.Closed);
        return c == null ? null : BuildEntry(c, viewer, hintSide);
    }

    public List<SWCourtCaseEntry> BuildRegistry(string? viewer = null, int hintSide = 0)
    {
        var list = _cases.OrderByDescending(c => c.Day).ThenByDescending(c => c.Slot)
            .Select(c => BuildEntry(c, viewer, hintSide)).ToList();
        return list;
    }

    private SWCourtCaseEntry BuildEntry(ActiveCase c, string? viewer, int hintSide)
    {
        _proto.TryIndex<SWCourtCasePrototype>(c.ProtoId, out var proto);
        var judgeName = viewer != null && IsJudge(viewer) ? viewer : null;
        var judgeViewer = judgeName != null;

        var entry = new SWCourtCaseEntry
        {
            Id = c.Id,
            Number = $"Дело №{c.Id}",
            Title = proto != null ? Fill(proto.Name, c) : c.ProtoId,
            TypeLabel = TypeLabel(proto?.CaseType ?? SWCourtCaseType.SectorVsSector),
            Parties = $"{c.PlaintiffLabel} против {c.DefendantLabel}",
            Description = proto != null ? Fill(proto.Description, c) : string.Empty,
            HearingLabel = $"День {c.Day}, {HearingStart(c.Slot):00}:00-{HearingEnd(c.Slot):00}:00, совещание до {Deadline(c.Slot):00}:00",
            Presiding = c.Presiding.Length > 0 ? c.Presiding : "не назначен",
            StatusLabel = StatusLabel(c),
            Verdict = c.Closed
                ? c.Unresolved ? "Решение не вынесено" : $"В пользу {(c.PlaintiffWon ? "истца" : "ответчика")}"
                : string.Empty,
            Closed = c.Closed,
        };

        if (proto != null)
        {
            if (judgeViewer || hintSide is 1 or 3)
                entry.OutcomePlaintiff = Fill(proto.PlaintiffWin.Summary, c);
            if (judgeViewer || hintSide is 2 or 3)
                entry.OutcomeDefendant = Fill(proto.DefendantWin.Summary, c);

            if (hintSide is 1 or 3)
                foreach (var hint in proto.ProsecutionHints)
                    entry.Hints.Add($"[Обвинение] {Fill(hint, c)}");
            if (hintSide is 2 or 3)
                foreach (var hint in proto.DefenseHints)
                    entry.Hints.Add($"[Защита] {Fill(hint, c)}");
        }

        if (judgeName != null)
        {
            entry.CanVote = !c.Closed && c.Day == _time.Day && _time.DayActive
                && _time.CurrentHour >= HearingStart(c.Slot) && _time.AbsoluteHour < DeadlineAbs(c);
            entry.CanCloseVote = CanCloseVerdictVote(judgeName, c.Id);
            entry.MyVote = c.Votes.TryGetValue(judgeName, out var v) ? (v ? 1 : 2) : 0;
            var bench = BuildJudges().Select(j => j.Name).ToList();
            entry.VotesPlaintiff = c.Votes.Count(kv => bench.Contains(kv.Key) && kv.Value);
            entry.VotesDefendant = c.Votes.Count(kv => bench.Contains(kv.Key) && !kv.Value);
        }

        return entry;
    }

    private string StatusLabel(ActiveCase c)
    {
        if (c.Closed)
            return c.Unresolved ? "Не рассмотрено" : "Вердикт вынесен";
        if (c.Day != _time.Day)
            return "В реестре";
        if (!_time.DayActive || _time.CurrentHour < HearingStart(c.Slot))
            return "Ожидает слушания";
        if (_time.CurrentHour < HearingEnd(c.Slot))
            return "Слушание открыто";
        return "Совещательная стадия";
    }

    private static string TypeLabel(SWCourtCaseType type) => type switch
    {
        SWCourtCaseType.SectorVsRepublic => "Сектор против Республики",
        SWCourtCaseType.RepublicVsSector => "Республика против сектора",
        SWCourtCaseType.SectorVsSector => "Сектор против сектора",
        SWCourtCaseType.CitizenVsSector => "Гражданский иск к сектору",
        SWCourtCaseType.RepublicVsOfficial => "Республика против должностного лица",
        _ => "Иное",
    };

    private string Fill(string text, ActiveCase c) => text
        .Replace("{plaintiff}", c.PlaintiffLabel)
        .Replace("{defendant}", c.DefendantLabel)
        .Replace("{official}", c.OfficialName);

    private string SectorName(string sectorId) =>
        _proto.TryIndex<SWSectorPrototype>(sectorId, out var proto) ? proto.Name : sectorId;

    // --- stats for ambitions ---

    public int VerdictsForPlaintiff => _cases.Count(c => c is { Closed: true, Unresolved: false, PlaintiffWon: true });
    public int VerdictsForDefendant => _cases.Count(c => c is { Closed: true, Unresolved: false, PlaintiffWon: false });
    public int CasesResolved => _cases.Count(c => c is { Closed: true, Unresolved: false });
    public int CasesUnresolved => _cases.Count(c => c is { Closed: true, Unresolved: true });
    public int LawsRepealed { get; private set; }

    // --- notices ---

    public List<string> GetNotices(string name) =>
        _notices.TryGetValue(name, out var list) ? new List<string>(list) : new List<string>();

    private void AddNotice(string name, string text)
    {
        if (!_notices.TryGetValue(name, out var list))
            _notices[name] = list = new List<string>();

        list.Insert(0, $"[{_time.TimeString()}] {text}");
        if (list.Count > 6)
            list.RemoveRange(6, list.Count - 6);
    }

    private void Announce(string text)
    {
        RaiseLocalEvent(new SWSenateCustomAnnouncementEvent("Верховный Суд Республики", text, SWSenateAnnouncementKind.Justice));
    }
}
