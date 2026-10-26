using System.Linq;
using Content.Server._SW._Senate.Economy;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Ministries;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryDefenseSystem
{
    [Dependency] private readonly SWSenateProgramSystem _planetaryPrograms = default!;
    [Dependency] private readonly SWGovernorSystem _governors = default!;
    public const string GarrisonProgramId = "SWProgramGarrison";
    public const string OrbitalProgramId = "SWProgramShield";

    public bool StartWorldProgram(string worldId, string programId)
    {
        EnsureGroups();
        if (programId is not (GarrisonProgramId or OrbitalProgramId) || !IsDefenseWorld(worldId)
            || !_planetaryPrograms.ApplyFromDefense(worldId, programId)) return false;
        AddLog($"{_worlds[worldId].Name}: начато развёртывание {(programId == GarrisonProgramId ? "гарнизона" : "орбитальной обороны")}.");
        return true;
    }

    public bool ResupplyWorld(string worldId)
    {
        EnsureGroups();
        if (!IsDefenseWorld(worldId) || !_planetaryPrograms.ResupplyFromDefense(worldId)) return false;
        AddLog($"{_worlds[worldId].Name}: заказано аварийное снабжение планетарной обороны.");
        return true;
    }

    private bool IsDefenseWorld(string worldId) => worldId != null
        && _worlds.TryGetValue(worldId, out var world) && world.Faction == "republic" && world.SectorId.Length > 0
        && _proto.TryIndex<SWPlanetEconomyPrototype>(worldId, out var economy) && economy.ActiveEconomy;

    private float GarrisonReadiness(string world) => _planetaryPrograms.GetDefenseReadiness(world, GarrisonProgramId);
    private float OrbitalReadiness(string world) => _planetaryPrograms.GetDefenseReadiness(world, OrbitalProgramId);

    private float PlanetaryHarmFactor(string world, SWDefenseEventKind kind)
    {
        var ground = kind is SWDefenseEventKind.Unrest or SWDefenseEventKind.Incursion ? .3f * GarrisonReadiness(world) : 0;
        var orbital = kind is SWDefenseEventKind.Piracy or SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade ? .2f * OrbitalReadiness(world) : 0;
        return 1 - Math.Min(.4f, ground + orbital);
    }

    private float PlanetarySuppression(SWDefenseEventView incident) =>
        incident.Kind is SWDefenseEventKind.Unrest or SWDefenseEventKind.Incursion
        && incident.Severity <= 35 && incident.EnemyStrength <= 35 ? GarrisonReadiness(incident.WorldId) * 4 : 0;

    private float OrbitalDamageFactor(string world, SWDefenseEventKind kind) => 1 -
        (kind is SWDefenseEventKind.Piracy or SWDefenseEventKind.Incursion or SWDefenseEventKind.Blockade ? .15f * OrbitalReadiness(world) : 0);

    private bool HasOperatingShipyard(string world) => _economy.Definition?.Production.Any(p => p.World == world && p.Rate > 0
        && _proto.TryIndex<SWProductionRecipePrototype>(p.Recipe, out var recipe) && recipe.Facility.Id == "shipyard") == true;

    public SWDefensePlanetaryState BuildPlanetaryState()
    {
        EnsureGroups();
        var state = new SWDefensePlanetaryState { CanManage = _time.ModeActive && _time.DayActive && MinisterActive,
            Budget = _dilemma.GetMinistryBudget(DefenseMinistryId) };
        var governors = _governors.BuildViews().ToDictionary(g => g.Sector, g => g.Name);
        var traffic = _economy.Snapshot?.Flows.SelectMany(f => new[] { (World: f.From, Amount: f.Dispatched + f.InTransit),
                (World: f.To, Amount: f.Dispatched + f.InTransit) }).GroupBy(f => f.World)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Amount)) ?? new Dictionary<string, double>();
        var majorPorts = traffic.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(3).Select(p => p.Key).ToHashSet();
        foreach (var world in _worlds.Values.Where(w => IsDefenseWorld(w.Id)).OrderBy(w => w.Name))
        {
            var reserve = _planetaryPrograms.GetDefenseReserve(world.Id);
            var sites = new List<string>();
            if (HasOperatingShipyard(world.Id)) sites.Add("Действующая верфь");
            if (_groups.Any(g => g.Alive && g.Home == world.Id)) sites.Add("База флота");
            if (majorPorts.Contains(world.Id)) sites.Add("Крупный грузовой узел");
            else if (_economy.Snapshot?.Capacities.Any(c => c.World == world.Id && c.FleetCapacity > 0) == true)
                sites.Add("Грузовой порт");
            state.Worlds.Add(new SWDefensePlanetaryWorldView
            {
                WorldId = world.Id, Name = world.Name, SectorId = world.SectorId,
                SectorName = _proto.Index<SWSectorPrototype>(world.SectorId).Name,
                Governor = governors.GetValueOrDefault(world.SectorId, "Не назначен"), ProtectedSite = string.Join("; ", sites),
                GarrisonReadiness = GarrisonReadiness(world.Id), OrbitalReadiness = OrbitalReadiness(world.Id),
                ActiveThreats = _incidents.Count(e => !e.Resolved && e.WorldId == world.Id),
                Programs = _planetaryPrograms.BuildDefensePrograms(world.Id), CanResupply = state.CanManage && reserve.CanResupply,
                ResupplyCost = reserve.Cost, ReserveHours = reserve.ReserveHours, ResupplyStatus = reserve.Status,
            });
        }
        state.Requests = BuildSupportRequests(state.Worlds, majorPorts);
        return state;
    }

    private List<SWDefenseSupportRequestView> BuildSupportRequests(List<SWDefensePlanetaryWorldView> worlds, HashSet<string> majorPorts)
    {
        var requests = new List<SWDefenseSupportRequestView>();
        foreach (var world in worlds)
        {
            var threats = _incidents.Where(e => !e.Resolved && e.WorldId == world.WorldId).OrderByDescending(e => e.Severity).ToArray();
            foreach (var threat in threats.Take(1))
            {
                var program = threat.Kind is SWDefenseEventKind.Unrest or SWDefenseEventKind.Incursion ? GarrisonProgramId : OrbitalProgramId;
                var readiness = program == GarrisonProgramId ? world.GarrisonReadiness : world.OrbitalReadiness;
                var entry = world.Programs.FirstOrDefault(p => p.Id == program);
                var request = SupportRequest(world, $"planetary:incident:{threat.Id}", program,
                    threat.Title, $"{world.Name}: сила противника {threat.EnemyStrength:0.#}, опасность {threat.Severity:0.#}. " +
                    (threat.Severity > 35 || threat.EnemyStrength > 35 ? "Планетарная оборона снижает ущерб; для устранения сильной угрозы требуется флот." : "Местная оборона может сдержать небольшую угрозу."),
                    100 + (int) threat.Severity, entry);
                if (entry?.Applied == true && entry.WorldId == world.WorldId && readiness < .7f)
                { request.ProgramId = "resupply"; request.Status = world.ResupplyStatus; }
                requests.Add(request);
            }
            var installed = world.Programs.Where(p => p.Applied && p.WorldId == world.WorldId).ToArray();
            if (installed.Any(p => p.Id == GarrisonProgramId && world.GarrisonReadiness < .7f || p.Id == OrbitalProgramId && world.OrbitalReadiness < .7f)
                && !requests.Any(r => r.WorldId == world.WorldId && r.ProgramId == "resupply"))
                requests.Add(SupportRequest(world, $"planetary:supply:{world.WorldId}", "resupply", "Восстановить снабжение обороны",
                    "Оборонный комплекс построен, но текущих поставок недостаточно для полной боеготовности.", 90, null, world.ResupplyStatus));
            _senate.SectorGlobals.TryGetValue(world.SectorId, out var stats);
            var security = stats?.GetValueOrDefault("SWGlobalSecurity", 50) ?? 50;
            var defense = stats?.GetValueOrDefault("SWGlobalDefense", 50) ?? 50;
            var strategic = HasOperatingShipyard(world.WorldId) || _groups.Any(g => g.Alive && g.Home == world.WorldId) || majorPorts.Contains(world.WorldId);
            if (threats.Length > 0 || !strategic || Math.Min(security, defense) >= 50) continue;
            var needed = HasOperatingShipyard(world.WorldId) || majorPorts.Contains(world.WorldId) ? OrbitalProgramId : GarrisonProgramId;
            var project = world.Programs.FirstOrDefault(p => p.Id == needed);
            if (project?.Applied == true) continue;
            requests.Add(SupportRequest(world, $"planetary:site:{world.WorldId}:{needed}", needed, "Прикрыть важный объект",
                $"{world.ProtectedSite}. Местная безопасность {security:0}, оборона {defense:0}; объекту требуется защита.",
                50 + (int) (50 - Math.Min(security, defense)), project));
        }
        return requests.OrderByDescending(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).Take(6).ToList();
    }

    private static SWDefenseSupportRequestView SupportRequest(SWDefensePlanetaryWorldView world, string id, string program,
        string title, string reason, int priority, SWProgramEntry? entry, string status = "") => new()
    {
        Id = id, WorldId = world.WorldId, Governor = world.Governor, ProgramId = program, Title = title,
        Reason = reason, Priority = priority, Status = status.Length > 0 ? status : entry?.Started == true
            ? entry.WorldId != world.WorldId ? $"Проект уже размещён на мире {entry.WorldName}" : entry.Status : "Ожидает решения министра",
    };
}
