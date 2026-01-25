using Content.Server.Physics.Controllers;
using Content.Shared.Conveyor;
using Content.Shared.Physics.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Lathe.AutoSetConveyor;
public sealed class SWAutoSetConveyorSystem : EntitySystem
{
    [Dependency] private readonly ConveyorController _controller = default!;
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<SWAutoSetConveyorComponent, ConveyorComponent>();
        while (query.MoveNext(out var uid, out var autoSetComp, out var conveyorComp))
        {
            if (conveyorComp.State != autoSetComp.State
                && conveyorComp.Powered)
            {
                _controller.SetState(uid, autoSetComp.State, conveyorComp);
            }
        }
    }
}
