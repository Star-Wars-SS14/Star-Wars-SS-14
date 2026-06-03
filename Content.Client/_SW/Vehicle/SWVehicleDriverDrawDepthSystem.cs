using Content.Shared._SW.Vehicle;
using Robust.Client.GameObjects;

namespace Content.Client._SW.Vehicle;

public sealed class SWVehicleDriverDrawDepthSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWVehicleDriverDrawDepthComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWVehicleDriverDrawDepthComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWVehicleDriverDrawDepthComponent, AfterAutoHandleStateEvent>(OnDrawDepthAfterState);
        SubscribeLocalEvent<SWInVehicleComponent, ComponentStartup>(OnInVehicleStartup);
        SubscribeLocalEvent<SWInVehicleComponent, ComponentShutdown>(OnInVehicleShutdown);
        SubscribeLocalEvent<SWInVehicleComponent, AfterAutoHandleStateEvent>(OnInVehicleAfterState);
        SubscribeLocalEvent<SWVehicleDriverDrawDepthComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWVehicleDriverDrawDepthComponent, SWUserExitVehicleEvent>(OnExit);
    }

    private void OnStartup(Entity<SWVehicleDriverDrawDepthComponent> ent, ref ComponentStartup args)
    {
        TryApply(ent);
    }

    private void OnShutdown(Entity<SWVehicleDriverDrawDepthComponent> ent, ref ComponentShutdown args)
    {
        Restore(ent);
    }

    private void OnDrawDepthAfterState(Entity<SWVehicleDriverDrawDepthComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        TryApply(ent);
    }

    private void OnInVehicleStartup(Entity<SWInVehicleComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<SWVehicleDriverDrawDepthComponent>(ent, out var drawDepth))
            TryApply((ent.Owner, drawDepth));
    }

    private void OnInVehicleShutdown(Entity<SWInVehicleComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SWVehicleDriverDrawDepthComponent>(ent, out var drawDepth))
            Restore((ent.Owner, drawDepth));
    }

    private void OnInVehicleAfterState(Entity<SWInVehicleComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<SWVehicleDriverDrawDepthComponent>(ent, out var drawDepth))
            TryApply((ent.Owner, drawDepth));
    }

    private void OnEnter(Entity<SWVehicleDriverDrawDepthComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        Apply(ent, args.Vehicle);
    }

    private void OnExit(Entity<SWVehicleDriverDrawDepthComponent> ent, ref SWUserExitVehicleEvent args)
    {
        Restore(ent);
    }

    private void TryApply(Entity<SWVehicleDriverDrawDepthComponent> ent)
    {
        if (!TryComp<SWInVehicleComponent>(ent, out var inVehicle) ||
            !TryComp<SWVehicleComponent>(inVehicle.Vehicle, out var vehicle))
        {
            return;
        }

        Apply(ent, (inVehicle.Vehicle, vehicle));
    }

    private void Apply(Entity<SWVehicleDriverDrawDepthComponent> ent, Entity<SWVehicleComponent> vehicle)
    {
        if (!TryComp<SpriteComponent>(ent, out var driverSprite))
            return;

        ent.Comp.OriginalDrawDepth ??= driverSprite.DrawDepth;

        var drawDepth = ent.Comp.FallbackDrawDepth;
        if (TryComp<SpriteComponent>(vehicle, out var vehicleSprite))
            drawDepth = vehicleSprite.DrawDepth + ent.Comp.VehicleDrawDepthOffset;

        _sprite.SetDrawDepth((ent.Owner, driverSprite), drawDepth);
    }

    private void Restore(Entity<SWVehicleDriverDrawDepthComponent> ent)
    {
        if (ent.Comp.OriginalDrawDepth is not { } originalDrawDepth ||
            !TryComp<SpriteComponent>(ent, out var driverSprite))
        {
            return;
        }

        _sprite.SetDrawDepth((ent.Owner, driverSprite), originalDrawDepth);
        ent.Comp.OriginalDrawDepth = null;
    }
}
