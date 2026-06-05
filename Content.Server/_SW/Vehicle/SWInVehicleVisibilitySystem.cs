using Content.Shared._SW.Vehicle;
using Content.Shared.Eye;
using Robust.Shared.GameObjects;

namespace Content.Server._SW.Vehicle;

public sealed class SWInVehicleVisibilitySystem : EntitySystem
{
    [Dependency] private readonly SharedVisibilitySystem _visibility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWInVehicleVisibilityComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWInVehicleVisibilityComponent, SWUserExitVehicleEvent>(OnExit);
        SubscribeLocalEvent<SWInVehicleVisibilityComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnEnter(Entity<SWInVehicleVisibilityComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        if (ent.Comp.Hidden)
            return;

        ent.Comp.HadVisibilityComponent = TryComp<VisibilityComponent>(ent, out var visibility);
        ent.Comp.PreviousLayer = visibility?.Layer ?? (ushort) VisibilityFlags.Normal;

        visibility ??= EnsureComp<VisibilityComponent>(ent);
        _visibility.SetLayer((ent.Owner, visibility), ent.Comp.HiddenLayer);
        ent.Comp.Hidden = true;
    }

    private void OnExit(Entity<SWInVehicleVisibilityComponent> ent, ref SWUserExitVehicleEvent args)
    {
        RestoreVisibility(ent);
    }

    private void OnShutdown(Entity<SWInVehicleVisibilityComponent> ent, ref ComponentShutdown args)
    {
        RestoreVisibility(ent);
    }

    private void RestoreVisibility(Entity<SWInVehicleVisibilityComponent> ent)
    {
        if (!ent.Comp.Hidden || TerminatingOrDeleted(ent))
            return;

        if (!TryComp<VisibilityComponent>(ent, out var visibility))
        {
            ent.Comp.Hidden = false;
            return;
        }

        _visibility.SetLayer((ent.Owner, visibility), ent.Comp.PreviousLayer);

        if (!ent.Comp.HadVisibilityComponent)
            RemCompDeferred<VisibilityComponent>(ent);

        ent.Comp.Hidden = false;
    }
}
