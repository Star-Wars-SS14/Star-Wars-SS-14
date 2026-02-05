using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Server._SW.TaskSystem.ParallelTaskProtect;
using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.ParallelTaskInvertStatus;
public sealed class SWParallelTaskInvertStatusSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTaskComponent, AfterParallelTaskSpawnedEvent>(OnSpawned);
        SubscribeLocalEvent<SWParallelTaskInvertStatusComponent, SWGetProgressEvent>(OnGetProgress);
    }

    private void OnSpawned(Entity<SWTaskComponent> ent, ref AfterParallelTaskSpawnedEvent args)
    {
        if (TryComp<SWParallelTaskInvertStatusComponent>(args.SpawnedTask, out var comp))
        {
            comp.TaskToInvert = ent;
        }
    }

    private void OnGetProgress(Entity<SWParallelTaskInvertStatusComponent> ent, ref SWGetProgressEvent args)
    {
        args.Progress = ent.Comp.TaskToInvert.Comp.Completeness == 1.0 ? 0.0 : 1.0;
        args.Handled = true;
    }
}
