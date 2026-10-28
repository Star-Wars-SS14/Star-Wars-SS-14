using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed class SWMinistryJusticeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemma = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public const string JusticeMinistryId = "SWMinistryJustice";
    private const int MaxCases = 4, MaxChecks = 2;
    private sealed class Case
    {
        public int Id;
        public SWJusticeInstitutionView Institution = default!;
        public SWJusticeInspectionPrototype Template = default!;
        public SWJusticeVariantPrototype Variant = default!;
        public string Reference = string.Empty, Amount = string.Empty;
        public readonly HashSet<string> Collected = new();
        public readonly List<string> Cited = new();
        public string? Pending;
        public double CheckUntil, RemedyUntil;
        public double OpenedAt;
        public SWJusticeInspectionStage Stage;
        public SWJusticeConclusion? Conclusion;
        public SWJusticeRemedy? Remedy;
        public string Result = string.Empty;
    }
    private readonly List<Case> _cases = new(), _archive = new();
    private readonly List<string> _log = new();
    private readonly List<SWJusticeLogEntry> _journal = new();
    private int _nextJournalSequence = 1;
    private readonly Queue<string> _recent = new();
    private readonly List<(string Sector, double Until)> _wrongRestrictions = new();
    private readonly List<(string Sector, string Stat, double Until)> _unresolved = new();
    private readonly List<SWJusticeInstitutionView> _institutions = new();
    private readonly List<SWJusticeInspectionPrototype> _templates = new();
    private readonly HashSet<string> _completedSectors = new();
    private double _now, _arrival, _trustUntil;
    private float _backlogRelief;
    private float _conditionAccum;
    private int _nextId = 1, _correct, _wrong, _fulfilled;
    private bool _firstComplaint;
    private bool CanManage => _time.ModeActive && _time.DayActive && MinisterActive;
    public bool MinisterActive => _ministry.IsHolderActive(JusticeMinistryId);
    private int Busy => _cases.Count(c => c.Pending != null);

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _cases.Clear(); _archive.Clear(); _log.Clear(); _journal.Clear(); _nextJournalSequence = 1; _recent.Clear(); _wrongRestrictions.Clear(); _unresolved.Clear();
        _institutions.Clear(); _templates.Clear(); _completedSectors.Clear();
        _now = _arrival = _trustUntil = 0; _conditionAccum = _backlogRelief = 0; _nextId = 1; _correct = _wrong = _fulfilled = 0; _firstComplaint = false;
        PublishConditions();
    }
    private void Log(string message)
    {
        _log.Insert(0, message);
        if (_log.Count > 12) _log.RemoveAt(_log.Count - 1);
    }
    public List<string> GetLog() => new(_log);
    private void Journal(Case c, string message)
    {
        _journal.Insert(0, new SWJusticeLogEntry { Sequence = _nextJournalSequence++, CaseId = c.Id, ActiveHours = _now,
            Day = _time.Day, Hour = _time.CurrentHour, Minute = _time.CurrentMinute,
            CaseTitle = Expand(c, c.Template.Title), InstitutionName = c.Institution.Name, WorldName = c.Institution.WorldName, Message = message });
        if (_journal.Count > 200) _journal.RemoveAt(_journal.Count - 1);
        Log($"Проверка №{c.Id}: {message}");
    }

    private static string KindName(SWJusticeInstitutionKind kind) => kind switch
    {
        SWJusticeInstitutionKind.Medical => "Медицинское учреждение",
        SWJusticeInstitutionKind.Industry => "Промышленное предприятие",
        SWJusticeInstitutionKind.Port => "Портовая служба",
        SWJusticeInstitutionKind.Utilities => "Коммунальная служба",
        SWJusticeInstitutionKind.Procurement => "Служба закупок",
        _ => "Администрация",
    };
    private void EnsureCatalog()
    {
        if (_institutions.Count > 0) return;
        var map = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        var economy = _economy.Definition ?? SWRepublicEconomyFactory.Create(_proto);
        _templates.AddRange(_proto.EnumeratePrototypes<SWJusticeInspectionPrototype>().Where(p => p.IsValid()).OrderBy(p => p.ID));
        var active = economy.Worlds.Where(w => w.Active).ToDictionary(w => w.Id);
        foreach (var world in map.Worlds.OrderBy(w => w.Id))
        {
            var sector = world.Sector?.Id;
            if (sector == null || !_proto.TryIndex<SWSectorPrototype>(sector, out var sp)
                || !active.TryGetValue(world.Id, out var economyWorld)
                || map.PoliticalAreas.LastOrDefault(a => a.Contains(world.Position))?.Faction != "republic") continue;
            foreach (var kind in Enum.GetValues<SWJusticeInstitutionKind>())
            {
                var exists = kind switch
                {
                    SWJusticeInstitutionKind.Medical => economy.Demands.Any(d => d.World == world.Id && d.Rate > 0 && d.Group is "care_drugs" or "care_supplies"),
                    SWJusticeInstitutionKind.Industry => economy.Production.Any(p => p.World == world.Id && p.Rate > 0),
                    SWJusticeInstitutionKind.Port => economy.Edges.Any(e => e.CapacitySlotsPerHour > 0 && (e.A == world.Id || e.B == world.Id)),
                    SWJusticeInstitutionKind.Utilities => economyWorld.BaseloadPower > 0 || economyWorld.DomesticPower > 0 || economyWorld.ReactorCapacityPower > 0,
                    _ => true,
                };
                if (!exists) continue;
                var id = $"{world.Id}:{kind}";
                _institutions.Add(new SWJusticeInstitutionView { Id = id, Name = $"{KindName(kind)} — {world.Name}", WorldId = world.Id,
                    WorldName = world.Name, SectorId = sector, SectorName = sp.Name, Kind = kind, KindName = KindName(kind) });
            }
        }
    }
    private List<SWJusticeInstitutionView> Institutions(bool admin = false)
    {
        EnsureCatalog();
        var result = new List<SWJusticeInstitutionView>();
        foreach (var institution in _institutions)
        {
                var id = institution.Id;
                var occupied = _cases.Any(c => c.Institution.Id == id);
                var hasTemplate = _templates.Any(p => p.Kind == institution.Kind && _cases.All(c => c.Template.ID != p.ID));
                var reason = !(admin ? _time.ModeActive && _time.DayActive : CanManage) ? "Министр отсутствует или рабочий день завершён" : occupied ? "Уже проводится проверка" : _cases.Count >= MaxCases ? "Реестр проверок заполнен" : !hasTemplate ? "Нет доступных материалов проверки" : string.Empty;
                result.Add(new SWJusticeInstitutionView { Id = id, Name = institution.Name, WorldId = institution.WorldId,
                    WorldName = institution.WorldName, SectorId = institution.SectorId, SectorName = institution.SectorName,
                    Kind = institution.Kind, KindName = institution.KindName, Available = reason.Length == 0, Reason = reason });
        }
        return result;
    }
    public List<SWJusticeInstitutionView> BuildDirectorInstitutions() => Institutions(true);
    public bool OpenInspection(string institutionId) => OpenInspectionInternal(institutionId, false) != 0;
    public int AdminOpenInspection(string? institutionId = null)
    {
        var available = Institutions(true).Where(i => i.Available && (string.IsNullOrEmpty(institutionId) || i.Id == institutionId)).ToList();
        return available.Count == 0 ? 0 : OpenInspectionInternal(_random.Pick(available).Id, true);
    }
    public bool AdminCancelInspection(int caseId)
    {
        if (!_time.ModeActive || !_time.DayActive) return false;
        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (c == null) return false;
        _cases.Remove(c); Journal(c, "Проверка отменена директором"); PublishConditions(); return true;
    }
    private int OpenInspectionInternal(string institutionId, bool admin, bool complaint = false)
    {
        if (!(admin ? _time.ModeActive && _time.DayActive : CanManage) || _cases.Count >= MaxCases) return 0;
        var institution = Institutions(admin).FirstOrDefault(i => i.Id == institutionId && i.Available);
        if (institution == null) return 0;
        var options = _templates.Where(p => p.Kind == institution.Kind
            && _cases.All(c => c.Template.ID != p.ID)).OrderBy(p => p.ID).ToList();
        if (options.Count == 0) return 0;
        var fresh = options.Where(p => !_recent.Contains(p.ID)).ToList();
        if (fresh.Count > 0) options = fresh;
        var total = options.Sum(p => p.Weight);
        var roll = _random.Next(0, total);
        var template = options[0];
        foreach (var option in options) { roll -= option.Weight; if (roll < 0) { template = option; break; } }
        var c = new Case { Id = _nextId++, Institution = institution, Template = template, OpenedAt = _now,
            Variant = _random.Pick(template.Variants), Reference = $"Р-{_random.Next(10000, 99999)}", Amount = _random.Next(10, 100) * 100 + " кредитов" };
        _cases.Add(c); _recent.Enqueue(template.ID); if (_recent.Count > 12) _recent.Dequeue();
        Journal(c, admin ? "Проверка открыта директором" : complaint ? "Поступило обращение, открыта проверка" : "Открыта проверка"); PublishConditions(); return c.Id;
    }
    private string Expand(Case c, string text) => text.Replace("{world}", c.Institution.WorldName).Replace("{sector}", c.Institution.SectorName)
        .Replace("{institution}", c.Institution.Name).Replace("{amount}", c.Amount).Replace("{reference}", c.Reference);
    public bool RunCheck(int caseId, string checkId)
    {
        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        var check = c?.Template.Checks.FirstOrDefault(x => x.Id == checkId);
        if (!CanManage || c == null || c.Stage != SWJusticeInspectionStage.Inspecting || check == null || c.Pending != null
            || Busy >= MaxChecks || c.Collected.Contains(checkId) || !_dilemma.SpendMinistry(JusticeMinistryId, check.Cost)) return false;
        c.Pending = checkId; c.CheckUntil = _now + check.Hours; Journal(c, $"Назначена проверка «{check.Title}», стоимость {check.Cost} кр"); return true;
    }
    public bool Conclude(int caseId, SWJusticeConclusion conclusion, List<string> evidenceIds)
    {
        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (!CanManage || c == null || c.Stage != SWJusticeInspectionStage.Inspecting || c.Pending != null
            || !Enum.IsDefined(conclusion) || evidenceIds == null || evidenceIds.Count < 2
            || evidenceIds.Distinct().Count() != evidenceIds.Count || evidenceIds.Any(id => !c.Collected.Contains(id))) return false;
        c.Conclusion = conclusion; c.Cited.AddRange(evidenceIds); c.Stage = SWJusticeInspectionStage.Enforcement;
        Journal(c, $"Принято заключение: {ConclusionName(conclusion)}"); return true;
    }
    private static List<SWJusticeRemedy> Remedies(Case c) => c.Conclusion switch
    {
        SWJusticeConclusion.NoViolation => new() { SWJusticeRemedy.Close },
        SWJusticeConclusion.Negligence => new() { SWJusticeRemedy.CorrectRecords, SWJusticeRemedy.Restitution },
        SWJusticeConclusion.IntentionalViolation => new() { SWJusticeRemedy.Restitution, SWJusticeRemedy.RestrictOperation },
        _ => new(),
    };
    public bool ApplyRemedy(int caseId, SWJusticeRemedy remedy)
    {
        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (!CanManage || c == null || c.Stage != SWJusticeInspectionStage.Enforcement || c.Remedy != null || !Remedies(c).Contains(remedy)) return false;
        var cost = RemedyCost(remedy);
        if (cost > 0 && !_dilemma.SpendMinistry(JusticeMinistryId, cost)) return false;
        c.Remedy = remedy; c.RemedyUntil = _now + RemedyHours(remedy);
        Journal(c, $"Назначена мера: {RemedyName(remedy)}, стоимость {cost} кр"); PublishConditions(); return true;
    }
    public bool FollowUp(int caseId)
    {
        var c = _cases.FirstOrDefault(x => x.Id == caseId);
        if (!CanManage || c == null || c.Stage != SWJusticeInspectionStage.Enforcement || c.Remedy == null || _now < c.RemedyUntil) return false;
        var correct = c.Conclusion == c.Variant.Conclusion;
        if (correct) { _correct++; if (c.Remedy != SWJusticeRemedy.Close) _fulfilled++; _completedSectors.Add(c.Institution.SectorId); }
        else { _wrong++; _trustUntil = Math.Max(_trustUntil, _now + 4); }
        if (!correct && c.Remedy == SWJusticeRemedy.RestrictOperation) _wrongRestrictions.Add((c.Institution.SectorId, _now + 4));
        if (!correct && c.Variant.Conclusion != SWJusticeConclusion.NoViolation)
            _unresolved.Add((c.Institution.SectorId, RelevantStat(c.Institution.Kind), _now + 4));
        c.Result = correct ? "Заключение подтверждено" : "Повторная проверка выявила ошибочное заключение";
        c.Stage = SWJusticeInspectionStage.Closed; _cases.Remove(c); _archive.Insert(0, c);
        if (_archive.Count > 25) _archive.RemoveAt(_archive.Count - 1);
        Journal(c, $"Проверено исполнение. {c.Result}"); PublishConditions(); return true;
    }
    public override void Update(float frameTime)
    {
        var dt = _time.ActiveSeconds(frameTime);
        if (dt <= 0 || !MinisterActive) return;
        _now += dt / SWSenateTimeSystem.RealSecondsPerGameHour;
        _backlogRelief = Math.Max(0, _backlogRelief - dt / SWSenateTimeSystem.RealSecondsPerGameHour);
        foreach (var c in _cases.Where(c => c.Pending != null && c.CheckUntil <= _now))
        {
            var completed = c.Pending!;
            c.Collected.Add(completed); c.Pending = null;
            var title = c.Template.Checks.First(check => check.Id == completed).Title;
            Journal(c, $"Получены материалы проверки «{title}»");
        }
        if (!_firstComplaint || _now >= _arrival)
        {
            _firstComplaint = true; _arrival = _now + _random.NextFloat(2f, 3f);
            var available = Institutions().Where(i => i.Available).ToList();
            if (available.Count > 0) OpenInspectionInternal(_random.Pick(available).Id, false, true);
        }
        _conditionAccum += dt;
        if (_conditionAccum >= 1f)
        {
            _conditionAccum = 0;
            _wrongRestrictions.RemoveAll(r => r.Until <= _now);
            _unresolved.RemoveAll(r => r.Until <= _now);
            PublishConditions();
        }
    }
    private static string RelevantStat(SWJusticeInstitutionKind kind) => kind switch
    {
        SWJusticeInstitutionKind.Medical => "SWStatLiving", SWJusticeInstitutionKind.Industry => "SWStatManufacturing",
        SWJusticeInstitutionKind.Port => "SWStatTrade", SWJusticeInstitutionKind.Utilities => "SWStatLiving",
        SWJusticeInstitutionKind.Procurement => "SWStatTreasury", _ => "SWStatMorale",
    };
    private void PublishConditions()
    {
        // A complaint has the same public effect for all variants; its truth cannot be inferred from statistics.
        var current = _cases.Select(c => (Sector: c.Institution.SectorId, Stat: RelevantStat(c.Institution.Kind), Value: -.5f));
        var inspections = current.Concat(_unresolved.Select(r => (r.Sector, r.Stat, Value: -.5f)))
            .GroupBy(e => (e.Sector, e.Stat)).Select(g => new SWSituationalContribution(g.Key.Sector, g.Key.Stat, Math.Clamp(g.Sum(e => e.Value), -4f, 0f))).ToArray();
        var restrictionSectors = _wrongRestrictions.Select(r => r.Sector).Concat(_cases.Where(c => c.Remedy == SWJusticeRemedy.RestrictOperation).Select(c => c.Institution.SectorId));
        var restrictions = restrictionSectors.GroupBy(r => r).Select(g => new SWSituationalContribution(g.Key, "SWStatTrade", Math.Clamp(-g.Count(), -4f, 0f))).ToArray();
        _senate.ReplaceSituationalContributions("ministry.justice.inspections", inspections);
        _senate.ReplaceSituationalContributions("ministry.justice.enforcement", restrictions);
        _senate.ReplaceSituationalContributions("ministry.justice.trust", _trustUntil > _now
            ? new[] { new SWSituationalContribution(null, "SWStatRights", -2f), new SWSituationalContribution(null, "SWStatMorale", -2f) }
            : Array.Empty<SWSituationalContribution>());
    }
    private SWJusticeCaseView View(Case c, int budget)
    {
        var view = new SWJusticeCaseView { Id = c.Id, Title = Expand(c, c.Template.Title), Briefing = Expand(c, c.Template.Briefing),
            InstitutionId = c.Institution.Id, InstitutionName = c.Institution.Name, WorldId = c.Institution.WorldId, WorldName = c.Institution.WorldName,
            SectorId = c.Institution.SectorId, SectorName = c.Institution.SectorName, Stage = c.Stage, SelectedConclusion = c.Conclusion,
            SelectedRemedy = c.Remedy, CitedEvidenceIds = new List<string>(c.Cited), RemedyEtaHours = c.Remedy == null ? 0 : Math.Max(0, c.RemedyUntil - _now),
            CanConclude = CanManage && c.Stage == SWJusticeInspectionStage.Inspecting && c.Pending == null && c.Collected.Count >= 2,
            CanFollowUp = CanManage && c.Stage == SWJusticeInspectionStage.Enforcement && c.Remedy != null && _now >= c.RemedyUntil };
        foreach (var check in c.Template.Checks)
        {
            var collected = c.Collected.Contains(check.Id); var pending = c.Pending == check.Id;
            if (collected) view.Evidence.Add(new SWJusticeEvidenceView { CheckId = check.Id, Title = check.Title, Text = Expand(c, c.Variant.Evidence[check.Id]) });
            var reason = !CanManage ? "Министр отсутствует или рабочий день завершён" : c.Stage != SWJusticeInspectionStage.Inspecting ? "Заключение уже принято" : collected ? "Материал получен" : pending ? "Материал запрошен" : c.Pending != null ? "Другая проверка учреждения выполняется" : Busy >= MaxChecks ? "Все специалисты заняты" : budget < check.Cost ? "Недостаточно средств" : string.Empty;
            view.Checks.Add(new SWJusticeCheckView { Id = check.Id, Title = check.Title, Cost = check.Cost, Hours = check.Hours, Available = reason.Length == 0,
                Reason = reason, Completed = collected, Pending = pending, EtaHours = pending ? Math.Max(0, c.CheckUntil - _now) : 0 });
        }
        if (c.Stage == SWJusticeInspectionStage.Enforcement && c.Remedy == null && CanManage)
        {
            view.AvailableRemedies = Remedies(c).Where(r => budget >= RemedyCost(r)).ToList();
            view.RemedyOptions = Remedies(c).Select(r => new SWJusticeRemedyOptionView { Remedy = r, Cost = RemedyCost(r), Hours = RemedyHours(r),
                Available = budget >= RemedyCost(r), Reason = budget >= RemedyCost(r) ? string.Empty : "Недостаточно средств" }).ToList();
        }
        if (c.Stage == SWJusticeInspectionStage.Closed) { view.ActualConclusion = c.Variant.Conclusion; view.Explanation = Expand(c, c.Variant.Explanation); view.Result = c.Result; }
        return view;
    }
    public SWJusticeInspectionState BuildInspectionState()
    {
        var budget = _dilemma.GetMinistryBudget(JusticeMinistryId);
        var institutions = Institutions();
        return new SWJusticeInspectionState { NowHours = _now, RealSecondsPerHour = SWSenateTimeSystem.RealSecondsPerGameHour,
            ClockRunning = CanManage, MinisterActive = MinisterActive, Budget = budget, CaseCap = MaxCases, CheckCap = MaxChecks, BusyChecks = Busy,
            Workload = Backlog, WorkloadStatus = BacklogStatus(),
            Worlds = institutions.GroupBy(i => i.WorldId).Select(g => new SWJusticeWorldView { Id = g.Key, Name = g.First().WorldName,
                SectorId = g.First().SectorId, SectorName = g.First().SectorName }).ToList(), Institutions = institutions,
            ActiveCases = _cases.Select(c => View(c, budget)).ToList(), Archive = _archive.Select(c => View(c, budget)).ToList(), Log = GetLog(), Journal = new List<SWJusticeLogEntry>(_journal) };
    }

    // Legacy court controls are deliberately inert; court adjudication is outside the ministry.
    public SWJusticeStance Stance => SWJusticeStance.Balanced;
    public static string StanceName(SWJusticeStance stance) => "Ведомственный надзор";
    public static Color StanceColor(SWJusticeStance stance) => Color.FromHex("#5DADE2");
    private static int RemedyCost(SWJusticeRemedy remedy) => remedy switch
    { SWJusticeRemedy.CorrectRecords => 200, SWJusticeRemedy.Restitution => 350, SWJusticeRemedy.RestrictOperation => 250, _ => 0 };
    private static double RemedyHours(SWJusticeRemedy remedy) => remedy switch
    { SWJusticeRemedy.Close => .5, SWJusticeRemedy.CorrectRecords => 1, SWJusticeRemedy.RestrictOperation => 1.5, _ => 2 };
    private static string ConclusionName(SWJusticeConclusion conclusion) => conclusion switch
    { SWJusticeConclusion.NoViolation => "Нарушений нет", SWJusticeConclusion.Negligence => "Халатность", _ => "Умышленное нарушение" };
    private static string RemedyName(SWJusticeRemedy remedy) => remedy switch
    { SWJusticeRemedy.Close => "Закрыть проверку", SWJusticeRemedy.CorrectRecords => "Исправить документы", SWJusticeRemedy.Restitution => "Возместить ущерб", _ => "Ограничить работу" };
    public int Backlog => (int) Math.Clamp(_cases.Count * 12 + _cases.Sum(c => Math.Max(0, _now - c.OpenedAt)) + Overdue * 8 - _backlogRelief, 0, 100);
    public string BacklogStatus() => Backlog switch
    { >= 80 => "Критическая нагрузка", >= 60 => "Высокая нагрузка", >= 35 => "Повышенная нагрузка", _ => "Нагрузка в норме" };
    public int Capacity => MaxChecks - Busy;
    public int CapacityMax => MaxChecks;
    public int CaseCount => _cases.Count;
    public bool TribunalActive => false;
    public double TribunalHoursLeft => 0;
    public bool TribunalPending => false;
    public string? SelectedProgram => null;
    public int ProgramProgress => 0;
    public int OvertimeCost() => 0;
    public bool CanBuyOvertime() => false;
    public bool BuyOvertime() => false;
    public void SetStance(SWJusticeStance stance) { }
    public void SelectProgram(string id) { }
    public void DeclareTribunal() { }
    public bool ApproveTribunal() => false;
    public bool RejectTribunal() => false;
    public bool Investigate(int id) => false;
    public bool SendToCourt(int id) => false;
    public bool HoldHearing(int id) => false;
    public bool Rule(int id, SWVerdict verdict) => false;
    public void ClearCases() { foreach (var c in _cases) Journal(c, "Проверка отменена"); _cases.Clear(); PublishConditions(); }
    public void RelieveBacklog(float amount) => _backlogRelief = Math.Clamp(_backlogRelief + Math.Max(0, amount), 0, 100);
    public string AdminStatus() => BacklogStatus();
    public (float Cost, float Effect) GetRequestModifier(string? sectorId) => Backlog switch
    { >= 60 => (1.25f, .85f), <= 25 => (1f, 1.15f), _ => (1f, 1f) };
    public void OnSectorEngagement(string? sectorId) => RelieveBacklog(4f);
    public int ConvictJust => _correct;
    public int ConvictWrong => _wrong;
    public int AcquitJust => 0;
    public int AcquitWrong => 0;
    public int Dismissed => 0;
    public int Overdue => _cases.Count(c => _now - c.OpenedAt >= 12);
    public int CorrectInspections => _correct;
    public int WrongInspections => _wrong;
    public int FulfilledInspections => _fulfilled;
    public int CompletedSectors => _completedSectors.Count;
    public int Resolved => _correct + _wrong;
    public int Accuracy() => Resolved == 0 ? 0 : (int) MathF.Round(_correct * 100f / Resolved);
    public List<SWClosedCaseEntry> BuildArchive() => new();
    public List<SWCaseEntry> BuildCases() => new();
    public List<SWDefenseProgramEntry> BuildPrograms() => new();
}
