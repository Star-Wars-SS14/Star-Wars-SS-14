using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared._SW;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Autobalance;
public sealed class SWAutobalanceGameruleSystem : GameRuleSystem<SWAutoBalanceGameruleComponent>
{
    [Dependency] private readonly StationJobsSystem _stationJobsSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;


    private Entity<SWAutoBalanceGameruleComponent>? _gameruleSingleton = null;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWModifyAssignedJobsEvent>(OnModify);
    }

    private void OnModify(ref SWModifyAssignedJobsEvent ev)
    {
        if (_gameruleSingleton == null)
        {
            return;
        }
        BalanceJobs(ref ev.AssignedJobs, _gameruleSingleton.Value.Comp);
    }
    private void BalanceJobs(ref Dictionary<NetUserId, (ProtoId<JobPrototype>?, EntityUid)> assignedJobs,
                                 SWAutoBalanceGameruleComponent autobalance)
    {
        ISawmill s = Logger.GetSawmill("autobalance");
        int firstFactionCount = 0;
        int secondFactionCount = 0;
        var firstFactionProto = _prototypeManager.Index(autobalance.FirstFactionRelatedJobsId);
        var secondFactionProto = _prototypeManager.Index(autobalance.SecondRelatedJobsId);

        foreach (var job in assignedJobs)
        {
            var jobId = job.Value.Item1;
            if (jobId == null)
            {
                continue;
            }
            if (firstFactionProto.Jobs.Contains(jobId))
            {
                firstFactionCount++;
            }

            if (secondFactionProto.Jobs.Contains(jobId))
            {
                secondFactionCount++;
            }
        }

        s.Info($"firstFaction(republic) : {firstFactionCount};" +
            $"secondfaction(Empire) : {secondFactionCount}");

        if (firstFactionCount * autobalance.Ratio <= secondFactionCount)
        {
            // No autobalance needed
            s.Info($"{firstFactionCount} * {autobalance.Ratio} <= {secondFactionCount}; no balance needed");
            return;
        }
        else
        {
            //Remove some jobs of the first faction
            s.Info("Balancing started");
            //We use this flag to se if there are remaining jobs that we can disable
            bool jobRemain = true;
            while ((firstFactionCount * autobalance.Ratio > secondFactionCount) && jobRemain)
            {
                jobRemain = false;
                var keys = assignedJobs.Keys.ToArray();
                _random.Shuffle(keys);

                for (int i = 0; i < keys.Length; i++)
                {
                    var jobId = assignedJobs[keys[i]].Item1;
                    if (jobId == null)
                    {
                        continue;
                    }
                    if (firstFactionProto.Jobs.Contains(jobId))
                    {
                        s.Info($"disabled job {assignedJobs[keys[i]].Item1} for user {keys[i]}");
                        assignedJobs[keys[i]] = (null, assignedJobs[keys[i]].Item2);
                        firstFactionCount--;
                        jobRemain = true;
                    }
                }
            }
        }
    }

    protected override void Added(EntityUid uid, SWAutoBalanceGameruleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);
        if (_gameruleSingleton == null)
        {
            _gameruleSingleton = new Entity<SWAutoBalanceGameruleComponent>(uid, component);
        }
        else
        {
            QueueDel(uid);
        }
    }
    protected override void Started(EntityUid uid, SWAutoBalanceGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
    }

    public void Balance()
    {

    }

    public Entity<SWAutoBalanceGameruleComponent>? GetActiveRule()
    {
        return _gameruleSingleton;
    }
}
