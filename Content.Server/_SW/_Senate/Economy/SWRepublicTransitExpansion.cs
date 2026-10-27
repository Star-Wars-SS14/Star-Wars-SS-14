using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

/// <summary>A physical terminal expansion on an existing transport link.</summary>
public static class SWRepublicTransitExpansion
{
    public const double WorkHours = 4;
    public const double CapacityFraction = .5;

    public static EconomyProjectRequest CreateProject(string id, EconomyEdge edge) =>
        new(id, edge.A, new[] { new EconomyAmount("parts", .5), new EconomyAmount("circuits", .25) }, WorkHours)
        {
            TransitExpansion = new EconomyTransitExpansion(edge.Id, edge.CapacitySlotsPerHour * CapacityFraction),
        };
}
