using System.Linq;
using Robust.Shared.Random;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;
using Content.Shared._SW._Senate.Economy;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem
{
    private void GenerateIncident()
    {
        if (_incidents.Count(e => !e.Resolved) >= 4) return;
        // Threat selection is weighted by local institutions and uncovered crossings.
        var candidates = new List<(string World, string Edge, SWDefenseEventKind Kind, float Weight)>();
        foreach (var world in _worlds.Values.Where(w => w.Faction == "republic" && w.SectorId.Length > 0))
        {
            var sector = _proto.Index<SWSectorPrototype>(world.SectorId);
            _senate.SectorGlobals.TryGetValue(world.SectorId, out var current);
            var security = current?.GetValueOrDefault("SWGlobalSecurity", 50) ?? sector.StartingStats.GetValueOrDefault("SWGlobalSecurity", 50);
            var defense = current?.GetValueOrDefault("SWGlobalDefense", 50) ?? sector.StartingStats.GetValueOrDefault("SWGlobalDefense", 50);
            var edge = _links.FirstOrDefault(l => l.Accessible && (l.From == world.Id || l.To == world.Id));
            candidates.Add((world.Id, edge?.ID ?? "", SWDefenseEventKind.Piracy, Math.Max(1, 110 - security - LocalCoverage(world.Id))));
            candidates.Add((world.Id, "", SWDefenseEventKind.Unrest, Math.Max(1, 80 - defense)));
        }
        if (ImperialThreatsEnabled)
            foreach (var border in _borders.Where(b => b.Faction == "empire"))
            {
                candidates.Add((border.RepublicWorldId, border.Id, _random.Prob(.25f) ? SWDefenseEventKind.Blockade : SWDefenseEventKind.Incursion,
                    Math.Max(1, _border - LocalCoverage(border.RepublicWorldId) + 20)));
                if (_border >= 60)
                    candidates.Add((border.ForeignWorldId, border.Id, SWDefenseEventKind.Raid, _border * .4f));
            }
        var total = candidates.Sum(c => c.Weight);
        if (total <= 0) return;
        var roll = _random.NextFloat() * total;
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll > 0) continue;
            var severity = Math.Clamp(20 + _border * .4f + _random.Next(-10, 16), 10, 85);
            CreateIncident(candidate.World, candidate.Edge, candidate.Kind, severity, severity * .9f + 10, 6 + severity / 15, false, "");
            break;
        }
    }

    private void GenerateMinorDisruption()
    {
        if (_incidents.Any(e => !e.Resolved)) return;
        var worlds = _worlds.Values.Where(w => w.Faction == "republic" && w.SectorId.Length > 0).ToArray();
        if (worlds.Length == 0) return;
        var world = worlds[_random.Next(worlds.Length)];
        CreateIncident(world.Id, "", SWDefenseEventKind.Unrest, 10, 10, .75, false, "Небольшое местное нарушение порядка");
    }

    private float LocalCoverage(string world) => Math.Min(100, _groups.Where(g => g.Alive && g.World == world
        && g.Phase == SWDefenseOperationPhase.Standing && g.Order is SWDefenseFleetOrder.Guard or SWDefenseFleetOrder.Patrol or SWDefenseFleetOrder.Border).Sum(Effective));

    public float BorderCoverage(string sectorId)
    {
        EnsureGroups();
        var borders = _borders.Where(b => _worlds[b.RepublicWorldId].SectorId == sectorId).ToArray();
        return borders.Length == 0 ? 0 : borders.Average(b => LocalCoverage(b.RepublicWorldId));
    }

    public float BorderCoverageFraction
    {
        get
        {
            EnsureGroups();
            var borders = _borders.Where(b => b.Faction == "empire").ToArray();
            return borders.Length == 0 ? 0 : borders.Average(b => LocalCoverage(b.RepublicWorldId)) / 100;
        }
    }

    public bool TryCreateImperialIncident(string sectorId, bool blockade)
    {
        EnsureGroups();
        if (!_time.ModeActive || !_time.DayActive || !MinisterActive || !ImperialThreatsEnabled || _incidents.Count(e => !e.Resolved) >= 4) return false;
        var crossing = _borders.FirstOrDefault(b => b.Faction == "empire" && _worlds[b.RepublicWorldId].SectorId == sectorId);
        if (crossing == null) return false;
        return CreateIncident(crossing.RepublicWorldId, crossing.Id, blockade ? SWDefenseEventKind.Blockade : SWDefenseEventKind.Incursion,
            blockade ? 80 : 55, blockade ? 75 : 50, 8, false, "");
    }

    private static string IncidentTitle(SWDefenseEventKind kind) => kind switch
    {
        SWDefenseEventKind.Piracy => "Пиратская угроза", SWDefenseEventKind.Unrest => "Вооружённые беспорядки",
        SWDefenseEventKind.Incursion => "Имперское вторжение", SWDefenseEventKind.Blockade => "Блокада гиперлинии",
        _ => "Ограниченный рейд",
    };

    private bool CreateIncident(string world, string edge, SWDefenseEventKind kind, float severity, float enemy,
        double duration, bool director, string title)
    {
        if (_incidents.Any(e => !e.Resolved && e.WorldId == world && e.Kind == kind)) return false;
        var incident = new SWDefenseEventView { Id = _nextEventId++, Title = title.Length == 0 ? IncidentTitle(kind) : title,
            WorldId = world, EdgeId = edge, Kind = kind, Severity = severity, EnemyStrength = enemy,
            ExpiresAt = _time.ActiveGameHours + duration, DirectorCreated = director, Status = "Требует реагирования",
            Risk = $"Сила противника {enemy:0}; снабжение и подготовка влияют на потери. Возможен полный разгром." };
        _incidents.Add(incident); AddLog($"{incident.Title}: {_worlds[world].Name}.");
        ApplyRouteRestrictions(); return true;
    }

    public bool ConfigureDirectorEvent(SWDefenseDirectorEventMessage message) => message.Resolve
        ? ResolveDirectorEvent(message.EventId)
        : ConfigureDirectorEvent(message.EventId, message.Title, message.WorldId, message.EdgeId, message.Severity,
            message.EnemyStrength, message.DurationHours, message.Kind);

    public bool ConfigureDirectorEvent(int eventId, string title, string worldId, string edgeId, float severity,
        float enemyStrength, double durationHours, SWDefenseEventKind kind = SWDefenseEventKind.Piracy)
    {
        EnsureGroups();
        if (title == null || worldId == null || edgeId == null || !_time.ModeActive || !_time.DayActive || title.Length > 80 || !Enum.IsDefined(kind)
            || !float.IsFinite(severity) || !float.IsFinite(enemyStrength) || !double.IsFinite(durationHours)
            || severity is < 1 or > 100 || enemyStrength is < 1 or > 200 || durationHours is < .25 or > 24
            || !_worlds.TryGetValue(worldId, out var world)) return false;
        // During vacancy only small, quickly self-resolving disruptions may be introduced.
        if (!MinisterActive && (severity > 20 || enemyStrength > 20 || durationHours > 1
            || kind is SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade or SWDefenseEventKind.Raid)) return false;
        if (kind == SWDefenseEventKind.Raid && world.Faction != "empire" || kind != SWDefenseEventKind.Raid && world.Faction != "republic") return false;
        var link = _links.FirstOrDefault(l => l.ID == edgeId);
        if (edgeId.Length > 0 && (link == null || link.From != worldId && link.To != worldId)) return false;
        if (kind == SWDefenseEventKind.Incursion)
        {
            var crossing = _borders.FirstOrDefault(b => b.Id == edgeId && b.Faction == "empire" && b.RepublicWorldId == worldId);
            if (crossing == null) return false;
        }
        if (kind == SWDefenseEventKind.Blockade && edgeId.Length == 0) return false;
        var ev = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (eventId != 0 && ev == null) return false;
        if (ev == null)
        {
            if (_incidents.Count(e => !e.Resolved) >= 8) return false;
            return CreateIncident(worldId, edgeId, kind, severity, enemyStrength, durationHours, true, title.Trim());
        }
        // Editing an assigned operation cannot teleport its objective underneath a traveling formation.
        if (_groups.Any(g => g.Alive && g.EventId == ev.Id) && (ev.WorldId != worldId || ev.Kind != kind)) return false;
        ev.Title = string.IsNullOrWhiteSpace(title) ? IncidentTitle(kind) : title.Trim(); ev.WorldId = worldId;
        ev.EdgeId = edgeId; ev.Kind = kind; ev.Severity = severity; ev.EnemyStrength = enemyStrength;
        ev.ExpiresAt = _time.ActiveGameHours + durationHours; ev.DirectorCreated = true;
        ApplyRouteRestrictions(); return true;
    }

    public bool ResolveDirectorEvent(int eventId, bool success = true)
    {
        var ev = _incidents.FirstOrDefault(e => e.Id == eventId && !e.Resolved);
        if (ev == null) return false;
        ResolveIncident(ev, success); return true;
    }

    private void ResolveIncident(SWDefenseEventView ev, bool success)
    {
        ev.Resolved = true; ev.Status = success ? "Угроза устранена" : "Операция прекращена";
        ev.ExpiresAt = _time.ActiveGameHours;
        if (success)
        {
            AddBorder(-Math.Min(8, ev.Severity * .08f));
            _weariness = Math.Clamp(_weariness - 1, 0, 100);
            var sector = _worlds[ev.WorldId].SectorId;
            if (sector.Length > 0) _senate.AddSectorBias(sector, "SWGlobalSecurity", Math.Min(3, ev.Severity * .03f));
        }
        AddLog($"{ev.Title}: {ev.Status}."); ApplyRouteRestrictions();
    }

    private void TickCombat(SWFleetGroup group, float hours)
    {
        // Preserve the retreat decision even when a slow server frame spans a large exchange.
        if (hours > .1f)
        {
            while (hours > 0 && group.Alive && group.Phase == SWDefenseOperationPhase.Execution)
            { var step = Math.Min(.1f, hours); TickCombat(group, step); hours -= step; }
            return;
        }
        var ev = _incidents.FirstOrDefault(e => e.Id == group.EventId && !e.Resolved);
        if (ev == null) { group.Phase = SWDefenseOperationPhase.Recovery; group.Until = _time.ActiveGameHours + .5; return; }
        if (group.Strength < 25 || group.Damage > 75 || group.Supply < 10)
        {
            // Retreat is attempted before another exchange can destroy the formation.
            group.Retreated = true;
            var escapes = _random.Prob(Math.Clamp(.8f + group.Training / 500f - ev.EnemyStrength / 500f, .2f, .95f));
            if (!escapes) { group.Strength = Math.Max(0, group.Strength - _random.Next(10, 26)); group.Damage = Math.Min(100, group.Damage + 15); }
            if (group.Strength <= 0 || group.Damage >= 100)
            { group.Strength = 0; group.Phase = SWDefenseOperationPhase.Lost; group.EventId = 0; group.Next = null; group.Path.Clear();
                ev.Status = "Флот потерян; угроза сохраняется"; AddLog($"{group.Id}-й флот погиб при отходе."); }
            else { ev.Status = "Флот отступил; угроза сохраняется"; AddLog($"{group.Id}-й флот отступает с потерями."); BeginReturn(group); }
            return;
        }
        ev.EnemyStrength = Math.Max(0, ev.EnemyStrength - Effective(group) * .5f * hours);
        var damage = Math.Max(2, ev.EnemyStrength * .22f) * hours * OrbitalDamageFactor(group.World, ev.Kind);
        group.Strength = Math.Max(0, group.Strength - damage * .55f);
        group.Damage = Math.Min(100, group.Damage + damage);
        group.Supply = Math.Max(0, group.Supply - hours * 10);
        _weariness = Math.Min(100, _weariness + hours * .5f);
        if (ev.EnemyStrength <= 0)
        {
            group.Training = Math.Min(100, group.Training + 5); ResolveIncident(ev, true);
            group.Phase = SWDefenseOperationPhase.Recovery; group.Until = _time.ActiveGameHours + .75;
        }
        else if (_time.ActiveGameHours >= group.Until)
        {
            const float finishingHours = .25f;
            // A nearly won battle may need a final exchange beyond the planned two hours.
            // This grants time once, never victory: normal damage, supply and retreat still apply.
            if (!group.FinishingWindowUsed && ev.EnemyStrength <= 5
                && group.Strength >= 25 && group.Damage <= 75 && group.Supply >= 10
                && ev.EnemyStrength <= Effective(group) * .5f * finishingHours)
            {
                group.FinishingWindowUsed = true;
                group.Until = _time.ActiveGameHours + finishingHours;
                ev.Status = "Флот завершает подавление оставшихся сил";
                AddLog($"{group.Id}-й флот продолжает бой ещё до 15 минут: осталось {ev.EnemyStrength:0.#} сил противника.");
            }
            else
            {
                ev.Status = "Время операции истекло; угроза сохраняется";
                group.Phase = SWDefenseOperationPhase.Recovery;
                group.Until = _time.ActiveGameHours + .5;
                AddLog($"{group.Id}-й флот прекратил операцию: осталось {ev.EnemyStrength:0.#} сил противника.");
            }
        }
    }

    private void ApplyRouteRestrictions()
    {
        var restrictions = new Dictionary<string, double>();
        foreach (var ev in _incidents.Where(e => !e.Resolved && e.EdgeId.Length > 0))
        {
            if (IncidentTransitRestriction(ev) is not { } restriction) continue;
            restrictions[restriction.Edge] = Math.Min(restrictions.GetValueOrDefault(restriction.Edge, 1), restriction.Factor);
        }
        _economy.SetDefenseTransitRestrictions(restrictions);
    }

    // One source of truth for the enforced restriction and the public statistics card.
    private (string Edge, double Factor)? IncidentTransitRestriction(SWDefenseEventView ev)
    {
        if (ev.Resolved || ev.EdgeId.Length == 0 || !MinisterActive || ev.Kind == SWDefenseEventKind.Raid) return null;
        var factor = ev.Kind == SWDefenseEventKind.Blockade && ev.Severity >= 75 ? 0 : Math.Clamp(1 - ev.Severity / 125d, .3, .8);
        if (factor > 0) factor += (1 - factor) * .2 * OrbitalReadiness(ev.WorldId);
        var affected = _links.FirstOrDefault(l => l.ID == ev.EdgeId);
        if (affected?.Accessible != true)
            affected = _links.FirstOrDefault(l => l.Accessible && (l.From == ev.WorldId || l.To == ev.WorldId));
        return affected == null ? null : (affected.ID, factor);
    }

    private string ProjectStatus(string projectId)
    {
        var project = _economy.GetProject(projectId);
        if (project == null) return "Заказ принят; доставка ещё не началась";
        if (project.Status == Content.Server._SW._Senate.Economy.Simulation.EconomyProjectStatus.Working)
            return $"Материалы доставлены; работы {project.WorkDone:0.0}/{project.WorkHours:0.0} ч";
        var missing = project.Materials.Where(m => m.Required - m.Delivered > .001).Select(m =>
            $"{(_proto.TryIndex<SWCommodityPrototype>(m.Good, out var commodity) ? commodity.Name : m.Good)} {m.Delivered:0.###}/{m.Required:0.###} (+{m.Incoming:0.###} в пути)");
        return "Поставка: " + string.Join("; ", missing);
    }

    private static string PhaseName(SWDefenseOperationPhase phase) => phase switch
    {
        SWDefenseOperationPhase.Preparation => "Подготовка", SWDefenseOperationPhase.Travel => "Гиперпереход",
        SWDefenseOperationPhase.Execution => "Выполнение", SWDefenseOperationPhase.Recovery => "Восстановление после операции",
        SWDefenseOperationPhase.Lost => "Флот утрачен", _ => "На позиции",
    };

    public SWDefenseMapState BuildMapState(bool includeOrderOptions = true)
    {
        EnsureGroups();
        var state = new SWDefenseMapState { NowHours = _time.ActiveGameHours, MinisterActive = MinisterActive,
            ClockRunning = _time.ModeActive && _time.DayActive, Worlds = _worlds.Values.ToList(), Events = _incidents.ToList(),
            Budget = _dilemma.GetMinistryBudget(DefenseMinistryId), UpkeepPerHour = FleetUpkeep };
        var supplies = _economy.GetNavySupplyState();
        state.SupplyHour = supplies.Hour;
        state.SupplyFlows = supplies.Flows.ToList();
        foreach (var link in _links)
        {
            var military = !link.Accessible && (_worlds[link.From].Faction == "empire" || _worlds[link.To].Faction == "empire");
            state.Links.Add(new SWDefenseLinkView { Id = link.ID, From = link.From, To = link.To,
                TravelHours = FleetTravelHours(link), MainHyperlane = _mainHyperlanes.ContainsKey(link.ID),
                RouteName = _mainHyperlanes.GetValueOrDefault(link.ID, "Местная линия"),
                MilitaryOnly = military, Traversable = link.Accessible || military && _stance >= SWDefenseStance.Mobilization });
        }
        foreach (var border in _borders)
            state.Borders.Add(new SWDefenseBorderView { Id = border.Id, RepublicWorldId = border.RepublicWorldId,
                ForeignWorldId = border.ForeignWorldId, Faction = border.Faction,
                Coverage = LocalCoverage(border.RepublicWorldId), Pressure = Math.Clamp(_border - LocalCoverage(border.RepublicWorldId) * .4f, 0, 100) });
        foreach (var world in _worlds.Values.Where(w => w.Shipyard && w.Faction == "republic"))
            state.Shipyards.Add(new SWDefenseShipyardView { WorldId = world.Id, Name = world.Name,
                Busy = _construction.ContainsKey(world.Id), Status = _construction.TryGetValue(world.Id, out var project)
                    ? ProjectStatus(project) : "Принимает заказы; строительство требует военных кораблей и комплектующих" });
        foreach (var group in _groups)
        {
            state.Fleets.Add(new SWDefenseFleetView { Id = group.Id, Name = $"{group.Id}-й флот", HomeWorldId = group.Home,
                CurrentWorldId = group.World, NextWorldId = group.Next, TargetWorldId = group.Target, Order = group.Order,
                Phase = group.Phase, Strength = group.Strength, Damage = group.Damage, Supply = group.Supply, Training = group.Training,
                EtaHours = group.Until, LegStartedAt = group.LegStarted, EventId = group.EventId,
                DestinationEtaHours = group.Phase is SWDefenseOperationPhase.Travel or SWDefenseOperationPhase.Preparation
                    ? group.Until + PathHours(group.Next ?? group.World, group.Path) : _time.ActiveGameHours,
                PatrolEdgeId = group.PatrolEdge,
                Status = group.WorkProject != null ? "Ремонт: " + ProjectStatus(group.WorkProject)
                    : group.SupplyProject != null ? ProjectStatus(group.SupplyProject) : PhaseName(group.Phase),
                Risk = group.Strength < 25 || group.Damage > 75 ? "Критические потери; требуется верфь" : "Боеспособность зависит от снабжения и подготовки" });
            if (!includeOrderOptions || !group.Alive) continue;
            foreach (var world in _worlds.Values.Where(w => w.Faction == "republic"))
                state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Move, world.Id, 0));
            foreach (var world in _worlds.Values.Where(w => w.Faction == "republic" && w.SectorId.Length > 0))
            {
                state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Guard, world.Id, 0));
                state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Patrol, world.Id, 0));
                state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Train, world.Id, 0));
            }
            state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Return, group.Home, 0));
            foreach (var link in _links.Where(l => l.Accessible && _worlds[l.From].Faction == "republic" && _worlds[l.To].Faction == "republic"))
                state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Patrol, link.ID, 0));
            foreach (var yard in state.Shipyards) state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Repair, yard.WorldId, 0));
            foreach (var border in _borders) state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Border, border.Id, 0));
            foreach (var ev in _incidents.Where(e => !e.Resolved)) state.OrderOptions.Add(PreviewOrder(group, SWDefenseFleetOrder.Respond, ev.WorldId, ev.Id));
        }
        return state;
    }
}
