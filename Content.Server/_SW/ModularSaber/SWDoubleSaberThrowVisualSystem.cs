using Content.Shared._SW.ForceAbilities;
using Content.Shared._SW.ModularSaber;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Throwing;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWDoubleSaberThrowVisualComponent : Component
{
    public EntityUid? ThrowUser;
    public bool ThrowScope;
    public bool WasActive;
    public bool UnwieldedForThrow;
    public bool SuppressUnwieldDeactivation;
    public bool ThrowSucceeded;
    public bool RestoredForFlight;
}

public sealed class SWDoubleSaberThrowVisualSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, SWSaberThrowAttemptStartedEvent>(OnThrowStarted);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, SWSaberThrowAttemptFinishedEvent>(OnThrowFinished);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, ItemUnwieldedEvent>(OnUnwielded, before: [typeof(ItemToggleSystem)]);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, ItemToggleDeactivateAttemptEvent>(OnDeactivateAttempt);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, LandEvent>(OnLand, after: [typeof(SWAbilityThrowSwordSystem)]);
        SubscribeLocalEvent<BeingAttractedByForceComponent, ComponentShutdown>(OnAttractionShutdown);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, StopThrowEvent>(OnStopThrow);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
    }

    private void OnThrowStarted(Entity<SWDoubleSaberThrowVisualComponent> ent, ref SWSaberThrowAttemptStartedEvent args)
    {
        ent.Comp.ThrowScope = true;
        ent.Comp.ThrowUser = args.User;
        ent.Comp.WasActive = _toggle.IsActivated(ent.Owner) &&
                            TryComp<WieldableComponent>(ent.Owner, out var wieldable) && wieldable.Wielded;
        ent.Comp.UnwieldedForThrow = false;
        ent.Comp.SuppressUnwieldDeactivation = false;
        ent.Comp.ThrowSucceeded = false;
    }

    private void OnUnwielded(Entity<SWDoubleSaberThrowVisualComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (!ent.Comp.ThrowScope || !ent.Comp.WasActive || ent.Comp.ThrowUser != args.User ||
            !args.Force || !_toggle.IsActivated(ent.Owner))
            return;

        ent.Comp.UnwieldedForThrow = true;
        ent.Comp.SuppressUnwieldDeactivation = true;
    }

    private void OnDeactivateAttempt(Entity<SWDoubleSaberThrowVisualComponent> ent, ref ItemToggleDeactivateAttemptEvent args)
    {
        if (!ent.Comp.ThrowScope || !ent.Comp.SuppressUnwieldDeactivation || ent.Comp.ThrowUser != args.User)
            return;

        ent.Comp.SuppressUnwieldDeactivation = false;
        args.Cancelled = true;
        args.Silent = true;
    }

    private void OnThrown(Entity<SWDoubleSaberThrowVisualComponent> ent, ref ThrownEvent args)
    {
        if (!ent.Comp.ThrowScope || ent.Comp.ThrowUser != args.User)
            return;

        ent.Comp.ThrowSucceeded = true;
        ent.Comp.RestoredForFlight = ent.Comp.WasActive && ent.Comp.UnwieldedForThrow && _toggle.IsActivated(ent.Owner);
        _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, false);
    }

    private void OnThrowFinished(Entity<SWDoubleSaberThrowVisualComponent> ent, ref SWSaberThrowAttemptFinishedEvent args)
    {
        if (!ent.Comp.ThrowScope || ent.Comp.ThrowUser != args.User)
            return;

        var rollback = ent.Comp.WasActive && ent.Comp.UnwieldedForThrow && !ent.Comp.ThrowSucceeded;
        ent.Comp.ThrowScope = false;
        ent.Comp.ThrowUser = null;
        ent.Comp.WasActive = false;
        ent.Comp.UnwieldedForThrow = false;
        ent.Comp.SuppressUnwieldDeactivation = false;
        ent.Comp.ThrowSucceeded = false;
        if (rollback)
            _toggle.TryDeactivate(ent.Owner, predicted: false);
    }

    private void OnLand(Entity<SWDoubleSaberThrowVisualComponent> ent, ref LandEvent args)
    {
        if (!HasComp<BeingAttractedByForceComponent>(ent.Owner))
            EndFlight(ent);
    }

    private void OnStopThrow(Entity<SWDoubleSaberThrowVisualComponent> ent, ref StopThrowEvent args)
    {
        if (!HasComp<BeingAttractedByForceComponent>(ent.Owner))
            EndFlight(ent);
    }

    private void OnInserted(Entity<SWDoubleSaberThrowVisualComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        EndFlight(ent);
    }

    private void OnAttractionShutdown(Entity<BeingAttractedByForceComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SWDoubleSaberThrowVisualComponent>(ent.Owner, out var visual))
            return;
        if (TryComp<ThrownItemComponent>(ent.Owner, out var thrown) && !thrown.Landed)
            return;
        EndFlight((ent.Owner, visual));
    }

    private void EndFlight(Entity<SWDoubleSaberThrowVisualComponent> ent)
    {
        _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, false);
        if (!ent.Comp.RestoredForFlight)
            return;
        ent.Comp.RestoredForFlight = false;
        _toggle.TryDeactivate(ent.Owner, predicted: false);
    }
}
