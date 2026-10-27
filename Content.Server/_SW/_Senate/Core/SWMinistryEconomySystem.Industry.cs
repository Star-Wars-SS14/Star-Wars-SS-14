using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Sectors;
using SWEconomyGoodView = Content.Shared._SW._Senate.SWEconomyGoodView;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryEconomySystem
{
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWGovernorSystem _governors = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;

    private sealed record IndustrialProject(string Id, string Kind, string Key, string Name, string World);
    private readonly List<IndustrialProject> _industrialProjects = new();
    private readonly Dictionary<string, (string Name, string Sector)> _industrialWorlds = new();
    private EconomySnapshot? _industrialSnapshot;
    private List<SWGovernorCandidate> _industrialCandidates = new();
    private List<SWEconomyProblemView> _industrialProblems = new();
    private int _nextIndustrialId;
    public string SelectedGood { get; private set; } = string.Empty;
    private const int TransitCost = 1800;

    private void ResetIndustrial()
    {
        _industrialProjects.Clear();
        _industrialSnapshot = null;
        _industrialCandidates.Clear();
        _industrialProblems.Clear();
        _nextIndustrialId = 0;
        SelectedGood = string.Empty;
    }

    private void EnsureIndustrialWorlds()
    {
        if (_industrialWorlds.Count != 0) return;
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        foreach (var world in _proto.Index(scenario.Map).Worlds)
            _industrialWorlds[world.Id] = (world.Name, world.Sector?.Id ?? string.Empty);
    }
    private string IndustrialWorldName(string id) => _industrialWorlds.TryGetValue(id, out var w) && !string.IsNullOrWhiteSpace(w.Name) ? w.Name : "Неизвестный мир";
    private string IndustrialSector(string id) => _industrialWorlds.TryGetValue(id, out var w) ? w.Sector : string.Empty;
    private string IndustrialSectorName(string id) => _proto.TryIndex<SWSectorPrototype>(id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name : "Неизвестный сектор";
    private string IndustrialGoodName(string id) => _economy.GoodNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Неизвестный товар";
    private string IndustrialRecipeName(string id) => _proto.TryIndex<SWProductionRecipePrototype>(id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name : "Неизвестное производство";
    private string Materials(IEnumerable<EconomyAmount> materials) => string.Join(", ", materials.Select(m => $"{IndustrialGoodName(m.Good)} {m.Quantity:0.##} ед."));

    // Compute once per immutable worker publication, not per console refresh.
    private void RefreshIndustrialPublication()
    {
        var snapshot = _economy.Snapshot;
        var definition = _economy.Definition;
        if (snapshot == null || definition == null || ReferenceEquals(snapshot, _industrialSnapshot)) return;
        EnsureIndustrialWorlds();
        _industrialSnapshot = snapshot;
        var activeWorlds = definition.Worlds.Where(w => w.Active).Select(w => w.Id).ToArray();
        _industrialCandidates = SWGovernorPolicy.Evaluate(definition, snapshot, activeWorlds,
            new SWGovernorPersonality(true, false, false)).ToList();
        var stores = snapshot.Inventory.ToDictionary(i => (i.World, i.Good));
        var governors = _governors.BuildViews().ToDictionary(g => g.Sector);
        var problems = new List<SWEconomyProblemView>();
        foreach (var good in snapshot.Goods)
        {
            var deficit = Math.Max(0, good.Required - good.Consumed);
            stores.TryGetValue((good.World, good.Good), out var stock);
            if (deficit <= 1e-6) continue;
            var demandRate = good.Required / SWRepublicEconomyRunner.StepHours;
            var coverage = demandRate > 0 ? (stock?.Stock ?? 0) / demandRate : 0;
            var sources = definition.Production.Where(p => p.Outputs.Any(o => o.Good == good.Good))
                .Select(p => IndustrialWorldName(p.World)).Distinct().Take(8);
            var routes = snapshot.Flows.Where(f => f.To == good.World && f.Good == good.Good).ToArray();
            var local = definition.Production.Where(p => p.World == good.World && p.Outputs.Any(o => o.Good == good.Good)).ToArray();
            var lackingInputs = local.SelectMany(p => p.Inputs).Where(i => !stores.TryGetValue((good.World, i.Good), out var input) || input.Stock < i.Quantity)
                .Select(i => IndustrialGoodName(i.Good)).Distinct().ToArray();
            var reason = lackingInputs.Length > 0 ? $"Возможная нехватка сырья: {string.Join(", ", lackingInputs)}"
                : local.Length > 0 ? "Проверить загрузку мощностей и энергоснабжение"
                : routes.Length == 0 ? "Нет зафиксированных поставок: проверить маршрут и источники"
                : "Проверить пропускную способность и транспорт";
            var sector = IndustrialSector(good.World);
            problems.Add(new SWEconomyProblemView
            {
                World = good.World, WorldName = IndustrialWorldName(good.World), Sector = sector,
                Good = good.Good, GoodName = IndustrialGoodName(good.Good), Kind = "Дефицит",
                Detail = $"Не обеспечено {deficit / SWRepublicEconomyRunner.StepHours:0.##}/ч; запас {stock?.Stock ?? 0:0.##}, покрытие {coverage:0.#} ч; в пути {good.Incoming:0.##}. {reason}.",
                Sources = string.Join(", ", sources),
                Governor = governors.TryGetValue(sector, out var governor) ? $"{governor.Name}: {governor.Status}; {governor.Priority}" : "Губернатор ещё не назначен",
            });
        }
        foreach (var line in snapshot.Production)
        {
            var recipe = definition.Production.FirstOrDefault(p => p.World == line.World && p.Recipe == line.Recipe);
            if (recipe == null || line.EffectiveCyclesPerHour <= 0) continue;
            var loading = line.StartedCyclesLastStep / SWRepublicEconomyRunner.StepHours / line.EffectiveCyclesPerHour;
            if (loading >= .5) continue;
            var missing = recipe.Inputs.Where(i => !stores.TryGetValue((line.World, i.Good), out var input) || input.Stock < i.Quantity)
                .Select(i => IndustrialGoodName(i.Good)).ToArray();
            if (missing.Length == 0) continue;
            var sector = IndustrialSector(line.World);
            foreach (var output in recipe.Outputs)
                problems.Add(new SWEconomyProblemView { World = line.World, WorldName = IndustrialWorldName(line.World), Sector = sector,
                    Good = output.Good, GoodName = IndustrialGoodName(output.Good), Kind = "Производство",
                    Detail = $"{IndustrialRecipeName(line.Recipe)}: начато {line.StartedCyclesLastStep / SWRepublicEconomyRunner.StepHours:0.##} циклов/ч при мощности {line.EffectiveCyclesPerHour:0.##}; недостаточно сырья: {string.Join(", ", missing)}.",
                    Sources = string.Join(", ", definition.Production.Where(p => p.Outputs.Any(o => recipe.Inputs.Any(i => i.Good == o.Good))).Select(p => IndustrialWorldName(p.World)).Distinct().Take(8)),
                    Governor = governors.TryGetValue(sector, out var g) ? $"{g.Name}: {g.Status}; {g.Priority}" : "Губернатор ещё не назначен" });
        }
        foreach (var edge in snapshot.Edges.Where(e => e.CapacitySlotsPerHour > 0 && e.UsedSlotsLastStep / SWRepublicEconomyRunner.StepHours >= e.CapacitySlotsPerHour * .9))
        {
            var link = definition.Edges.FirstOrDefault(e => e.Id == edge.Id);
            if (link == null) continue;
            foreach (var flow in snapshot.Flows.Where(f => f.Edge == edge.Id && (f.Dispatched > 0 || f.InTransit > 0)).GroupBy(f => (f.To, f.Good)).Select(g => g.First()))
            {
                var sector = IndustrialSector(flow.To);
                problems.Add(new SWEconomyProblemView { World = flow.To, WorldName = IndustrialWorldName(flow.To), Sector = sector,
                    Good = flow.Good, GoodName = IndustrialGoodName(flow.Good), Kind = "Перевозки",
                    Detail = $"Маршрут {IndustrialWorldName(link.A)} — {IndustrialWorldName(link.B)} загружен: {edge.UsedSlotsLastStep / SWRepublicEconomyRunner.StepHours:0.##} из {edge.CapacitySlotsPerHour:0.##} мест/ч; в пути {flow.InTransit:0.##}.",
                    Sources = IndustrialWorldName(flow.From), Governor = governors.TryGetValue(sector, out var g) ? $"{g.Name}: {g.Status}; {g.Priority}" : "Губернатор ещё не назначен" });
            }
        }
        _industrialProblems = problems.OrderBy(p => p.WorldName, StringComparer.Ordinal).ThenBy(p => p.GoodName, StringComparer.Ordinal).ToList();
    }

    public void SelectGood(string good)
    {
        if (_economy.Definition?.Production.Any(p => p.Outputs.Any(o => o.Good == good)) == true)
            SelectedGood = good;
    }
    public List<SWEconomyGoodView> BuildIndustrialGoods() => _economy.Definition?.Production.SelectMany(p => p.Outputs)
        .Select(o => o.Good).Distinct().Select(id => new SWEconomyGoodView { Id = id, Name = IndustrialGoodName(id) })
        .OrderBy(g => g.Name, StringComparer.Ordinal).ToList() ?? new();
    public List<SWEconomyProblemView> BuildIndustrialProblems() => _industrialProblems;
    private bool ActiveIndustrialKey(string world, string recipe) => _economy.Runner?.IsExpansionPending(world, recipe) == true;
    private bool IndustrialAvailable => _time.ModeActive && _time.DayActive && _budget.Approved && _economy.Runner?.Fault == null;
    public List<SWEconomyExpansionView> BuildIndustrialCandidates()
    {
        var definition = _economy.Definition;
        if (definition == null || string.IsNullOrEmpty(SelectedGood)) return new();
        return _industrialCandidates.Where(c => definition.Production.Any(p => p.World == c.World && p.Recipe == c.Recipe
                && p.Outputs.Any(o => o.Good == SelectedGood)))
            .Select(c => new SWEconomyExpansionView
            {
                World = c.World, WorldName = IndustrialWorldName(c.World), Sector = IndustrialSector(c.World), SectorName = IndustrialSectorName(IndustrialSector(c.World)),
                Recipe = c.Recipe, RecipeName = IndustrialRecipeName(c.Recipe), Good = SelectedGood,
                Reason = c.Reason, Materials = Materials(c.Materials), Cost = c.BaseCost,
                AddedRate = c.AddedCyclesPerHour * definition.Production.First(p => p.World == c.World && p.Recipe == c.Recipe).Outputs.Where(o => o.Good == SelectedGood).Sum(o => o.Quantity) * (_industrialSnapshot?.Capacities.FirstOrDefault(v => v.World == c.World)?.ProductionMultiplier ?? 1), WorkHours = c.WorkHours,
                Available = IndustrialAvailable && !ActiveIndustrialKey(c.World, c.Recipe) && _dilemma.GetMinistryBudget(EconomyMinistryId) >= c.BaseCost,
                Status = ActiveIndustrialKey(c.World, c.Recipe) ? "Проект уже финансируется" : !IndustrialAvailable ? "Ожидается утверждённый бюджет и рабочее время" : _dilemma.GetMinistryBudget(EconomyMinistryId) < c.BaseCost ? "Недостаточно бюджета" : "Доступно",
            }).ToList();
    }
    public bool FundExpansion(string world, string recipe)
    {
        if (!IndustrialAvailable || _economy.Snapshot == null || !ReferenceEquals(_industrialSnapshot, _economy.Snapshot)) return false;
        var candidate = _industrialCandidates.FirstOrDefault(c => c.World == world && c.Recipe == recipe);
        if (candidate == null || ActiveIndustrialKey(world, recipe) || _economy.Definition?.Production.Any(p => p.World == world
                && p.Recipe == recipe && p.Outputs.Any(o => o.Good == SelectedGood)) != true) return false;
        var id = $"ministry-economy:industry:{++_nextIndustrialId}";
        var request = new EconomyProjectRequest(id, world, candidate.Materials, candidate.WorkHours,
            new EconomyProductionExpansion(recipe, candidate.AddedCyclesPerHour));
        if (!QueueIndustrialPaid(request, candidate.BaseCost)) return false;
        _industrialProjects.Add(new IndustrialProject(id, "industry", world + ":" + recipe, IndustrialRecipeName(recipe), world));
        AddLog($"Профинансировано расширение: {IndustrialRecipeName(recipe)}, {IndustrialWorldName(world)}.");
        return true;
    }
    private bool QueueIndustrialPaid(EconomyProjectRequest request, int cost)
    {
        var before = _dilemma.GetMinistryBudget(EconomyMinistryId);
        if (cost < 0 || !_dilemma.SpendMinistry(EconomyMinistryId, cost)) return false;
        if (_economy.QueueProject(request)) return true;
        _dilemma.SetMinistryBudget(EconomyMinistryId, before);
        return false;
    }
    private static string? ActiveRouteEndpoint(EconomyDefinition definition, EconomyEdge edge) =>
        definition.Worlds.Any(w => w.Id == edge.A && w.Active) ? edge.A
        : definition.Worlds.Any(w => w.Id == edge.B && w.Active) ? edge.B : null;

    public List<SWEconomyRouteView> BuildIndustrialRoutes()
    {
        var definition = _economy.Definition;
        if (definition == null || _industrialSnapshot == null) return new();
        var states = _industrialSnapshot.Edges.ToDictionary(e => e.Id);
        return definition.Edges.Select(edge =>
        {
            states.TryGetValue(edge.Id, out var state);
            var endpoint = ActiveRouteEndpoint(definition, edge);
            var project = _industrialProjects.FirstOrDefault(p => p.Kind == "route" && p.Key == edge.Id);
            var status = project == null ? "Доступно" : _economy.GetProject(project.Id)?.CompletedHour != null ? "Расширение завершено" : "Расширение оплачено";
            var construction = SWRepublicTransitExpansion.CreateProject(string.Empty, edge);
            return new SWEconomyRouteView { Id = edge.Id, A = edge.A, B = edge.B, AName = IndustrialWorldName(edge.A), BName = IndustrialWorldName(edge.B),
                ConstructionWorld = endpoint ?? string.Empty, ConstructionWorldName = endpoint == null ? "Нет действующей стройплощадки" : IndustrialWorldName(endpoint),
                Capacity = state?.CapacitySlotsPerHour ?? edge.CapacitySlotsPerHour, BaseCapacity = edge.CapacitySlotsPerHour,
                Loaded = (state?.UsedSlotsLastStep ?? 0) / SWRepublicEconomyRunner.StepHours, AddedCapacity = edge.CapacitySlotsPerHour * SWRepublicTransitExpansion.CapacityFraction, Cost = TransitCost,
                Detail = $"Пропускная способность {state?.CapacitySlotsPerHour ?? edge.CapacitySlotsPerHour:0.##} мест/ч; занято {((state?.UsedSlotsLastStep ?? 0) / SWRepublicEconomyRunner.StepHours):0.##} мест/ч. Улучшение +{edge.CapacitySlotsPerHour * SWRepublicTransitExpansion.CapacityFraction:0.##} мест/ч; {Materials(construction.Materials)}; работы {construction.WorkHours:0.#} ч. Сведения о блокировках маршрута недоступны.",
                Available = endpoint != null && project == null && IndustrialAvailable && _dilemma.GetMinistryBudget(EconomyMinistryId) >= TransitCost,
                Status = project != null ? status : endpoint == null ? "Нет действующего узла для строительства" : !IndustrialAvailable ? "Ожидается утверждённый бюджет и рабочее время" : _dilemma.GetMinistryBudget(EconomyMinistryId) < TransitCost ? "Недостаточно бюджета" : status };
        }).ToList();
    }
    public bool FundRoute(string edgeId)
    {
        if (!IndustrialAvailable || _industrialSnapshot == null || !ReferenceEquals(_industrialSnapshot, _economy.Snapshot)
            || _industrialProjects.Any(p => p.Kind == "route" && p.Key == edgeId)) return false;
        var definition = _economy.Definition;
        var edge = definition?.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge == null || definition == null) return false;
        var endpoint = ActiveRouteEndpoint(definition, edge);
        if (endpoint == null) return false;
        var id = $"ministry-economy:route:{++_nextIndustrialId}";
        if (!QueueIndustrialPaid(SWRepublicTransitExpansion.CreateProject(id, edge with { A = endpoint }), TransitCost)) return false;
        _industrialProjects.Add(new IndustrialProject(id, "route", edgeId, $"Маршрут {IndustrialWorldName(edge.A)} — {IndustrialWorldName(edge.B)}", endpoint));
        AddLog($"Профинансировано расширение маршрута {IndustrialWorldName(edge.A)} — {IndustrialWorldName(edge.B)}.");
        return true;
    }
    public List<SWEconomyIndustrialProjectView> BuildIndustrialProjects() => _industrialProjects.Select(p =>
    {
        var state = _economy.GetProject(p.Id);
        var delivery = state == null || state.Materials.Count == 0 ? 0 : state.Materials.Average(m => m.Required > 0 ? Math.Clamp(m.Delivered / m.Required, 0, 1) : 1);
        var done = state?.CompletedHour != null;
        var status = state == null ? "Заказ принят" : done ? "Завершён" : state.Status == EconomyProjectStatus.Working ? "Монтаж" : state.Reason switch
        { "fuel" => "Ожидает топлива", "transport" => "Ожидает транспорта", "transit" => "Материалы в пути", "no_route" => "Нет маршрута доставки", _ => "Ожидает материалов" };
        return new SWEconomyIndustrialProjectView { Id = p.Id, Name = p.Name, WorldName = IndustrialWorldName(p.World), Status = status, Completed = done,
            Progress = done ? 1 : state == null ? 0 : .5 * delivery + .5 * Math.Clamp(state.WorkDone / state.WorkHours, 0, 1),
            Materials = state == null ? "Ожидается публикация материалов" : string.Join("\n", state.Materials.Select(m =>
                $"{IndustrialGoodName(m.Good)}: получено {m.Delivered:0.##} из {m.Required:0.##} ед., в пути {m.Incoming:0.##} ед.; " +
                (m.DeliveryHours is { } eta ? $"доставка: {eta:0.#} ч" : m.MinimumTravelHours is { } minimum ? $"минимум в пути: {minimum:0.#} ч" : "оценка доставки недоступна"))),
            Work = state == null ? "Работы: ожидается публикация" : $"Работы: выполнено {state.WorkDone:0.#} из {state.WorkHours:0.#} ч, осталось {Math.Max(0, state.WorkHours - state.WorkDone):0.#} ч; оценка завершения недоступна" };
    }).ToList();
}
