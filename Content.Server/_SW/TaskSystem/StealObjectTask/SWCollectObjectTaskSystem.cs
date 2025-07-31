using Content.Server._SW.TransferSystem;
using Content.Shared._SW.TaskSystem;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._SW.TaskSystem.StealObjectTask;

public sealed partial class SWCollectObjectTaskSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly EntityManager _entity = default!;
    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWCollectObjectTaskComponent, SWTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<SWCollectObjectTaskComponent, SWGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<TryTransferObject>(OnTransfer);
    }

    private void OnTransfer(ref TryTransferObject ev)
    {
        var queryTasks = EntityQueryEnumerator<SWCollectObjectTaskComponent>();
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
                    && ev.OwnerFaction == taskComp.OwningFaction
                    && comp.ObjectsCollected[protolist] < comp.ObjectsToCollect[protolist])
                {
                    comp.ObjectsCollected[protolist]++;
                    ev.Handled = true;
                }
            }
        }
    }

    private void OnTaskInit(Entity<SWCollectObjectTaskComponent> ent, ref SWTaskInitEvent args)
    {
        //инициализируем количество собранных объектов нулями
        foreach (var protolist in ent.Comp.ObjectsToCollect.Keys)
        {
            ent.Comp.ObjectsCollected[protolist] = 0;
        }
    }

    private void OnGetProgress(Entity<SWCollectObjectTaskComponent> ent, ref SWGetProgressEvent args)
    {
        FixedPoint2 countAll = ent.Comp.ObjectsToCollect.Values.Sum();
        FixedPoint2 collected = ent.Comp.ObjectsCollected.Values.Sum();
        var p = collected / countAll;
        args.Progress = p > 1.0 ? 1.0 : p;
        args.Handled = true;
    }
}
