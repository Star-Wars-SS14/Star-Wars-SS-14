using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Atmos.Components;
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
using System.Xml.Schema;

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
        if (!TryComp<SWTaskComponent>(args.Task, out var comp))
        {
            return;
        }
        comp.Completeness = args.NewProgress;
        if (comp.Completeness == 1.0)
        {
            RaiseLocalEvent(args.Task, new SWOnTaskCompleteEvent());
            //QueueDel(args.Task);
        }
    }

    protected override void Started(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        var factions = _swFaction.GetFactions();
        component.TimeToAddStartTasks = _timing.CurTime + TimeSpan.FromSeconds(component.DelayBeforeAddingStartTasksInSeconds);
    }
    public void AddStartTasks()
    {
        var factions = _swFaction.GetFactions();
        foreach (var (faction, factionEntity) in factions)
        {
            if (factionEntity != null)
            {
                if (!TryComp<SWTaskContainerComponent>(factionEntity.Value, out var comp))
                {
                    continue;
                }
                for (int i = 0; i < comp.StartAmountOfTasks; i++)
                {
                    AddRandomTask(new Entity<SWTaskContainerComponent>(factionEntity.Value.Owner, comp), faction);
                }
                comp.NextAddTime = _timing.CurTime + TimeSpan.FromSeconds(comp.AddIntervalInSeconds);
            }
        }
        _sawmill.Debug("added start tasks");
    }
    protected override void AppendRoundEndText(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        var query = EntityQueryEnumerator<SWFactionComponent>();
        while (query.MoveNext(out var fuid, out var factionComp))
        {
            if (!TryComp<SWTaskContainerComponent>(fuid, out var taskComp))
            {
                continue;
            }
            args.AddLine($"Фракция {factionComp.Name}:");
            foreach (var task in taskComp.CurrentTasks)
            {
                string text = $"""- Задание "{task.Comp.Name}": """;
                if (task.Comp.Completeness == 1.0)
                {
                    var ev = new SWTaskGetScoreEvent();
                    RaiseLocalEvent(task.Owner, ref ev);
                    int score = task.Comp.Score;
                    if (ev.Handled)
                    {
                        score = ev.Score;
                    }
                    factionComp.CurrentScore += score;
                    text += $"Выполнено. Очков Получено: {score}.";
                }
                else
                {
                    text += "Не выполнено.";
                }
                args.AddLine(text);
            }
        }


        query = EntityQueryEnumerator<SWFactionComponent>(); //reset query
        List<Entity<SWFactionComponent>> res = CalculateWinner(query);
        if (res.Count() != 0)
        {
            args.AddLine("Победа присуждена следующим фракциям:");
            foreach (var fact in res)
            {
                args.AddLine($"- {fact.Comp.Name}");
            }
        }
        else
        {
            args.AddLine("Ничья");
        }
    }

    private List<Entity<SWFactionComponent>> CalculateWinner(EntityQueryEnumerator<SWFactionComponent> query)
    {
        List<Entity<SWFactionComponent>> majorFactions = new();
        List<Entity<SWFactionComponent>> minorFactions = new();
        List<Entity<SWFactionComponent>> majorCompletedAllTasks = new();
        List<Entity<SWFactionComponent>> minorCompletedAllTasks = new();
        while (query.MoveNext(out var fuid, out var fComp))
        {
            if (!TryComp<SWTaskContainerComponent>(fuid, out var taskComp))
            {
                continue;
            }
            if (fComp.IsMajor)
            {
                majorFactions.Add(new Entity<SWFactionComponent>(fuid, fComp));
                if (FactionCompletedAllTasks(fuid, fComp) && taskComp.CurrentTasks.Count() != 0)
                {

                    majorCompletedAllTasks.Add(new Entity<SWFactionComponent>(fuid, fComp));
                }
            }
            else
            {
                minorFactions.Add(new Entity<SWFactionComponent>(fuid, fComp));
                if (FactionCompletedAllTasks(fuid, fComp) && taskComp.CurrentTasks.Count() != 0)
                {

                    minorCompletedAllTasks.Add(new Entity<SWFactionComponent>(fuid, fComp));
                }
            }
        }

        if (majorFactions.Count() == majorCompletedAllTasks.Count())
        {
            return new List<Entity<SWFactionComponent>>(); // пустой список, то есть победителей нет, то бишь ничья
        }

        if (majorCompletedAllTasks.Count() > 0)
        {
            return majorCompletedAllTasks;
        }

        if (majorCompletedAllTasks.Count() == 0 && minorCompletedAllTasks.Count() == 0)
        {
            List<Entity<SWFactionComponent>> res = new();
            int maxScoreAmongMajors = 0;

            foreach (var fact in majorFactions)
            {
                maxScoreAmongMajors = Math.Max(fact.Comp.CurrentScore, maxScoreAmongMajors);
            }

            foreach (var fact in majorFactions)
            {
                if (fact.Comp.CurrentScore == maxScoreAmongMajors)
                {
                    res.Add(fact);
                }
            }
            return res;
        }
        else
        {
            return minorCompletedAllTasks;
        }
    }

    private bool FactionCompletedAllTasks(EntityUid factionUid, SWFactionComponent comp)
    {
        if (!TryComp<SWTaskContainerComponent>(factionUid, out var taskComp))
        {
            return true;
        }
        foreach (var task in taskComp.CurrentTasks)
        {
            if (task.Comp.Completeness != 1.0)
            {
                return false;
            }
        }
        return true;
    }
    protected override void ActiveTick(EntityUid uid, SWTaskGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (!component.StartTasksAdded && component.TimeToAddStartTasks <= _timing.CurTime)
        {
            AddStartTasks();
            component.StartTasksAdded = true;
        }

        var query = EntityQueryEnumerator<SWTaskContainerComponent>();
        while (query.MoveNext(out var factionUid, out var comp))
        {
            if (comp.NextAddTime <= _timing.CurTime)
            {
                TryPrototype(factionUid, out var proto);
                if (proto != null)
                {
                    AddRandomTask(new Entity<SWTaskContainerComponent>(factionUid, comp), proto.ID);
                    comp.NextAddTime = _timing.CurTime + TimeSpan.FromSeconds(comp.AddIntervalInSeconds);
                }
            }
        }

        var taskQuery = EntityQueryEnumerator<SWTaskComponent>();
        while (taskQuery.MoveNext(out var taskUid, out var taskComp))
        {
            var ev = new SWGetProgressEvent();
            RaiseLocalEvent(taskUid, ref ev);
            if (ev.Handled)
            {
                taskComp.Completeness = ev.Progress;
            }
            if (ev.Progress == 1.0)
            {
                RaiseLocalEvent(taskUid, new SWOnTaskCompleteEvent());
                //QueueDel(taskUid);
            }
        }
    }
    public WeightedRandomPrototype FilterAvailableTasks(WeightedRandomPrototype proto, Entity<SWTaskContainerComponent> faction)
    {
        var keys = proto.Weights.Keys;
        WeightedRandomPrototype weightedProto = new();
        EntityUid uid;
        var currentTasksProtolist = new List<string>();
        foreach (var task in faction.Comp.CurrentTasks)
        {
            TryPrototype(task.Owner, out var taskProto);
            if (taskProto != null)
            {
                currentTasksProtolist.Add(taskProto.ID);
            }
        }
        foreach (var (key, value) in proto.Weights)
        {
            //if (currentTasksProtolist.Contains(key))
            //{
            //    continue;
            //}
            uid = Spawn(key);
            var ev = new SWTaskCheckRequirementsEvent();
            RaiseLocalEvent(uid, ref ev);
            if (ev.Cancelled)
            {
                Del(uid);
            }
            else
            {
                weightedProto.Weights.Add(key, value);
            }
        }
        return weightedProto;
    }
    public void AddRandomTask(Entity<SWTaskContainerComponent> faction, string factionId)
    {
        if (faction.Comp.AvailableTasks == null)
        {
            return;
        }
        if (faction.Comp.CurrentTasks.Count() == faction.Comp.MaxAmountOfTasks)
        {
            return;
        }
        _prototypeManager.TryIndex(faction.Comp.AvailableTasks, out var proto);
        if (proto == null)
        {
            return;
        }
        var protoFiltered = FilterAvailableTasks(proto, faction);
        //Если доступных заданий не окажется, то мы просто попробуем в следующий раз
        if (protoFiltered.Weights.Count != 0)
        {
            var taskProto = protoFiltered.Pick();
            var task = Spawn(taskProto);
            var comp = EnsureComp<SWTaskComponent>(task);
            RaiseLocalEvent(task, new SWOnTaskInitEvent());
            faction.Comp.CurrentTasks.Add(new Entity<SWTaskComponent>(task, comp));
            comp.OwnerFaction = factionId;
            var ev = new SWTaskAddAdditionalDescEvent();
            RaiseLocalEvent(task, ref ev);
            comp.Description += ev.Description;
        }
    }

    public List<Entity<SWTaskComponent>> GetFactionTasks(string? faction)
    {
        var res = new List<Entity<SWTaskComponent>>();
        if (faction == null)
        {
            return res;
        }
        Entity<SWFactionComponent>? ent = _swFaction.GetFactionEntity(faction);
        if (ent == null || !TryComp<SWTaskContainerComponent>(ent.Value.Owner, out var taskComp))
        {
            return res;
        }


        res = taskComp.CurrentTasks;
        return res;
    }

    public void UpdateTaskProgress(EntityUid task, FixedPoint2 progress)
    {
        var query1 = EntityQueryEnumerator<SWTaskGameruleComponent>();
        query1.MoveNext(out var taskGameruleUid, out var _);
        RaiseLocalEvent(taskGameruleUid, new SWTaskProgressUpdateEvent(task, progress));
    }
}


