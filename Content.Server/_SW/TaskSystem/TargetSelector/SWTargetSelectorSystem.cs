using Content.Server._SW.TaskSystem.KillRandomOfFaction;
using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles.Jobs;
using Content.Shared.Whitelist;
using Robust.Shared.Random;
using Robust.Shared.Timing;
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
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
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
        var taskCompThis = EnsureComp<SWTaskComponent>(ent);
        if (!ent.Comp.SelectTargetOnInit ||
            ent.Comp.InheritTargetFromParent && taskCompThis.ParentTask != null)
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

        var query2 = EntityQueryEnumerator<MindContainerComponent>();
        while (query2.MoveNext(out var uid, out var _))
        {
            if (!targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid)
                && PassWhitelist(ent, uid))
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

        var taskCompThis = EnsureComp<SWTaskComponent>(ent);
        if (ent.Comp.InheritTargetFromParent &&
            taskCompThis.ParentTask != null &&
            TryComp<SWTargetSelectorComponent>(taskCompThis.ParentTask.Value, out var parentComp))
        {
            ent.Comp.Target = parentComp.Target;
            ent.Comp.JobOfTarget = parentComp.JobOfTarget;
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

        var query2 = EntityQueryEnumerator<MindContainerComponent>();
        List<EntityUid> possible_targets = new();
        while (query2.MoveNext(out var uid, out var _))
        {
            if (!chosen_targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid)
                && PassWhitelist(ent, uid))
            {
                possible_targets.Add(uid);
            }
        }
        ent.Comp.Target = _random.Pick(possible_targets);
        ent.Comp.JobOfTarget = GetJob(ent.Comp.Target.Value);
    }

    private bool PassWhitelist(Entity<SWTargetSelectorComponent> ent, EntityUid uid)
    {
        if (ent.Comp.Whitelist == null)
        {
            return true;
        }
        return  _whitelistSystem.IsWhitelistPass(ent.Comp.Whitelist, uid);
    }

    public string? GetJob(EntityUid ent, EntityUid? mind = null)
    {
        if (!TryComp<SWFactionMemberComponent>(ent, out var comp))
        {
            return null;
        }

        return comp.Job;
    }

    public bool CheckJob(List<string> jobs, EntityUid ent)
    {
        if (!TryComp<SWFactionMemberComponent>(ent, out var comp))
        {
            return false;
        }

        if (jobs.Count() == 0)
        {
            return true; // Если jobs пустой, то мы считаем, что любая должность подходит
        }

        if (comp.Job == null)
        {
            return false;
        }

        return jobs.Contains(comp.Job);
    }
}
