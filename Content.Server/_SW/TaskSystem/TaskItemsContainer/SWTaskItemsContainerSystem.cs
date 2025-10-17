using Content.Server._SW.TaskSystem.StealObjectTask;
using Content.Shared._SW.TaskSystem;
using JetBrains.FormatRipper.Elf;
using Robust.Shared.Containers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.TaskItemsContainer;
public sealed class SWTaskItemsContainerSystem : EntitySystem
{
    [Dependency] SharedContainerSystem _containerSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTaskItemsContainerComponent, EntInsertedIntoContainerMessage>(OnEntityInserted);
        SubscribeLocalEvent<SWTaskItemsContainerComponent, EntRemovedFromContainerMessage>(OnEntityRemoved);
    }

    private void OnEntityRemoved(Entity<SWTaskItemsContainerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateTasks(ent, args.Entity, false);
        ISawmill _s = Logger.GetSawmill("dsfg");
        _s.Fatal("df");
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

            foreach (var requirement in comp.ObjectsCollected)
            {
                var protolist = requirement.ObjectsPrototypes;
                TryPrototype(affectedObject, out var proto);
                if (proto == null)
                {
                    return;
                }

                if (protolist.Contains(proto.ID)
                    && comp.ObjectsCollected.Find(x => x.ObjectsPrototypes == protolist)?.Number
                    < comp.ObjectsToCollect.Find(x => x.ObjectsPrototypes == protolist)?.Number)
                {
                    var tmp = comp.ObjectsCollected.Find(x => x.ObjectsPrototypes == protolist);
                    if (tmp != null)
                    {
                        if (inserted)
                        {
                            tmp.Number++;
                        }
                        else
                        {
                            tmp.Number--;
                        }
                    }
                }
            }
        }
    }
}
