using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Robust.Shared.Random;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryEconomySystem
{
    [Dependency] private readonly SWMinistrySystem _eventMinistry = default!;
    [Dependency] private readonly SWMinistryHealthSystem _eventHealth = default!;
    private const string EventSource = "economy-events";
    private const int StandardEventCost = 600, FastEventCost = 1050;
    private sealed class EconomyIncident
    {
        public int Id;
        public string Kind = "", World = "", Edge = "", Recipe = "", Group = "", Project = "";
        public double Age, Strength = 1, DecayFrom = -1, DecayAge, AidQuantity;
        public bool Resolved, ResolvedByProject;
    }
    private readonly List<EconomyIncident> _economyEvents = new();
    private readonly Dictionary<string, double> _eventCooldown = new();
    private int _eventId, _eventDay = -1, _dailyEvents;
    private bool _hadEventEffect;
    private EconomySnapshot? _eventLastSnapshot, _eventPreviousSnapshot;
    private double _ministerHours, _lastEventAt = double.NegativeInfinity, _eventCheck, _effectCheck;

    private void ResetEconomyEvents()
    {
        _economyEvents.Clear(); _eventCooldown.Clear(); _eventId = 0; _eventDay = -1; _dailyEvents = 0;
        _ministerHours = 0; _lastEventAt = double.NegativeInfinity; _eventCheck = 0; _effectCheck = 0; _hadEventEffect = false; _eventLastSnapshot = null; _eventPreviousSnapshot = null;
        _economy.SetProductionModifiers(EventSource, Array.Empty<EconomyProductionModifier>());
        _economy.SetDemandModifiers(EventSource, Array.Empty<EconomyDemandModifier>());
        _economy.SetTransitRestrictions(EventSource, new Dictionary<string, double>());
    }

    private void TickEconomyEvents(double hours)
    {
        if (_economy.Snapshot is { } observed && !ReferenceEquals(observed, _eventLastSnapshot))
        { _eventPreviousSnapshot = _eventLastSnapshot; _eventLastSnapshot = observed; }
        if (_eventDay != _time.Day) { _eventDay = _time.Day; _dailyEvents = 0; _ministerHours = 0; }
        var minister = _eventMinistry.IsHolderActive(EconomyMinistryId);
        _ministerHours = minister && _budget.Approved ? _ministerHours + hours : 0;
        foreach (var ev in _economyEvents.Where(e => !e.Resolved))
        {
            if (ev.Project.Length > 0 && _economy.GetProject(ev.Project)?.CompletedHour != null)
            { ev.Strength = 0; ev.Resolved = true; ev.ResolvedByProject = true; continue; }
            ev.Age += hours;
            if (!minister && ev.DecayFrom < 0) { ev.DecayFrom = ev.Strength; ev.DecayAge = 0; }
            if (ev.DecayFrom >= 0)
            { ev.DecayAge += hours; ev.Strength = ev.DecayFrom * Math.Max(0, 1 - ev.DecayAge); }
            else ev.Strength = Math.Clamp((6 - ev.Age) / 2, 0, 1);
            if (ev.Strength <= 0) { ev.Strength = 0; ev.Resolved = true; }
        }
        _effectCheck -= hours;
        var activeEffect = _economyEvents.Any(e => !e.Resolved);
        if ((activeEffect && _effectCheck <= 0) || (!activeEffect && _hadEventEffect))
        { ApplyEconomyEventEffects(); _effectCheck = .05; }
        _eventCheck -= hours;
        if (_eventCheck > 0) return;
        _eventCheck = .25;
        if (!minister || !_budget.Approved || _ministerHours < 1 || _dailyEvents >= 2
            || _economy.Snapshot == null || _economy.Runner?.Fault != null
            || _economyEvents.Any(e => !e.Resolved) || _time.ActiveGameHours - _lastEventAt < 5) return;
        var candidates = EventCandidates();
        if (candidates.Count == 0) return;
        var kinds = candidates.Select(c => c.Kind).Distinct().ToArray();
        var chosenKind = _random.Pick(kinds);
        var incident = _random.Pick(candidates.Where(c => c.Kind == chosenKind).ToArray());
        AdmitEconomyIncident(incident, automatic: true);
    }

    private void AdmitEconomyIncident(EconomyIncident incident, bool automatic)
    {
        incident.Id = ++_eventId;
        _economyEvents.Add(incident);
        if (automatic) _dailyEvents++;
        _lastEventAt = _time.ActiveGameHours;
        _eventCooldown[incident.World] = _lastEventAt;
        if (incident.Edge.Length > 0) _eventCooldown[incident.Edge] = _lastEventAt;
        ApplyEconomyEventEffects();
    }

    public bool CreateDirectorEconomyIncident(string kind, string target, out string notice)
    {
        notice = string.Empty;
        if (!_time.ModeActive || !_time.DayActive)
        { notice = "Событие можно создать только в рабочее время Сената."; return false; }
        if (kind is not ("production" or "transit" or "demand"))
        { notice = "Неизвестный тип экономического события."; return false; }
        if (_economy.Snapshot == null || _economy.Runner?.Fault != null)
        { notice = "Экономика ещё не готова к созданию событий."; return false; }
        if (_economyEvents.Any(e => !e.Resolved))
        { notice = "Другое экономическое событие ещё действует."; return false; }
        var candidates = EventCandidatesCore(ignoreCooldown: true).Where(e => e.Kind == kind
            && (string.IsNullOrEmpty(target) || (kind == "transit" ? e.Edge == target : e.World == target))).ToArray();
        if (candidates.Length == 0)
        { notice = "Нет подходящего мира или маршрута с доступными материалами для восстановления."; return false; }
        var incident = _random.Pick(candidates);
        AdmitEconomyIncident(incident, automatic: false);
        var place = kind == "transit" && _economy.Definition?.Edges.FirstOrDefault(e => e.Id == incident.Edge) is { } edge
            ? $"{IndustrialWorldName(edge.A)} — {IndustrialWorldName(edge.B)}" : IndustrialWorldName(incident.World);
        var title = kind switch { "production" => "Авария производства", "transit" => "Повреждение грузового узла", _ => "Повышенный спрос на воду" };
        notice = $"Создано событие: {title}, {place}.";
        return true;
    }

    private void ApplyEconomyEventEffects()
    {
        var active = _economyEvents.Where(e => !e.Resolved && e.Strength > 0).ToArray();
        _hadEventEffect = active.Length > 0;
        _economy.SetProductionModifiers(EventSource, active.Where(e => e.Kind == "production")
            .Select(e => new EconomyProductionModifier(e.World, e.Recipe, 1 - .4 * e.Strength)).ToArray());
        _economy.SetDemandModifiers(EventSource, active.Where(e => e.Kind == "demand")
            .Select(e => new EconomyDemandModifier(e.World, e.Group, 1 + .25 * e.Strength)).ToArray());
        _economy.SetTransitRestrictions(EventSource, active.Where(e => e.Kind == "transit")
            .ToDictionary(e => e.Edge, e => 1 - .4 * e.Strength));
    }

    private List<EconomyIncident> EventCandidates() => EventCandidatesCore(ignoreCooldown: false);

    private List<EconomyIncident> EventCandidatesCore(bool ignoreCooldown)
    {
        var definition = _economy.Definition!;
        var snapshot = _economy.Snapshot!;
        EnsureIndustrialWorlds();
        var blocked = _eventHealth.BuildQuarantines().Where(q => !q.Pending).SelectMany(q => q.WorldIds).ToHashSet();
        var map = _proto.Index(_proto.Index<SWRepublicEconomyScenarioPrototype>("SenateRepublicEconomy").Map);
        var republic = map.Worlds.Where(w => map.PoliticalAreas.LastOrDefault(a => a.Contains(w.Position))?.Faction == "republic")
            .Select(w => w.Id).ToHashSet();
        var active = definition.Worlds.Where(w => w.Active && republic.Contains(w.Id) && !blocked.Contains(w.Id) && IndustrialSector(w.Id).Length > 0).Select(w => w.Id).ToHashSet();
        var liveEdges = snapshot.Edges.Where(e => e.CapacitySlotsPerHour > 1e-6).Select(e => e.Id).ToHashSet();
        var edges = definition.Edges.Where(e => liveEdges.Contains(e.Id) && active.Contains(e.A) && active.Contains(e.B)).ToArray();
        active.IntersectWith(edges.SelectMany(e => new[] { e.A, e.B }));
        bool Cool(string key) => ignoreCooldown || !_eventCooldown.TryGetValue(key, out var last) || _time.ActiveGameHours - last >= SWSenateTimeSystem.EndHour - SWSenateTimeSystem.StartHour;
        var kits = new Dictionary<string, bool>();
        bool Kits(string world)
        {
            if (!kits.TryGetValue(world, out var available))
                kits[world] = available = HasTimelyEventSupply(new EconomyIncident { Kind = "production", World = world }, false);
            return available;
        }
        var result = new List<EconomyIncident>();
        foreach (var p in definition.Production.Where(p => p.Rate > 0 && active.Contains(p.World) && Cool(p.World)))
            if (Kits(p.World)) result.Add(new EconomyIncident { Kind = "production", World = p.World, Recipe = p.Recipe });
        foreach (var edge in edges.Where(e => Cool(e.Id)))
        {
            var endpoint = new[] { edge.A, edge.B }.FirstOrDefault(w => Cool(w) && Kits(w));
            if (endpoint != null) result.Add(new EconomyIncident { Kind = "transit", World = endpoint, Edge = edge.Id });
        }
        foreach (var d in definition.Demands.Where(d => d.Group == "water" && d.Good == "water" && d.Rate > 0 && active.Contains(d.World) && Cool(d.World)))
        {
            var quantity = Math.Clamp(d.Rate * .25 * 4, .1, 2);
            var incident = new EconomyIncident { Kind = "demand", World = d.World, Group = d.Group, AidQuantity = quantity };
            if (HasTimelyEventSupply(incident, false)) result.Add(incident);
        }
        return result;
    }

    // This is a lower bound, not an ETA: queueing, carrier availability and later blockades can add delay.
    private bool HasTimelyEventSupply(EconomyIncident ev, bool accelerated)
    {
        var definition = _economy.Definition;
        var snapshot = _economy.Snapshot;
        if (definition == null || snapshot == null) return false;
        var remaining = ev.DecayFrom >= 0 ? Math.Max(0, 1 - ev.DecayAge) : Math.Max(0, 6 - ev.Age);
        var travelBudget = remaining - (accelerated ? 1 : 2) - .5 - SWRepublicEconomyRunner.StepHours;
        if (travelBudget < 0) return false;
        var blocked = _eventHealth.BuildQuarantines().Where(q => !q.Pending).SelectMany(q => q.WorldIds).ToHashSet();
        if (blocked.Contains(ev.World)) return false;
        var live = snapshot.Edges.Where(e => e.CapacitySlotsPerHour > 1e-6).Select(e => e.Id).ToHashSet();
        var allowed = definition.Worlds.Where(w => w.Active && !blocked.Contains(w.Id)).Select(w => w.Id).ToHashSet();
        if (!allowed.Contains(ev.World)) return false;
        var edges = definition.Edges.Where(e => live.Contains(e.Id) && allowed.Contains(e.A) && allowed.Contains(e.B)).ToArray();
        var distances = new Dictionary<string, double> { [ev.World] = 0 };
        var firstLeg = new Dictionary<string, EconomyEdge>();
        bool CanDepart(string world, EconomyEdge edge)
        {
            if (definition.EnableAssets && !snapshot.Capacities.Any(c => c.World == world && c.FleetCapacity - c.FleetOccupied > 1e-6)) return false;
            return edge.FuelPerSlot <= 1e-6 || snapshot.ProjectSupply.Any(i => i.World == world && i.Good == "ship_fuel" && i.Available > 1e-6);
        }
        var visited = new HashSet<string>();
        while (true)
        {
            var next = distances.Where(p => !visited.Contains(p.Key) && p.Value <= travelBudget).OrderBy(p => p.Value).FirstOrDefault();
            if (next.Key == null) break;
            visited.Add(next.Key);
            // Reverse traversal through a non-destination promises that node can relay freight.
            if (next.Key != ev.World && !CanDepart(next.Key, firstLeg[next.Key])) continue;
            foreach (var edge in edges.Where(e => e.A == next.Key || e.B == next.Key))
            {
                var other = edge.A == next.Key ? edge.B : edge.A;
                var distance = next.Value + Math.Ceiling(edge.TravelHours / SWRepublicEconomyRunner.StepHours) * SWRepublicEconomyRunner.StepHours;
                if (distance <= travelBudget && (!distances.TryGetValue(other, out var old) || distance < old))
                { distances[other] = distance; firstLeg[other] = edge; }
            }
        }
        var sources = distances.Keys.Where(w => w == ev.World || CanDepart(w, firstLeg[w])).ToHashSet();
        return EventMaterials(ev, accelerated).All(m => EventSupplyAvailable(snapshot, sources, m.Good, m.Quantity));
    }

    internal static bool EventSupplyAvailable(EconomySnapshot snapshot, HashSet<string> sources, string good, double amount) =>
        snapshot.ProjectSupply.Where(i => sources.Contains(i.World) && i.Good == good).Sum(i => i.Available) + 1e-9 >= amount;

    private EconomyAmount[] EventMaterials(EconomyIncident ev, bool accelerated)
    {
        var materials = ev.Kind == "demand" ? new List<EconomyAmount> { new("water", ev.AidQuantity) }
            : new List<EconomyAmount> { new("parts", .2), new("circuits", .1) };
        if (accelerated) materials.Add(new EconomyAmount("machinery", .1));
        return materials.ToArray();
    }

    public bool RespondEconomyEvent(int id, bool accelerated)
    {
        var ev = _economyEvents.FirstOrDefault(e => e.Id == id);
        if (ev == null || ev.Resolved || ev.Project.Length > 0 || !_eventMinistry.IsHolderActive(EconomyMinistryId)
            || !IndustrialAvailable || !HasTimelyEventSupply(ev, accelerated)) return false;
        var projectId = $"ministry-economy:event:{id}";
        var request = new EconomyProjectRequest(projectId, ev.World, EventMaterials(ev, accelerated), accelerated ? 1 : 2) { EconomicRelief = true };
        if (!QueueIndustrialPaid(request, accelerated ? FastEventCost : StandardEventCost)) return false;
        ev.Project = projectId;
        _industrialProjects.Add(new IndustrialProject(projectId, "event", id.ToString(),
            ev.Kind switch { "production" => "Ремонт производства", "transit" => "Ремонт грузового узла", _ => "Поставка воды" }, ev.World));
        return true;
    }

    private SWEconomyEventOptionView EventOption(EconomyIncident ev, bool accelerated)
    {
        var cost = accelerated ? FastEventCost : StandardEventCost;
        var status = ev.Resolved ? "Событие завершено" : ev.Project.Length > 0 ? "Ответ уже оплачен"
            : !_eventMinistry.IsHolderActive(EconomyMinistryId) ? "Министр отсутствует"
            : !IndustrialAvailable ? "Ожидается утверждённый бюджет и рабочее время"
            : !HasTimelyEventSupply(ev, accelerated) ? "Недостаточно времени или доступных материалов для восстановления"
            : _dilemma.GetMinistryBudget(EconomyMinistryId) < cost ? "Недостаточно бюджета" : "Доступно";
        return new SWEconomyEventOptionView { Cost = cost, Materials = Materials(EventMaterials(ev, accelerated)), WorkHours = accelerated ? 1 : 2,
            Available = status == "Доступно", Status = status };
    }

    public List<SWEconomyEventView> BuildEconomyEvents() => _economyEvents.OrderByDescending(e => e.Id).Take(20).Select(ev =>
    {
        var edge = _economy.Definition?.Edges.FirstOrDefault(e => e.Id == ev.Edge);
        var state = ev.Project.Length > 0 ? _economy.GetProject(ev.Project) : null;
        var title = ev.Kind switch { "production" => "Авария производства", "transit" => "Повреждение грузового узла", _ => "Повышенный спрос на воду" };
        var effect = ev.Kind switch { "production" => $"Мощность «{IndustrialRecipeName(ev.Recipe)}»: {100 - 40 * ev.Strength:0}%", "transit" => $"Пропускная способность: {100 - 40 * ev.Strength:0}%", _ => $"Потребность в воде: {100 + 25 * ev.Strength:0}%" };
        var status = ev.Resolved ? ev.ResolvedByProject ? "Восстановление завершено" : "Естественное восстановление завершено" : ev.DecayFrom >= 0 ? "Ответственные службы восстанавливают работу: министр отсутствует" : ev.Age >= 4 ? "Естественное восстановление" : "Действует";
        var materialText = state == null ? (ev.Project.Length == 0 ? "Проект не запущен" : "Заявка принята") : string.Join("\n", state.Materials.Select(m =>
            $"{IndustrialGoodName(m.Good)}: получено {m.Delivered:0.##} из {m.Required:0.##} ед., в пути {m.Incoming:0.##} ед.; " +
            (m.Delivered + m.QuantityTolerance >= m.Required ? "доставлено" : m.DeliveryHours is { } eta ? $"доставка {eta:0.#} ч" : m.MinimumTravelHours is { } min ? $"минимум {min:0.#} ч" : "оценка доставки недоступна")));
        var work = state == null ? "Работы ещё не начаты"
            : state.CompletedHour != null ? $"Работы завершены: {state.WorkHours:0.#} ч"
            : state.Status == EconomyProjectStatus.Working ? $"Работы: выполнено {state.WorkDone:0.#} из {state.WorkHours:0.#} ч"
            : $"{state.Reason switch { "no_route" => "Нет маршрута", "fuel" => "Ожидает топлива", "transport" => "Ожидает транспорта", "transit" => "Материалы в пути", _ => "Ожидает материалов" }}. Работы после доставки: {state.WorkHours - state.WorkDone:0.#} ч";
        return new SWEconomyEventView { Id = ev.Id, Active = !ev.Resolved, CrisisId = $"economy:event:{ev.Id}", Sector = IndustrialSector(ev.World), SectorName = IndustrialSectorName(IndustrialSector(ev.World)), Kind = ev.Kind, World = ev.World, WorldName = IndustrialWorldName(ev.World), Edge = ev.Edge,
            AName = edge == null ? "" : IndustrialWorldName(edge.A), BName = edge == null ? "" : IndustrialWorldName(edge.B), Title = title, Effect = effect, Status = status,
            Detail = ev.Kind == "demand" ? $"Целевая поставка: {ev.AidQuantity:0.##} ед. воды."
                : ev.Kind == "production" ? "Повреждено оборудование производственной линии." : "Повреждено оборудование грузового терминала.",
            RemainingHours = ev.Resolved ? 0 : ev.DecayFrom >= 0 ? Math.Max(0, 1 - ev.DecayAge) : Math.Max(0, 6 - ev.Age), ProjectId = ev.Project,
            Materials = materialText, Work = work, Standard = EventOption(ev, false), Fast = EventOption(ev, true) };
    }).ToList();

    public List<SWMapCrisisEntry> BuildPublicMapCrises() => BuildEconomyEvents().Where(e => e.RemainingHours > 0).Select(e => new SWMapCrisisEntry
    {
        Id = e.CrisisId, Title = e.Title, Description = e.Detail, WorldId = e.World, EdgeId = e.Edge,
        LocationLabel = e.Edge.Length == 0 ? e.WorldName : $"{e.AName} — {e.BName}",
        SectorIds = IndustrialSector(e.World) is { Length: > 0 } sector ? new List<string> { sector } : new(),
        Status = e.Status, Consequences = $"{e.Effect}; осталось до восстановления {e.RemainingHours:0.#} ч.", Severity = (float) (e.Kind == "demand" ? 25 : 40),
        Routes = e.Edge.Length == 0 ? new() : new() { new SWMapCrisisRouteEntry { EdgeId = e.Edge,
            CapacityMultiplier = 1 - .4 * _economyEvents.First(v => v.Id == e.Id).Strength,
            BaseCapacity = _economy.Definition?.Edges.FirstOrDefault(v => v.Id == e.Edge)?.CapacitySlotsPerHour ?? 0 } }
    }).ToList();
}
