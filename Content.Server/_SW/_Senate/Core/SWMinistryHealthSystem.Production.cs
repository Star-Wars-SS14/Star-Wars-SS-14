using System.Collections.Generic;
using System.Linq;
using Content.Server._SW._Senate.Economy.Simulation;
using Content.Shared._SW._Senate.Ministries;

namespace Content.Server._SW._Senate.Core;

public sealed partial class SWMinistryHealthSystem
{
    private bool HasVaccineProductionSite(string world) => _worlds.ContainsKey(world)
        && _economy.Definition?.Production.Any(p => p.World == world && p.Rate > 0
            && p.Outputs.Any(a => a.Good is "drugs" or "medical_supplies")) == true;

    private EconomyMedicalProductionState? ProductionCapacity(string world) =>
        _economy.Snapshot?.MedicalProductionSites.FirstOrDefault(p => p.World == world);

    // Admission and the displayed availability use the same published capacity as the allocator.
    // This is current capacity, not a reservation against later fuel or workforce shortages.
    public string? VaccineProductionUnavailableReason(string world)
    {
        if (!HasVaccineProductionSite(world)) return "На планете нет производства вакцин";
        var site = ProductionCapacity(world);
        if (site == null) return "Ожидаются данные о мощности производства";
        if (site.Ready) return null;
        return site.Reason == "power" ? "Нет свободной энергии" : "Нет свободного персонала";
    }

    private void PublishProductionAvailability(IEnumerable<SWHealthWorldView> worlds)
    {
        foreach (var world in worlds)
        {
            world.HasProductionSite = HasVaccineProductionSite(world.Id);
            if (!world.HasProductionSite) continue;
            world.ProductionStatus = VaccineProductionUnavailableReason(world.Id) ?? "";
            if (ProductionCapacity(world.Id) is not { } site) continue;
            world.ProductionRate = site.WorkFraction;
            world.ProductionPowerAvailable = site.PowerAvailable;
            world.ProductionPowerRequired = site.PowerRequired;
            world.ProductionLaborAvailable = site.LaborAvailable;
            world.ProductionLaborRequired = site.LaborRequired;
            if (site.Ready && site.WorkFraction < 1)
                world.ProductionStatus = site.PowerAvailable / site.PowerRequired <= site.LaborAvailable / site.LaborRequired
                    ? "Выпуск ограничен энергией" : "Выпуск ограничен персоналом";
        }
    }
}
