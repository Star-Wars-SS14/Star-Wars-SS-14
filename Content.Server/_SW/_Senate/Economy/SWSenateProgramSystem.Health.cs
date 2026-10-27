using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate;
using Content.Shared._SW._Senate.Economy;
using Content.Shared._SW._Senate.Programs;
using Content.Shared._SW._Senate.Sectors;

namespace Content.Server._SW._Senate.Economy;

public sealed partial class SWSenateProgramSystem
{
    private readonly Dictionary<string, string> _healthWorldSectors = new();
    private EconomySnapshot? _healthCoverageSnapshot;
    private readonly Dictionary<string, float> _medicalAssetCoverage = new();
    private static bool IsHealthProgram(string id) => id is "SWProgramClinics" or "SWProgramVaccination";
    private bool HealthCanManage => _time.ModeActive && _time.DayActive && _defenseMinistry.IsHolderActive(SWMinistryHealthSystem.HealthMinistryId);
    private void InitializeHealthPrograms(SWGalacticMapPrototype map)
    {
        foreach (var world in map.Worlds)
            if (world.Sector is { } sector && _proto.Index<SWPlanetEconomyPrototype>(world.Id).ActiveEconomy)
                _healthWorldSectors[world.Id] = sector.Id;
    }
    public bool ApplyFromHealth(string worldId, string programId)
    {
        if (!HealthCanManage || !IsHealthProgram(programId) || !_healthWorldSectors.TryGetValue(worldId, out var sector)
            || !_proto.TryIndex<SWProgramPrototype>(programId, out var proto) || _projects.ContainsKey((sector, programId))) return false;
        var cost = _corruption.ProjectCost(sector, proto.Cost);
        if (cost < 0 || !float.IsFinite(proto.WorkHours) || proto.WorkHours <= 0 || proto.Materials.Any(m => !float.IsFinite(m.Value) || m.Value <= 0)) return false;
        var before = _defenseBudget.GetMinistryBudget(SWMinistryHealthSystem.HealthMinistryId);
        if (!_defenseBudget.SpendMinistry(SWMinistryHealthSystem.HealthMinistryId, cost)) return false;
        var id = $"sector-program:{sector}:{programId}";
        var request = CreateProgramRequest(id, worldId, proto);
        if (!_economy.QueueProject(request))
        {
            _defenseBudget.SetMinistryBudget(SWMinistryHealthSystem.HealthMinistryId, before);
            return false;
        }
        _projects.Add((sector, programId), new Project(sector, programId, worldId, id));
        return true;
    }
    public List<SWProgramEntry> BuildHealthPrograms(string worldId)
    {
        if (!_healthWorldSectors.TryGetValue(worldId, out var sector)) return new();
        var available = _defenseBudget.GetMinistryBudget(SWMinistryHealthSystem.HealthMinistryId);
        var list = BuildProgramEntries(sector, healthOnly: true);
        foreach (var entry in list)
        {
            entry.Affordable = !entry.Started && HealthCanManage && available >= entry.Cost && _economy.Runner?.Fault == null;
            if (entry.Started && entry.WorldId != worldId)
            {
                entry.Affordable = false;
                entry.Status = $"Размещено на {entry.WorldName}: {entry.Status}";
            }
        }
        return list;
    }
    public bool IsHealthProgramCompleted(string worldId, string programId)
    {
        Update(0);
        return IsHealthProgram(programId) && _healthWorldSectors.TryGetValue(worldId, out var sector)
            && _projects.TryGetValue((sector, programId), out var project) && project.World == worldId
            && _projectStates.TryGetValue(project.Request, out var state) && state.CompletedHour != null;
    }
    public float GetHealthClinicCoverage(string worldId)
    {
        Update(0);
        var snapshot = _economy.Snapshot;
        if (!ReferenceEquals(snapshot, _healthCoverageSnapshot))
        {
            _healthCoverageSnapshot = snapshot;
            _medicalAssetCoverage.Clear();
            if (snapshot != null)
                foreach (var group in snapshot.Assets.Where(a => a.Effect == "healthcare").GroupBy(a => a.World))
                    _medicalAssetCoverage[group.Key] = (float) Math.Clamp(group.Sum(a => a.EffectiveBonus) / 2, 0, 1);
        }
        var coverage = _medicalAssetCoverage.GetValueOrDefault(worldId);
        if (_healthWorldSectors.TryGetValue(worldId, out var sector) && _projects.TryGetValue((sector, "SWProgramClinics"), out var project)
            && project.World == worldId && _projectStates.TryGetValue(project.Request, out var state) && state.CompletedHour != null)
            coverage += .5f * OperatingFraction(_proto.Index<SWProgramPrototype>("SWProgramClinics"), worldId, _serviceStates);
        return Math.Clamp(coverage, 0, 1);
    }
}
