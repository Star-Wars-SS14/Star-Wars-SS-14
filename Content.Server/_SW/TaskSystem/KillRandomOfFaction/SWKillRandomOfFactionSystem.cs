using Content.Shared._SW.TaskSystem;
using Content.Shared.Damage;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
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
        _s = Logger.GetSawmill("asgh");
    }

    private void OnDescUpdated(Entity<SWKillRandomOfFactionComponent> ent, ref SWTaskAddAdditionalDescEvent args)
    {
        if (ent.Comp.PersonToKill == null)
        {
            return;
        }
        args.Description = $"Ваша цель: {Name(ent.Comp.PersonToKill.Value)}. Должнсть цели: {GetJobName(ent.Comp.PersonToKill.Value)}";
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
            _s.Debug($"{CheckJob(ent.Comp.Jobs, uid)} {ent.Comp.Factions.Contains(comp.Faction)} {!targets.Contains(uid)}");
            if (ent.Comp.Factions.Contains(comp.Faction) && !targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid))
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
            if (ent.Comp.Factions.Contains(comp.Faction) && !chosen_targets.Contains(uid) && CheckJob(ent.Comp.Jobs, uid))
            {
                possible_targets.Add(uid);
            }
        }
        ent.Comp.PersonToKill = _random.Pick(possible_targets);
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
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                }
            }
        }
    }

    private bool CheckJob(List<string> jobs, EntityUid ent)
    {
        
        var mind = _mindSystem.GetMind(ent);
        _s.Debug($"{jobs.Count()}");
        if (mind != null && jobs.Count() == 0)
        {
            return true; // Если jobs пустой, то мы считаем, что любая должность подходит
        }
        bool res = false;
        foreach (var job in jobs)
        {
            _s.Debug(job);
            res = res || _jobSystem.MindHasJobWithId(mind, job);
        }
        return res;
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
