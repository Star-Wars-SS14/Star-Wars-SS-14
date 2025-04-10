using Content.Server.Administration.Commands;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Damage;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using SixLabors.ImageSharp.ColorSpaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;

public sealed partial class SWKillRandomJobSystem : EntitySystem
{
    [Dependency] private readonly SWTasksGameRuleSystem _sWTasks = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly SharedJobSystem _jobSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByName);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWOnTaskInitEvent>(OnTaskInitByName);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskAddAdditionalDescEvent>(OnDescUpdatedByName);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskGetScoreEvent>(GetScoreByName);

        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByJob);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWOnTaskInitEvent>(OnTaskInitByJob);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskAddAdditionalDescEvent>(OnDescUpdatedByJob);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskGetScoreEvent>(GetScoreByJob);

        SubscribeLocalEvent<SWFactionMemberComponent, MobStateChangedEvent>(OnMobStateChanged);
    }
    private void OnCheckRequirementsByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var query = EntityQueryEnumerator<SWKillRandomByNameComponent>();
        List<EntityUid?> targets = new();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.PersonToKill != null)
            {
                targets.Add(comp.PersonToKill);
            }
        }

        var query2 = EntityQueryEnumerator<SWFactionMemberComponent>();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && !targets.Contains(uid) && CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid))
            {
                return;
            }
        }
        args.Cancel();
    }

    private void OnTaskInitByName(Entity<SWKillRandomByNameComponent> ent, ref SWOnTaskInitEvent args)
    {
        var query2 = EntityQueryEnumerator<SWKillRandomByNameComponent>();
        List<EntityUid?> chosen_targets = new();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (comp.PersonToKill != null)
            {
                chosen_targets.Add(comp.PersonToKill);
            }
        }
        var query = EntityQueryEnumerator<SWFactionMemberComponent>();
        List<EntityUid> possible_targets = new();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && !chosen_targets.Contains(uid) && CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid))
            {
                possible_targets.Add(uid);
            }
        }
        ent.Comp.PersonToKill = _random.Pick(possible_targets);
        ent.Comp.JobOfTarget = GetJob(ent.Comp.PersonToKill.Value);
    }

    private void OnDescUpdatedByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        if (ent.Comp.PersonToKill == null)
        {
            return;
        }
        args.Description = $"\nВаша цель: {Name(ent.Comp.PersonToKill.Value)}.\n";
    }

    private void GetScoreByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskGetScoreEvent args)
    {
        if (ent.Comp.PersonToKill != null)
        {
            if (ent.Comp.JobOfTarget != null)
            {
                args.Score = ent.Comp.JobsToScores[ent.Comp.JobOfTarget];
                args.Handled = true;
            }
        }
    }
    private void OnCheckRequirementsByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var query2 = EntityQueryEnumerator<SWFactionMemberComponent>();
        while (query2.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid))
            {
                return;
            }
        }
        args.Cancel();
    }
    private void OnTaskInitByJob(Entity<SWKillRandomByJobComponent> ent, ref SWOnTaskInitEvent args)
    {
        if (ent.Comp.KillAny)
        {
            return;
        }
        var query = EntityQueryEnumerator<SWFactionMemberComponent>();
        List<EntityUid> possible_targets = new();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (ent.Comp.Factions.Contains(comp.Faction) && CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid))
            {
                possible_targets.Add(uid);
            }
        }
        var tuid = _random.Pick(possible_targets);
        ent.Comp.JobProtoToKill = GetJob(tuid);

    }
    private void OnDescUpdatedByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        if (ent.Comp.KillAny)
        {
            args.Description = $"\nВозможные должности:\n";
            foreach (var job in ent.Comp.JobsToScores.Keys)
            {
                _prototypeManager.TryIndex(job, out JobPrototype? proto);
                args.Description += $"- {proto?.Name}\n";
            }

        }
        else if (ent.Comp.JobProtoToKill != null)
        {
            _prototypeManager.TryIndex(ent.Comp.JobProtoToKill, out JobPrototype? proto);
            args.Description += $"\nЦелевая должность: {proto?.Name}\n";
        }
    }
    private void GetScoreByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskGetScoreEvent args)
    {
        if (ent.Comp.JobProtoOfKilled != null)
        {
            if (ent.Comp.KillAny || ent.Comp.JobProtoToKill == ent.Comp.JobProtoOfKilled)
            {
                args.Score = ent.Comp.JobsToScores[ent.Comp.JobProtoOfKilled];
                args.Handled = true;
            }
        }
    }
    private void OnMobStateChanged(Entity<SWFactionMemberComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            var query = EntityQueryEnumerator<SWKillRandomByNameComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.PersonToKill == ent.Owner)
                {
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                }
            }

            var query2 = EntityQueryEnumerator<SWKillRandomByJobComponent>();
            while (query2.MoveNext(out var uid, out var comp))
            {
                var job = GetJob(ent.Owner);
                var taskComp = EnsureComp<SWTaskComponent>(uid);
                if (taskComp.Completeness < 1.0
                    && job != null && comp.JobsToScores.ContainsKey(job)
                       && (comp.JobProtoToKill == job || comp.KillAny))
                {
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                    comp.JobProtoOfKilled = job;
                }
            }
        }
    }

    private bool CheckJob(List<string> jobs, EntityUid ent)
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

    public string? GetJob(EntityUid ent)
    {
        var mind = _mindSystem.GetMind(ent);
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
    private string GetJobName(EntityUid ent)
    {
        var mind = _mindSystem.GetMind(ent);
        if (mind == null)
        {
            return "-";
        }
        return _jobSystem.MindTryGetJobName(mind);
    }
}
