using Content.Server.Shuttles.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.ForbidFTL;

public sealed partial class SWForbidFTLSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWForbidFTLComponent, ConsoleFTLAttemptEvent>(OnTryFTL);
    }

    private void OnTryFTL(Entity<SWForbidFTLComponent> ent, ref ConsoleFTLAttemptEvent args)
    {
        args.Cancelled = true;
        args.Reason = "БСС недоступен";
    }
}
