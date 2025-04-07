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
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirements);
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWOnTaskInitEvent>(OnTaskInit);
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskAddAdditionalDescEvent>(OnDescUpdated);
        SubscribeLocalEvent<DamageableComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWKillRandomOfFactionComponent, SWTaskGetScoreEvent>(GetScore);
        _s = Logger.GetSawmill("asgh");
    }

    private void GetScore(Entity<SWKillRandomOfFactionComponent> ent, ref SWTaskGetScoreEvent args)
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

    private void OnDescUpdated(Entity<SWKillRandomOfFactionComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        if (ent.Comp.PersonToKill == null)
        {
            return;
        }
        args.Description += $"Ваша цель: {Name(ent.Comp.PersonToKill.Value)}. Должнсть цели: {GetJobName(ent.Comp.PersonToKill.Value)}\n";
        args.Description += $"Ваша цель: {Name(ent.Comp.PersonToKill.Value)}. Должнсть цели: {GetJobName(ent.Comp.PersonToKill.Value)}\n";
    }

    private void OnCheckRequirements(Entity<SWKillRandomOfFactionComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        var query = EntityQueryEnumerator<SWKillRandomOfFactionComponent>();
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

    private void OnTaskInit(Entity<SWKillRandomOfFactionComponent> ent, ref SWOnTaskInitEvent args)
    {
        var query2 = EntityQueryEnumerator<SWKillRandomOfFactionComponent>();
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

    private void OnMobStateChanged(Entity<DamageableComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            var query = EntityQueryEnumerator<SWKillRandomOfFactionComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (comp.PersonToKill == ent.Owner)
                {
                    _s.Debug("killed");
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
