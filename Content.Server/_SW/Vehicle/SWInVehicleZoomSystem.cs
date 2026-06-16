using Content.Server.Movement.Systems;
using Content.Shared._SW.Vehicle;
using Content.Shared.Camera;
using Content.Shared.Movement.Components;
using System.Numerics;

namespace Content.Server._SW.Vehicle;

public sealed class SWInVehicleZoomSystem : EntitySystem
{
    [Dependency] private readonly ContentEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWInVehicleZoomComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWInVehicleZoomComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWInVehicleZoomComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWInVehicleZoomComponent, SWUserExitVehicleEvent>(OnExit);
        SubscribeLocalEvent<SWInVehicleZoomComponent, GetEyePvsScaleEvent>(OnGetPvsScale);
    }

    private void OnStartup(Entity<SWInVehicleZoomComponent> ent, ref ComponentStartup args)
    {
        if (HasComp<SWInVehicleComponent>(ent))
            Apply(ent);
    }

    private void OnEnter(Entity<SWInVehicleZoomComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        Apply(ent);
    }

    private void OnExit(Entity<SWInVehicleZoomComponent> ent, ref SWUserExitVehicleEvent args)
    {
        Restore(ent);
    }

    private void OnShutdown(Entity<SWInVehicleZoomComponent> ent, ref ComponentShutdown args)
    {
        Restore(ent);
    }

    private void OnGetPvsScale(Entity<SWInVehicleZoomComponent> ent, ref GetEyePvsScaleEvent args)
    {
        if (!ent.Comp.Applied)
            return;

        args.Scale += MathF.Max(0f, ent.Comp.PvsScale - 1f);
    }

    private void Apply(Entity<SWInVehicleZoomComponent> ent)
    {
        if (ent.Comp.Applied ||
            !TryComp<ContentEyeComponent>(ent, out var contentEye))
        {
            return;
        }

        ent.Comp.PreviousZoom = contentEye.TargetZoom;
        ent.Comp.PreviousMaxZoom = contentEye.MaxZoom;
        ent.Comp.Applied = true;

        _eye.SetMaxZoom(ent, Vector2.Max(contentEye.MaxZoom, ent.Comp.Zoom), contentEye);
        _eye.SetZoom(ent, ent.Comp.Zoom, eye: contentEye);
        _eye.UpdatePvsScale(ent, contentEye);
    }

    private void Restore(Entity<SWInVehicleZoomComponent> ent)
    {
        if (!ent.Comp.Applied || TerminatingOrDeleted(ent))
            return;

        ent.Comp.Applied = false;

        if (TryComp<ContentEyeComponent>(ent, out var contentEye))
        {
            if (ent.Comp.PreviousMaxZoom is { } previousMaxZoom)
                _eye.SetMaxZoom(ent, previousMaxZoom, contentEye);

            if (ent.Comp.PreviousZoom is { } previousZoom)
                _eye.SetZoom(ent, previousZoom, eye: contentEye);

            _eye.UpdatePvsScale(ent, contentEye);
        }

        ent.Comp.PreviousZoom = null;
        ent.Comp.PreviousMaxZoom = null;
    }
}
