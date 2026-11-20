using Content.Server.Power.Components;
using Content.Server._SW.GlobalWar.Market;
using Content.Shared._SW.GlobalWar;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.Lathe;
using Content.Shared.Lathe;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.SWLathe;
public sealed class SWLatheSystem : EntitySystem
{
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private readonly FixtureSystem _fixturesChangeSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWLatheComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<SWLatheComponent, SWLatheSelectRecipeMessage>(OnSelectRecipe);
    }

    private void OnBeforeUiOpen(Entity<SWLatheComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnSelectRecipe(Entity<SWLatheComponent> ent, ref SWLatheSelectRecipeMessage args)
    {
        ent.Comp.ProductionDisabled = args.ProductionDisabled;

        if (string.IsNullOrEmpty(args.Recipe))
        {
            ent.Comp.SelectedRecipe = null;
            UpdateUserInterface(ent);
            return;
        }

        var selectedRecipe = args.Recipe;
        if (!ent.Comp.Recipes.Any(recipe => recipe == selectedRecipe))
            return;

        ent.Comp.SelectedRecipe = selectedRecipe;
        UpdateUserInterface(ent);
    }

    public List<SWLatheRecipePrototype> GetRecipes(SWLatheComponent comp)
    {
        var protos = new List<SWLatheRecipePrototype>();

        foreach (var id in comp.Recipes)
        {
            protos.Add(_prototype.Index(id));
        }
        return protos;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWLatheComponent, FixturesComponent>();

        while (query.MoveNext(out var uid, out var comp, out var fixComp))
        {
            if (!Transform(uid).Anchored || !_chargeSystem.IsPowered(uid, EntityManager))
            {
                if (comp.CurrentRecipeProducting != null)
                {
                    comp.ProductionStartTime += TimeSpan.FromSeconds(frameTime);
                    comp.ProductionEndTime += TimeSpan.FromSeconds(frameTime);
                }
                continue;
            }

            foreach (var otherEntity in GetEntitiesInInputFixtures(uid, comp, fixComp))
            {
                if (CanUseInAnyRecipe(comp, otherEntity) == null)
                {
                    continue;
                }

                if (TryAddMaterial(new Entity<SWLatheComponent>(uid, comp), otherEntity))
                {
                    UpdateUserInterface((uid, comp));
                }
            }

            if (comp.CurrentRecipeProducting != null
                && comp.ProductionEndTime <= _timing.CurTime)
            {
                FinishProduction(new Entity<SWLatheComponent>(uid, comp));
            }

            if (comp.CurrentRecipeProducting == null && !comp.ProductionDisabled)
            {
                TryStartProducingAnything(new Entity<SWLatheComponent>(uid, comp));
            }
        }
    }

    private List<EntityUid> GetEntitiesInInputFixtures(EntityUid latheUid, SWLatheComponent comp, FixturesComponent fixComp)
    {
        var res = new List<EntityUid>();
        var xform = Comp<TransformComponent>(latheUid);
        foreach (var (fixtureId, fixture) in fixComp.Fixtures)
        {
            if (!fixtureId.StartsWith(comp.FixtureInPrefix))
            {
                continue;
            }

            foreach (var (_, contact) in fixture.Contacts)
            {
                res.Add(contact.OtherEnt(latheUid));
            }
        }

        return res;
    }

    public void FinishProduction(Entity<SWLatheComponent> ent)
    {
        DebugTools.Assert(ent.Comp.CurrentRecipeProducting != null);
        var coords = _transformSystem.ToMapCoordinates(new EntityCoordinates(ent, ent.Comp.SpawnPointOut));
        foreach (var (id, amount) in ent.Comp.CurrentRecipeProducting.Result)
        {
            for (var _ = 0; _ < amount; _++)
            {
                Spawn(id, coords);
            }
        }
        ent.Comp.CurrentRecipeProducting = null;
        UpdateUserInterface(ent);
    }

    public string? CanUseInAnyRecipe(SWLatheComponent comp, EntityUid material)
    {
        if (TerminatingOrDeleted(material))
            return null;
        var prototype = Prototype(material);
        if (prototype == null)
            return null;
        var stack = CompOrNull<StackComponent>(material);
        if (stack is { Unlimited: true } || stack is { Count: <= 0 })
            return null;
        foreach (var recipe in GetRecipes(comp))
        foreach (var ingredient in recipe.Materials.Keys)
        {
            if (prototype.ID == ingredient)
                return ingredient;
            if (stack != null && _prototype.Index<EntityPrototype>(ingredient)
                    .TryGetComponent<StackComponent>(out var expected, EntityManager.ComponentFactory) &&
                stack.StackTypeId == expected.StackTypeId)
                return ingredient;
        }
        return null;
    }

    /// <summary>Consumes only the quantity accepted into the machine; any excess stays on the input.</summary>
    public bool TryAddMaterial(Entity<SWLatheComponent> ent, EntityUid material)
    {
        if (CanUseInAnyRecipe(ent.Comp, material) is not { } ingredient)
            return false;
        var stored = ent.Comp.StashedMaterials.GetValueOrDefault(ingredient);
        var quantity = Math.Min(_stacks.GetCount(material), ent.Comp.MaxMaterialAmount - stored);
        if (quantity <= 0)
            return false;
        if (TryComp<StackComponent>(material, out var stack))
        {
            if (!_stacks.TryUse((material, stack), quantity))
                return false;
        }
        else
            Del(material);
        ent.Comp.StashedMaterials[ingredient] = stored + quantity;
        return true;
    }

    public bool TryStartProducingAnything(Entity<SWLatheComponent> ent)
    {
        if (ent.Comp.CurrentRecipeProducting != null)
        {
            return false;
        }

        var recipe = GetRecipeSatisfied(ent);

        if (recipe == null)
        {
            return false;
        }

        foreach (var (id, amount) in recipe.Materials)
        {
            ent.Comp.StashedMaterials[id] -= amount;
        }

        ent.Comp.CurrentRecipeProducting = recipe;
        ent.Comp.ProductionStartTime = _timing.CurTime;
        ent.Comp.ProductionEndTime = _timing.CurTime + TimeSpan.FromSeconds(recipe.ProduceTime);
        UpdateUserInterface(ent);
        return true;
    }

    public SWLatheRecipePrototype? GetRecipeSatisfied(Entity<SWLatheComponent> ent)
    {
        var availableRecipes = GetRecipes(ent.Comp);

        if (ent.Comp.SelectedRecipe != null)
        {
            var recipe = availableRecipes.FirstOrDefault(recipe => recipe.ID == ent.Comp.SelectedRecipe);
            if (recipe != null && IsRecipeSatisfied(ent.Comp.StashedMaterials, recipe))
                return recipe;

            return null;
        }

        foreach (var recipe in availableRecipes)
        {
            if (IsRecipeSatisfied(ent.Comp.StashedMaterials, recipe))
            {
                return recipe;
            }
        }
        return null;
    }

    public bool IsRecipeSatisfied(Dictionary<EntProtoId, int> stashedMaterials, SWLatheRecipePrototype recipeProto)
    {
        foreach (var (id, amount) in recipeProto.Materials)
        {
            if (!stashedMaterials.TryGetValue(id, out var value))
            {
                return false;
            }
            if (amount > value)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateUserInterface(Entity<SWLatheComponent> ent)
    {
        var recipes = GetRecipes(ent.Comp)
            .Select(recipe => new SWLatheRecipeInfo(
                recipe.ID,
                new Dictionary<string, int>(recipe.Materials),
                new Dictionary<string, int>(recipe.Result),
                recipe.ProduceTime))
            .ToList();

        var state = new SWLatheBoundUserInterfaceState(
            recipes,
            ent.Comp.StashedMaterials.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            ent.Comp.MaxMaterialAmount,
            ent.Comp.ProductionDisabled,
            ent.Comp.SelectedRecipe,
            ent.Comp.CurrentRecipeProducting?.ID,
            ent.Comp.CurrentRecipeProducting == null ? null : ent.Comp.ProductionStartTime,
            ent.Comp.CurrentRecipeProducting == null ? null : ent.Comp.ProductionEndTime);

        _uiSystem.SetUiState(ent.Owner, SWLatheUiKey.Key, state);
    }
}
