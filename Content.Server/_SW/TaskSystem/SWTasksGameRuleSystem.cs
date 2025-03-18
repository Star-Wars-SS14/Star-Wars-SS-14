using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._SW.TaskSystem;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Random.Helpers;
using Robust.Shared.Random;
using Robust.Shared.Timing;
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
    ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("taskSystem");
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
            if (ev.Progress == 1.0)
            {
                RaiseLocalEvent(taskUid, new SWOnTaskCompleteEvent());
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
    public void TryAddRandomTask(Entity<SWFactionComponent> faction)
    {
        var taskProto = faction.Comp.AvailableTasks.Pick();
        var task = Spawn(taskProto);
        var comp = EnsureComp<SWTaskComponent>(task);
        faction.Comp.CurrentTasks.Add(new Entity<SWTaskComponent>(task, comp));
        faction.Comp.NextAddTime = _timing.CurTime + TimeSpan.FromSeconds(faction.Comp.AddIntervalInSeconds);
    }
}

[ByRefEvent]
public sealed partial class SWGetProgressEvent : EntityEventArgs
{
    public FixedPoint2 Progress = 0.0;
}

public sealed partial class SWOnTaskCompleteEvent : EntityEventArgs
{

}

