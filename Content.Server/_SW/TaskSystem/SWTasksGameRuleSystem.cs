using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._SW.TaskSystem;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

public sealed partial class SWTasksGameRuleSystem : GameRuleSystem<SWTaskGameruleComponent>
{
    [Dependency] IGameTiming _timing = default!;
    [Dependency] SWFactionGameruleSystem _swFaction = default!;
    [Dependency] IPrototypeManager _prototypeManager = default!;
    [Dependency] IRobustRandom _random = default!;
    ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("taskSystem");
        SubscribeLocalEvent<SWTaskGameruleComponent, SWTaskProgressUpdateEvent>(OnTaskComplete);
    }

    private void OnTaskComplete(Entity<SWTaskGameruleComponent> ent, ref SWTaskProgressUpdateEvent args)
    {
        _sawmill.Debug("got");
        if (!TryComp<SWTaskComponent>(args.Task, out var comp))
        {
            return;
        }
        _sawmill.Debug("updated");
        comp.Completeness = args.NewProgress;
        if (comp.Completeness == 1.0)
        {
            RaiseLocalEvent(args.Task, new SWOnTaskCompleteEvent());
            //QueueDel(args.Task);
        }
        _sawmill.Debug("updated2");
    }

    protected override void Started(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        TryAddRandomTaskAllFactions();
    }

    protected override void AppendRoundEndText(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {

    }
    protected override void ActiveTick(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        var query = EntityQueryEnumerator<SWFactionComponent>();
        while (query.MoveNext(out var factionUid, out var comp))
        {
            if (comp.NextAddTime <= _timing.CurTime)
            {
                TryAddRandomTask(new Entity<SWFactionComponent>(factionUid, comp));
            }
        }

        var taskQuery = EntityQueryEnumerator<SWTaskComponent>();
        while (taskQuery.MoveNext(out var taskUid, out var taskComp))
        {
            var ev = new SWGetProgressEvent();
            RaiseLocalEvent(taskUid, ref ev);
            taskComp.Completeness = ev.Progress;
            if (ev.Progress == 1.0)
            {
                RaiseLocalEvent(taskUid, new SWOnTaskCompleteEvent());
                //QueueDel(taskUid);
            }
        }
    }
    public void TryAddRandomTaskAllFactions()
    {
        var factions = _swFaction.GetFactionsEntities();
        foreach (var faction in factions)
        {
            TryAddRandomTask(faction);
        }
    }

    public WeightedRandomPrototype FilterAvailableTasks(WeightedRandomPrototype proto)
    {
        var keys = proto.Weights.Keys;
        WeightedRandomPrototype weightedProto = new();
        EntityUid uid;
        foreach (var (key, value) in proto.Weights)
        {
            uid = Spawn(key);
            var ev = new SWTaskCheckRequirementsEvent();
            RaiseLocalEvent(uid, ref ev);
            if (ev.Cancelled)
            {
                Del(uid);
            } else
            {
                weightedProto.Weights.Add(key, value);
            }
        }
        return weightedProto;
    }
    public void TryAddRandomTask(Entity<SWFactionComponent> faction)
    {
        if (faction.Comp.AvailableTasks == null)
        {
            return;
        }
        _prototypeManager.TryIndex(faction.Comp.AvailableTasks, out var proto);
        if (proto == null)
        {
            return;
        }


        var protoFiltered = FilterAvailableTasks(proto);
        //Если доступных заданий не окажется, то мы просто попробуем в следующий раз
        if (protoFiltered.Weights.Count != 0)
        {
            var taskProto = protoFiltered.Pick();
            var task = Spawn(taskProto);
            var comp = EnsureComp<SWTaskComponent>(task);
            RaiseLocalEvent(task, new SWOnTaskInitEvent());
            faction.Comp.CurrentTasks.Add(new Entity<SWTaskComponent>(task, comp));
            var ev = new SWTaskAddAdditionalDescEvent();
            RaiseLocalEvent(task, ref ev);
            comp.Description += ev.Description;
        } 
        faction.Comp.NextAddTime = _timing.CurTime + TimeSpan.FromSeconds(faction.Comp.AddIntervalInSeconds);
    }

    public List<Entity<SWTaskComponent>> GetFactionTasks(string? faction)
    {
        var res = new List<Entity<SWTaskComponent>>();
        if (faction == null)
        {
            return res;
        }

        Entity<SWFactionComponent>? ent = _swFaction.GetFactionEntity(faction);
        if (ent == null)
        {
            return res;
        }

        res = ent.Value.Comp.CurrentTasks;
        return res;
    }

    public void UpdateTaskProgress(EntityUid task, FixedPoint2 progress)
    {
        var query1 = EntityQueryEnumerator<SWTaskGameruleComponent>();
        query1.MoveNext(out var taskGameruleUid, out var _);
        RaiseLocalEvent(taskGameruleUid, new SWTaskProgressUpdateEvent(task, progress));
    }
}


