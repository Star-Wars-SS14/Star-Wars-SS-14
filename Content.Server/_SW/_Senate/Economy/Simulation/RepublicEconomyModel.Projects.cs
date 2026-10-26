using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private sealed class Project(EconomyProjectRequest request)
    {
        public readonly EconomyProjectRequest Request = request;
        public readonly Dictionary<string, double> Delivered = request.Materials.ToDictionary(a => a.Good, _ => 0d);
        public double WorkDone;
        public double? Completed;
        public string Reason = "materials";
        public bool Ready => Request.Materials.All(a => Delivered[a.Good] + Epsilon >= a.Quantity);
    }

    private sealed record ProjectLeg(string Edge, string From, string To, double Hours, double FuelPerSlot);
    private sealed class ProjectCargo(long id, Project project, string good, double quantity, ProjectLeg[] path, int index = 0)
    {
        public readonly long Id = id;
        public readonly Project Project = project;
        public readonly string Good = good;
        public readonly ProjectLeg[] Path = path;
        public int Index = index;
        public double Quantity = quantity;
        public bool Traveling;
        public double Remaining;
        public double DepartedHour;
        public double TravelHours = path[index].Hours;
        public ProjectLeg Leg => Path[Index];
        public FlowKey Flow => new(Leg.Edge, Leg.From, Leg.To, Good, TravelHours);
    }

    private enum ReturnStage : byte { Outbound, Waiting, Returning }
    private sealed class ProjectReturn(ProjectLeg leg, double slots)
    {
        public readonly ProjectLeg Leg = leg;
        public double Slots = slots;
        public ReturnStage Stage;
        public double Remaining = leg.Hours;
    }

    private readonly Dictionary<string, Project> _projects = new();
    private readonly Dictionary<Key, double> _expandedProduction = new();
    private readonly Dictionary<Key, double> _committedProduction = new();

    private double ProductionRate(EconomyProduction recipe) => recipe.Rate
        + Get(_expandedProduction, new Key(recipe.World, recipe.Recipe));
    private readonly List<ProjectCargo> _projectCargo = new();
    private long _nextProjectCargoId = 1;
    private readonly List<ProjectReturn> _projectReturns = new();
    private readonly Dictionary<string, double> _projectFleetOccupied = new();
    // One shortest-path tree per destination, populated only when a new project destination is used.
    private readonly Dictionary<string, Dictionary<string, ProjectLeg[]>> _projectPaths = new();
    private readonly HashSet<string> _projectCarrierWorlds = new();
    private readonly Dictionary<Key, double> _projectSupplyReserves = new();

    private void RefreshProjectRoutes()
    {
        var carriers = _worlds.Keys.Where(w => !_definition.EnableAssets || FleetCapacity(w) > Epsilon).ToHashSet();
        if (_projectCarrierWorlds.SetEquals(carriers)) return;
        _projectCarrierWorlds.Clear();
        _projectCarrierWorlds.UnionWith(carriers);
        _projectPaths.Clear();
    }

    private Project[] ProjectsCollectingMaterials()
    {
        // Funded kits are filled in admission order. Splitting scarce equipment equally across
        // every project can leave all of them unfinished; a blocked material still lets later
        // projects use other goods and routes. IDs and the clock do not reorder this queue.
        return _projects.Values.Where(p => p.Completed == null && !p.Ready).ToArray();
    }

    public static bool ValidProject(EconomyDefinition definition, EconomyProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 128 ||
            !double.IsFinite(request.WorkHours) || request.WorkHours <= 0 || request.Materials == null ||
            request.NavyKind is { } navyKind && !Enum.IsDefined(navyKind) || request.RecipientFleetId is <= 0 ||
            !definition.Worlds.Any(w => w.Id == request.World && w.Active))
            return false;
        var seen = new HashSet<string>();
        if (request.Expansion is { } expansion)
        {
            var recipe = definition.Production.FirstOrDefault(p => p.World == request.World && p.Recipe == expansion.Recipe);
            if (recipe == null || !double.IsFinite(expansion.AddedCyclesPerHour)
                || expansion.AddedCyclesPerHour <= 0 || expansion.AddedCyclesPerHour > recipe.Rate
                || !double.IsFinite(recipe.Rate + expansion.AddedCyclesPerHour))
                return false;
        }
        foreach (var material in request.Materials)
        {
            if (material == null || !double.IsFinite(material.Quantity) || material.Quantity <= 0 ||
                !seen.Add(material.Good) || !definition.Goods.Any(g => g.Id == material.Good && g.CargoSlots > 0 && double.IsFinite(g.CargoSlots)))
                return false;
        }
        return true;
    }

    /// <summary>Queues a finite kit. Duplicate IDs are rejected even after completion.</summary>
    public bool TryAddProject(EconomyProjectRequest request)
    {
        if (!ValidProject(_definition, request) || _projects.ContainsKey(request.Id))
            return false;
        if (request.Expansion is { } expansion)
        {
            var key = new Key(request.World, expansion.Recipe);
            var recipe = _definition.Production.Single(p => p.World == request.World && p.Recipe == expansion.Recipe);
            var committed = Get(_committedProduction, key) + expansion.AddedCyclesPerHour;
            if (!double.IsFinite(committed) || committed > recipe.Rate || !double.IsFinite(recipe.Rate + committed))
                return false;
            _committedProduction[key] = committed;
        }
        _projects.Add(request.Id, new Project(request with { Materials = request.Materials.ToArray() }));
        return true;
    }

    private double ProjectIncoming(Project project, string good) => _projectCargo
        .Where(c => c.Project == project && c.Good == good).Sum(c => c.Quantity);

    // Goods already allocated to a shipment are outside warehouse stock and already cover this demand.
    private double ProjectOutstanding(string good) => _projects.Values.Sum(p => p.Request.Materials
        .Where(a => a.Good == good).Sum(a => Math.Max(0, a.Quantity - p.Delivered[good] - ProjectIncoming(p, good))));

    private double ProjectCommissioningHold(Key key)
    {
        var amount = 0d;
        foreach (var project in _projects.Values)
        {
            if (!project.Delivered.TryGetValue(key.Good, out var delivered) || project.Completed != null ||
                !PathsTo(project.Request.World).ContainsKey(key.World))
                continue;
            amount += Math.Max(0, project.Request.Materials.First(a => a.Good == key.Good).Quantity - delivered - ProjectIncoming(project, key.Good));
        }
        return amount;
    }

    private void AdvanceProjectWork()
    {
        foreach (var project in _projects.Values)
        {
            if (project.Completed != null || !project.Ready)
                continue;
            project.WorkDone = Math.Min(project.Request.WorkHours, project.WorkDone + StepHours);
            project.Reason = "";
            if (project.WorkDone + Epsilon >= project.Request.WorkHours)
            {
                project.Completed = _hour + StepHours;
                if (project.Request.Expansion is { } expansion)
                {
                    var key = new Key(project.Request.World, expansion.Recipe);
                    Add(_expandedProduction, key, expansion.AddedCyclesPerHour);
                    var recipe = _definition.Production.Single(p => p.World == key.World && p.Recipe == key.Good);
                    foreach (var output in recipe.Outputs)
                    {
                        Add(_pipeline, new Key(recipe.World, output.Good), expansion.AddedCyclesPerHour * output.Quantity * recipe.Hours);
                        Add(_replenishment, new Key(recipe.World, output.Good), expansion.AddedCyclesPerHour * output.Quantity);
                    }
                }
            }
        }
    }

    private void AdvanceProjectCargo()
    {
        for (var i = _projectReturns.Count - 1; i >= 0; i--)
        {
            var carrier = _projectReturns[i];
            if (carrier.Stage == ReturnStage.Waiting)
                continue;
            carrier.Remaining -= StepHours;
            if (carrier.Remaining > Epsilon)
                continue;
            if (carrier.Stage == ReturnStage.Outbound)
                carrier.Stage = ReturnStage.Waiting;
            else
            {
                if (_definition.EnableAssets)
                    Add(_projectFleetOccupied, carrier.Leg.From, -carrier.Slots);
                _projectReturns.RemoveAt(i);
            }
        }
        foreach (var cargo in _projectCargo)
        {
            var after = cargo.Quantity * Survival(cargo.Good, cargo.Traveling ? Math.Min(StepHours, cargo.Remaining) : StepHours);
            _ledgers[cargo.Good].Losses += cargo.Quantity - after;
            cargo.Quantity = after;
            if (!cargo.Traveling)
                continue;
            cargo.Remaining -= StepHours;
            if (cargo.Remaining > Epsilon)
                continue;
            ActivityFor(new Key(cargo.Leg.To, cargo.Good)).Imported += cargo.Quantity;
            cargo.Traveling = false;
            cargo.Index++;
            if (cargo.Index < cargo.Path.Length)
            {
                cargo.TravelHours = cargo.Leg.Hours;
                continue;
            }
            // Delivered materials become commissioned project equipment. They never enter the
            // generic asset orders, and are consumed exactly once in the commodity ledger.
            cargo.Project.Delivered[cargo.Good] += cargo.Quantity;
            _ledgers[cargo.Good].Consumed += cargo.Quantity;
            ActivityFor(new Key(cargo.Project.Request.World, cargo.Good)).Consumed += cargo.Quantity;
        }
        _projectCargo.RemoveAll(c => c.Index >= c.Path.Length || c.Quantity <= Epsilon);
    }

    private double ProjectSupplyReserve(Key key, Dictionary<Key, double> modifiers)
    {
        if (_projectSupplyReserves.TryGetValue(key, out var cached)) return cached;
        // Four active hours of recurring services and fuel stay at the origin. Factory feedstock
        // gets two hours as well, so a public project cannot empty operating supply chains.
        var services = _definition.Demands.Where(d => d.World == key.World && d.Good == key.Good && d.Group != "orders")
            .Sum(d => d.Rate * modifiers.GetValueOrDefault(new Key(d.World, d.Group), 1)) * 4;
        var inputs = _definition.Production.Where(p => p.World == key.World)
            .Sum(p => ProductionRate(p) * p.Inputs.Where(a => a.Good == key.Good).Sum(a => a.Quantity)) * 2;
        // A transit warehouse also serves other worlds. Construction may use only its export surplus.
        // Finite commissioning orders remain lower priority than player projects.
        var exports = !_protectedShippingGoods.Contains(key.Good) ? 0 : _definition.Shipping
            .Where(f => f.Source == key.World && f.Good == key.Good).Sum(f => f.Rate) * 4;
        if (key.Good == "ship_fuel")
            services += _definition.Shipping.Where(f => f.Source == key.World).Sum(f => f.Rate * f.FuelPerLot) * 4;
        if (key.Good == "reactor_fuel")
            services += _worlds[key.World].ReactorCapacityPower / _definition.ReactorPowerPerFuel * 4;
        var reserve = services + inputs + exports;
        _projectSupplyReserves.Add(key, reserve);
        return reserve;
    }

    private double ProjectAvailable(Key key, Dictionary<Key, double> modifiers) =>
        Math.Max(0, Stock(key) - ProjectSupplyReserve(key, modifiers));

    private void GatherProjectMaterials(Dictionary<Key, double> modifiers)
    {
        foreach (var project in ProjectsCollectingMaterials())
        {
            if (project.Completed != null || project.Ready)
                continue;
            foreach (var material in project.Request.Materials)
            {
                var missing = Math.Max(0, material.Quantity - project.Delivered[material.Good] - ProjectIncoming(project, material.Good));
                var key = new Key(project.Request.World, material.Good);
                var amount = Math.Min(missing, ProjectAvailable(key, modifiers));
                project.Delivered[material.Good] += Withdraw(key, amount);
            }
        }
    }

    private Dictionary<string, ProjectLeg[]> PathsTo(string destination)
    {
        if (_projectPaths.TryGetValue(destination, out var cached))
            return cached;
        var distances = new Dictionary<string, double> { [destination] = 0 };
        var paths = new Dictionary<string, ProjectLeg[]> { [destination] = Array.Empty<ProjectLeg>() };
        var open = new HashSet<string>(_worlds.Keys);
        while (open.Count > 0)
        {
            var current = open.OrderBy(w => distances.GetValueOrDefault(w, double.PositiveInfinity)).ThenBy(w => w, StringComparer.Ordinal).First();
            if (!distances.TryGetValue(current, out var distance))
                break;
            open.Remove(current);
            // A destination (or the source of a direct shipment) needs no local relay service.
            // Expanding the reverse tree through a node promises it can forward someone else's cargo.
            if (current != destination && (_definition.EnableAssets && FleetCapacity(current) <= Epsilon
                || paths[current][0].FuelPerSlot > Epsilon && !_stores.ContainsKey(new Key(current, "ship_fuel"))))
                continue;
            foreach (var edge in _definition.Edges.Where(e => e.CapacitySlotsPerHour > Epsilon && (e.A == current || e.B == current)))
            {
                var other = edge.A == current ? edge.B : edge.A;
                var candidate = distance + edge.TravelHours;
                if (!open.Contains(other) || candidate + Epsilon >= distances.GetValueOrDefault(other, double.PositiveInfinity))
                    continue;
                distances[other] = candidate;
                paths[other] = new[] { new ProjectLeg(edge.Id, other, current, edge.TravelHours, edge.FuelPerSlot) }.Concat(paths[current]).ToArray();
            }
        }
        _projectPaths.Add(destination, paths);
        return paths;
    }

    private double DispatchProjectLeg(ProjectCargo cargo, Dictionary<Key, double> modifiers, out string reason)
    {
        var leg = cargo.Leg;
        cargo.TravelHours = leg.Hours;
        var slots = _goods[cargo.Good].CargoSlots;
        var amount = cargo.Quantity;
        reason = "transport";
        amount = Math.Min(amount, Math.Max(0, EdgeCapacity(leg.Edge) * StepHours - Get(_edgeUsed, leg.Edge)) / slots);
        if (_definition.EnableAssets)
            amount = Math.Min(amount, Math.Max(0, FleetCapacity(leg.From) - FleetOccupied(leg.From)) / slots);
        // The origin supplies the return fuel as well. Both legs are prepaid when the
        // carrier leaves, using the same up-front fuel accounting as scheduled freight.
        var fuel = 2 * leg.FuelPerSlot * slots;
        if (fuel > Epsilon)
        {
            var available = ProjectAvailable(new Key(leg.From, "ship_fuel"), modifiers);
            ActivityFor(new Key(leg.From, "ship_fuel")).Required += amount * fuel;
            if (available / fuel < amount)
                reason = "fuel";
            amount = Math.Min(amount, available / fuel);
        }
        if (amount <= Epsilon)
            return 0;
        Withdraw(new Key(leg.From, "ship_fuel"), amount * fuel);
        _projectReturns.Add(new ProjectReturn(leg, amount * slots));
        if (_definition.EnableAssets)
            Add(_projectFleetOccupied, leg.From, amount * slots);
        Add(_edgeUsed, leg.Edge, amount * slots);
        Add(_departures, cargo.Flow, amount);
        ActivityFor(new Key(leg.From, cargo.Good)).Exported += amount;
        reason = "transit";
        return amount;
    }

    // Scheduled empty repositioning already pays for the crossing, fuel and carrier time.
    // Loading it substitutes cargo for empty slots, rather than inventing another vehicle.
    private double LoadScheduledReturn(EconomyShipping route, double slots, Dictionary<Key, double> modifiers)
    {
        var remaining = slots;
        foreach (var cargo in _projectCargo.Where(c => !c.Traveling && c.Leg.Edge == route.Edge
                     && c.Leg.From == route.Source && c.Leg.To == route.Target).ToArray())
        {
            var amount = Math.Min(cargo.Quantity, remaining / _goods[cargo.Good].CargoSlots);
            if (amount <= Epsilon) continue;
            ProjectCargo shipment;
            if (amount + Epsilon >= cargo.Quantity)
                shipment = cargo;
            else
            {
                cargo.Quantity -= amount;
                shipment = CreateProjectCargo(cargo.Project, cargo.Good, amount, cargo.Path, cargo.Index);
                _projectCargo.Add(shipment);
            }
            Depart(shipment, amount);
        }
        foreach (var project in ProjectsCollectingMaterials())
        {
            if (remaining <= Epsilon) break;
            if (!PathsTo(project.Request.World).TryGetValue(route.Source, out var path) || path.Length == 0
                || path[0].Edge != route.Edge || path[0].To != route.Target) continue;
            foreach (var material in project.Request.Materials)
            {
                var missing = Math.Max(0, material.Quantity - project.Delivered[material.Good] - ProjectIncoming(project, material.Good));
                var key = new Key(route.Source, material.Good);
                var amount = Math.Min(missing, Math.Min(ProjectAvailable(key, modifiers), remaining / _goods[material.Good].CargoSlots));
                if (amount <= Epsilon) continue;
                _stores[key].Quantity -= amount;
                var shipment = CreateProjectCargo(project, material.Good, amount, path);
                _projectCargo.Add(shipment);
                Depart(shipment, amount);
            }
        }
        return slots - remaining;

        void Depart(ProjectCargo shipment, double amount)
        {
            StartProjectCargo(shipment, route.TravelHours);
            remaining -= amount * _goods[shipment.Good].CargoSlots;
            ActivityFor(new Key(route.Source, shipment.Good)).Exported += amount;
            Add(_departures, shipment.Flow, amount);
            shipment.Project.Reason = "transit";
        }
    }

    private void DispatchProjects(Dictionary<Key, double> modifiers)
    {
        // Returning empty carriers use physical reverse crossings and shared edge capacity.
        // They retain their origin's fleet slots until they actually arrive back, including
        // time waiting at the destination for a free crossing.
        var splitReturns = new List<ProjectReturn>();
        foreach (var carrier in _projectReturns.Where(c => c.Stage == ReturnStage.Waiting))
        {
            var available = Math.Max(0, EdgeCapacity(carrier.Leg.Edge) * StepHours - Get(_edgeUsed, carrier.Leg.Edge));
            var slots = Math.Min(carrier.Slots, available);
            if (slots <= Epsilon)
                continue;
            if (slots + Epsilon >= carrier.Slots)
            {
                carrier.Stage = ReturnStage.Returning;
                carrier.Remaining = carrier.Leg.Hours;
            }
            else
            {
                carrier.Slots -= slots;
                splitReturns.Add(new ProjectReturn(carrier.Leg, slots) { Stage = ReturnStage.Returning });
            }
            Add(_edgeUsed, carrier.Leg.Edge, slots);
            Add(_edgeEmpty, carrier.Leg.Edge, slots);
        }
        _projectReturns.AddRange(splitReturns);
        // Freight already en route is forwarded first. A smaller second leg can split a load;
        // the undispatched portion remains physically at its relay and continues to decay.
        var forwarded = new List<ProjectCargo>();
        foreach (var cargo in _projectCargo.Where(c => !c.Traveling))
        {
            var amount = DispatchProjectLeg(cargo, modifiers, out var reason);
            cargo.Project.Reason = reason;
            if (amount <= Epsilon)
                continue;
            if (amount + Epsilon >= cargo.Quantity)
            {
                StartProjectCargo(cargo, cargo.Leg.Hours);
            }
            else
            {
                cargo.Quantity -= amount;
                var shipment = CreateProjectCargo(cargo.Project, cargo.Good, amount, cargo.Path, cargo.Index);
                StartProjectCargo(shipment, cargo.Leg.Hours);
                forwarded.Add(shipment);
            }
        }
        _projectCargo.AddRange(forwarded);
        foreach (var project in ProjectsCollectingMaterials())
        {
            if (project.Completed != null || project.Ready)
                continue;
            var paths = PathsTo(project.Request.World);
            foreach (var material in project.Request.Materials)
            {
                var missing = Math.Max(0, material.Quantity - project.Delivered[material.Good] - ProjectIncoming(project, material.Good));
                if (missing <= Epsilon)
                    continue;
                project.Reason = "materials";
                var sources = _stores.Keys.Where(k => k.Good == material.Good && k.World != project.Request.World)
                    .OrderBy(k => paths.TryGetValue(k.World, out var p) ? p.Sum(l => l.Hours) : double.PositiveInfinity)
                    .ThenBy(k => k.World, StringComparer.Ordinal);
                var reachable = false;
                var waitingForFleet = false;
                foreach (var source in sources)
                {
                    if (!paths.TryGetValue(source.World, out var path))
                    {
                        waitingForFleet |= _definition.EnableAssets && FleetCapacity(source.World) <= Epsilon
                            && ProjectAvailable(source, modifiers) > Epsilon;
                        continue;
                    }
                    reachable = true;
                    var available = Math.Min(missing, ProjectAvailable(source, modifiers));
                    if (material.Good == "ship_fuel")
                        available = Math.Min(available, ProjectAvailable(source, modifiers) /
                            (1 + 2 * path[0].FuelPerSlot * _goods[material.Good].CargoSlots));
                    if (available <= Epsilon)
                        continue;
                    var cargo = CreateProjectCargo(project, material.Good, available, path);
                    var amount = DispatchProjectLeg(cargo, modifiers, out var reason);
                    project.Reason = reason;
                    if (amount <= Epsilon)
                        continue;
                    _stores[source].Quantity -= amount;
                    cargo.Quantity = amount;
                    StartProjectCargo(cargo, cargo.Leg.Hours);
                    _projectCargo.Add(cargo);
                    missing -= amount;
                    if (missing <= Epsilon)
                        break;
                }
                if (!reachable && !_definition.Production.Any(p => p.World == project.Request.World && p.Outputs.Any(a => a.Good == material.Good)))
                    project.Reason = waitingForFleet ? "transport" : "no_route";
            }
        }
    }

    private IReadOnlyList<EconomyProjectState> ProjectSnapshot() => Array.AsReadOnly(_projects.Values.Select(p =>
        new EconomyProjectState(p.Request.Id, p.Request.World,
            Array.AsReadOnly(p.Request.Materials.Select(a => new EconomyProjectMaterialState(a.Good, a.Quantity,
                p.Delivered[a.Good], ProjectIncoming(p, a.Good))).ToArray()),
            p.Request.WorkHours, p.WorkDone,
            p.Completed != null ? EconomyProjectStatus.Completed : p.Ready ? EconomyProjectStatus.Working : EconomyProjectStatus.WaitingMaterials,
            p.Completed != null || p.Ready ? "" : p.Reason, p.Completed)).ToArray());

    private ProjectCargo CreateProjectCargo(Project project, string good, double quantity, ProjectLeg[] path, int index = 0) =>
        new(_nextProjectCargoId++, project, good, quantity, path, index);

    private void StartProjectCargo(ProjectCargo cargo, double travelHours)
    {
        cargo.Traveling = true;
        cargo.TravelHours = travelHours;
        cargo.Remaining = travelHours;
        // Dispatch is the end of this economic step; the new snapshot has the full leg remaining.
        cargo.DepartedHour = _hour + StepHours;
    }

    private IReadOnlyList<EconomyNavySupplyFlow> NavySupplySnapshot() => Array.AsReadOnly(_projectCargo
        .Where(c => c.Traveling && c.Quantity > Epsilon && c.Project.Request.NavyKind.HasValue)
        .Select(c => new EconomyNavySupplyFlow($"{c.Project.Request.Id}/{c.Id}", c.Project.Request.Id,
            c.Project.Request.NavyKind!.Value, c.Project.Request.RecipientFleetId, c.Project.Request.World,
            c.Leg.Edge, c.Leg.From, c.Leg.To, c.Good, c.Quantity, c.DepartedHour,
            c.DepartedHour + Math.Ceiling(c.TravelHours / StepHours) * StepHours)).ToArray());
}
