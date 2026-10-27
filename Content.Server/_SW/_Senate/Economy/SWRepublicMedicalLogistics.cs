using Content.Server._SW._Senate.Economy.Simulation;

namespace Content.Server._SW._Senate.Economy;

/// <summary>Finite campaign orders; quantities are the existing economy's commodity lots.</summary>
public static class SWRepublicMedicalLogistics
{
    public static EconomyProjectRequest CreateVaccineProject(string id, string producer, string target, string strain, double quantity) =>
        new(id, producer, new[]
        {
            new EconomyAmount("drugs", quantity * .4),
            new EconomyAmount("medical_biomass", quantity * .3),
            new EconomyAmount("medical_supplies", quantity * .5),
        }, quantity, Medical: new EconomyMedicalProject(EconomyMedicalProjectKind.VaccineProduction, strain, target, quantity));
    public static EconomyProjectRequest CreateCareProject(string id, string world, bool prosthetic, double courses) =>
        new(id, world, new[]
        {
            new EconomyAmount(prosthetic ? "implants" : "kolto", courses * .001),
            new EconomyAmount("medical_supplies", courses * .001),
        }, prosthetic ? 3 : 2, Medical: new EconomyMedicalProject(
            prosthetic ? EconomyMedicalProjectKind.ProstheticCare : EconomyMedicalProjectKind.RegenerativeCare, "", world, courses));

}
