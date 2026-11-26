using System;
using System.Linq;

namespace Content.Server._SW._Senate.Economy.Simulation;

public sealed partial class RepublicEconomyModel
{
    private EconomyProjectMaterialState ProjectMaterialSnapshot(Project project, EconomyAmount material) =>
        ProjectDeliverySnapshot(project, material.Good, material.Quantity, project.Delivered[material.Good], payload: false);

    private EconomyProjectMaterialState ProjectPayloadSnapshot(Project project) =>
        ProjectDeliverySnapshot(project, project.Request.Medical!.Good, project.Request.Medical.Quantity,
            project.PayloadDelivered, payload: true);

    private bool CargoRouteBlocked(ProjectCargo cargo) => cargo.Path.Skip(cargo.Index + (cargo.Traveling ? 1 : 0)).Any(leg =>
        EdgeCapacity(leg.Edge) <= Epsilon || !CrossingAllowed(leg.From, leg.To, cargo.Good,
            IsMedicalCargo(cargo.Project, cargo.Good, cargo.Payload)));

    private static double CargoMinimumTravel(ProjectCargo cargo) =>
        RoundedTravel(cargo.Traveling ? cargo.Remaining : cargo.Leg.Hours)
        + cargo.Path.Skip(cargo.Index + 1).Sum(leg => RoundedTravel(leg.Hours));

    private double CargoExpectedArrival(ProjectCargo cargo) => cargo.Payload ? cargo.Quantity : cargo.Quantity * Survival(cargo.Good,
        (cargo.Traveling ? cargo.Remaining : cargo.Leg.Hours) + cargo.Path.Skip(cargo.Index + 1).Sum(leg => leg.Hours));

    private EconomyProjectMaterialState ProjectDeliverySnapshot(Project project, string good, double required, double delivered, bool payload)
    {
        var cargo = _projectCargo.Where(c => c.Project == project && c.Good == good && c.Payload == payload).ToArray();
        var incoming = cargo.Sum(c => c.Quantity);
        var remaining = Math.Max(0, required - delivered);
        var priority = !payload && remaining > Epsilon && IsResearchMaterial(project, good);
        var tolerance = payload ? 0 : MaterialTolerance(project);
        var supplied = payload ? remaining <= Epsilon : MaterialSatisfied(project, required, delivered);
        if (supplied || payload && project.Completed != null)
            return new(good, required, delivered, incoming)
            {
                QuantityTolerance = tolerance, DeliveryHours = 0, DeliveryStatus = supplied ? "delivered" : "delivered_with_losses",
            };
        var blocked = cargo.Any(CargoRouteBlocked);
        // A finite batch still at its producer, or goods lost in transit, cannot be
        // declared fully supplied merely because the closest allocated part is arriving.
        var expected = cargo.Sum(CargoExpectedArrival);
        var full = payload ? expected + Epsilon >= remaining : MaterialSatisfied(project, required, delivered + expected);
        var allTraveling = cargo.Length > 0 && cargo.All(c => c.Traveling);
        var status = blocked ? "route_blocked" : cargo.Any(c => !c.Traveling) ? project.Reason == "fuel" ? "fuel" : "waiting_transport"
            : !full ? incoming > Epsilon ? "partial_incoming" : project.Reason == "no_route" ? "route_blocked"
                : project.Reason == "fuel" ? "fuel" : project.Reason == "transport" || payload ? "waiting_transport" : "waiting_materials"
            : cargo.Any(c => c.Index + 1 < c.Path.Length) ? "transshipment" : "in_transit";
        double? minimum = full && allTraveling && !blocked ? cargo.Max(CargoMinimumTravel) : null;
        var confirmed = full && allTraveling && cargo.All(c => c.Index + 1 == c.Path.Length) ? minimum : null;

        // Prefer the next arrival that is actually guaranteed by accepted final legs.
        // If none exists, expose only the earliest possible whole-path travel lower bound.
        var candidates = cargo.Where(c => c.Traveling && !CargoRouteBlocked(c) && CargoExpectedArrival(c) > Epsilon).ToArray();
        var final = candidates.Where(c => c.Index + 1 == c.Path.Length).ToArray();
        var next = final.Length > 0 ? final : candidates;
        double? nextMinimum = next.Length > 0 ? next.Min(CargoMinimumTravel) : null;
        var nextQuantity = nextMinimum is { } nearest ? next.Where(c => Math.Abs(CargoMinimumTravel(c) - nearest) <= Epsilon).Sum(CargoExpectedArrival) : 0;
        return new(good, required, delivered, incoming)
        {
            QuantityTolerance = tolerance, DeliveryHours = confirmed, MinimumTravelHours = minimum, DeliveryStatus = status, Priority = priority,
            NextDeliveryHours = final.Length > 0 ? nextMinimum : null,
            NextMinimumTravelHours = nextMinimum, NextDeliveryQuantity = nextQuantity,
        };
    }

    private static double RoundedTravel(double hours) => Math.Ceiling(Math.Max(0, hours - Epsilon) / StepHours) * StepHours;
}
