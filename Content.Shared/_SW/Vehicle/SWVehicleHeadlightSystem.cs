using Content.Shared.Actions;
using Content.Shared.Actions.Components;

namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleHeadlightSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWVehicleHeadlightComponent, SWToggleVehicleHeadlightsEvent>(OnToggleHeadlights);
        SubscribeLocalEvent<SWVehicleHeadlightComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<SWVehicleHeadlightUserComponent, SWUserEnterVehicleEvent>(OnUserEnterVehicle);
        SubscribeLocalEvent<SWVehicleHeadlightUserComponent, SWUserExitVehicleEvent>(OnUserExitVehicle);
        SubscribeLocalEvent<SWVehicleHeadlightUserComponent, ComponentShutdown>(OnUserShutdown);
    }

    private void OnMapInit(Entity<SWVehicleHeadlightComponent> ent, ref MapInitEvent args)
    {
        if (_pointLight.TryGetLight(ent, out var light))
            _pointLight.SetEnabled(ent, false, light);
    }

    private void OnUserEnterVehicle(Entity<SWVehicleHeadlightUserComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        if (!HasComp<SWVehicleHeadlightComponent>(args.Vehicle))
            return;

        if (_actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action, args.Vehicle))
            _actions.SetToggled(ent.Comp.ActionEntity, IsHeadlightEnabled(args.Vehicle));
    }

    private void OnUserExitVehicle(Entity<SWVehicleHeadlightUserComponent> ent, ref SWUserExitVehicleEvent args)
    {
        RemoveHeadlightAction(ent);
    }

    private void OnUserShutdown(Entity<SWVehicleHeadlightUserComponent> ent, ref ComponentShutdown args)
    {
        RemoveHeadlightAction(ent);
    }

    private void OnToggleHeadlights(Entity<SWVehicleHeadlightComponent> ent, ref SWToggleVehicleHeadlightsEvent args)
    {
        if (args.Handled ||
            !TryComp<SWInVehicleComponent>(args.Performer, out var inVehicle) ||
            inVehicle.Vehicle != ent.Owner ||
            !HasComp<SWVehicleHeadlightUserComponent>(args.Performer) ||
            !_pointLight.TryGetLight(ent, out var light))
        {
            return;
        }

        var enabled = !light.Enabled;
        _pointLight.SetEnabled(ent, enabled, light);
        _actions.SetToggled(new Entity<ActionComponent?>(args.Action.Owner, args.Action.Comp), enabled);
        args.Handled = true;
    }

    private bool IsHeadlightEnabled(EntityUid vehicle)
    {
        return _pointLight.TryGetLight(vehicle, out var light) && light.Enabled;
    }

    private void RemoveHeadlightAction(Entity<SWVehicleHeadlightUserComponent> ent)
    {
        if (ent.Comp.ActionEntity == null)
            return;

        _actions.RemoveAction(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }
}
