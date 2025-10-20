using Content.Server._SW.TaskSystem.GetScoreByTargetJob;
using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared._SW.TaskSystem;
using Content.Shared.FixedPoint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.Subtask;
public sealed class SWSubtaskSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _taskSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWSubtaskContainerComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWSubtaskContainerComponent, SWTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<SWSubtaskContainerComponent, SWGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SWSubtaskContainerComponent, SWTaskGetScoreEvent>(OnGetScore);
    }

    private void OnCheckRequirements(Entity<SWSubtaskContainerComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var comp = EnsureComp<SWTaskComponent>(ent);
        bool ok = true;
        foreach (var task in ent.Comp.Subtasks)
        {
            ok = ok && _taskSystem.CheckTask(task.TaskProto, comp.OwningFaction, ent.Owner);
        }

        if (!ok)
        {
            args.Cancel();
        }
    }

    private void OnTaskInit(Entity<SWSubtaskContainerComponent> ent, ref SWTaskInitEvent args)
    {
        var comp = EnsureComp<SWTaskComponent>(ent);
        foreach (var task in ent.Comp.Subtasks)
        {
            task.SpawnedEntity = _taskSystem.AddTaskNoInit(comp.OwningFaction, task.TaskProto);
            task.SpawnedEntity.Comp.ParentTask = ent.Owner;
            _taskSystem.InitTask(task.SpawnedEntity);
        }
    }

    private void OnGetProgress(Entity<SWSubtaskContainerComponent> ent, ref SWGetProgressEvent args)
    {
        FixedPoint2 progress = 0;
        foreach (var task in ent.Comp.Subtasks)
        {
            var tmp = new SWGetProgressEvent();
            RaiseLocalEvent(task.SpawnedEntity, ref tmp);
            if (tmp.Handled)
            {
                progress = tmp.Progress > progress ? tmp.Progress : progress;
            }
            else
            {
                var comp = EnsureComp<SWTaskComponent>(task.SpawnedEntity);
                progress = comp.Completeness > progress ? comp.Completeness : progress;
            }
        }
        args.Progress = progress;
        args.Handled = true;
    }

    private void OnGetScore(Entity<SWSubtaskContainerComponent> ent, ref SWTaskGetScoreEvent args)
    {
        var tmp = new SWTaskGetScoreEvent();
        int score = 0;
        foreach (var task in ent.Comp.Subtasks)
        {
            var comp = EnsureComp<SWTaskComponent>(task.SpawnedEntity);
            if (comp.Completeness < 1.0)
            {
                continue;
            }
            RaiseLocalEvent(task.SpawnedEntity, ref tmp);
            if (tmp.Handled)
            {
                score = tmp.Score > score ? tmp.Score : score;
            }
            else
            {
                score = comp.Score > score ? comp.Score : score;
            }
        }

        args.Score = score;
        args.Handled = true;
    }
}
