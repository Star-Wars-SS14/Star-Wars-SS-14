using System.Linq;
using Robust.Shared.Random;
using Content.Server._SW._Senate.Economy;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem
{
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWMinistrySystem _ministry = default!;
    private readonly List<SWDefenseEventView> _incidents = new();
    private readonly Dictionary<string, string> _construction = new();
    // Delivered kits remain at their receiving port until one present formation collects them.
    // This is a reference to the economy's physical project, never another stock ledger.
    private readonly Dictionary<string, string> _supplyPorts = new();
    private readonly Dictionary<string, SWDefenseWorldView> _worlds = new();
    private readonly List<SWTransitLinkPrototype> _links = new();
    private readonly Dictionary<string, string> _mainHyperlanes = new();
    private readonly List<SWDefenseBorderView> _borders = new();
    private readonly Dictionary<(string From, string To, bool Raid, SWDefenseStance Stance), List<string>?> _pathCache = new();
    private bool _initializedFleet;
    private int _nextEventId = 1;
    private int _nextProjectId = 1;
    private double _nextIncident;
    public bool ImperialThreatsEnabled { get; set; } = true;
    public bool MinisterActive => _ministry.IsHolderActive(DefenseMinistryId);
    public bool HasBorderFleet => _groups.Any(g => g.Alive && g.Order == SWDefenseFleetOrder.Border && g.Phase == SWDefenseOperationPhase.Standing);
    public int FleetUpkeep => (int) (_groups.Count(g => g.Alive) * UpkeepPerHour);
    public int FleetSize => _groups.Count(g => g.Alive);
    public int FleetActive => _groups.Count(g => g.Alive && g.Phase == SWDefenseOperationPhase.Standing && g.Order is SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol or SWDefenseFleetOrder.Border);
    public int ActiveOpSlots => FleetSize;
    public int ActiveOpUsed => _groups.Count(g => g.Alive && g.EventId != 0);

    private void ResetStrategic()
    {
        _initializedFleet = false;
        _nextEventId = 1;
        _nextProjectId = 1;
        _nextIncident = 0;
        ImperialThreatsEnabled = true;
        _incidents.Clear();
        _construction.Clear();
        _supplyPorts.Clear();
        _pathCache.Clear();
        _mainHyperlanes.Clear();
        _economy.SetDefenseTransitRestrictions(new Dictionary<string, double>());
    }

    // One roll per round, based on the full prototype catalogue, independent of office holders.
    private void EnsureGroups()
    {
        if (_initializedFleet)
            return;
        _initializedFleet = true;
        _worlds.Clear(); _links.Clear(); _borders.Clear();
        var map = _proto.Index<SWGalacticMapPrototype>("SenateRepublic");
        foreach (var route in map.Routes)
        {
            if (route.Local || route.Corridor == null) continue;
            var corridor = map.Corridors.FirstOrDefault(c => c.Id == route.Corridor);
            if (corridor != null)
                _mainHyperlanes[SWTransitLinkPrototype.LinkId(route.From, route.To)] = corridor.Name;
        }
        foreach (var world in map.Worlds)
        {
            var sector = world.Sector?.Id ?? string.Empty;
            _worlds[world.Id] = new SWDefenseWorldView
            {
                Id = world.Id, Name = world.Name, SectorId = sector,
                Faction = map.PoliticalAreas.LastOrDefault(a => a.Contains(world.Position))?.Faction ?? "neutral",
                Shipyard = sector.Length > 0 && _proto.TryIndex<SWSectorPrototype>(sector, out var sp) && sp.Shipyard && world.Anchor,
            };
        }
        _links.AddRange(_proto.EnumeratePrototypes<SWTransitLinkPrototype>()
            .Where(l => _worlds.ContainsKey(l.From) && _worlds.ContainsKey(l.To)).OrderBy(l => l.ID));
        foreach (var link in _links)
        {
            var a = _worlds[link.From]; var b = _worlds[link.To];
            // Only explicit Imperial control creates a military crossing in this scenario.
            // Unshaded map extent and neutral/Hutt worlds do not establish a treaty frontier.
            if (!(a.Faction == "republic" && b.Faction == "empire"
                || a.Faction == "empire" && b.Faction == "republic"))
                continue;
            var republic = a.Faction == "republic" ? a : b;
            var foreign = a.Faction == "republic" ? b : a;
            _borders.Add(new SWDefenseBorderView { Id = link.ID, RepublicWorldId = republic.Id,
                ForeignWorldId = foreign.Id, Faction = foreign.Faction });
        }
        var sectors = _proto.EnumeratePrototypes<SWSectorPrototype>().OrderBy(s => s.ID).ToArray();
        var target = Math.Clamp(2 + sectors.Length / 6 + _random.Next(-1, 2), 2, FleetCap);
        var homes = sectors.Select(s => _worlds.Values.FirstOrDefault(w => w.SectorId == s.ID && w.Faction == "republic"))
            .Where(w => w != null).Select(w => w!).ToArray();
        for (var i = 0; i < target && homes.Length > 0; i++)
        {
            var home = homes[i * homes.Length / target];
            _groups.Add(new SWFleetGroup { Id = _nextGroupId++, World = home.Id, Home = home.Id,
                Target = home.Id, Strength = _random.Next(85, 101), Supply = _random.Next(70, 91), Training = _random.Next(35, 56) });
        }
        _nextIncident = _time.ActiveGameHours + 1.5;
    }

    public override void Update(float frameTime)
    {
        var active = _time.ActiveSeconds(frameTime);
        if (active <= 0f)
            return;
        _accum += active;
        if (_accum < TickInterval)
            return;
        var hours = _accum / SWSenateTimeSystem.RealSecondsPerGameHour;
        _accum = 0f;
        if (_pending is { } pending && _senate.OfficesVacant)
        { _pending = null; ChangeStance(pending); }
        EnsureGroups();
        TickStrategic(hours);
        var cfg = Config(_stance);
        var service = _dilemma.FundWork(DefenseMinistryId, FleetSize * UpkeepPerHour * hours, ref _upkeepAccum);
        var coverage = _groups.Where(g => g.Alive && g.Order == SWDefenseFleetOrder.Border
            && g.Phase == SWDefenseOperationPhase.Standing).Sum(g => Effective(g) / 100f);
        AddBorder((cfg.Border * _senate.StatPressureScale("SWGlobalDefense", cfg.Border) - coverage * BorderReliefPerGroup * service) * hours);
        foreach (var (stat, delta) in cfg.Nudges) NudgeSectors(stat, delta * hours);
        _weariness = Math.Clamp(_weariness + cfg.Weariness * _senate.StatPressureScale("SWGlobalSociety", cfg.Weariness) * hours, 0f, 100f);
        foreach (var group in _groups.Where(g => g.Alive))
        {
            if (service == 0f) group.Supply = Math.Max(0, group.Supply - hours * 6);
            if (group.Phase != SWDefenseOperationPhase.Standing || group.Order is not (SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol)) continue;
            var sector = _worlds[group.World].SectorId;
            _senate.AddSectorBias(sector, "SWGlobalDefense", GarrisonDefense * Effective(group) / 100 * hours);
            _senate.AddSectorBias(sector, "SWGlobalSecurity", GarrisonSecurity * Effective(group) / 100 * hours);
        }
    }

    private static float Effective(SWFleetGroup group) => group.Strength * (1f - group.Damage / 125f)
        * (0.25f + group.Supply / 133.333f) * (0.75f + group.Training / 200f);

    private void TickStrategic(float hours)
    {
        EnsureGroups();
        var now = _time.ActiveGameHours;
        foreach (var ev in _incidents.Where(e => !e.Resolved))
        {
            if (!MinisterActive)
            {
                ev.Severity = Math.Max(0, ev.Severity - hours * 20);
                ev.ExpiresAt = Math.Min(ev.ExpiresAt, now + 1);
                ev.Status = "Деэскалация: министр отсутствует";
            }
            else if (_worlds.TryGetValue(ev.WorldId, out var affected) && affected.SectorId.Length > 0)
            {
                var protection = PlanetaryHarmFactor(ev.WorldId, ev.Kind);
                _senate.NudgeSectorGlobal(affected.SectorId, "SWGlobalSecurity", -ev.Severity * .003f * hours * protection);
                if (ev.Kind is SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade)
                    _senate.NudgeSectorGlobal(affected.SectorId, "SWGlobalDefense", -ev.Severity * .002f * hours * protection);
            }
            if (MinisterActive)
            {
                var fleetSuppression = ev.Severity <= 35 && ev.Kind is SWDefenseEventKind.Piracy or SWDefenseEventKind.Unrest
                    ? LocalCoverage(ev.WorldId) * .15f : 0;
                var suppression = (fleetSuppression + PlanetarySuppression(ev)) * hours;
                ev.EnemyStrength = Math.Max(0, ev.EnemyStrength - suppression);
                ev.Severity = Math.Max(0, ev.Severity - suppression);
                if (ev.EnemyStrength <= 0 || ev.Severity <= 0) { ResolveIncident(ev, true); continue; }
            }
            if (now < ev.ExpiresAt && ev.Severity > 0) continue;
            ev.Resolved = true; ev.Status = "Истёк срок угрозы";
            if (MinisterActive) { AddBorder(ev.Severity * .03f); _weariness = Math.Min(100, _weariness + ev.Severity * .015f); }
            AddLog($"{ev.Title}: {ev.Status}.");
        }
        foreach (var item in _construction.ToArray())
        {
            if (_economy.GetProject(item.Value)?.Status != EconomyProjectStatus.Completed) continue;
            _groups.Add(new SWFleetGroup { Id = _nextGroupId++, World = item.Key, Home = item.Key,
                Target = item.Key, Strength = 70, Supply = 12, Training = 10 });
            _construction.Remove(item.Key); AddLog("Новая флотская группа принята с верфи.");
        }
        foreach (var group in _groups.Where(g => g.Alive))
        {
            group.Supply = Math.Max(0, group.Supply - hours * (group.Phase == SWDefenseOperationPhase.Travel ? 7 : 3));
            TickSupply(group);
            if (group.Next == null && MustRecallOperation(group))
            { BeginReturn(group); continue; }
            if (group.Phase == SWDefenseOperationPhase.Travel && now >= group.Until)
            {
                group.World = group.Next!; group.Next = null;
                if (MustRecallOperation(group)) BeginReturn(group);
                else if (!StartNextLeg(group)) Arrive(group);
            }
            else if (group.Phase == SWDefenseOperationPhase.Preparation && now >= group.Until)
            { if (!StartNextLeg(group)) Arrive(group); }
            else if (group.Phase == SWDefenseOperationPhase.Execution && group.Order == SWDefenseFleetOrder.Respond)
                TickCombat(group, hours);
            else if (group.Phase == SWDefenseOperationPhase.Recovery && now >= group.Until)
            { group.EventId = 0; BeginReturn(group); }
            else if (group.Order == SWDefenseFleetOrder.Repair && group.Phase == SWDefenseOperationPhase.Execution)
            {
                if (group.WorkProject == null)
                {
                    var id = ProjectId("repair");
                    if (_economy.QueueNavyProject(id, group.World, SWNavyProjectKind.Repair, recipientFleetId: group.Id)) group.WorkProject = id;
                }
                if (group.WorkProject != null && _economy.GetProject(group.WorkProject)?.Status == EconomyProjectStatus.Completed)
                { group.Damage = 0; group.Strength = Math.Min(100, group.Strength + 30); group.WorkProject = null;
                    group.Order = SWDefenseFleetOrder.Guard; group.Phase = SWDefenseOperationPhase.Standing; AddLog($"{group.Id}-й флот завершил ремонт."); }
            }
            else if (group.Order == SWDefenseFleetOrder.Train && group.Phase == SWDefenseOperationPhase.Standing && group.Supply >= 20)
                group.Training = Math.Min(100, group.Training + hours * 4);
            else if (group.Order == SWDefenseFleetOrder.Patrol && group.Phase == SWDefenseOperationPhase.Standing && now >= group.Until)
            {
                if (group.PatrolEdge.Length > 0 && _links.FirstOrDefault(l => l.ID == group.PatrolEdge) is { } patrol)
                { group.Path.Enqueue(patrol.From == group.World ? patrol.To : patrol.From); StartNextLeg(group); continue; }
                var options = _links.Where(l => l.Accessible && (l.From == group.World || l.To == group.World))
                    .Select(l => l.From == group.World ? l.To : l.From).Where(w => _worlds[w].Faction == "republic"
                        && _worlds[w].SectorId == _worlds[group.Target].SectorId).ToArray();
                if (options.Length > 0) { group.Path.Enqueue(options[_random.Next(options.Length)]); StartNextLeg(group); }
                else group.Until = now + 1;
            }
        }
        if (now >= _nextIncident)
        {
            _nextIncident = now + (MinisterActive ? _random.Next(2, 5) : _random.Next(4, 8));
            if (MinisterActive) GenerateIncident();
            else GenerateMinorDisruption();
        }
        _incidents.RemoveAll(e => e.Resolved && now - e.ExpiresAt > 12);
        ApplyRouteRestrictions();
    }

    private bool MustRecallOperation(SWFleetGroup group) => group.EventId != 0
        && group.Phase != SWDefenseOperationPhase.Recovery
        && (!MinisterActive || !_incidents.Any(e => e.Id == group.EventId && !e.Resolved));

    private string ProjectId(string purpose) => $"navy-{purpose}-{_nextProjectId++}";
    private void TickSupply(SWFleetGroup group)
    {
        group.SupplyProject = null;
        // Freight cannot rendezvous with a formation in hyperspace or at an enemy world.
        if (group.Supply > 60 || group.Next != null || _worlds[group.World].Faction != "republic") return;
        if (_supplyPorts.TryGetValue(group.World, out var projectId))
        {
            var project = _economy.GetProject(projectId);
            if (project is { Status: EconomyProjectStatus.Completed } && project.World == group.World)
            {
                group.Supply = Math.Min(100, group.Supply + (float) SWRepublicNavyLogistics.SupplyHours * 3);
                _supplyPorts.Remove(group.World);
            }
            else group.SupplyProject = projectId;
            return;
        }
        var id = ProjectId("supply");
        if (!_economy.QueueNavyProject(id, group.World, SWNavyProjectKind.Supply)) return;
        _supplyPorts[group.World] = id;
        group.SupplyProject = id;
    }

    public bool RecruitFleet(string worldId)
    {
        EnsureGroups();
        if (!_time.ModeActive || !_time.DayActive || !MinisterActive || !_worlds.TryGetValue(worldId, out var world)
            || !world.Shipyard || world.Faction != "republic" || _construction.ContainsKey(worldId)
            || FleetSize + _construction.Count >= FleetCap + 3 || _dilemma.GetMinistryBudget(DefenseMinistryId) < 5000) return false;
        var id = ProjectId("construction");
        if (!_economy.QueueNavyProject(id, worldId, SWNavyProjectKind.Construction)) return false;
        _dilemma.SpendMinistry(DefenseMinistryId, 5000);
        _construction[worldId] = id; AddLog($"Верфь {world.Name}: заказано формирование флотской группы."); return true;
    }

    public bool IssueFleetOrder(int fleetId, SWDefenseFleetOrder order, string target, int eventId = 0)
    {
        if (target == null || !Enum.IsDefined(order)) return false;
        EnsureGroups();
        var group = _groups.FirstOrDefault(g => g.Id == fleetId && g.Alive);
        if (group == null) return false;
        var preview = PreviewOrder(group, order, target, eventId);
        if (!preview.Available) return false;
        var destination = ResolveTarget(group, order, target, eventId);
        var route = FindPath(group.Next ?? group.World, destination, MilitaryRoute(group, order));
        if (route == null || preview.Cost > 0 && !_dilemma.SpendMinistry(DefenseMinistryId, preview.Cost)) return false;
        group.Path.Clear(); foreach (var world in route) group.Path.Enqueue(world);
        group.Target = destination; group.Order = order; group.EventId = order == SWDefenseFleetOrder.Respond ? eventId : 0;
        group.FinishingWindowUsed = false;
        group.PatrolEdge = order == SWDefenseFleetOrder.Patrol && _links.Any(l => l.ID == target) ? target : string.Empty;
        // Recall and movement redirects replace only the route after the current crossing.
        if (group.Next == null)
        { group.Phase = SWDefenseOperationPhase.Preparation; group.Until = _time.ActiveGameHours + preview.PreparationHours; }
        AddLog($"{group.Id}-й флот: {OrderName(order)}, {_worlds[destination].Name}."); return true;
    }

    private string ResolveTarget(SWFleetGroup group, SWDefenseFleetOrder order, string target, int eventId)
    {
        if (order == SWDefenseFleetOrder.Return) return group.Home;
        if (order == SWDefenseFleetOrder.Respond) return _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved)?.WorldId ?? string.Empty;
        if (order == SWDefenseFleetOrder.Border) return _borders.FirstOrDefault(b => b.Id == target || b.RepublicWorldId == target)?.RepublicWorldId ?? string.Empty;
        if (order == SWDefenseFleetOrder.Patrol && _links.FirstOrDefault(l => l.ID == target && l.Accessible) is { } link)
        {
            var source = group.Next ?? group.World;
            if (source == link.From || source == link.To) return source;
            return PathHours(source, FindPath(source, link.From, false)) <= PathHours(source, FindPath(source, link.To, false)) ? link.From : link.To;
        }
        return target;
    }

    private SWDefenseOrderOptionView PreviewOrder(SWFleetGroup group, SWDefenseFleetOrder order, string target, int eventId)
    {
        var preview = new SWDefenseOrderOptionView { FleetId = group.Id, Order = order, Target = target, EventId = eventId,
            PreparationHours = order == SWDefenseFleetOrder.Respond ? .5
                : order == SWDefenseFleetOrder.Move ? group.Next != null ? 0 : .03 : .15,
            ExecutionHours = order is SWDefenseFleetOrder.Respond or SWDefenseFleetOrder.Repair ? 2 : 0,
            Cost = order == SWDefenseFleetOrder.Repair ? YardRepairCost : 0,
            Risk = "Снабжение расходуется; повреждения требуют верфи." };
        var destination = ResolveTarget(group, order, target, eventId);
        preview.ResolvedWorldId = destination;
        var ev = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        var route = FindPath(group.Next ?? group.World, destination, MilitaryRoute(group, order));
        if (route != null)
        {
            var current = group.Next ?? group.World;
            if (group.Next != null) preview.TravelHours += Math.Max(0, group.Until - _time.ActiveGameHours);
            foreach (var next in route) { preview.TravelHours += FleetTravelHours(Link(current, next)!); current = next; }
        }
        if (!Enum.IsDefined(order)) preview.Reason = "Неизвестный приказ";
        else if (!_time.ModeActive || !_time.DayActive) preview.Reason = "Штаб закрыт";
        else if (!MinisterActive) preview.Reason = "Министр не на службе";
        else if (!CanChangeOrder(group, order)) preview.Reason = "Флот занят; возможен отзыв незавершённой операции";
        else if (!_worlds.TryGetValue(destination, out var world)) preview.Reason = "Нет цели";
        else if (order == SWDefenseFleetOrder.Move && group.Phase == SWDefenseOperationPhase.Standing && destination == group.World) preview.Reason = "Флот уже находится здесь";
        else if (world.Faction != "republic" && (order != SWDefenseFleetOrder.Respond || ev?.Kind != SWDefenseEventKind.Raid)) preview.Reason = "Разрешены только ограниченные рейды";
        else if (world.Faction != "republic" && _stance < SWDefenseStance.Mobilization) preview.Reason = "Рейд требует DEFCON 1–2";
        else if (route == null) preview.Reason = "Нет разрешённого маршрута";
        else if (order == SWDefenseFleetOrder.Patrol && _links.FirstOrDefault(l => l.ID == target) is { } patrol
            && (!patrol.Accessible || _worlds[patrol.From].Faction != "republic" || _worlds[patrol.To].Faction != "republic")) preview.Reason = "Патруль требует разрешённой линии Республики";
        else if (order == SWDefenseFleetOrder.Repair && (!world.Shipyard || group.Damage <= 0 && group.Strength >= 100)) preview.Reason = "Нужны повреждения и верфь";
        else if (order == SWDefenseFleetOrder.Respond && (ev == null || _groups.Any(g => g.Alive && g.EventId == eventId))) preview.Reason = "Событие занято или недоступно";
        else if (order != SWDefenseFleetOrder.Return && group.Supply < 15) preview.Reason = "Нет снабжения";
        else if (_dilemma.GetMinistryBudget(DefenseMinistryId) < preview.Cost) preview.Reason = "Нет средств министерства";
        else preview.Available = true;
        if (ev != null) preview.Risk = $"Сила флота {Effective(group):0}, противника {ev.EnemyStrength:0}. При тяжёлых потерях — отход; возможна гибель.";
        return preview;
    }

    private static bool CanChangeOrder(SWFleetGroup group, SWDefenseFleetOrder order) =>
        group.Phase == SWDefenseOperationPhase.Standing
        || group.WorkProject == null && (order == SWDefenseFleetOrder.Return
            || order == SWDefenseFleetOrder.Move && group.Phase is SWDefenseOperationPhase.Preparation or SWDefenseOperationPhase.Travel);

    private static string OrderName(SWDefenseFleetOrder order) => order switch
    {
        SWDefenseFleetOrder.Guard => "Охрана мира", SWDefenseFleetOrder.Patrol => "Патруль сектора",
        SWDefenseFleetOrder.Border => "Прикрытие границы", SWDefenseFleetOrder.Return => "Возвращение на базу",
        SWDefenseFleetOrder.Repair => "Ремонт на верфи", SWDefenseFleetOrder.Train => "Учения",
        SWDefenseFleetOrder.Move => "Перемещение", _ => "Реагирование на угрозу",
    };

    private SWTransitLinkPrototype? Link(string a, string b) => _links.FirstOrDefault(l => l.From == a && l.To == b || l.From == b && l.To == a);
    // Existing named corridors give military formations priority. Local links keep a slower
    // military crossing; neither classification changes the civilian freight clock or graph.
    private double FleetTravelHours(SWTransitLinkPrototype link) => _mainHyperlanes.ContainsKey(link.ID)
        ? Math.Clamp(link.TravelHours * .1, .05, .15)
        : Math.Clamp(link.TravelHours * .2, .1, .3);
    private bool MilitaryRoute(SWFleetGroup group, SWDefenseFleetOrder order) => order == SWDefenseFleetOrder.Respond
        || order is SWDefenseFleetOrder.Return or SWDefenseFleetOrder.Move && _worlds[group.Next ?? group.World].Faction == "empire";
    private double PathHours(string from, IEnumerable<string>? path)
    {
        if (path == null) return double.PositiveInfinity;
        var total = 0d;
        foreach (var next in path) { total += FleetTravelHours(Link(from, next)!); from = next; }
        return total;
    }
    private List<string>? FindPath(string from, string to, bool raid)
    {
        if (!_worlds.ContainsKey(from) || !_worlds.ContainsKey(to)) return null;
        var key = (from, to, raid, _stance);
        if (_pathCache.TryGetValue(key, out var cached)) return cached;
        var path = CalculatePath(from, to, raid);
        _pathCache[key] = path;
        return path;
    }
    private List<string>? CalculatePath(string from, string to, bool raid)
    {
        if (!_worlds.ContainsKey(from) || !_worlds.ContainsKey(to)) return null;
        var distance = new Dictionary<string, double> { [from] = 0 };
        var previous = new Dictionary<string, string>(); var remaining = new HashSet<string>(_worlds.Keys);
        while (remaining.Count > 0)
        {
            var current = remaining.Where(distance.ContainsKey).OrderBy(w => distance[w]).FirstOrDefault();
            if (current == null) break;
            if (current == to)
            {
                var path = new List<string>();
                while (current != from) { path.Add(current); current = previous[current]; }
                path.Reverse(); return path;
            }
            remaining.Remove(current);
            foreach (var link in _links.Where(l => l.From == current || l.To == current))
            {
                var next = link.From == current ? link.To : link.From;
                var imperial = _worlds[current].Faction == "empire" || _worlds[next].Faction == "empire";
                if (!remaining.Contains(next) || !link.Accessible && !(raid && imperial
                    && (_stance >= SWDefenseStance.Mobilization || _worlds[from].Faction == "empire"))) continue;
                if (_worlds[next].Faction == "empire" && !raid && _worlds[from].Faction != "empire") continue;
                var candidate = distance[current] + FleetTravelHours(link);
                if (distance.TryGetValue(next, out var old) && old <= candidate) continue;
                distance[next] = candidate; previous[next] = current;
            }
        }
        return null;
    }

    private bool StartNextLeg(SWFleetGroup group)
    {
        if (group.Path.Count == 0) return false;
        var next = group.Path.Dequeue(); var link = Link(group.World, next);
        if (link == null) { group.Path.Clear(); return false; }
        // A foreign return is always allowed: lowering DEFCON must not trap a deployed fleet.
        if (!link.Accessible && group.Order != SWDefenseFleetOrder.Return && _stance < SWDefenseStance.Mobilization)
        { group.Path.Clear(); group.EventId = 0; group.Order = SWDefenseFleetOrder.Guard; group.Target = group.World; return false; }
        group.SupplyProject = null;
        group.Next = next; group.LegStarted = _time.ActiveGameHours;
        group.Until = _time.ActiveGameHours + FleetTravelHours(link); group.Phase = SWDefenseOperationPhase.Travel; return true;
    }

    private void Arrive(SWFleetGroup group)
    {
        group.Phase = group.Order is SWDefenseFleetOrder.Respond or SWDefenseFleetOrder.Repair
            ? SWDefenseOperationPhase.Execution : SWDefenseOperationPhase.Standing;
        group.Until = _time.ActiveGameHours + (group.Order == SWDefenseFleetOrder.Respond ? 2 : 1);
        if (group.Order == SWDefenseFleetOrder.Return) group.Order = SWDefenseFleetOrder.Guard;
    }
    private void BeginReturn(SWFleetGroup group)
    {
        group.EventId = 0; group.Order = SWDefenseFleetOrder.Return;
        group.Path.Clear(); var path = FindPath(group.World, group.Home, _worlds[group.World].Faction == "empire");
        if (path != null) foreach (var world in path) group.Path.Enqueue(world);
        group.Target = group.Home;
        if (!StartNextLeg(group)) { group.Target = group.World; group.Order = SWDefenseFleetOrder.Guard; group.Phase = SWDefenseOperationPhase.Standing; }
    }

    public bool DeployGroup(int groupId, string target)
    {
        EnsureGroups(); var group = _groups.FirstOrDefault(g => g.Id == groupId && g.Alive);
        if (group == null || target == "express") return false;
        if (target is "" or "reserve") return IssueFleetOrder(groupId, SWDefenseFleetOrder.Return, group.Home);
        if (target == "border") return IssueFleetOrder(groupId, SWDefenseFleetOrder.Border, _borders.FirstOrDefault()?.Id ?? "");
        var repair = target.StartsWith("repair:");
        var sector = repair ? target[7..] : target;
        var world = _worlds.Values.FirstOrDefault(w => w.SectorId == sector && (!repair || w.Shipyard));
        return world != null && IssueFleetOrder(groupId, repair ? SWDefenseFleetOrder.Repair : SWDefenseFleetOrder.Guard, world.Id);
    }

    public List<SWFleetGroupEntry> BuildFleet()
    {
        EnsureGroups(); return _groups.Where(g => g.Alive).Select(g => new SWFleetGroupEntry { Id = g.Id,
            State = g.Order == SWDefenseFleetOrder.Border ? SWFleetState.Border : g.Order == SWDefenseFleetOrder.Repair ? SWFleetState.Repairing : SWFleetState.Sector,
            SectorId = _worlds[g.World].SectorId, LocationLabel = _worlds[g.World].Name,
            Damaged = g.Damage > 15, RepairLeft = Math.Max(0, g.Until - _time.ActiveGameHours) }).ToList();
    }
    private void NudgeSectors(string globalId, float delta)
    { foreach (var sector in _proto.EnumeratePrototypes<SWSectorPrototype>()) _senate.AddSectorBias(sector.ID, globalId, delta); }
    public (float Cost, float Effect) GetRequestModifier(string? sectorId)
    {
        var garrison = _groups.Any(g => g.Alive && g.Phase == SWDefenseOperationPhase.Standing
            && _worlds.GetValueOrDefault(g.World)?.SectorId == sectorId && Effective(g) >= 40);
        var cost = garrison ? .6f : 1f; var effect = garrison ? 1.25f : 1f;
        if (_stance >= SWDefenseStance.Mobilization) effect *= 1.2f;
        if (_stance == SWDefenseStance.Peace && !garrison) { cost *= 1.3f; effect *= .8f; }
        return (cost, effect);
    }
    public void OnSectorEngagement(string? sectorId)
    {
        var group = _groups.FirstOrDefault(g => g.Alive && g.Phase == SWDefenseOperationPhase.Standing && _worlds.GetValueOrDefault(g.World)?.SectorId == sectorId);
        if (group != null && _random.Prob(.3f)) group.Damage = Math.Min(100, group.Damage + 15);
    }
    // Compatibility endpoints cannot issue free policy effects or create an operation without an incident.
    public bool LaunchOperation(string opId, string target) => false;
    public List<SWDefenseOperationEntry> BuildOperations() => new();
    public List<SWDefenseProgramEntry> BuildPrograms() => new();
    public void SelectProgram(string programId) { }
    public List<SWActiveOpEntry> BuildActiveOps() => _groups.Where(g => g.Alive && g.EventId != 0).Select(g =>
        new SWActiveOpEntry { Name = $"{g.Id}-й флот: {g.Phase}", Detail = _worlds[g.World].Name,
            HoursLeft = Math.Max(0, g.Until - _time.ActiveGameHours) }).ToList();
}

public sealed class SWFleetGroup
{
    public int Id;
    public string Home = string.Empty;
    public string World = string.Empty;
    public string Target = string.Empty;
    public string? Next;
    public float Strength = 100;
    public float Damage;
    public float Supply = 80;
    public float Training = 40;
    public SWDefenseFleetOrder Order = SWDefenseFleetOrder.Guard;
    public SWDefenseOperationPhase Phase = SWDefenseOperationPhase.Standing;
    public Queue<string> Path = new();
    public double Until;
    public double LegStarted;
    public int EventId;
    public bool Retreated;
    public bool FinishingWindowUsed;
    // Display reference to the current port's pending kit; ownership stays with _supplyPorts.
    public string? SupplyProject;
    public string? WorkProject;
    public string PatrolEdge = string.Empty;
    public bool Alive => Phase != SWDefenseOperationPhase.Lost && Strength > 0;
}

// Also used by the Internal Security ministry's existing timed operations.
public sealed class SWActiveOperation
{
    public string OpId = string.Empty;
    public string? SectorId;
    public double CompleteHour;
}
