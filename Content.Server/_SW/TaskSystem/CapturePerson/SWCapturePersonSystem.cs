using Content.Shared.Cuffs.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared.Cuffs;
using Content.Shared._SW.TaskSystem;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared.Roles;

namespace Content.Server._SW.TaskSystem.CapturePerson;

public sealed partial class SWCapturePersonSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly SWTargetSelectorSystem _sWTarget = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CuffableComponent, OnTargetGetCuffed>(OnCuffed);
        SubscribeLocalEvent<CuffableComponent, OnTargetGetFullyUnCuffed>(OnUnCuffed);
    }
    private void OnCuffed(Entity<CuffableComponent> ent, ref OnTargetGetCuffed args)
    {
        if (!TryComp<SWFactionMemberComponent>(args.User, out var factionComp))
        {
            return;
        }

        var query = EntityQueryEnumerator<SWCapturePersonComponent, SWTargetSelectorComponent>();
        while (query.MoveNext(out var uid, out var captureComp, out var targetComp))
        {
            var taskComp = EnsureComp<SWTaskComponent>(uid);
            if (_sWTarget.GetJob(ent.Owner) == targetComp.JobOfTarget && factionComp.Faction == taskComp.OwningFaction)
            {
                captureComp.CuffedTargets.Add(ent.Owner);
                _sWTasks.UpdateTaskProgress(uid, 1.0);
            }
        }
    }

    private void OnUnCuffed(Entity<CuffableComponent> ent, ref OnTargetGetFullyUnCuffed args)
    {
        var query = EntityQueryEnumerator<SWCapturePersonComponent, SWTargetSelectorComponent>();
        while (query.MoveNext(out var uid, out var captureComp, out var targetComp))
        {
            if (_sWTarget.GetJob(ent.Owner) == targetComp.JobOfTarget)
            {
                captureComp.CuffedTargets.Remove(uid);
            }
            if (captureComp.CuffedTargets.Count == 0)
            {
                _sWTasks.UpdateTaskProgress(uid, 0.0);
            }
        }
    }
}
