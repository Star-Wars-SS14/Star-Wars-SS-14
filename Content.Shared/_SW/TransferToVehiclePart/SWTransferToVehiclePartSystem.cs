using Content.Shared._SW.Vehicle;
using Content.Shared.Actions;
using Content.Shared.Mind;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TransferToVehiclePart;
public sealed class SWTransferToVehiclePartSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _sharedMindSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SWSharedVehicleSystem _swSharedVehicleSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTransferMindToVehiclePartComponent, SWUserEnterVehicleEvent>(OnStartup);
        SubscribeLocalEvent<SWMindTransferredToVehiclePartComponent, SWExitVehicleRequested>(OnExit);
    }

    private void OnStartup(Entity<SWTransferMindToVehiclePartComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        ISawmill _s = Logger.GetSawmill("adfsg");
        _s.Fatal("0");
        var mind = _sharedMindSystem.GetMind(ent);
        if (mind == null)
        {
            return;
        }
        _s.Fatal("1");
        EntityUid part = args.Vehicle.Owner;
        _s.Fatal($"{part}");
        if (ent.Comp.PartId != null)
        {
            part = args.Vehicle.Comp.VehicleParts[ent.Comp.PartId.Value].SpawnedPart;
        }
        _s.Fatal($"{part}");
        _sharedMindSystem.TransferTo(mind.Value, part);
        var comp = AddComp<SWMindTransferredToVehiclePartComponent>(part);
        comp.OriginalEntity = ent;
        comp.Action = _actions.AddAction(part, SWSharedVehicleSystem.ActionExitVehicle);
    }

    private void OnExit(Entity<SWMindTransferredToVehiclePartComponent> ent, ref SWExitVehicleRequested args)
    {
        var mind = _sharedMindSystem.GetMind(ent);
        if (mind == null)
        {
            return;
        }
        _sharedMindSystem.TransferTo(mind.Value, ent.Comp.OriginalEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.Action);
        RemComp<SWMindTransferredToVehiclePartComponent>(ent);
        RaiseLocalEvent(ent.Comp.OriginalEntity, args);
    }
}
