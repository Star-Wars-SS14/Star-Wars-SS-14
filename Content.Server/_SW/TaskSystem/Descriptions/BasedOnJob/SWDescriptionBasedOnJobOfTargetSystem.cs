using Content.Server._SW.TaskSystem.KillRandomOfFaction;
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

namespace Content.Server._SW.TaskSystem.DynamicDescription.BasedOnJob;

public sealed partial class SWDescriptionBasedOnJobOfTargetSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWDescriptionBasedOnJobOfTargetComponent, SWTaskProvideDescriptionEvent>(OnDescUpdated,
                                                                after: [typeof(SWTargetSelectorSystem)],
                                                                before: [typeof(SWParallelTaskSystem)]);
    }

    private void OnDescUpdated(Entity<SWDescriptionBasedOnJobOfTargetComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        if (args.Handled)
        {
            return;
        }
        var targetComp = EnsureComp<SWTargetSelectorComponent>(ent.Owner);

        if (targetComp.JobOfTarget != null)
        {
            _prototypeManager.TryIndex(targetComp.JobOfTarget, out JobPrototype? proto);
            var comp = EnsureComp<SWTaskComponent>(ent.Owner);
            comp.Description = Loc.GetString(ent.Comp.JobsToDescriptions[targetComp.JobOfTarget]);
        }
        args.Handled = true;
    }
}
