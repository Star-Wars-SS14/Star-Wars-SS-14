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
using Content.Shared.Mobs.Systems;

namespace Content.Server._SW.TaskSystem.CapturePerson;

public sealed partial class SWCapturePersonSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SWTargetSelectorSystem _sWTarget = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CuffableComponent, OnTargetGetCuffed>(OnCuffed);
        SubscribeLocalEvent<CuffableComponent, OnTargetGetFullyUnCuffed>(OnUnCuffed);
        SubscribeLocalEvent<SWCapturePersonComponent, SWGetProgressEvent>(OnGetProgress);
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
        }
    }

    private void OnGetProgress(Entity<SWCapturePersonComponent> ent, ref SWGetProgressEvent args)
    {
        foreach (var target in ent.Comp.CuffedTargets)
        {
            if (_mobStateSystem.IsAlive(target))
            {
                args.Progress = 1;
                args.Handled = true;
                return;
            }
        }
        args.Progress = 0;
        args.Handled = true;
    }
}
