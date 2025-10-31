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
        SubscribeLocalEvent<DestroyObjectsTaskComponent, SWTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<DestroyObjectsTaskComponent, SWGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<MetaDataComponent, DestructionEventArgs>(OnDestruct);
        SubscribeLocalEvent<MetaDataComponent, BreakageEventArgs>(OnBreak);
    }

    private void OnBreak(Entity<MetaDataComponent> ent, ref BreakageEventArgs args)
    {
        UpdateTask(ent);
    }

    private void UpdateTask(Entity<MetaDataComponent> ent)
    {
        var queryTasks = EntityQueryEnumerator<DestroyObjectsTaskComponent>();
        while (queryTasks.MoveNext(out var uid, out var comp))
        {
            foreach (var req in comp.ObjectsDestroyed)
            {
                var protolist = req.ObjectsPrototypes;
                if (ent.Comp.EntityPrototype == null)
                {
                    continue;
                }
                if (protolist.Contains(ent.Comp.EntityPrototype.ID))
                {
                    var satisfied_req = comp.ObjectsDestroyed.Find(x => x.ObjectsPrototypes == protolist);
                    if (satisfied_req != null)
                    {
                        satisfied_req.Number++;
                    }
                }
            }
        }
    }
    private void OnTaskInit(Entity<DestroyObjectsTaskComponent> ent, ref SWTaskInitEvent args)
    {
        //инициализируем количество разрушенных объектов нулями
        foreach (var req in ent.Comp.ObjectsToDestroy)
        {
            ent.Comp.ObjectsDestroyed.Add(new ObjectRequirement(0, req.ObjectsPrototypes));
        }
    }

    private void OnDestruct(Entity<MetaDataComponent> ent, ref DestructionEventArgs args)
    {
        UpdateTask(ent);
    }

    private void OnGetProgress(Entity<DestroyObjectsTaskComponent> ent, ref SWGetProgressEvent args)
    {
        FixedPoint2 countAll = ent.Comp.ObjectsToDestroy.Sum(x => x.Number);
        FixedPoint2 destroyed = ent.Comp.ObjectsDestroyed.Sum(x => x.Number);
        var p = destroyed / countAll;
        args.Progress = p > 1.0 ? 1.0 : p;
        args.Handled = true;
    }
}
