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
        if (TryComp<SWParallelTaskProtectComponent>(args.SpawnedTask, out var comp))
        {
            comp.PersonToProtect = EnsureComp<SWTargetSelectorComponent>(ent.Owner).PersonToKill;
        }
    }
    private void OnDescriptionRequested(Entity<SWParallelTaskProtectComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        if (ent.Comp.PersonToProtect == null)
        {
            return;
        }
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        comp.Description = Loc.GetString(ent.Comp.DescriptionId, ("name", Name(ent.Comp.PersonToProtect.Value)));
    }
    private void OnGetProgress(Entity<SWParallelTaskProtectComponent> ent, ref SWGetProgressEvent args)
    {
        if (!TryComp<MobStateComponent>(ent.Comp.PersonToProtect, out var comp))
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
