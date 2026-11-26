using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    private void PublishVaccinationTelemetry(SWHealthView view)
    {
        view.DayActive = _time.ModeActive && _time.DayActive;
        foreach (var entry in view.Worlds)
        {
            var world = _worlds[entry.Id];
            var care = Care(world);
            var clinics = _programs.GetHealthClinicCoverage(entry.Id);
            entry.PendingImmunity = world.PendingImmunity;
            // The same policy used by progression supplies the attainable next active
            // hour, limited by delivered doses and remaining susceptible population.
            entry.ImmunityApplicationPerHour = view.DayActive
                ? SWHealthWorldPolicy.AdvanceCare(world.Infection, world.Immunity, world.PendingImmunity, 0, clinics, care, 1).Immunity - world.Immunity : 0;
            entry.ImmunitySuppressionPerHour = view.DayActive && _active && MinisterActive
                ? SWHealthWorldPolicy.ImmunitySuppression(world.Infection, world.Immunity, world.IncomingInfectionPerHour, _virulence) : 0;
            entry.VaccinationStatus = world.Immunity >= 100 ? "Полный охват достигнут"
                : world.PendingImmunity <= 0 ? world.Immunity > 0 ? "Доставленные дозы применены" : "Нет доставленных доз для применения"
                : !view.DayActive ? "Ночная пауза"
                : care <= 0 ? "Нет базового медицинского снабжения"
                : "Применение доставленных доз";
        }
    }
}
