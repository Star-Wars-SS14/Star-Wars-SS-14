using Content.Server._SW.TaskSystem.StealObjectTask;
using Content.Shared._SW.TaskSystem;
using Robust.Shared.Containers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.TaskItemsContainer;
public sealed class SWTaskItemsContainerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTaskItemsContainerComponent, EntInsertedIntoContainerMessage>(OnEntityInserted);
        SubscribeLocalEvent<SWTaskItemsContainerComponent, EntRemovedFromContainerMessage>(OnEntityRemoved);
    }

    private void OnEntityRemoved(Entity<SWTaskItemsContainerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateTasks(ent, args.Entity, false);
    }

    private void OnEntityInserted(Entity<SWTaskItemsContainerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        UpdateTasks(ent, args.Entity, true);
    }

    void UpdateTasks(Entity<SWTaskItemsContainerComponent> ent, EntityUid affectedObject, bool inserted)
    {
        var queryTasks = EntityQueryEnumerator<SWTaskComponent, SWCollectObjectTaskComponent>();
        while (queryTasks.MoveNext(out var uid, out var taskComp, out var comp))
        {
            if (taskComp.OwningFaction != ent.Comp.Faction)
            {
                continue;
            }
            foreach (var requirement in comp.ObjectsToCollect)
            {
                var protolist = requirement.ObjectsPrototypes;
                TryPrototype(affectedObject, out var proto);
                if (proto == null)
                {
                    return;
                }
                if (protolist.Contains(proto.ID))
                {
                    var collected = comp.ObjectsCollected.Find(x => x.ObjectsPrototypes == protolist);
                    var toCollect = comp.ObjectsToCollect.Find(x => x.ObjectsPrototypes == protolist);
                    if (collected != null && toCollect != null)
                    {
                        if (inserted && collected.Number < toCollect.Number)
                        {
                            collected.Number++;
                        }
                        else if (collected.Number > 0)
                        {
                            collected.Number--;
                        }
                    }
                }
            }
        }
    }
}
