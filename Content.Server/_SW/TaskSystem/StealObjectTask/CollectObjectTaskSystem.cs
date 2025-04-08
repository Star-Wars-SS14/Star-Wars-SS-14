using Content.Server._SW.TaskSystem.DestroyObjectsTask;
using Content.Server._SW.TransferSystem;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using JetBrains.FormatRipper.Elf;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.StealObjectTask;

public sealed partial class CollectObjectTaskSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly EntityManager _entity = default!;
    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<CollectObjectTaskComponent, SWOnTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<CollectObjectTaskComponent, SWGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<TryTransferObject>(OnTransfer);
    }

    private void OnTransfer(ref TryTransferObject ev)
    {
        var queryTasks = EntityQueryEnumerator<CollectObjectTaskComponent>();
        while (queryTasks.MoveNext(out var uid, out var comp))
        {
            foreach (var protolist in comp.ObjectsCollected.Keys)
            {
                TryPrototype(ev.PlacedUid, out var proto);
                if (proto == null)
                {
                    return;
                }
                var taskComp = EnsureComp<SWTaskComponent>(uid);
                if (protolist.Contains(proto.ID)
                    && ev.OwnerFaction == taskComp.OwnerFaction
                    && comp.ObjectsCollected[protolist] < comp.ObjectsToCollect[protolist])
                {
                    comp.ObjectsCollected[protolist]++;
                    ev.Handled = true;
                }
            }
        }
    }

    private void OnTaskInit(Entity<CollectObjectTaskComponent> ent, ref SWOnTaskInitEvent args)
    {
        //инициализируем количество собранных объектов нулями
        foreach (var protolist in ent.Comp.ObjectsToCollect.Keys)
        {
            ent.Comp.ObjectsCollected[protolist] = 0;
        }
    }

    private void OnGetProgress(Entity<CollectObjectTaskComponent> ent, ref SWGetProgressEvent args)
    {
        FixedPoint2 countAll = ent.Comp.ObjectsToCollect.Values.Sum();
        FixedPoint2 destroyed = ent.Comp.ObjectsCollected.Values.Sum();
        var p = destroyed / countAll;
        args.Progress = p > 1.0 ? 1.0 : p;
        args.Handled = true;
    }
}
