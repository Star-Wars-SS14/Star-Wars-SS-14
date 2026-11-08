using Content.Shared._SW.ModularSaber;
using Content.Shared._SW.ForceAbilities;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Throwing;
using Content.Shared.Wieldable;
using Robust.Shared.Timing;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWDoubleSaberThrowVisualComponent : Component
{
    public EntityUid? LastUser;
    public TimeSpan LastUnwieldTime;
    public bool WasActive;
}

public sealed class SWDoubleSaberThrowVisualSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, ItemUnwieldedEvent>(OnUnwielded, before: [typeof(ItemToggleSystem)]);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, LandEvent>(OnLand, after: [typeof(SWAbilityThrowSwordSystem)]);
        SubscribeLocalEvent<BeingAttractedByForceComponent, ComponentShutdown>(OnAttractionShutdown);
        SubscribeLocalEvent<SWDoubleSaberThrowVisualComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnUnwielded(Entity<SWDoubleSaberThrowVisualComponent> ent, ref ItemUnwieldedEvent args)
    {
        ent.Comp.WasActive = _toggle.IsActivated(ent.Owner);
        ent.Comp.LastUnwieldTime = _timing.CurTime;
        ent.Comp.LastUser = args.User;
    }

    private void OnThrown(Entity<SWDoubleSaberThrowVisualComponent> ent, ref ThrownEvent args)
    {
        var show = ent.Comp.WasActive && ent.Comp.LastUser == args.User && ent.Comp.LastUnwieldTime == _timing.CurTime;
        ent.Comp.WasActive = false;
        _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, show);
    }

    private void OnLand(Entity<SWDoubleSaberThrowVisualComponent> ent, ref LandEvent args)
    {
        if (!HasComp<BeingAttractedByForceComponent>(ent.Owner))
            _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, false);
    }

    private void OnStopThrow(Entity<SWDoubleSaberThrowVisualComponent> ent, ref StopThrowEvent args)
    {
        if (!HasComp<BeingAttractedByForceComponent>(ent.Owner))
            _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, false);
    }

    private void OnAttractionShutdown(Entity<BeingAttractedByForceComponent> ent, ref ComponentShutdown args)
    {
        if (!HasComp<SWDoubleSaberThrowVisualComponent>(ent.Owner))
            return;

        if (TryComp<ThrownItemComponent>(ent.Owner, out var thrown) && !thrown.Landed)
            return;

        _appearance.SetData(ent.Owner, SWDoubleSaberThrowVisuals.Flying, false);
    }
}
