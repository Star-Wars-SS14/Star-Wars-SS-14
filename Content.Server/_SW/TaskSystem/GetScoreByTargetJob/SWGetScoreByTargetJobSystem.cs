using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.GetScoreByTargetJob;

public sealed partial class SWGetScoreByTargetJobSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGetScoreByTargetJobComponent, SWTaskGetScoreEvent>(OnGetScore);
    }

    private void OnGetScore(Entity<SWGetScoreByTargetJobComponent> ent, ref SWTaskGetScoreEvent args)
    {
        if (!TryComp<SWTargetSelectorComponent>(ent.Owner, out var comp))
        {
            return;
        }

        if (comp.JobOfTarget != null && ent.Comp.JobsToScores.TryGetValue(comp.JobOfTarget, out var score))
        {
            args.Score = score;
            args.Handled = true;
        }
    }
}
