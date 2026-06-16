using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._SW.EntityEffects.Effects.Solution;

/// <summary>
/// Sets the amount of a reagent in the bloodstream solution to a specific value.
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class SetReagentEntityEffectSystem : EntityEffectSystem<BloodstreamComponent, SetReagent>
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    protected override void Effect(Entity<BloodstreamComponent> entity, ref EntityEffectEvent<SetReagent> args)
    {
        if (!_solutionContainer.ResolveSolution(entity.Owner, entity.Comp.BloodSolutionName, ref entity.Comp.BloodSolution))
            return;

        var amount = FixedPoint2.Max(args.Effect.Amount, FixedPoint2.Zero);
        var reagent = args.Effect.Reagent;
        var solutionEntity = entity.Comp.BloodSolution.Value;
        var solution = solutionEntity.Comp.Solution;
        var current = solution.GetTotalPrototypeQuantity(reagent);

        if (current == amount)
            return;

        if (current < amount)
        {
            _solutionContainer.TryAddReagent(solutionEntity, reagent, amount - current);
            return;
        }

        var removed = solution.RemoveReagent(reagent, current - amount, ignoreReagentData: true);
        if (removed > FixedPoint2.Zero)
            _solutionContainer.UpdateChemicals(solutionEntity);
    }
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class SetReagent : EntityEffectBase<SetReagent>
{
    /// <summary>
    ///     The reagent ID to set.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>
    ///     The amount the reagent should have in the solution.
    /// </summary>
    [DataField(required: true)]
    public FixedPoint2 Amount;

    public override bool Scaling => false;
}
