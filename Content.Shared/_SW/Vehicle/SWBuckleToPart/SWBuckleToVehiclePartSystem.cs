using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.SWBuckleToPart;
public sealed partial class SWBuckleToVehiclePartSystem : EntitySystem
{
    [Dependency] private readonly SharedBuckleSystem _sharedBuckleSystem = default!;
    [Dependency] private readonly SWVehicleSystem _vehicleSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWBuckleToVehiclePartComponent, SWUserEnterVehicleEvent>(OnUserEnter);
        SubscribeLocalEvent<SWBuckleToVehiclePartComponent, SWUserExitVehicleEvent>(OnUserExit);
        SubscribeLocalEvent<SWBuckleToVehiclePartComponent, UnbuckleAttemptEvent>(OnUnBuckle);
        SubscribeLocalEvent<SWBuckleToVehiclePartComponent, UnbuckledEvent>(OnUnbuckled);
    }

    private void OnUserEnter(Entity<SWBuckleToVehiclePartComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        var part = GetEntity(_vehicleSystem.GetPart(args.Vehicle, ent.Comp.Part));
        if (part == null)
        {
            return;
        }

        _sharedBuckleSystem.TryBuckle(ent, ent, part.Value, overrideRange: SharedInteractionSystem.InteractionRange + 10f);
    }

    private void OnUserExit(Entity<SWBuckleToVehiclePartComponent> ent, ref SWUserExitVehicleEvent args)
    {
        var part = GetEntity(_vehicleSystem.GetPart(args.Vehicle, ent.Comp.Part));
        if (part == null)
        {
            return;
        }

        _sharedBuckleSystem.Unbuckle(ent.Owner, ent.Owner);
    }

    private void OnUnBuckle(Entity<SWBuckleToVehiclePartComponent> ent, ref UnbuckleAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnUnbuckled(Entity<SWBuckleToVehiclePartComponent> ent, ref UnbuckledEvent args)
    {
        if (!TryComp<SWInVehicleComponent>(ent, out var inVehicle) ||
            inVehicle.Exiting ||
            !TryComp<SWVehicleComponent>(inVehicle.Vehicle, out var vehicle))
        {
            return;
        }

        _vehicleSystem.TryDeoccupyRole((inVehicle.Vehicle, vehicle), inVehicle.OccupiedRole);
    }
}
