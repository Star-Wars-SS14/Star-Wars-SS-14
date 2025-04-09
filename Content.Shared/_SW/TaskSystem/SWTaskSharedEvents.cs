using Content.Shared.FixedPoint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[ByRefEvent]
public sealed partial class SWGetProgressEvent : HandledEntityEventArgs
{
    public FixedPoint2 Progress = 0.0;
}

public sealed partial class SWOnTaskCompleteEvent : EntityEventArgs
{

}

[ByRefEvent]
public sealed partial class SWTaskCheckRequirementsEvent : CancellableEntityEventArgs
{

}

[ByRefEvent]
public sealed partial class SWTaskAddAdditionalDescEvent : EntityEventArgs
{
    public string Description = "";
}

[ByRefEvent]
public sealed partial class SWTaskGetScoreEvent : HandledEntityEventArgs
{
    public int Score = 0;
}
public sealed partial class SWOnTaskInitEvent : EntityEventArgs
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

