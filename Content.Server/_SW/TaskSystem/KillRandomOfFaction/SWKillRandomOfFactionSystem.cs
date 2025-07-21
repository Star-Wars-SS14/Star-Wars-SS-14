using Content.Server._SW.TaskSystem.ParallelTask;
using Content.Server._SW.TaskSystem.TargetSelector;
using Content.Server.Administration.Commands;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Damage;
using Content.Shared.Gibbing.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
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
    [Dependency] private readonly SWTargetSelectorSystem _swTargetSelector = default!;
    ISawmill _s = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByName, before: [typeof(SWTargetSelectorSystem)]);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskInitEvent>(OnTaskInitByName, before: new[] {typeof(SWParallelTaskSystem),
                                                                                                           typeof(SWTargetSelectorSystem)});
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskProvideDescriptionEvent>(OnDescUpdatedByName);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskGetScoreEvent>(GetScoreByName);

        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByJob, before: [typeof(SWTargetSelectorSystem)]);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskInitEvent>(OnTaskInitByJob, before: new[] {typeof(SWParallelTaskSystem),
                                                                                                         typeof(SWTargetSelectorSystem)});
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskProvideDescriptionEvent>(OnDescUpdatedByJob);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskGetScoreEvent>(GetScoreByJob);

        SubscribeLocalEvent<SWFactionMemberComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWFactionMemberComponent, MindRemovedMessage>(OnMindRemoved);
        //SubscribeLocalEvent<SWFactionMemberComponent, AttemptEntityGibEvent>(OnGib);
        _s = Logger.GetSawmill("killOfFactionSystem");
    }

    

    private void OnCheckRequirementsByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
        //handled in SWTargetselector
    }

    private void OnTaskInitByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskInitEvent args)
    {
        if (!HasComp<SWTargetSelectorComponent>(ent.Owner))
        {
            _s.Error($"Task {Prototype(ent.Owner)} has component which needs a target selector, but doesnt have a target seleector!");
        }
    }

    private void OnDescUpdatedByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        var targetComp = EnsureComp<SWTargetSelectorComponent>(ent.Owner);
        if (targetComp.PersonToKill == null)
        {
            return;
        }
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        comp.Description = Loc.GetString(ent.Comp.DescriptionId, ("name", Name(targetComp.PersonToKill.Value)));
    }

    private void GetScoreByName(Entity<SWKillRandomByNameComponent> ent, ref SWTaskGetScoreEvent args)
    {
        var targetComp = EnsureComp<SWTargetSelectorComponent>(ent.Owner);
        if (targetComp.PersonToKill != null)
        {
            if (targetComp.JobOfTarget != null)
            {
                args.Score = ent.Comp.JobsToScores[targetComp.JobOfTarget];
                args.Handled = true;
            }
        }
    }
    private void OnCheckRequirementsByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskCheckRequirementsEvent args)
    {
       //handled in SWTargetselector
    }
    private void OnTaskInitByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskInitEvent args)
    {
        if (!HasComp<SWTargetSelectorComponent>(ent.Owner))
        {
            _s.Error($"Task {Prototype(ent.Owner)} has component which needs a target selector, but doesnt have a target seleector!");
        }
    }
    private void OnDescUpdatedByJob(Entity<SWKillRandomByJobComponent> ent, ref SWTaskProvideDescriptionEvent args)
    {
        if (args.Handled)
        {
            return;
        }

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
            var comp = EnsureComp<SWTaskComponent>(ent.Owner);
            comp.Description = Loc.GetString(ent.Comp.JobsToDescriptions[ent.Comp.JobProtoToKill]);
        }
        args.Handled = true;
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
        if (args.NewMobState == MobState.Dead || args.NewMobState == MobState.Invalid)
        {
            UpdateProgress(ent);
        }
    }

    private void OnMindRemoved(Entity<SWFactionMemberComponent> ent, ref MindRemovedMessage args)
    {
        //when entity is gibbbed mind is transfered to brain, and we cant use getmind on entity, because we will get the wrong mind
        //so we pass mind to updateProgess manually
        UpdateProgress(ent, args.Mind);
    }
    private void UpdateProgress(Entity<SWFactionMemberComponent> ent, EntityUid? mind = null)
    {
        var query = EntityQueryEnumerator<SWKillRandomByNameComponent>();
        while (query.MoveNext(out var uid, out var _))
        {
            var targetComp = EnsureComp<SWTargetSelectorComponent>(uid);
            if (targetComp.PersonToKill == ent.Owner)
            {
                _sWTasks.UpdateTaskProgress(uid, 1.0);
            }
        }

        var query2 = EntityQueryEnumerator<SWKillRandomByJobComponent>();
        while (query2.MoveNext(out var uid, out var comp))
        {
            var job = _swTargetSelector.GetJob(ent.Owner, mind);
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
