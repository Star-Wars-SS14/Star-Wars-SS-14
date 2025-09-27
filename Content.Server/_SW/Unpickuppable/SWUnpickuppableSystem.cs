using Content.Shared.Item;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Unpickuppable;
public sealed class SWUnpickuppableSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWUnpickuppableComponent, GettingPickedUpAttemptEvent>(OnTryPickup);
    }

    private void OnTryPickup(Entity<SWUnpickuppableComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        args.Cancel();
    }
}
