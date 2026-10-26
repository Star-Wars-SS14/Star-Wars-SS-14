using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Validates routing limits before a simulation is allowed to use them.</summary>
public static class SWTransitLinkValidator
{
    public static IReadOnlyList<string> Validate(IPrototypeManager prototypes) => Validate(
        prototypes.Index<SWGalacticMapPrototype>("SenateRepublic"),
        prototypes.EnumeratePrototypes<SWTransitLinkPrototype>());

    public static IReadOnlyList<string> Validate(SWGalacticMapPrototype map, IEnumerable<SWTransitLinkPrototype> links)
    {
        var errors = new List<string>();
        var worlds = map.Worlds.Select(w => w.Id).ToHashSet();
        var expected = map.Routes.Select(r => SWTransitLinkPrototype.LinkId(r.From, r.To)).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var link in links)
        {
            if (!seen.Add(link.ID))
                errors.Add($"Duplicate transit capacity {link.ID}.");
            if (link.From == link.To || !worlds.Contains(link.From) || !worlds.Contains(link.To))
                errors.Add($"Transit link {link.ID} has invalid endpoints.");
            if (link.ID != SWTransitLinkPrototype.LinkId(link.From, link.To) || !expected.Contains(link.ID))
                errors.Add($"Transit link {link.ID} does not match the galactic graph.");
            if (!double.IsFinite(link.TravelHours) || link.TravelHours <= 0)
                errors.Add($"Transit link {link.ID} needs a positive travel time.");
            CheckNonNegative(link.ID, nameof(link.FuelPerSlot), link.FuelPerSlot);
            CheckNonNegative(link.ID, nameof(link.CapacitySlotsPerHour), link.CapacitySlotsPerHour);
            CheckNonNegative(link.ID, nameof(link.PlannedLoadedSlotsPerHour), link.PlannedLoadedSlotsPerHour);
            CheckNonNegative(link.ID, nameof(link.PlannedEmptySlotsPerHour), link.PlannedEmptySlotsPerHour);
            if (link.Accessible && link.CapacitySlotsPerHour <= 0)
                errors.Add($"Accessible transit link {link.ID} has no capacity.");
            if (!link.Accessible && (link.CapacitySlotsPerHour != 0 || link.PlannedSlotsPerHour != 0))
                errors.Add($"Closed transit link {link.ID} carries traffic or offers capacity.");
            if (link.PlannedSlotsPerHour > link.CapacitySlotsPerHour + .0001f)
                errors.Add($"Transit link {link.ID} exceeds the capacity shared by both directions.");
        }
        foreach (var id in expected.Where(id => !seen.Contains(id)))
            errors.Add($"Missing transit capacity for {id}.");
        return errors;

        void CheckNonNegative(string id, string field, double value)
        {
            if (!double.IsFinite(value) || value < 0)
                errors.Add($"Transit link {id} has invalid {field}.");
        }
    }
}
