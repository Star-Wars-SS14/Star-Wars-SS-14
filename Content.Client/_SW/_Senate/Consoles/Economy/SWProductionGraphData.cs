using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Shared._SW._Senate.Economy;
using Robust.Shared.Prototypes;

namespace Content.Client._SW._Senate.Consoles.Economy;

public enum SWProductionGraphMode : byte
{
    Chain,
    Recipe,
    Uses,
}

public enum SWProductionNodeKind : byte
{
    Commodity,
    Recipe,
    Demand,
    Condition,
}

public sealed record SWProductionNode(string Id, SWProductionNodeKind Kind, string ReferenceId,
    string Title, string Subtitle, int Column);

/// <summary>Recipe quantities are per cycle; demand labels retain the profile's coefficient semantics.</summary>
public sealed record SWProductionEdge(string From, string To, string Label);

public sealed record SWProductionGraphData(IReadOnlyList<SWProductionNode> Nodes,
    IReadOnlyList<SWProductionEdge> Edges, string FocusNodeId);

/// <summary>
/// A static catalogue graph, not a claim about installed factories or current material flows.
/// Shared ingredients and recipes have one node. Separate visited sets bound both traversal directions
/// by the catalogue even when an alternative recipe introduces a material cycle.
/// </summary>
public sealed partial class SWProductionGraphBuilder
{
    private readonly Dictionary<string, SWCommodityPrototype> _goods;
    private readonly Dictionary<string, SWProductionRecipePrototype> _recipes;
    private readonly Dictionary<string, SWProductionConditionPrototype> _conditions;
    private readonly Dictionary<string, SWProductionFacilityPrototype> _facilities;
    private readonly Dictionary<string, SWProductionRecipePrototype[]> _producers;
    private readonly Dictionary<string, SWProductionRecipePrototype[]> _consumers;
    private readonly Dictionary<string, SWCommodityDemandPrototype[]> _demands;
    private readonly SWCommodityDemandPrototype[] _allDemands;
    private readonly HashSet<string> _finiteConditions;

    public SWProductionGraphBuilder(IPrototypeManager prototypes) : this(
        prototypes.EnumeratePrototypes<SWCommodityPrototype>(),
        prototypes.EnumeratePrototypes<SWProductionRecipePrototype>(),
        prototypes.EnumeratePrototypes<SWCommodityDemandPrototype>(),
        prototypes.EnumeratePrototypes<SWProductionConditionPrototype>(),
        prototypes.EnumeratePrototypes<SWProductionFacilityPrototype>())
    {
    }

    /// <summary>Pure constructor for deterministic catalogue and cyclic-recipe tests.</summary>
    public SWProductionGraphBuilder(IEnumerable<SWCommodityPrototype> goods,
        IEnumerable<SWProductionRecipePrototype> recipes, IEnumerable<SWCommodityDemandPrototype> demands,
        IEnumerable<SWProductionConditionPrototype>? conditions = null,
        IEnumerable<SWProductionFacilityPrototype>? facilities = null)
    {
        _goods = goods.ToDictionary(g => g.ID);
        var orderedRecipes = recipes.OrderBy(r => r.Order).ThenBy(r => r.ID).ToArray();
        _recipes = orderedRecipes.ToDictionary(r => r.ID);
        _conditions = (conditions ?? Array.Empty<SWProductionConditionPrototype>()).ToDictionary(c => c.ID);
        _facilities = (facilities ?? Array.Empty<SWProductionFacilityPrototype>()).ToDictionary(f => f.ID);
        _producers = orderedRecipes.SelectMany(r => r.Outputs.Keys.Select(g => (Good: g.Id, Recipe: r)))
            .GroupBy(p => p.Good).ToDictionary(g => g.Key, g => g.Select(p => p.Recipe).ToArray());
        _consumers = orderedRecipes.SelectMany(r => r.Inputs.Keys.Select(g => (Good: g.Id, Recipe: r)))
            .GroupBy(p => p.Good).ToDictionary(g => g.Key, g => g.Select(p => p.Recipe).ToArray());
        _allDemands = demands.OrderBy(d => d.ID).ToArray();
        _demands = _allDemands
            .SelectMany(d => d.Goods.Keys.Select(g => (Good: g.Id, Demand: d)))
            .GroupBy(p => p.Good).ToDictionary(g => g.Key, g => g.Select(p => p.Demand).ToArray());
        _finiteConditions = orderedRecipes.SelectMany(r => r.ResourceCosts.Keys.Select(c => c.Id)).ToHashSet();
    }

    public SWProductionGraphData Build(string commodity, IReadOnlyDictionary<string, string> selectedRecipes,
        SWProductionGraphMode mode)
    {
        if (!_goods.ContainsKey(commodity))
            return new SWProductionGraphData(Array.Empty<SWProductionNode>(), Array.Empty<SWProductionEdge>(), string.Empty);

        var nodes = new Dictionary<string, SWProductionNode>();
        var edges = new Dictionary<(string From, string To), SWProductionEdge>();
        var upstream = new HashSet<string>();
        var downstream = new HashSet<string>();

        void AddGood(string id)
        {
            if (nodes.ContainsKey("good:" + id))
                return;
            if (!_goods.TryGetValue(id, out var good))
                return;
            nodes.Add("good:" + id, new SWProductionNode("good:" + id, SWProductionNodeKind.Commodity,
                id, good.Name, UseLabel(good.Use), 0));
        }

        void AddEdge(string from, string to, string label)
        {
            if (nodes.ContainsKey(from) && nodes.ContainsKey(to))
                edges[(from, to)] = new SWProductionEdge(from, to, label);
        }

        void AddCondition(string id)
        {
            if (nodes.ContainsKey("condition:" + id))
                return;
            var title = _conditions.TryGetValue(id, out var condition) ? condition.Name : id;
            var subtitle = _finiteConditions.Contains(id) ? "Конечный местный фонд" : "Условие размещения";
            nodes.Add("condition:" + id, new SWProductionNode("condition:" + id,
                SWProductionNodeKind.Condition, id, title, subtitle, 0));
        }

        void AddRecipe(SWProductionRecipePrototype recipe)
        {
            var id = "recipe:" + recipe.ID;
            if (nodes.ContainsKey(id))
                return;
            var facility = _facilities.TryGetValue(recipe.Facility.Id, out var definition) ? definition.Name : recipe.Facility.Id;
            nodes.Add(id, new SWProductionNode(id, SWProductionNodeKind.Recipe, recipe.ID, recipe.Name,
                $"{facility}\n{Number(recipe.Hours)} ч, мощность {Number(recipe.Power)}, труд {Number(recipe.Labor)}", 0));
            foreach (var (good, amount) in recipe.Inputs.OrderBy(p => p.Key.Id))
            {
                AddGood(good.Id);
                AddEdge("good:" + good.Id, id, $"{Number(amount)} за цикл");
            }
            foreach (var (good, amount) in recipe.Outputs.OrderBy(p => p.Key.Id))
            {
                AddGood(good.Id);
                AddEdge(id, "good:" + good.Id, $"{Number(amount)} за цикл");
            }
            foreach (var condition in recipe.Requirements.OrderBy(c => c.Id))
            {
                AddCondition(condition.Id);
                AddEdge("condition:" + condition.Id, id, "Условие");
            }
            foreach (var (condition, amount) in recipe.ResourceCosts.OrderBy(p => p.Key.Id))
            {
                AddCondition(condition.Id);
                // A debited prerequisite is one input with a finite cost, never a second free source.
                AddEdge("condition:" + condition.Id, id, $"Расход {Number(amount)} за цикл");
            }
        }

        SWProductionRecipePrototype? SelectedRecipe(string good)
        {
            if (!_producers.TryGetValue(good, out var alternatives) || alternatives.Length == 0)
                return null;
            if (selectedRecipes.TryGetValue(good, out var selected) && _recipes.TryGetValue(selected, out var chosen) &&
                chosen.Outputs.ContainsKey(good))
                return chosen;
            return alternatives[0];
        }

        void AddDemands(string good)
        {
            if (!_demands.TryGetValue(good, out var demandsForGood))
                return;
            foreach (var demand in demandsForGood)
            {
                var id = "demand:" + demand.ID;
                nodes.TryAdd(id, new SWProductionNode(id, SWProductionNodeKind.Demand, demand.ID,
                    demand.Name, DemandLabel(demand.Mode) + "\n" + demand.Basis, 0));
                // Show the complete basket or alternatives, without recursively expanding unrelated
                // co-consumed goods. In particular, kolto alone must never look like a full treatment.
                foreach (var (input, amount) in demand.Goods.OrderBy(p => p.Key.Id))
                {
                    AddGood(input.Id);
                    AddEdge("good:" + input.Id, id, DemandCoefficient(demand.Mode, amount));
                }
            }
        }

        AddGood(commodity);
        if (mode == SWProductionGraphMode.Recipe)
        {
            if (SelectedRecipe(commodity) is { } recipe)
                AddRecipe(recipe);
        }
        else
        {
            if (mode == SWProductionGraphMode.Chain)
            {
                var pending = new Stack<string>();
                pending.Push(commodity);
                while (pending.TryPop(out var good))
                {
                    if (!upstream.Add(good) || SelectedRecipe(good) is not { } recipe)
                        continue;
                    AddRecipe(recipe);
                    foreach (var input in recipe.Inputs.Keys.OrderByDescending(g => g.Id))
                        pending.Push(input.Id);
                }
            }

            var uses = new Queue<string>();
            uses.Enqueue(commodity);
            while (uses.TryDequeue(out var good))
            {
                if (!downstream.Add(good))
                    continue;
                AddGood(good);
                AddDemands(good);
                if (!_consumers.TryGetValue(good, out var consumingRecipes))
                    continue;
                foreach (var recipe in consumingRecipes)
                {
                    AddRecipe(recipe);
                    foreach (var output in recipe.Outputs.Keys.OrderBy(g => g.Id))
                        uses.Enqueue(output.Id);
                }
            }
        }

        return Arrange(nodes, edges.Values, "good:" + commodity);
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string UseLabel(SWCommodityUse use) => use switch
    {
        SWCommodityUse.Asset => "Ввод в строй",
        SWCommodityUse.Durable => "Оборудование",
        SWCommodityUse.Consumable => "Расходный товар",
        _ => "Сырьё и компоненты",
    };

    private static string DemandLabel(SWCommodityDemandMode mode) => mode switch
    {
        SWCommodityDemandMode.Bundle => "Совместное потребление",
        SWCommodityDemandMode.Commission => "Ввод в эксплуатацию",
        SWCommodityDemandMode.Derived => "Оценка потреблённых товаров",
        SWCommodityDemandMode.Substitute => "Взаимозаменяемые товары",
        _ => "Расходование",
    };

    private static string DemandCoefficient(SWCommodityDemandMode mode, float amount) => mode switch
    {
        SWCommodityDemandMode.Bundle => $"×{Number(amount)} в комплекте",
        SWCommodityDemandMode.Commission => $"Ввод ×{Number(amount)}",
        SWCommodityDemandMode.Derived => $"Оценка ×{Number(amount)}",
        SWCommodityDemandMode.Substitute => $"Замещение ×{Number(amount)}",
        _ => $"Норма ×{Number(amount)}",
    };
}
