using Content.Shared._SW.Vehicle;
using Robust.Client.GameObjects;

namespace Content.Client._SW.Vehicle;

public sealed class SWInVehicleVisibilitySystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWInVehicleVisibilityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SWInVehicleVisibilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SWInVehicleVisibilityComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWInVehicleVisibilityComponent, SWUserExitVehicleEvent>(OnExit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWInVehicleVisibilityComponent, SWInVehicleComponent>();
        while (query.MoveNext(out var uid, out var visibility, out _))
        {
            Apply((uid, visibility));
        }

        var visibilityQuery = EntityQueryEnumerator<SWInVehicleVisibilityComponent>();
        while (visibilityQuery.MoveNext(out var uid, out var visibility))
        {
            if (visibility.Hidden && !HasComp<SWInVehicleComponent>(uid))
                Restore((uid, visibility));
        }
    }

    private void OnStartup(Entity<SWInVehicleVisibilityComponent> ent, ref ComponentStartup args)
    {
        TryApply(ent);
    }

    private void OnShutdown(Entity<SWInVehicleVisibilityComponent> ent, ref ComponentShutdown args)
    {
        Restore(ent);
    }

    private void OnEnter(Entity<SWInVehicleVisibilityComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        Apply(ent);
    }

    private void OnExit(Entity<SWInVehicleVisibilityComponent> ent, ref SWUserExitVehicleEvent args)
    {
        Restore(ent);
    }

    private void TryApply(Entity<SWInVehicleVisibilityComponent> ent)
    {
        if (HasComp<SWInVehicleComponent>(ent))
            Apply(ent);
    }

    private void Apply(Entity<SWInVehicleVisibilityComponent> ent)
    {
        if (ent.Comp.Hidden ||
            !TryComp<SpriteComponent>(ent, out var sprite))
        {
            return;
        }

        ent.Comp.OriginalVisible ??= sprite.Visible;
        _sprite.SetVisible((ent.Owner, sprite), false);
        ent.Comp.Hidden = true;
    }

    private void Restore(Entity<SWInVehicleVisibilityComponent> ent)
    {
        if (!ent.Comp.Hidden)
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            ent.Comp.OriginalVisible is { } originalVisible)
        {
            _sprite.SetVisible((ent.Owner, sprite), originalVisible);
        }

        ent.Comp.OriginalVisible = null;
        ent.Comp.Hidden = false;
    }
}
