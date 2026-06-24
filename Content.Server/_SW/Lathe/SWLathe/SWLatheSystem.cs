using Content.Server.Power.Components;
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
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private readonly FixtureSystem _fixturesChangeSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWLatheComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<SWLatheComponent, SWLatheSelectRecipeMessage>(OnSelectRecipe);
        //SubscribeLocalEvent<SWLatheComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnBeforeUiOpen(Entity<SWLatheComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnSelectRecipe(Entity<SWLatheComponent> ent, ref SWLatheSelectRecipeMessage args)
    {
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

    //private void OnStartCollide(Entity<SWLatheComponent> ent, ref StartCollideEvent args)
    //{
    //    if (!_chargeSystem.IsPowered(ent, EntityManager))
    //    {
    //        return;
    //    }

    //    if (!args.OurFixtureId.StartsWith(ent.Comp.FixtureInPrefix))
    //    {
    //        return;
    //    }

    //    if (CanUseInAnyRecipe(ent.Comp, args.OtherEntity) == null)
    //    {
    //        return;
    //    }

    //    if (TryAddMaterial(ent, args.OtherEntity))
    //    {
    //        Del(args.OtherEntity);
    //    }
    //}

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
            if (!_chargeSystem.IsPowered(uid, EntityManager))
            {
                continue;
            }

            //var fix = fixComp.

            foreach (var otherEntity in GetEntitiesInInputFixtures(uid, comp, fixComp))
            {
                if (CanUseInAnyRecipe(comp, otherEntity) == null)
                {
                    continue;
                }

                if (TryAddMaterial(new Entity<SWLatheComponent>(uid, comp), otherEntity))
                {
                    Del(otherEntity);
                    UpdateUserInterface((uid, comp));
                }
            }

            if (comp.CurrentRecipeProducting != null
                && comp.ProductionEndTime <= _timing.CurTime)
            {
                FinishProduction(new Entity<SWLatheComponent>(uid, comp));
            }

            if (comp.CurrentRecipeProducting == null)
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
        var coords = new EntityCoordinates(ent, ent.Comp.SpawnPointOut);
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
        var protos = GetRecipes(comp);

        var matProto = Prototype(material);
        if (matProto == null)
        {
            return null;
        }

        foreach (var proto in protos)
        {
            if (proto.Materials.Keys.Contains(matProto.ID))
            {
                return matProto.ID;
            }
        }

        return null;
    }

    public bool TryAddMaterial(Entity<SWLatheComponent> ent, EntityUid material)
    {
        var matProto = Prototype(material);
        if (matProto == null)
        {
            return false;
        }
        if (ent.Comp.StashedMaterials.TryGetValue(matProto, out var amount))
        {
            if (amount < ent.Comp.MaxMaterialAmount)
            {
                ent.Comp.StashedMaterials[matProto]++;
                return true;
            }
        }
        else
        {
            ent.Comp.StashedMaterials[matProto] = 1;
            return true;
        }

        return false;
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
            ent.Comp.SelectedRecipe,
            ent.Comp.CurrentRecipeProducting?.ID,
            ent.Comp.CurrentRecipeProducting == null ? null : ent.Comp.ProductionStartTime,
            ent.Comp.CurrentRecipeProducting == null ? null : ent.Comp.ProductionEndTime);

        _uiSystem.SetUiState(ent.Owner, SWLatheUiKey.Key, state);
    }
}
