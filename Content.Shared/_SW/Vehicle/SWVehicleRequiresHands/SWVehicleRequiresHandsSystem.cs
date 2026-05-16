using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.SWVehicleRequiresHands;
public sealed class SWVehicleRequiresHandsSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItemSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWVehicleRequiresHandsUserComponent, SWUserTryEnterVehicleEvent>(OnTryEnter);
        SubscribeLocalEvent<SWVehicleRequiresHandsUserComponent, SWUserEnterVehicleEvent>(OnEnter);
        SubscribeLocalEvent<SWVehicleRequiresHandsUserComponent, SWUserExitVehicleEvent>(OnExit);
    }

    private void OnTryEnter(Entity<SWVehicleRequiresHandsUserComponent> ent, ref SWUserTryEnterVehicleEvent args)
    {
        if (_handsSystem.GetEmptyHandCount(ent.Owner) < ent.Comp.EmptyHandsRequiredAmount)
        {
            args.Cancel();
            args.Message = Loc.GetString("sw-vehicle-requires-hands");
        }
    }

    private void OnEnter(Entity<SWVehicleRequiresHandsUserComponent> ent, ref SWUserEnterVehicleEvent args)
    {
        for (int _ = 0; _ < ent.Comp.EmptyHandsRequiredAmount; _++)
        {
            _virtualItemSystem.TrySpawnVirtualItemInHand(args.Vehicle, ent, out var item);
            if (item != null)
            {
                EnsureComp<UnremoveableComponent>(item.Value);
            }
        }
    }

    private void OnExit(Entity<SWVehicleRequiresHandsUserComponent> ent, ref SWUserExitVehicleEvent args)
    {
        _virtualItemSystem.DeleteInHandsMatching(ent, args.Vehicle);
    }
}
