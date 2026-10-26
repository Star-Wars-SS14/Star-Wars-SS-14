using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._SW._Senate.Sectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Checks geographic coverage, placement prerequisites and units without running production.</summary>
public static class SWPlanetEconomyValidator
{
    public static bool IsNaturalResource(string condition) => condition is
        "water_source" or "moisture" or "arable" or "fish_stock" or "metal_deposit" or
        "rare_deposit" or "mineral_deposit" or "chemical_deposit" or "fuel_deposit" or
        "tibanna_deposit" or "crystal_deposit" or "medical_biome" or "manaan_kolto" or "special_precursor";

    public static IReadOnlyList<string> Validate(IPrototypeManager prototypes)
    {
        return Validate(prototypes, prototypes.EnumeratePrototypes<SWPlanetEconomyPrototype>());
    }

    public static IReadOnlyList<string> Validate(IPrototypeManager prototypes,
        IEnumerable<SWPlanetEconomyPrototype> planets)
    {
        var errors = new List<string>();
        var worlds = prototypes.Index<SWGalacticMapPrototype>("SenateRepublic").Worlds.ToDictionary(w => w.Id);
        var recipes = prototypes.EnumeratePrototypes<SWProductionRecipePrototype>().ToArray();
        var seen = new HashSet<string>();
        foreach (var planet in planets)
        {
            if (!seen.Add(planet.ID))
                errors.Add($"Planet economy repeats world {planet.ID}.");
            if (!worlds.TryGetValue(planet.ID, out var world))
                errors.Add($"Planet economy {planet.ID} has no graph world.");
            else if (world.Sector == null && planet.ActiveEconomy)
                errors.Add($"External world {planet.ID} cannot have a domestic economy.");

            CheckNumber(planet.ID, nameof(planet.PopulationUnits), planet.PopulationUnits);
            CheckNumber(planet.ID, nameof(planet.BaseloadPower), planet.BaseloadPower);
            CheckNumber(planet.ID, nameof(planet.LaborCapacity), planet.LaborCapacity);
            CheckNumber(planet.ID, nameof(planet.BufferHours), planet.BufferHours);
            if (planet.ActiveEconomy && (planet.PopulationUnits <= 0 || planet.BufferHours <= 0))
                errors.Add($"Active world {planet.ID} needs population and a positive supply buffer.");
            if (!planet.ActiveEconomy && (planet.PopulationUnits != 0 || planet.BaseloadPower != 0 ||
                planet.LaborCapacity != 0 || planet.BufferHours != 0 || planet.Facilities.Count != 0 ||
                planet.Conditions.Count != 0 || planet.NaturalResources.Count != 0))
                errors.Add($"Inactive world {planet.ID} cannot create population, power, labor or resources.");

            CheckReferences(planet.ID, planet.Facilities);
            CheckReferences(planet.ID, planet.Conditions);
            var resources = new HashSet<ProtoId<SWProductionConditionPrototype>>();
            foreach (var resource in planet.NaturalResources)
            {
                if (!resources.Add(resource.Condition))
                    errors.Add($"World {planet.ID} repeats resource {resource.Condition}.");
                if (!prototypes.HasIndex(resource.Condition) || !planet.Conditions.Contains(resource.Condition) ||
                    !IsNaturalResource(resource.Condition.Id))
                    errors.Add($"World {planet.ID} resource {resource.Condition} is not a present natural condition.");
                var available = recipes.Where(r => r.Requirements.Contains(resource.Condition) &&
                    r.Requirements.All(planet.Conditions.Contains) && planet.Facilities.Contains(r.Facility)).ToArray();
                foreach (var (good, rate) in resource.BaselineYieldPerHour)
                {
                    if (!prototypes.HasIndex(good) || !available.Any(r => r.Outputs.ContainsKey(good)))
                        errors.Add($"World {planet.ID} cannot extract {good} from {resource.Condition}.");
                    if (!double.IsFinite(rate) || rate <= 0)
                        errors.Add($"World {planet.ID} yield {good} must be finite and positive.");
                }
            }
            if (!resources.SetEquals(planet.Conditions.Where(c => IsNaturalResource(c.Id))))
                errors.Add($"World {planet.ID} resource list must exactly cover its natural conditions.");
        }
        foreach (var id in worlds.Keys.Where(id => !seen.Contains(id)))
            errors.Add($"Graph world {id} has no economy profile.");
        return errors;

        void CheckNumber(string owner, string field, double value)
        {
            if (!double.IsFinite(value) || value < 0)
                errors.Add($"World {owner} {field} must be finite and non-negative.");
        }

        void CheckReferences<T>(string owner, List<ProtoId<T>> references) where T : class, IPrototype
        {
            var unique = new HashSet<ProtoId<T>>();
            foreach (var reference in references)
            {
                if (!prototypes.HasIndex(reference))
                    errors.Add($"World {owner} references missing {typeof(T).Name} {reference}.");
                if (!unique.Add(reference))
                    errors.Add($"World {owner} repeats {typeof(T).Name} {reference}.");
            }
        }
    }
}
