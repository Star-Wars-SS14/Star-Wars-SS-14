using Content.Shared.FixedPoint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[ByRefEvent]
public sealed partial class SWGetProgressEvent : EntityEventArgs
{
    public FixedPoint2 Progress = 0.0;
}

public sealed partial class SWOnTaskCompleteEvent : EntityEventArgs
{

}

public sealed partial class SWTaskProgressUpdateEvent : EntityEventArgs
{
    public EntityUid Task;
    public FixedPoint2 NewProgress;
    public SWTaskProgressUpdateEvent(EntityUid task, FixedPoint2 prog)
    {
        Task = task;
        NewProgress = prog;
    }
}

