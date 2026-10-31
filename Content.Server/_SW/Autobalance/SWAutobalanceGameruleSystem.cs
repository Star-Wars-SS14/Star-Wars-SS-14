using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._SW;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
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
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWModifyAssignedJobsEvent>(OnModify);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnPlayersAssigned);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<SWAutoBalanceMemberComponent, MobStateChangedEvent>(OnMemberStateChanged);
        SubscribeLocalEvent<SWAutoBalanceMemberComponent, PlayerAttachedEvent>(OnMemberAttached);
        SubscribeLocalEvent<SWAutoBalanceMemberComponent, PlayerDetachedEvent>(OnMemberDetached);
        SubscribeLocalEvent<SWAutoBalanceMemberComponent, ComponentShutdown>(OnMemberShutdown);
    }

    private void OnMemberStateChanged(Entity<SWAutoBalanceMemberComponent> ent, ref MobStateChangedEvent args)
    {
        ChangeAvailableJobsToFitAutoBalance();
    }

    private void OnMemberAttached(Entity<SWAutoBalanceMemberComponent> ent, ref PlayerAttachedEvent args)
    {
        ChangeAvailableJobsToFitAutoBalance();
    }

    private void OnMemberDetached(Entity<SWAutoBalanceMemberComponent> ent, ref PlayerDetachedEvent args)
    {
        ChangeAvailableJobsToFitAutoBalance();
    }

    private void OnMemberShutdown(Entity<SWAutoBalanceMemberComponent> ent, ref ComponentShutdown args)
    {
        ChangeAvailableJobsToFitAutoBalance();
    }

    private void OnModify(ref SWModifyAssignedJobsEvent ev)
    {
        var rules = QueryActiveRules();

        if (rules.MoveNext(out var uid, out var _, out var comp, out var _))
        {
            BalanceJobs(ref ev.AssignedJobs, comp);
        }
    }

    private void OnPlayersAssigned(RulePlayerJobsAssignedEvent ev)
    {
        var rules = QueryActiveRules();

        if (rules.MoveNext(out var uid, out var _, out var comp, out var _))
        {
            ChangeAvailableJobsToFitAutoBalance();
        }
    }

    private void OnSpawn(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId == null)
        {
            return;
        }

        var rules = QueryActiveRules();

        if (!rules.MoveNext(out var uid, out var _, out var comp, out var _))
        {
            return;
        }

        var autobalance = comp;

        var firstFactionProto = _prototypeManager.Index(autobalance.FirstFactionRelatedJobsId);
        var secondFactionProto = _prototypeManager.Index(autobalance.SecondRelatedJobsId);

        if (firstFactionProto.Jobs.Contains(ev.JobId))
        {
            EnsureComp<SWAutoBalanceMemberComponent>(ev.Mob).FirstFaction = true;
        }

        if (secondFactionProto.Jobs.Contains(ev.JobId))
        {
            EnsureComp<SWAutoBalanceMemberComponent>(ev.Mob).FirstFaction = false;
        }
        if (autobalance.JobsSpawned.ContainsKey(ev.JobId))
        {
            autobalance.JobsSpawned[ev.JobId]++;
        }
        else
        {
            autobalance.JobsSpawned[ev.JobId] = 1;
        }

        ChangeAvailableJobsToFitAutoBalance();
    }

    private void ChangeAvailableJobsToFitAutoBalance()
    {
        var rules = QueryActiveRules();

        if (!rules.MoveNext(out var uid, out var _, out var comp, out var _))
        {
            return;
        }
        var autobalance = comp;

        var firstFactionProto = _prototypeManager.Index(autobalance.FirstFactionRelatedJobsId);
        var (firstFactionAlive, secondFactionAlive) = CountLivingPlayers();
        var availableOfFirstFaction = secondFactionAlive / autobalance.Ratio - firstFactionAlive;

        foreach (var station in GetSpawnableStations())
        {
            foreach (var (id, amount) in _stationJobsSystem.GetRoundStartJobs(station))
            {
                if (amount == null || !firstFactionProto.Jobs.Contains(id))
                {
                    continue;
                }

                // Slots are lives: a death opens the budget again, but never gives the used slot back.
                autobalance.JobsSpawned.TryGetValue(id, out var spawned);
                var slots = Math.Max(0, Math.Min(availableOfFirstFaction, amount.Value - spawned));
                _stationJobsSystem.TrySetJobSlot(station, id, slots);
            }
        }
    }

    /// <summary>
    /// Counts mobs of connected players that are not dead yet. A critical player still counts.
    /// </summary>
    private (int First, int Second) CountLivingPlayers()
    {
        var first = 0;
        var second = 0;
        var query = EntityQueryEnumerator<SWAutoBalanceMemberComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var member, out _))
        {
            if (TerminatingOrDeleted(uid) || _mobState.IsDead(uid))
            {
                continue;
            }

            if (member.FirstFaction)
            {
                first++;
            }
            else
            {
                second++;
            }
        }

        return (first, second);
    }

    private List<EntityUid> GetSpawnableStations()
    {
        var spawnableStations = new List<EntityUid>();
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            spawnableStations.Add(uid);
        }

        return spawnableStations;
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

        //Remove only the excess jobs of the first faction
        s.Info("Balancing started");
        var keys = assignedJobs.Keys.ToArray();
        _random.Shuffle(keys);

        foreach (var key in keys)
        {
            if (firstFactionCount * autobalance.Ratio <= secondFactionCount)
            {
                break;
            }

            var jobId = assignedJobs[key].Item1;
            if (jobId == null || !firstFactionProto.Jobs.Contains(jobId))
            {
                continue;
            }

            s.Info($"disabled job {jobId} for user {key}");
            assignedJobs[key] = (null, assignedJobs[key].Item2);
            firstFactionCount--;
        }
    }

    protected override void Started(EntityUid uid, SWAutoBalanceGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

    }

    protected override void Ended(EntityUid uid, SWAutoBalanceGameruleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {

    }

    public Entity<SWAutoBalanceGameruleComponent>? GetActiveRule()
    {
        var rules = QueryActiveRules();

        if (!rules.MoveNext(out var uid, out var _, out var comp, out var _))
        {
            return null;
        }
        else
        {
            return new Entity<SWAutoBalanceGameruleComponent>(uid, comp);
        }
    }
}
