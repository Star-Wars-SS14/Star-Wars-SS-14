using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Economy;
using Content.Shared.GameTicking;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Programs;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;
using Content.Server._SW._Senate.Core;

namespace Content.Server._SW._Senate.Economy;

public sealed partial class SWSenateProgramSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SWSenateStateSystem _senate = default!;
    [Dependency] private readonly SWSenateBudgetSystem _budget = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly SWRepublicEconomySystem _economy = default!;
    [Dependency] private readonly SWCorruptionSystem _corruption = default!;

    private sealed record Project(string Sector, string Program, string World, string Request);
    private readonly Dictionary<(string Sector, string Program), Project> _projects = new();
    private readonly Dictionary<string, SWGalacticMapWorld[]> _worlds = new();
    private readonly Dictionary<string, string> _worldNames = new();
    private EconomySnapshot? _lastSnapshot;
    private Dictionary<string, EconomyProjectState> _projectStates = new();
    private Dictionary<(string World, string Group), EconomyServiceState> _serviceStates = new();

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SWSenateTimeSystem));
        UpdatesAfter.Add(typeof(SWRepublicEconomySystem));
        UpdatesBefore.Add(typeof(SWSenateStateSystem));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _projects.Clear();
            _lastSnapshot = null;
            _projectStates.Clear();
            _serviceStates.Clear();
            ResetDefensePrograms();
            _senate.ClearProgramEffects();
        });
        var scenario = _proto.Index<SWRepublicEconomyScenarioPrototype>(SWRepublicEconomyFactory.ScenarioId);
        var map = _proto.Index(scenario.Map);
        InitializeDefensePrograms(map);
        foreach (var group in map.Worlds.Where(w => w.Sector != null
                     && _proto.Index<SWPlanetEconomyPrototype>(w.Id).ActiveEconomy).GroupBy(w => w.Sector!.Value.Id))
        {
            _worlds[group.Key] = group.OrderByDescending(w => w.Anchor).ThenBy(w => w.Name).ToArray();
            foreach (var world in group)
                _worldNames[world.Id] = world.Name;
        }
    }

    public override void Update(float frameTime)
    {
        // A final accepted daytime step can publish at the night boundary. Its result still counts.
        if (!_time.ModeActive || _economy.Snapshot is not { } snapshot || ReferenceEquals(snapshot, _lastSnapshot))
            return;
        _lastSnapshot = snapshot;
        var states = snapshot.Projects.ToDictionary(p => p.Id);
        var services = snapshot.Services.ToDictionary(s => (s.World, s.Group));
        _projectStates = states;
        _serviceStates = services;
        RefreshDefenseReserves(snapshot, states, services);
        var offsets = new Dictionary<(string Sector, string Global), float>();
        foreach (var project in _projects.Values)
        {
            if (!states.TryGetValue(project.Request, out var state) || state.CompletedHour == null)
                continue;
            var proto = _proto.Index<SWProgramPrototype>(project.Program);
            var service = OperatingFraction(proto, project.World, services);
            foreach (var (global, delta) in proto.SectorEffects)
            {
                var key = (project.Sector, global.Id);
                offsets[key] = offsets.GetValueOrDefault(key) + delta * service;
            }
        }
        _senate.ClearProgramEffects();
        foreach (var (key, value) in offsets)
            _senate.SetProgramEffect(key.Sector, key.Global, value);
    }

    public bool IsApplied(string sectorId, string programId) =>
        _projects.TryGetValue((sectorId, programId), out var project)
        && _economy.Snapshot?.Projects.Any(p => p.Id == project.Request && p.CompletedHour != null) == true;

    public bool Apply(string sectorId, string programId, string? worldId = null)
    {
        if (!_time.ModeActive || !_time.DayActive || !_senate.IsClaimed(sectorId)
            || !_proto.TryIndex<SWProgramPrototype>(programId, out var proto)
            || _projects.ContainsKey((sectorId, programId))
            || !_worlds.TryGetValue(sectorId, out var worlds) || worlds.Length == 0)
            return false;
        worldId ??= worlds[0].Id;
        var cost = _corruption.ProjectCost(sectorId, proto.Cost);
        if (!worlds.Any(w => w.Id == worldId) || proto.Cost < 0
            || !float.IsFinite(proto.WorkHours) || proto.WorkHours <= 0
            || proto.Materials.Any(m => !float.IsFinite(m.Value) || m.Value <= 0)
            || !_budget.Spend(sectorId, cost))
            return false;
        var requestId = $"sector-program:{sectorId}:{programId}";
        var request = CreateProgramRequest(requestId, worldId, proto);
        if (!_economy.QueueProject(request))
        {
            // Both the budget and command queue run on the game thread; rejection returns every credit.
            _budget.Grant(sectorId, cost);
            return false;
        }
        _projects.Add((sectorId, programId), new Project(sectorId, programId, worldId, requestId));
        return true;
    }

    public List<SWProgramEntry> BuildPrograms(string sectorId) => BuildProgramEntries(sectorId);

    private List<SWProgramEntry> BuildProgramEntries(string sectorId, bool defenseOnly = false)
    {
        Update(0);
        var available = _budget.Available(sectorId);
        var states = _projectStates;
        var services = _serviceStates;
        var list = new List<SWProgramEntry>();
        foreach (var proto in _proto.EnumeratePrototypes<SWProgramPrototype>()
                     .Where(p => !defenseOnly || IsDefenseProgram(p.ID)).OrderBy(p => p.Order))
        {
            var cost = _corruption.ProjectCost(sectorId, proto.Cost);
            _projects.TryGetValue((sectorId, proto.ID), out var project);
            var state = project != null ? states?.GetValueOrDefault(project.Request) : null;
            var complete = state?.CompletedHour != null;
            var operating = project != null && services != null ? OperatingFraction(proto, project.World, services) : 0f;
            var status = project == null ? string.Empty : state == null ? "Заказ принят"
                : complete ? operating >= .999f ? "Действует" : $"Снабжение: {operating * 100:0}%"
                : state.Status == EconomyProjectStatus.Working ? "Выполнение"
                : state.Reason switch
                {
                    "fuel" => "Ожидает топлива для перевозки",
                    "transport" => "Ожидает свободного транспорта",
                    "transit" => "Материалы в пути",
                    "no_route" => "Нет доступного маршрута",
                    _ => "Ожидает поставок",
                };
            if (_economy.Runner?.Fault != null && project != null && !complete)
                status = "Расчёт экономики остановлен";
            var materials = state?.Materials.ToDictionary(m => m.Good);
            list.Add(new SWProgramEntry
            {
                Id = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                Category = proto.Category,
                Cost = cost,
                Applied = complete,
                Started = project != null,
                Affordable = project == null && available >= cost && _time.ModeActive && _time.DayActive
                    && _senate.IsClaimed(sectorId) && _worlds.ContainsKey(sectorId) && _economy.Runner?.Fault == null,
                WorldId = project?.World ?? string.Empty,
                WorldName = project != null ? _worldNames.GetValueOrDefault(project.World, project.World) : string.Empty,
                Status = status,
                WorkHours = proto.WorkHours,
                WorkedHours = (float) (state?.WorkDone ?? 0),
                Materials = proto.Materials.Select(m => new SWProgramMaterialEntry
                {
                    GoodId = m.Key.Id,
                    Name = _proto.Index(m.Key).Name,
                    Required = m.Value,
                    Delivered = (float) (materials?.GetValueOrDefault(m.Key.Id)?.Delivered ?? 0),
                    InTransit = (float) (materials?.GetValueOrDefault(m.Key.Id)?.Incoming ?? 0),
                }).ToList(),
                Effects = EffectEntries(proto.SectorEffects, complete ? operating : 1f),
            });
        }

        return list;
    }

    public List<SWProgramWorldEntry> BuildWorlds(string sectorId) =>
        _worlds.TryGetValue(sectorId, out var worlds)
            ? worlds.Select(w => new SWProgramWorldEntry { Id = w.Id, Name = w.Name }).ToList()
            : new List<SWProgramWorldEntry>();

    private float OperatingFraction(SWProgramPrototype program, string world,
        Dictionary<(string World, string Group), EconomyServiceState> services)
    {
        var supplied = BaseOperatingFraction(program, world, services);
        return IsDefenseProgram(program.ID) ? _defenseReserve.Readiness(world, supplied) : supplied;
    }

    private static float BaseOperatingFraction(SWProgramPrototype program, string world,
        Dictionary<(string World, string Group), EconomyServiceState> services)
    {
        var supplied = 1d;
        foreach (var group in program.SuppliesGroups)
        {
            var fraction = services.TryGetValue((world, group), out var service) && service.LastRequired > 1e-9
                ? Math.Clamp(service.LastServed / service.LastRequired, 0, 1) : 0;
            supplied = Math.Min(supplied, fraction);
        }
        return (float) supplied;
    }

    private List<SWBillEffectEntry> EffectEntries(Dictionary<ProtoId<SWGlobalStatPrototype>, float> effects, float scale)
    {
        var list = new List<SWBillEffectEntry>();
        foreach (var global in _proto.EnumeratePrototypes<SWGlobalStatPrototype>().OrderBy(g => g.Order))
        {
            if (effects.TryGetValue(global.ID, out var delta))
                list.Add(new SWBillEffectEntry { StatName = global.ShortName, Color = global.Color, Delta = delta * scale });
        }

        return list;
    }
}
