using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW._Senate.Economy;

/// <summary>Checks the static catalogue without inventing world stocks or running a simulation.</summary>
public static class SWCommodityCatalogValidator
{
    public static IReadOnlyList<string> Validate(IPrototypeManager prototypes)
    {
        return Validate(
            prototypes.EnumeratePrototypes<SWCommodityCategoryPrototype>(),
            prototypes.EnumeratePrototypes<SWProductionFacilityPrototype>(),
            prototypes.EnumeratePrototypes<SWProductionConditionPrototype>(),
            prototypes.EnumeratePrototypes<SWCommodityPrototype>(),
            prototypes.EnumeratePrototypes<SWProductionRecipePrototype>(),
            prototypes.EnumeratePrototypes<SWCommodityDemandPrototype>());
    }

    public static IReadOnlyList<string> Validate(
        IEnumerable<SWCommodityCategoryPrototype> categories,
        IEnumerable<SWProductionFacilityPrototype> facilities,
        IEnumerable<SWProductionConditionPrototype> conditions,
        IEnumerable<SWCommodityPrototype> commodities,
        IEnumerable<SWProductionRecipePrototype> recipes,
        IEnumerable<SWCommodityDemandPrototype> demands)
    {
        var errors = new List<string>();
        var categoryIndex = Index(categories, errors);
        var facilityIndex = Index(facilities, errors);
        var conditionIndex = Index(conditions, errors);
        var commodityIndex = Index(commodities, errors);
        var recipeIndex = Index(recipes, errors);
        var demandIndex = Index(demands, errors);

        foreach (var item in categoryIndex.Values)
        {
            RequireText(item.ID, nameof(item.Name), item.Name, errors);
            RequireNonNegative(item.ID, nameof(item.Order), item.Order, errors);
        }
        foreach (var item in facilityIndex.Values)
            RequireText(item.ID, nameof(item.Name), item.Name, errors);
        foreach (var item in conditionIndex.Values)
            RequireText(item.ID, nameof(item.Name), item.Name, errors);

        foreach (var item in commodityIndex.Values)
        {
            RequireText(item.ID, nameof(item.Name), item.Name, errors);
            RequireText(item.ID, nameof(item.Description), item.Description, errors);
            RequireText(item.ID, nameof(item.Shortage), item.Shortage, errors);
            RequireReference(item.ID, item.Category, categoryIndex, errors);
            RequireNonNegative(item.ID, nameof(item.Order), item.Order, errors);
            RequireNonNegative(item.ID, nameof(item.CargoSlots), item.CargoSlots, errors);
            if (!Enum.IsDefined(item.Storage) || !Enum.IsDefined(item.Use) || !Enum.IsDefined(item.Regulation))
                errors.Add($"Commodity {item.ID} has an undefined storage, use or regulation value.");
            if (item.Storage == SWCommodityStorage.Commissioning)
            {
                if (item.CargoSlots != 0 || item.Use != SWCommodityUse.Asset)
                    errors.Add($"Commissioned commodity {item.ID} must be an asset with zero cargo slots.");
            }
            else if (item.CargoSlots == 0)
                errors.Add($"Commodity {item.ID} needs positive cargo slots unless it is commissioned.");
        }

        // The default string equality is ordinal and is supported by the client sandbox.
        var produced = new HashSet<string>();
        var consumed = new HashSet<string>();
        var dependencies = commodityIndex.Keys.ToDictionary(id => id, _ => new HashSet<string>());
        foreach (var item in recipeIndex.Values)
        {
            RequireText(item.ID, nameof(item.Name), item.Name, errors);
            RequireReference(item.ID, item.Facility, facilityIndex, errors);
            RequirePositive(item.ID, nameof(item.Hours), item.Hours, errors);
            RequireNonNegative(item.ID, nameof(item.Power), item.Power, errors);
            RequireNonNegative(item.ID, nameof(item.Labor), item.Labor, errors);
            RequireNonNegative(item.ID, nameof(item.Order), item.Order, errors);
            if (item.Outputs.Count == 0)
                errors.Add($"Recipe {item.ID} has no outputs.");
            ValidateAmounts(item.ID, item.Inputs, commodityIndex, errors);
            ValidateAmounts(item.ID, item.Outputs, commodityIndex, errors);
            ValidateAmounts(item.ID, item.ResourceCosts, conditionIndex, errors);

            var requirements = new HashSet<ProtoId<SWProductionConditionPrototype>>();
            foreach (var requirement in item.Requirements)
            {
                RequireReference(item.ID, requirement, conditionIndex, errors);
                if (!requirements.Add(requirement))
                    errors.Add($"Recipe {item.ID} repeats requirement {requirement}.");
            }
            foreach (var resource in item.ResourceCosts.Keys)
            {
                if (!requirements.Contains(resource))
                    errors.Add($"Recipe {item.ID} consumes resource {resource} without requiring access to it.");
            }

            foreach (var output in item.Outputs.Keys)
                produced.Add(output.Id);
            foreach (var input in item.Inputs.Keys)
            {
                consumed.Add(input.Id);
                if (!dependencies.TryGetValue(input.Id, out var outputs))
                    continue;
                foreach (var output in item.Outputs.Keys)
                {
                    if (commodityIndex.ContainsKey(output.Id))
                        outputs.Add(output.Id);
                }
            }
        }

        foreach (var item in demandIndex.Values)
        {
            RequireText(item.ID, nameof(item.Name), item.Name, errors);
            RequireText(item.ID, nameof(item.Basis), item.Basis, errors);
            if (!Enum.IsDefined(item.Mode))
                errors.Add($"Demand {item.ID} has an undefined mode.");
            if (item.Goods.Count == 0)
                errors.Add($"Demand {item.ID} has no goods.");
            ValidateAmounts(item.ID, item.Goods, commodityIndex, errors);
            // Derived demand scores goods already consumed through another profile.
            if (item.Mode != SWCommodityDemandMode.Derived)
            {
                foreach (var good in item.Goods.Keys)
                    consumed.Add(good.Id);
            }
        }

        foreach (var id in commodityIndex.Keys)
        {
            if (!produced.Contains(id))
                errors.Add($"Commodity {id} has no production source.");
            if (!consumed.Contains(id))
                errors.Add($"Commodity {id} has no production, consumption or commissioning sink.");
        }

        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        foreach (var id in dependencies.Keys)
            Visit(id);
        return errors;

        void Visit(string id)
        {
            if (visited.Contains(id))
                return;
            if (!visiting.Add(id))
            {
                errors.Add($"Commodity production dependencies contain a cycle through {id}.");
                return;
            }
            foreach (var output in dependencies[id])
                Visit(output);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> values, List<string> errors) where T : IPrototype
    {
        var result = new Dictionary<string, T>();
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item.ID))
                errors.Add($"{typeof(T).Name} has an empty ID.");
            else if (!result.TryAdd(item.ID, item))
                errors.Add($"{typeof(T).Name} repeats ID {item.ID}.");
        }
        return result;
    }

    private static void RequireReference<T>(string owner, ProtoId<T> reference, Dictionary<string, T> index,
        List<string> errors) where T : class, IPrototype
    {
        if (string.IsNullOrEmpty(reference.Id) || !index.ContainsKey(reference.Id))
            errors.Add($"{owner} references missing {typeof(T).Name} {reference}.");
    }

    private static void ValidateAmounts<T>(string owner, Dictionary<ProtoId<T>, float> amounts,
        Dictionary<string, T> index, List<string> errors) where T : class, IPrototype
    {
        foreach (var (id, amount) in amounts)
        {
            RequireReference(owner, id, index, errors);
            RequirePositive(owner, id.Id, amount, errors);
        }
    }

    private static void RequireText(string owner, string field, string value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{owner} has empty {field}.");
    }

    private static void RequirePositive(string owner, string field, float value, List<string> errors)
    {
        if (!float.IsFinite(value) || value <= 0)
            errors.Add($"{owner} {field} must be finite and positive.");
    }

    private static void RequireNonNegative(string owner, string field, float value, List<string> errors)
    {
        if (!float.IsFinite(value) || value < 0)
            errors.Add($"{owner} {field} must be finite and non-negative.");
    }
}
