using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.Lathe;
using Content.Shared.Stacks;
using Robust.Shared.Map;
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

namespace Content.Server._SW.Lathe;
public sealed class SWLatheSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PowerChargeSystem _chargeSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWLatheComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<SWLatheComponent> ent, ref StartCollideEvent args)
    {
        if (!_chargeSystem.IsPowered(ent, EntityManager))
        {
            return;
        }

        if (!args.OurFixtureId.StartsWith(ent.Comp.FixtureInPrefix))
        {
            return;
        }

        if (CanUseInAnyRecipe(ent.Comp, args.OtherEntity) == null)
        {
            return;
        }

        if (TryAddMaterial(ent, args.OtherEntity))
        {
            Del(args.OtherEntity);
        }
    }

    public List<SWLatheRecipePrototype> GetRecipes(SWLatheComponent comp)
    {
        List<SWLatheRecipePrototype> protos = new List<SWLatheRecipePrototype>();

        foreach (var id in comp.Recipes)
        {
            protos.Add(_prototype.Index(id));
        }
        return protos;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWLatheComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_chargeSystem.IsPowered(uid, EntityManager))
            {
                continue;
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

    public void FinishProduction(Entity<SWLatheComponent> ent)
    {
        DebugTools.Assert(ent.Comp.CurrentRecipeProducting != null);
        var coords = new EntityCoordinates(ent, ent.Comp.SpawnPointOut);
        foreach (var (id, amount) in ent.Comp.CurrentRecipeProducting.Result)
        {
            for (int _ = 0; _ < amount; _++)
            {
                Spawn(id, coords);
            }
        }
        ent.Comp.CurrentRecipeProducting = null;
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
        ent.Comp.ProductionEndTime = _timing.CurTime + TimeSpan.FromSeconds(recipe.ProduceTime);
        return true;
    }

    public SWLatheRecipePrototype? GetRecipeSatisfied(Entity<SWLatheComponent> ent)
    {
        var availableRecipes = GetRecipes(ent.Comp);

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
}
