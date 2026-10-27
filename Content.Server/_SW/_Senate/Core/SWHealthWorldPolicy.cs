using System;
namespace Content.Server._SW._Senate.Core;
/// <summary>Active-hour health dynamics; care uses already fulfilled economic services.</summary>
public readonly record struct SWHealthLocalCare(float Infection, float Immunity, float PendingImmunity, float Reserve);

public static class SWHealthWorldPolicy
{
    // Population units represent normalized demand. A batch contains doses for one unit,
    // so a populous world needs more physical shipments for the same coverage.
    public static float DoseCoverage(double batches, double population) =>
        population <= 0 || batches <= 0 ? 0 : (float) Math.Clamp(batches / population * 100, 0, 100);

    public static SWHealthLocalCare AdvanceCare(float infection, float immunity, float pending, float reserve, float clinics, float care, float hours)
    {
        if (hours <= 0) return new(infection, immunity, pending, reserve);
        var dose = Math.Min(pending, Math.Max(0, VaccinationRate(clinics, care) * hours));
        var used = Math.Min(dose, Math.Max(0, 100 - immunity));
        var reserveHours = Math.Min(reserve, hours);
        return new(Math.Max(0, infection - 4 * reserveHours), immunity + used, pending - used, reserve - reserveHours);
    }
    /// <summary>Clinic throughput uses fulfilled basic care once, rather than penalizing clinic coverage twice.</summary>
    public static float VaccinationRate(float clinics, float care) =>
        (6 + 12 * clinics) * Math.Clamp(care / (.25f + .75f * clinics), 0, 1);
    public static float AdvanceInfection(float infection, float immunity, float care, float incoming, float virulence, bool ministerActive, float hours)
    {
        if (hours <= 0) return infection;
        if (!ministerActive) return Math.Clamp(infection - Math.Max(2.5f, infection * .15f) * hours, 0, 100);
        return Math.Clamp(infection + (InfectionPressure(infection, immunity, incoming, virulence) - 1.2f * care - .15f) * hours, 0, 100);
    }
    /// <summary>Local and imported exposure both affect the same effectively susceptible population.</summary>
    public static float InfectionPressure(float infection, float immunity, float incoming, float virulence) =>
        ((infection > .1f ? .9f * virulence : 0) + incoming) * (1 - immunity / 100);

    public static float ImmunitySuppression(float infection, float immunity, float incoming, float virulence) =>
        Math.Max(0, InfectionPressure(infection, 0, incoming, virulence) - InfectionPressure(infection, immunity, incoming, virulence));
}
