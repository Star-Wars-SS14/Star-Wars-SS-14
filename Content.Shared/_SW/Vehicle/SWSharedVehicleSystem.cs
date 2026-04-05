using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Movement.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;
public sealed class SWSharedVehicleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;

    public const string ActionExitVehicle = "SWVehicleGetOffAction";
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWInVehicleComponent, PreventCollideEvent>(OnPreventCollide);

    }

    private void OnPreventCollide(Entity<SWInVehicleComponent> ent, ref PreventCollideEvent args)
    {
        if (args.OtherEntity == ent.Comp.Vehicle)
        {
            args.Cancelled = true;
        }
    }
}


public sealed partial class SWExitVehicleRequested : InstantActionEvent
{

}

public sealed class SWUserEnterVehicleEvent : EntityEventArgs
{
    public Entity<SWVehicleComponent> Vehicle;
    public SWUserEnterVehicleEvent(Entity<SWVehicleComponent> vehicle)
    {
        Vehicle = vehicle;
    }
}
