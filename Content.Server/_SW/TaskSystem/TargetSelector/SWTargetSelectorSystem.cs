using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.TargetSelector;

public sealed class SWTargetSelectorSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly SharedJobSystem _jobSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWTargetSelectorComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWTargetSelectorComponent, SWTaskInitEvent>(OnTaskInit);
    }

    //Мне лень писать это на английском, напишу так
    //Для одной фракции мы выбираем цели с разными именами, то есть для убийства по работе и убийства по имени будут выбраны разные сущности
    //Таки образом, с другими фракциями цели заданий могут пересекаться.
    //Таким образом, SWTargetSelector выбирает цели, уникальыне в рамках заданий фракции, то есть для любого задания фракции,
    //цели во всех этих компонентых будут разные
    private void OnCheckRequirements(Entity<SWTargetSelectorComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        if (!ent.Comp.SelectTargetOnInit)
        {
            return;
        }
        var query = EntityQueryEnumerator<SWTargetSelectorComponent, SWTaskComponent>();
        List<EntityUid?> targets = new();
        string thisTaskOwningfaction = EnsureComp<SWTaskComponent>(ent.Owner).OwningFaction;
        while (query.MoveNext(out var uid, out var targetComp, out var taskComp))
        {
            if (targetComp.Target != null && thisTaskOwningfaction == taskComp.OwningFaction)
            {
                targets.Add(targetComp.Target);
            }
        }

        var query2 = EntityQueryEnumerator<SWFactionMemberComponent>();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && !targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid))
            {
                return;
            }
        }
        args.Cancel();
    }
    private void OnTaskInit(Entity<SWTargetSelectorComponent> ent, ref SWTaskInitEvent args)
    {
        if (!ent.Comp.SelectTargetOnInit)
        {
            return;
        }
        var query = EntityQueryEnumerator<SWTargetSelectorComponent, SWTaskComponent>();
        List<EntityUid?> chosen_targets = new();
        string thisTaskOwningfaction = EnsureComp<SWTaskComponent>(ent.Owner).OwningFaction;
        while (query.MoveNext(out var uid, out var targetComp, out var taskComp))
        {
            if (targetComp.Target != null && thisTaskOwningfaction == taskComp.OwningFaction)
            {
                chosen_targets.Add(targetComp.Target);
            }
        }

        var query2 = EntityQueryEnumerator<SWFactionMemberComponent>();
        List<EntityUid> possible_targets = new();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && !chosen_targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid))
            {
                possible_targets.Add(uid);
            }
        }
        ent.Comp.Target = _random.Pick(possible_targets);
        ent.Comp.JobOfTarget = GetJob(ent.Comp.Target.Value);
    }

    public string? GetJob(EntityUid ent, EntityUid? mind = null)
    {
        if (mind == null)
        {
            mind = _mindSystem.GetMind(ent);
        }

        if (mind == null)
        {
            return null;
        }
        _jobSystem.MindTryGetJob(mind, out var prototype);
        if (prototype != null)
        {
            return prototype.ID;
        }
        return null;
    }

    public bool CheckJob(List<string> jobs, EntityUid ent)
    {

        var mind = _mindSystem.GetMind(ent);
        if (mind != null && jobs.Count() == 0)
        {
            return true; // Если jobs пустой, то мы считаем, что любая должность подходит
        }
        bool res = false;
        foreach (var job in jobs)
        {
            res = res || _jobSystem.MindHasJobWithId(mind, job);
        }
        return res;
    }
}
