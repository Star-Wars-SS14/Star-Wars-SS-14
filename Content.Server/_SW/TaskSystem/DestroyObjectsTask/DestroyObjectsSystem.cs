using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server.Destructible;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.DestroyObjectsTask;

public sealed partial class DestroyObjectsSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly EntityManager _entity = default!;
    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<DestroyObjectsTaskComponent, SWOnTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<DestroyObjectsTaskComponent, SWGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<MetaDataComponent, DestructionEventArgs>(OnDestruct);
    }

    private void OnTaskInit(Entity<DestroyObjectsTaskComponent> ent, ref SWOnTaskInitEvent args)
    {
        //инициализируем количество разрушенных объектов нулями
        foreach (var protolist in ent.Comp.ObjectsToDestroy.Keys)
        {
            ent.Comp.ObjectsDestroyed[protolist] = 0;
        }
    }

    private void OnDestruct(Entity<MetaDataComponent> ent, ref DestructionEventArgs args)
    {
        var queryTasks = EntityQueryEnumerator<DestroyObjectsTaskComponent>();
        while (queryTasks.MoveNext(out var uid, out var comp))
        {
            foreach (var protolist in comp.ObjectsDestroyed.Keys)
            {
                if (ent.Comp.EntityPrototype == null)
                {
                    continue;
                }
                if (protolist.Contains(ent.Comp.EntityPrototype.ID))
                {
                    comp.ObjectsDestroyed[protolist]++;
                }
            }
        }
    }

    private void OnGetProgress(Entity<DestroyObjectsTaskComponent> ent, ref SWGetProgressEvent args)
    {
        FixedPoint2 countAll = ent.Comp.ObjectsToDestroy.Values.Sum();
        FixedPoint2 destroyed = ent.Comp.ObjectsDestroyed.Values.Sum();
        var p = destroyed / countAll;
        args.Progress = p > 1.0 ? 1.0 : p;
    }
}
