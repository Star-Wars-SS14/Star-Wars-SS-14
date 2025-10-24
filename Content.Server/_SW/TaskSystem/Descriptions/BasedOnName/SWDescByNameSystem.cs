using Content.Server._SW.TaskSystem.DynamicDescription.BasedOnJob;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.Descriptions.BasedOnName;
public sealed class SWDescByNameSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDescByNameComponent, SWTaskProvideDescriptionEvent>(OnDescUpdated,
                                                                after: [typeof(SWTargetSelectorSystem)],
                                                                before: [typeof(SWParallelTaskSystem)]);
    }

    private void OnDescUpdated(Entity<SWDescByNameComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        if (args.Handled ||
            !TryComp<SWTargetSelectorComponent>(ent.Owner, out var targetComp) ||
            targetComp.Target == null)
        {
            return;
        }
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        comp.Description = Loc.GetString(ent.Comp.DescriptionId,
            ("name", Name(targetComp.Target.Value)));

        args.Handled = true;
    }
}
