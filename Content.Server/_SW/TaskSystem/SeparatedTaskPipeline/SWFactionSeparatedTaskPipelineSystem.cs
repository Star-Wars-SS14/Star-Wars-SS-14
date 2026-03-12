using Content.Shared._SW.TaskSystem;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SeparatedTaskPipeline;
public sealed class SWFactionSeparatedTaskPipelineSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _taskSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SWFactionSeparatedTaskPipelineComponent>();
        while (query.MoveNext(out var uid, out var pipelineComp))
        {
            foreach (var task in pipelineComp.Tasks)
            {
                if (task.TaskAddTime <= _timing.CurTime && !task.Added)
                {
                    var result = _taskSystem.TryAddTask(Prototype(uid)!.ID, task.TaskId);
                    if (result != null || !task.TryAddUntilSuccess)
                    {
                        task.Added = true;
                    }
                }
            }
        }
    }
}
