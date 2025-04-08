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
    ISawmill _s = default!;
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
        _s = Logger.GetSawmill("asgh");
    }

    private void OnTaskInitByJob(Entity<SWKillRandomByJobComponent> ent, ref SWOnTaskInitEvent args)
    {
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
        ent.Comp.JobProtoOfKilled = GetJob(tuid);
        _s.Debug($"сщ : {ent.Comp.JobsToScores.Keys.Count}");
    }

    private void OnDescUpdatedByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        args.Description += $"\nЦелевая должность: {ent.Comp.JobProtoOfKilled}\n";
    }
    private void GetScoreByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskGetScoreEvent args)
    {
        if (ent.Comp.JobProtoOfKilled != null)
        {
            args.Score = ent.Comp.JobsToScores[ent.Comp.JobProtoOfKilled];
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

    private void GetScoreByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskGetScoreEvent args)
    {  
        if (ent.Comp.PersonToKill != null)
        {
            if (ent.Comp.JobOfTarget != null)
            {
                _s.Debug($"{ent.Comp.JobsToScores.Keys.Count()}");
                _s.Debug($"{ent.Comp.JobsToScores.Keys.ToArray()[0]}");
                args.Score = ent.Comp.JobsToScores[ent.Comp.JobOfTarget];
            }
        }
    }

    private void OnDescUpdatedByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        if (ent.Comp.PersonToKill == null)
        {
            return;
        }
        args.Description += $"\nВаша цель: {Name(ent.Comp.PersonToKill.Value)}.\n";
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
            _s.Debug($"{CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid)} {ent.Comp.Factions.Contains(comp.Faction)} {!targets.Contains(uid)}");
            if (ent.Comp.Factions.Contains(comp.Faction) && !targets.Contains(uid) && CheckJob(ent.Comp.JobsToScores.Keys.ToList(), uid))
            {
                return;
            }
        }
        _s.Debug("cancelled");
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
        _s.Debug("initted");
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
        _s.Debug($"сщ : {ent.Comp.JobsToScores.Keys.Count}");
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
                    _s.Debug("killed");
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                }
            }

            var query2 = EntityQueryEnumerator<SWKillRandomByJobComponent>();
            while (query2.MoveNext(out var uid, out var comp))
            {
                if (comp.Factions.Contains(ent.Comp.Faction) && comp.JobProtoOfKilled == GetJob(ent.Owner))
                {
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
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
