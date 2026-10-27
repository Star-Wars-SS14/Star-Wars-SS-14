using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    /// <summary>Eligibility comes from a real admitted addressed order and its actual commodity,
    /// never from a generic medical label on arbitrary construction or industrial freight.</summary>
    private static bool IsMedicalCargo(Project project, string good, bool payload = false)
    {
        if (project.Request.Medical is not { } medical) return false;
        if (payload) return medical.Kind == EconomyMedicalProjectKind.VaccineProduction && good == medical.Good;
        if (medical.Kind != EconomyMedicalProjectKind.VaccineProduction && medical.TargetWorld != project.Request.World) return false;
        if (!project.Request.Materials.Any(m => m.Good == good)) return false;
        return medical.Kind switch
        {
            EconomyMedicalProjectKind.Research => good is "lab_equipment" or "medical_supplies",
            EconomyMedicalProjectKind.VaccineProduction => good is "drugs" or "medical_biomass" or "medical_supplies",
            EconomyMedicalProjectKind.Relief => good is "drugs" or "medical_supplies" or "medical_equipment",
            EconomyMedicalProjectKind.Prevention => good is "drugs" or "medical_supplies",
            EconomyMedicalProjectKind.RegenerativeCare => good is "kolto" or "medical_supplies",
            EconomyMedicalProjectKind.ProstheticCare => good is "implants" or "medical_supplies",
            _ => false,
        };
    }

    private static bool IsPriorityProjectCargo(ProjectCargo cargo) =>
        IsResearchMaterial(cargo.Project, cargo.Good) || cargo.Payload && IsMedicalCargo(cargo.Project, cargo.Good, payload: true);

    private static bool IsCareKind(EconomyMedicalProjectKind kind) =>
        kind is EconomyMedicalProjectKind.RegenerativeCare or EconomyMedicalProjectKind.ProstheticCare;

    private static bool IsCareProject(Project project) => project.Request.Medical is { } medical && IsCareKind(medical.Kind);

    // Only supplied cases occupy the shared three-team pool. Stable OrderBy keeps admission
    // order among peers; started cases retain their teams until their finite work is complete.
    private HashSet<Project> CareTeamSelection() => _projects.Values
        .Where(p => p.Completed == null && p.Ready && IsCareProject(p))
        .OrderByDescending(p => p.WorkDone > Epsilon).Take(3).ToHashSet();

    private static bool IsResearchMaterial(Project project, string good) =>
        project.Request.Medical is { Kind: EconomyMedicalProjectKind.Research } && IsMedicalCargo(project, good);

    private static double MaterialTolerance(Project project) =>
        project.Request.Medical is { Kind: EconomyMedicalProjectKind.VaccineProduction } ? .001 : 0;

    private static bool MaterialSatisfied(Project project, double required, double delivered) =>
        MaterialTolerance(project) > 0 ? delivered > required - MaterialTolerance(project) : delivered + Epsilon >= required;

    private static double ProjectMaterialMissing(Project project, double required, double allocated) =>
        MaterialSatisfied(project, required, allocated) ? 0 : Math.Max(0, required - allocated);
    private readonly Dictionary<string, double> _medicalWorkFractions = new();

    private readonly Dictionary<string, EconomyMedicalProductionState> _medicalProductionSites = new();

    /// <summary>Existing industry is served first. Vaccine work draws only spare capacity;
    /// reactor fuel for both consumers is then withdrawn once by the normal world power step.</summary>
    private (double Power, double Labor) AllocateMedicalWork(EconomyWorld world, double sparePower, double spareLabor)
    {
        var site = MeasureMedicalProduction(world, sparePower, spareLabor);
        if (site == null) return (0, 0);
        _medicalProductionSites[world.Id] = site;
        var queue = _projects.Values.Where(p => p.Request.World == world.Id && p.Completed == null && !p.Manufactured
            && p.Ready && p.Request.Medical is { Kind: EconomyMedicalProjectKind.VaccineProduction }).ToArray();
        if (queue.Length == 0) return (0, 0);
        foreach (var waiting in queue.Skip(1)) waiting.Reason = "production_queue";
        var project = queue[0];
        var power = site.PowerRequired;
        var labor = site.LaborRequired;
        var fraction = site.WorkFraction;
        fraction = Math.Min(fraction, Math.Max(0, project.Request.WorkHours - project.WorkDone) / StepHours);
        project.Reason = site.PowerAvailable / power <= site.LaborAvailable / labor ? "power" : "labor";
        _medicalWorkFractions[project.Request.Id] = fraction;
        return (power * fraction, labor * fraction);
    }

    private EconomyMedicalProductionState? MeasureMedicalProduction(EconomyWorld world, double sparePower, double spareLabor)
    {
        if (!world.Active) return null;
        var recipe = _definition.Production.Where(p => p.World == world.Id && p.Rate > 0 && p.Outputs.Any(a => a.Good is "drugs" or "medical_supplies"))
            .OrderBy(p => p.Recipe, StringComparer.Ordinal).FirstOrDefault();
        if (recipe == null) return null;
        // Share the actual workstation footprint with allocation, including the positive minimum.
        var power = Math.Max(.001, recipe.Power * recipe.Rate * recipe.Hours * .1);
        var labor = Math.Max(.001, recipe.Labor * recipe.Rate * recipe.Hours * .1);
        var availablePower = Math.Max(0, sparePower);
        var availableLabor = Math.Max(0, spareLabor);
        return new(world.Id, power, availablePower, labor, availableLabor,
            Math.Min(1, Math.Min(availablePower / power, availableLabor / labor)));
    }
    private void DispatchMedicalPayloads(Dictionary<Key, double> modifiers)
    {
        foreach (var project in _projects.Values.Where(p => p.Manufactured && p.Completed == null && p.PayloadRemaining > Epsilon))
        {
            var medical = project.Request.Medical!;
            if (medical.TargetWorld == project.Request.World)
            {
                project.PayloadDelivered += project.PayloadRemaining;
                _ledgers[medical.Good].Consumed += project.PayloadRemaining;
                ActivityFor(new Key(medical.TargetWorld, medical.Good)).Consumed += project.PayloadRemaining;
                project.PayloadRemaining = 0;
                project.Completed = _hour + StepHours;
                continue;
            }
            if (!PathsTo(medical.TargetWorld, medical.Good, IsMedicalCargo(project, medical.Good, payload: true)).TryGetValue(project.Request.World, out var path))
            {
                project.Reason = "no_route";
                continue;
            }
            var cargo = CreateProjectCargo(project, medical.Good, project.PayloadRemaining, path);
            cargo.Payload = true;
            var amount = DispatchProjectLeg(cargo, modifiers, out var reason);
            project.Reason = reason;
            if (amount <= Epsilon) continue;
            project.PayloadRemaining -= amount;
            cargo.Quantity = amount;
            StartProjectCargo(cargo, cargo.Leg.Hours);
            _projectCargo.Add(cargo);
        }
    }
    private IReadOnlyList<EconomyMedicalSupplyFlow> MedicalSupplySnapshot() => Array.AsReadOnly(_projectCargo
        .Where(c => c.Traveling && c.Quantity > Epsilon && c.Project.Request.Medical != null)
        .Select(MedicalSupplyFlowSnapshot).ToArray());

    private EconomyMedicalSupplyFlow MedicalSupplyFlowSnapshot(ProjectCargo cargo)
    {
        var blocked = CargoRouteBlocked(cargo);
        return new EconomyMedicalSupplyFlow($"{cargo.Project.Request.Id}/{cargo.Id}", cargo.Project.Request.Id,
            cargo.Project.Request.Medical!, cargo.Payload ? cargo.Project.Request.Medical!.TargetWorld : cargo.Project.Request.World,
            cargo.Leg.Edge, cargo.Leg.From, cargo.Leg.To, cargo.Good, cargo.Quantity, cargo.DepartedHour,
            cargo.DepartedHour + Math.Ceiling(cargo.TravelHours / StepHours) * StepHours, cargo.Payload)
        {
            DeliveryAtHours = !blocked && CargoExpectedArrival(cargo) > Epsilon ? _hour + CargoMinimumTravel(cargo) : null,
            FinalLeg = cargo.Index + 1 == cargo.Path.Length, RouteBlocked = blocked,
        };
    }
}
