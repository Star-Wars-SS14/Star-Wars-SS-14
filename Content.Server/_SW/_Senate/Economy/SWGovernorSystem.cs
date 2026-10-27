using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Round-local administrations. One decision per published active hour, never per UI request.</summary>
public sealed class SWGovernorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _dilemmas = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    private sealed class Governor(string name, SWGovernorPersonality personality)
    {
        public readonly string Name = name;
        public readonly SWGovernorPersonality Personality = personality;
        public readonly List<string> History = new();
        public int Day = -1;
        public int Envelope;
        public int LastBudget;
        public string Status = "Изучает хозяйство округа";
        public string Priority = string.Empty;
        public string? ProjectId;
        public SWGovernorCandidate? Project;
        public string? Observed;
        public double ObservedHour = -1;
        public double DecisionHour;
        public string? RequestKey;
        public int RequestedCredits;
        public SWGovernorCandidate? FinanceCandidate;
        public string? FinanceRequestId;
        public int FinanceRetryDay;
    }

    private readonly Dictionary<string, Governor> _governors = new();
    private readonly Dictionary<string, string[]> _sectorWorlds = new();
    private readonly Dictionary<string, string> _worldNames = new();
    private int _lastHour;
    private int _nextProject;
    public string FinanceResult { get; private set; } = string.Empty;
    public string FinanceResultRequestId { get; private set; } = string.Empty;
    public void ReportFinanceDenied(string requestId)
    {
        FinanceResultRequestId = requestId ?? string.Empty;
        FinanceResult = "Действие доступно только министру финансов";
    }
    private readonly Queue<string> _decisions = new();
    private EconomySnapshot? _decisionSnapshot;
    public int Revision { get; private set; }

    private static readonly string[] FirstNames =
        { "Арен", "Бейл", "Келлан", "Дарен", "Элиан", "Гален", "Теран", "Оррен", "Лиам", "Варен", "Джейс", "Нолан", "Кай", "Лукан", "Сорен", "Тавис" };
    private static readonly string[] LastNames =
        { "Восс", "Таллен", "Ренн", "Келл", "Дейн", "Мерек", "Вейл", "Ралтис", "Кресс", "Солар", "Тарис", "Норр", "Каден", "Дрейк", "Теван", "Релл" };

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWRepublicEconomySystem));
        UpdatesAfter.Add(typeof(SWSenateBudgetSystem));
        UpdatesAfter.Add(typeof(SWSenateDilemmaSystem));
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _governors.Clear();
            _lastHour = 0;
            _nextProject = 0;
            FinanceResult = string.Empty;
            FinanceResultRequestId = string.Empty;
            _decisions.Clear();
            _decisionSnapshot = null;
            Revision++;
        });
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        foreach (var group in _proto.Index(scenario.Map).Worlds
                     .Where(w => w.Sector != null && _proto.Index<SWPlanetEconomyPrototype>(w.Id).ActiveEconomy)
                     .GroupBy(w => w.Sector!.Value.Id))
        {
            _sectorWorlds[group.Key] = group.Select(w => w.Id).ToArray();
            foreach (var world in group) _worldNames[world.Id] = world.Name;
        }
    }

    private void Appoint()
    {
        if (_governors.Count != 0)
            return;
        var names = new HashSet<string>();
        foreach (var sector in _sectorWorlds.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            string name;
            do name = FirstNames[_random.Next(FirstNames.Length)] + " " + LastNames[_random.Next(LastNames.Length)];
            while (!names.Add(name));
            // Independent round RNG draws: neither the world nor its ordinal encodes a personality.
            var governor = new Governor(name, new SWGovernorPersonality(_random.Prob(.5f), _random.Prob(.5f), _random.Prob(.5f)));
            governor.History.Add("Назначен губернатором округа");
            _governors.Add(sector, governor);
        }
        Revision++;
    }

    public override void Update(float frameTime)
    {
        if (!_time.ModeActive || !_time.DayActive)
            return;
        Appoint();
        if (_economy.Runner?.Fault != null || _economy.Snapshot is not { } snapshot || _economy.Definition is not { } definition)
            return;
        var hour = (int) Math.Floor(snapshot.Hour + 1e-8);
        if (hour > _lastHour)
        {
            _lastHour = hour;
            _decisionSnapshot = snapshot;
            _decisions.Clear();
            foreach (var sector in _governors.Keys) _decisions.Enqueue(sector);
        }
        // Spread administrations across frames. There is no Republic-wide planning spike,
        // and all decisions for an hour still use one immutable economic publication.
        if (_decisionSnapshot != null && _decisions.TryDequeue(out var next))
        {
            Decide(next, _governors[next], definition, _decisionSnapshot);
            if (_decisions.Count == 0) Revision++;
        }
    }

    private void Decide(string sector, Governor governor, EconomyDefinition definition, EconomySnapshot snapshot)
    {
        governor.DecisionHour = snapshot.Hour;
        var available = _budget.Available(sector);
        if (governor.Day != _time.Day)
        {
            governor.Day = _time.Day;
            governor.Envelope = available;
        }
        else if (available > governor.LastBudget)
            governor.Envelope += available - governor.LastBudget;
        governor.LastBudget = available;
        _corruption.AddSectorCorruption(sector, governor.Personality.Honest ? -.15f : .45f);

        if (governor.ProjectId is { } active)
        {
            var project = snapshot.Projects.FirstOrDefault(p => p.Id == active);
            if (project?.CompletedHour == null)
            {
                governor.Status = ProjectStatus(project);
                return;
            }
            Record(governor, $"Введено расширение: {RecipeName(governor.Project!.Recipe)} ({WorldName(governor.Project.World)})");
            governor.ProjectId = null;
        }

        var candidates = SWGovernorPolicy.Evaluate(definition, snapshot, _sectorWorlds[sector], governor.Personality);
        var best = candidates.FirstOrDefault();
        if (best == null)
        {
            CloseRequest(governor);
            governor.Observed = null;
            governor.Status = "Наблюдает за снабжением";
            governor.Priority = "Нет обеспеченного расширения с подтверждённым спросом";
            return;
        }

        var key = $"{sector}:{best.World}:{best.Recipe}";
        governor.Priority = $"{RecipeName(best.Recipe)}: {best.Reason}";
        var confirmed = governor.Observed == key && snapshot.Hour - governor.ObservedHour <= 1.01;
        governor.Observed = key;
        governor.ObservedHour = snapshot.Hour;
        if (!confirmed)
        {
            if (governor.RequestKey != key) CloseRequest(governor);
            governor.Status = "Проверяет устойчивость спроса";
            return;
        }
        if (!_budget.Approved)
        {
            governor.Status = "Ожидает утверждения бюджета";
            return;
        }

        var reserve = Reserve(governor);
        var cost = _corruption.ProjectCost(sector, best.BaseCost);
        if (available - reserve < cost)
        {
            governor.Status = "Ожидает финансирования";
            if (governor.RequestKey != key) CloseRequest(governor);
            governor.RequestKey = key;
            // A grant enlarges the same envelope and therefore also its protected reserve.
            var missing = (int) Math.Ceiling((cost + reserve - available) / (1 - SWGovernorPolicy.ReserveFraction(governor.Personality)));
            governor.RequestedCredits = Math.Clamp(missing, 1, 10000);
            if (governor.FinanceCandidate != null && (governor.FinanceCandidate.World != best.World || governor.FinanceCandidate.Recipe != best.Recipe))
            {
                governor.FinanceCandidate = null;
                governor.FinanceRequestId = null;
            }
            if (_time.Day >= governor.FinanceRetryDay && governor.FinanceRequestId == null
                && _governors.Values.Count(g => g.FinanceRequestId != null) < 6)
            {
                governor.FinanceCandidate = best;
                governor.FinanceRequestId = $"governor-finance:{Guid.NewGuid():N}";
            }
            var materials = string.Join(", ", best.Materials.Select(m => $"{GoodName(m.Good)} {m.Quantity:0.##}"));
            if (_dilemmas.UpsertGovernorRequest(sector, key, $"{governor.Name}: расширение производства",
                $"{WorldName(best.World)}. {RecipeName(best.Recipe)}. {best.Reason}. " +
                $"Мощность увеличится на {best.AddedCyclesPerHour:0.###} цикла/ч. Смета {cost} кр., " +
                $"доступно сверх резерва {Math.Max(0, available - reserve)} кр. Материалы: {materials}. " +
                $"Работы после доставки: {best.WorkHours:0.#} ч.", governor.RequestedCredits))
                governor.Status = "Обращение передано сенатору";
            return;
        }

        if (!_budget.Spend(sector, cost))
            return;
        var id = $"governor:{sector}:{++_nextProject}";
        var request = new EconomyProjectRequest(id, best.World, best.Materials, best.WorkHours,
            new EconomyProductionExpansion(best.Recipe, best.AddedCyclesPerHour));
        if (!_economy.QueueProject(request))
        {
            _budget.Grant(sector, cost);
            governor.Status = "Проект не принят, средства сохранены";
            return;
        }
        CloseRequest(governor);
        governor.LastBudget = _budget.Available(sector);
        governor.ProjectId = id;
        governor.Project = best;
        governor.Observed = null;
        governor.Status = "Заказано расширение производства";
        Record(governor, $"{WorldName(best.World)}: {RecipeName(best.Recipe)}, {cost} кр.");
    }

    private void CloseRequest(Governor governor)
    {
        if (governor.RequestKey is { } key) _dilemmas.CloseGovernorRequest(key);
        governor.RequestKey = null;
        governor.RequestedCredits = 0;
        governor.FinanceRequestId = null;
        governor.FinanceCandidate = null;
    }

    public List<SWFinanceGovernorRequest> FinanceRequests()
    {
        var list = new List<SWFinanceGovernorRequest>();
        foreach (var (sector, governor) in _governors)
        {
            if (governor.FinanceRequestId is not { } id || governor.FinanceCandidate is not { } candidate) continue;
            var cost = _corruption.ProjectCost(sector, candidate.BaseCost);
            var available = Math.Max(0, _budget.Available(sector) - Reserve(governor));
            list.Add(new SWFinanceGovernorRequest { RequestId = id, SectorId = sector,
                SectorName = _proto.TryIndex<SWSectorPrototype>(sector, out var sp) ? sp.Name : sector, Governor = governor.Name,
                WorldName = WorldName(candidate.World), ProjectName = RecipeName(candidate.Recipe), Reason = candidate.Reason,
                Outcome = $"+{candidate.AddedCyclesPerHour:0.###} цикла производства в час",
                Cost = cost, Missing = Math.Max(0, cost - available) });
        }
        return list;
    }

    public bool DecideFinanceRequest(string requestId, bool approve)
    {
        FinanceResultRequestId = requestId ?? string.Empty;
        var match = _governors.FirstOrDefault(x => x.Value.FinanceRequestId == requestId);
        var governor = match.Value;
        if (governor?.FinanceCandidate is not { } candidate) { FinanceResult = "Обращение устарело"; return false; }
        var sector = match.Key;
        if (!approve)
        {
            CloseRequest(governor);
            governor.FinanceRetryDay = _time.Day + 1;
            FinanceResult = "Обращение отклонено";
            return true;
        }
        if (_economy.Snapshot is not { } snapshot || _economy.Definition is not { } definition || governor.ProjectId != null
            || !SWGovernorPolicy.Evaluate(definition, snapshot, _sectorWorlds[sector], governor.Personality)
                .Any(c => c.World == candidate.World && c.Recipe == candidate.Recipe && c.AddedCyclesPerHour == candidate.AddedCyclesPerHour))
        {
            CloseRequest(governor); FinanceResult = "Проект устарел или больше недоступен"; return false;
        }
        var cost = _corruption.ProjectCost(sector, candidate.BaseCost);
        var sectorPart = Math.Min(cost, Math.Max(0, _budget.Available(sector) - Reserve(governor)));
        var reservePart = cost - sectorPart;
        if (reservePart <= 0) { CloseRequest(governor); FinanceResult = "Дополнительные средства больше не нужны"; return false; }
        if (!_budget.SpendReserveForProject(reservePart)) { FinanceResult = "Недостаточно средств в резерве"; return false; }
        if (!_budget.Spend(sector, sectorPart)) { _budget.RefundReserveForProject(reservePart); FinanceResult = "Секторный бюджет изменился"; return false; }
        var id = $"governor:{sector}:{++_nextProject}";
        var project = new EconomyProjectRequest(id, candidate.World, candidate.Materials, candidate.WorkHours,
            new EconomyProductionExpansion(candidate.Recipe, candidate.AddedCyclesPerHour));
        if (!_economy.QueueProject(project))
        {
            _budget.Grant(sector, sectorPart); _budget.RefundReserveForProject(reservePart);
            CloseRequest(governor); FinanceResult = "Проект не принят; средства возвращены"; return false;
        }
        CloseRequest(governor);
        governor.LastBudget = _budget.Available(sector); governor.ProjectId = id; governor.Project = candidate; governor.Observed = null;
        governor.Status = "Проект профинансирован из резерва";
        _budget.RecordReserveProject(governor.Name, reservePart, $"{WorldName(candidate.World)}: {RecipeName(candidate.Recipe)}");
        Record(governor, $"{WorldName(candidate.World)}: {RecipeName(candidate.Recipe)}, {cost} кр., из резерва {reservePart} кр.");
        FinanceResult = "Проект профинансирован и заказан";
        return true;
    }

    private void Record(Governor governor, string text)
    {
        governor.History.Add($"День {_time.Day}, {_time.CurrentHour:00}:00 — {text}");
        if (governor.History.Count > 8) governor.History.RemoveAt(0);
    }

    private static int Reserve(Governor governor) => (int) Math.Ceiling(governor.Envelope * SWGovernorPolicy.ReserveFraction(governor.Personality));
    private string RecipeName(string id) => _proto.TryIndex<SWProductionRecipePrototype>(id, out var p) ? p.Name : id;
    private string GoodName(string id) => _proto.TryIndex<SWCommodityPrototype>(id, out var p) ? p.Name : id;
    private string WorldName(string id) => _worldNames.GetValueOrDefault(id, id);

    private static string ProjectStatus(EconomyProjectState? project) => project == null ? "Заказ принят"
        : project.CompletedHour != null ? "Введено в эксплуатацию"
        : project.Status == EconomyProjectStatus.Working ? "Монтаж оборудования"
        : project.Reason switch
        {
            "fuel" => "Ожидает топлива для доставки",
            "transport" => "Ожидает свободного транспорта",
            "transit" => "Оборудование в пути",
            "no_route" => "Нет маршрута доставки",
            _ => "Сбор оборудования",
        };

    public List<SWGovernorView> BuildViews()
    {
        var projects = _economy.Snapshot?.Projects.ToDictionary(p => p.Id);
        return _governors.Select(pair =>
        {
            var (sector, g) = pair;
            var project = g.ProjectId != null ? projects?.GetValueOrDefault(g.ProjectId) : null;
            var delivery = project == null || project.Materials.Count == 0 ? 0 : project.Materials.Average(m => Math.Clamp(m.Delivered / m.Required, 0, 1));
            return new SWGovernorView
            {
                Sector = sector, Name = g.Name, Status = g.Status, Priority = g.Priority,
                Traits = new() { g.Personality.Honest ? "Честный" : "Корыстный", g.Personality.Cautious ? "Осторожный" : "Предприимчивый", g.Personality.Social ? "Социальный курс" : "Промышленный курс" },
                Budget = _budget.Available(sector), Reserve = Reserve(g),
                Project = g.Project != null ? RecipeName(g.Project.Recipe) : string.Empty,
                World = g.Project?.World ?? string.Empty, WorldName = g.Project != null ? WorldName(g.Project.World) : string.Empty,
                ProjectStatus = g.Project == null ? string.Empty : g.ProjectId == null ? "Введено в эксплуатацию" : ProjectStatus(project),
                ProjectProgress = g.Project == null ? 0 : g.ProjectId == null ? 1 : project == null ? 0
                    : (float) (.5 * delivery + .5 * Math.Clamp(project.WorkDone / project.WorkHours, 0, 1)),
                LastDecisionHour = g.DecisionHour, History = new(g.History),
                RequestedCredits = g.RequestedCredits, HasRequest = g.RequestKey != null && _dilemmas.HasGovernorRequest(g.RequestKey),
            };
        }).ToList();
    }
}
