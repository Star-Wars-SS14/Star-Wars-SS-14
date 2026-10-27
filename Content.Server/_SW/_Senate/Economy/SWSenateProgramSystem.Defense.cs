using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Programs;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Economy;

public readonly record struct SWPlanetaryDefenseReserveState(float ReserveHours, bool CanResupply, int Cost, string Status);

public sealed partial class SWSenateProgramSystem
{
    [Dependency] private readonly SWMinistrySystem _defenseMinistry = default!;
    [Dependency] private readonly SWSenateDilemmaSystem _defenseBudget = default!;
    private readonly SWPlanetaryDefenseReservePolicy _defenseReserve = new();
    private readonly Dictionary<string, string> _defenseWorldSectors = new();
    private int _nextDefenseReserve = 1;
    public const int DefenseResupplyCost = 800;
    private static bool IsDefenseProgram(string id) => id is "SWProgramGarrison" or "SWProgramShield";
    private bool DefenseCanManage => _time.ModeActive && _time.DayActive
        && _defenseMinistry.IsHolderActive(SWMinistryDefenseSystem.DefenseMinistryId);

    private void InitializeDefensePrograms(SWGalacticMapPrototype map)
    {
        foreach (var world in map.Worlds)
            if (world.Sector is { } sector && _proto.Index<SWPlanetEconomyPrototype>(world.Id).ActiveEconomy
                && map.PoliticalAreas.LastOrDefault(a => a.Contains(world.Position))?.Faction == "republic")
                _defenseWorldSectors[world.Id] = sector.Id;
    }

    private void ResetDefensePrograms()
    {
        _defenseReserve.Reset();
        _nextDefenseReserve = 1;
    }

    private EconomyProjectRequest CreateProgramRequest(string id, string world, SWProgramPrototype proto) =>
        new(id, world, proto.Materials.Select(m => new EconomyAmount(m.Key.Id, m.Value)).ToArray(), proto.WorkHours,
            NavyKind: IsDefenseProgram(proto.ID) ? EconomyNavyProjectKind.PlanetaryConstruction : null,
            Medical: IsHealthProgram(proto.ID) ? new EconomyMedicalProject(proto.ID == "SWProgramVaccination"
                ? EconomyMedicalProjectKind.Prevention : EconomyMedicalProjectKind.Relief, "", world, 1) : null);

    public bool ApplyFromDefense(string worldId, string programId)
    {
        if (!DefenseCanManage || !IsDefenseProgram(programId)
            || !_defenseWorldSectors.TryGetValue(worldId, out var sector)
            || !_proto.TryIndex<SWProgramPrototype>(programId, out var proto)
            || _projects.ContainsKey((sector, programId))) return false;
        var cost = _corruption.ProjectCost(sector, proto.Cost);
        if (cost < 0 || !float.IsFinite(proto.WorkHours) || proto.WorkHours <= 0
            || proto.Materials.Any(m => !float.IsFinite(m.Value) || m.Value <= 0)) return false;
        var before = _defenseBudget.GetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId);
        if (!_defenseBudget.SpendMinistry(SWMinistryDefenseSystem.DefenseMinistryId, cost)) return false;
        var id = $"sector-program:{sector}:{programId}";
        if (!_economy.QueueProject(CreateProgramRequest(id, worldId, proto)))
        {
            _defenseBudget.SetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId, before);
            return false;
        }
        _projects.Add((sector, programId), new Project(sector, programId, worldId, id));
        return true;
    }

    public List<SWProgramEntry> BuildDefensePrograms(string worldId)
    {
        Update(0);
        if (!_defenseWorldSectors.TryGetValue(worldId, out var sector)) return new();
        var available = _defenseBudget.GetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId);
        var list = BuildProgramEntries(sector, defenseOnly: true);
        foreach (var entry in list)
        {
            entry.Affordable = !entry.Started && DefenseCanManage && available >= entry.Cost && _economy.Runner?.Fault == null;
            if (entry.Started && entry.WorldId != worldId)
            {
                entry.Affordable = false;
                entry.Status = $"Размещено на {entry.WorldName}: {entry.Status}";
            }
        }
        return list;
    }

    public float GetDefenseReadiness(string worldId, string programId)
    {
        Update(0);
        if (!IsDefenseProgram(programId) || !_defenseWorldSectors.TryGetValue(worldId, out var sector)
            || !_projects.TryGetValue((sector, programId), out var project) || project.World != worldId
            || !_projectStates.TryGetValue(project.Request, out var state) || state.CompletedHour == null) return 0;
        return OperatingFraction(_proto.Index<SWProgramPrototype>(programId), worldId, _serviceStates);
    }

    private void RefreshDefenseReserves(EconomySnapshot snapshot, Dictionary<string, EconomyProjectState> states,
        Dictionary<(string World, string Group), EconomyServiceState> services)
    {
        var supplied = new Dictionary<string, float>();
        foreach (var project in _projects.Values.Where(p => IsDefenseProgram(p.Program)))
        {
            if (!states.TryGetValue(project.Request, out var state) || state.CompletedHour == null) continue;
            var fraction = BaseOperatingFraction(_proto.Index<SWProgramPrototype>(project.Program), project.World, services);
            supplied[project.World] = Math.Min(supplied.GetValueOrDefault(project.World, 1), fraction);
        }
        _defenseReserve.Update(snapshot.Hour, states, supplied,
            supplied.Keys.ToDictionary(w => w, w => DefenseDemandScale(w, snapshot)));
    }

    private double DefenseDemandScale(string world, EconomySnapshot? snapshot)
    {
        var demands = _economy.Definition?.Demands;
        if (demands == null || snapshot == null) return 1;
        var scale = 1d;
        foreach (var group in new[] { "maintenance", "military" })
        {
            var baseline = demands.Where(d => d.World == world && d.Group == group).Sum(d => d.Rate)
                * RepublicEconomyModel.StepHours;
            var actual = snapshot.Services.FirstOrDefault(s => s.World == world && s.Group == group)?.LastRequired ?? 0;
            if (baseline > 1e-9) scale = Math.Max(scale, actual / baseline);
        }
        return scale;
    }

    private EconomyProjectRequest DefenseReserveRequest(string id, string world, double scale)
    {
        var amounts = new Dictionary<string, double>
        {
            ["military_supplies"] = .5, ["parts"] = .25, ["circuits"] = .1,
        };
        foreach (var demand in (_economy.Definition?.Demands ?? Array.Empty<EconomyDemand>())
                     .Where(d => d.World == world && d.Group is "maintenance" or "military").GroupBy(d => d.Good))
            amounts[demand.Key] = Math.Max(amounts.GetValueOrDefault(demand.Key), demand.Sum(d => d.Rate) * SWPlanetaryDefenseReservePolicy.CapacityHours);
        return new EconomyProjectRequest(id, world, amounts.Select(a => new EconomyAmount(a.Key, a.Value * scale)).ToArray(), .5,
            NavyKind: EconomyNavyProjectKind.PlanetarySupply);
    }

    public bool ResupplyFromDefense(string worldId)
    {
        Update(0);
        if (!GetDefenseReserve(worldId).CanResupply) return false;
        var before = _defenseBudget.GetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId);
        if (!_defenseBudget.SpendMinistry(SWMinistryDefenseSystem.DefenseMinistryId, DefenseResupplyCost)) return false;
        var id = $"planetary-reserve:{worldId}:{_nextDefenseReserve++}";
        var scale = DefenseDemandScale(worldId, _economy.Snapshot);
        if (!_economy.QueueProject(DefenseReserveRequest(id, worldId, scale)))
        {
            _defenseBudget.SetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId, before);
            return false;
        }
        _defenseReserve.TryQueue(worldId, id, scale);
        return true;
    }

    public SWPlanetaryDefenseReserveState GetDefenseReserve(string worldId)
    {
        Update(0);
        var hours = (float) _defenseReserve.Hours(worldId, DefenseDemandScale(worldId, _economy.Snapshot));
        var pending = _defenseReserve.Pending(worldId);
        var project = pending == null ? null : _economy.GetProject(pending);
        var installed = _defenseWorldSectors.TryGetValue(worldId, out var sector)
            && _projects.Values.Any(p => p.Sector == sector && p.World == worldId && IsDefenseProgram(p.Program) && IsApplied(sector, p.Program));
        var status = hours > 1e-6f ? $"Резерв: {hours:0.##} ч нехватки снабжения"
            : pending == null ? installed ? "Резерва нет" : "Сначала разверните оборону"
            : project == null ? "Заказ резерва принят"
            : project.Status == EconomyProjectStatus.Working ? "Подготовка резерва"
            : string.Join("; ", project.Materials.Select(m =>
                $"{_proto.Index<SWCommodityPrototype>(m.Good).Name}: {m.Delivered:0.##}/{m.Required:0.##}, в пути {m.Incoming:0.##}"));
        if (_economy.Runner?.Fault != null && pending != null) status = "Расчёт экономики остановлен";
        return new(hours, DefenseCanManage && installed && _defenseReserve.CanQueue(worldId)
            && _economy.Runner?.Fault == null && _defenseBudget.GetMinistryBudget(SWMinistryDefenseSystem.DefenseMinistryId) >= DefenseResupplyCost,
            DefenseResupplyCost, status);
    }
}
