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
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByName, after: [typeof(SWTargetSelectorSystem)],
                                                                                                                  before: [typeof(SWParallelTaskSystem)]);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskInitEvent>(OnTaskInitByName, after: [typeof(SWTargetSelectorSystem)],
                                                                                            before: [typeof(SWParallelTaskSystem)]);
        SubscribeLocalEvent<SWKillRandomByNameComponent, SWTaskProvideDescriptionEvent>(OnDescUpdatedByName, after: [typeof(SWTargetSelectorSystem)],
                                                                                                             before: [typeof(SWParallelTaskSystem)]);

        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskCheckRequirementsEvent>(OnCheckRequirementsByJob, after: [typeof(SWTargetSelectorSystem)],
                                                                                                                before: [typeof(SWParallelTaskSystem)]);
        SubscribeLocalEvent<SWKillRandomByJobComponent, SWTaskInitEvent>(OnTaskInitByJob, after: [typeof(SWTargetSelectorSystem)],
                                                                                          before: [typeof(SWParallelTaskSystem)]);

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
        if (targetComp.Target == null)
        {
            return;
        }
        var comp = EnsureComp<SWTaskComponent>(ent.Owner);
        comp.Description = Loc.GetString(ent.Comp.DescriptionId, ("name", Name(targetComp.Target.Value)));
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

    private void OnMobStateChanged(Entity<SWFactionMemberComponent> ent, ref MobStateChangedEvent args)
    {
        UpdateProgress(ent, args.NewMobState);
    }

    private void OnMindRemoved(Entity<SWFactionMemberComponent> ent, ref MindRemovedMessage args)
    {
        //when entity is gibbbed mind is transfered to brain, and we cant use getmind on entity, because we will get the wrong mind
        //so we pass mind to updateProgess manually
        UpdateProgress(ent, mind: args.Mind);
    }
    private void UpdateProgress(Entity<SWFactionMemberComponent> ent, MobState newState = MobState.Invalid, EntityUid? mind = null)
    {
        var query = EntityQueryEnumerator<SWKillRandomByNameComponent>();
        while (query.MoveNext(out var uid, out var _))
        {
            var targetComp = EnsureComp<SWTargetSelectorComponent>(uid);
            if (targetComp.Target == ent.Owner)
            {
                if (newState == MobState.Dead || newState == MobState.Invalid)
                {
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                }
                else
                {
                    _sWTasks.UpdateTaskProgress(uid, 0.0);
                }
            }
        }

        var query2 = EntityQueryEnumerator<SWKillRandomByJobComponent, SWTargetSelectorComponent>();
        while (query2.MoveNext(out var uid, out var comp, out var targetComp))
        {
            var job = _swTargetSelector.GetJob(ent.Owner, mind);
            var taskComp = EnsureComp<SWTaskComponent>(uid);
            if (taskComp.Completeness < 1.0
                && job != null && (targetComp.JobOfTarget == job))
            {
                if (newState == MobState.Dead || newState == MobState.Invalid)
                {
                    _sWTasks.UpdateTaskProgress(uid, 1.0);
                }
                else
                {
                    _sWTasks.UpdateTaskProgress(uid, 0.0);
                }
            }
        }
    }
}
