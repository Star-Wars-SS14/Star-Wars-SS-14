using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Mobs.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.ParallelTaskProtect;

public sealed class SWParallelTaskProtectSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWKillRandomByNameComponent, AfterParallelTaskSpawnedEvent>(OnSpawned);
        SubscribeLocalEvent<SWParallelTaskProtectComponent, SWTaskProvideDescriptionEvent>(OnDescriptionRequested);
        SubscribeLocalEvent<SWParallelTaskProtectComponent, SWGetProgressEvent>(OnGetProgress);
    }
    private void OnSpawned(Entity<SWKillRandomByNameComponent> ent, ref AfterParallelTaskSpawnedEvent args)
    {
        if (TryComp<SWTargetSelectorComponent>(args.SpawnedTask, out var comp))
        {
            comp.Target = Comp<SWTargetSelectorComponent>(ent.Owner).Target;
            comp.JobOfTarget = Comp<SWTargetSelectorComponent>(ent.Owner).JobOfTarget;
        }
    }
    private void OnDescriptionRequested(Entity<SWParallelTaskProtectComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        var target = Comp<SWTargetSelectorComponent>(ent.Owner).Target;
        if (target == null)
        {
            return;
        }
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        comp.Description = Loc.GetString(ent.Comp.DescriptionId, ("name", Name(target.Value)));
    }
    private void OnGetProgress(Entity<SWParallelTaskProtectComponent> ent, ref SWGetProgressEvent args)
    {
        if (!TryComp<MobStateComponent>(Comp<SWTargetSelectorComponent>(ent.Owner).Target, out var comp))
        {
            //If we cant get comp it means that person is gibbed, or deleted, or smth else which means that person is dead
            args.Progress = 0.0f;
            args.Handled = true;
            return;
        }

        if (comp.CurrentState == Shared.Mobs.MobState.Dead || comp.CurrentState == Shared.Mobs.MobState.Invalid)
        {
            args.Progress = 0.0f;
        }
        else
        {
            args.Progress = 1.0f;
        }
        args.Handled = true;
    }
}
