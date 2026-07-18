using System.Linq;
using System.Numerics;
using Content.Server._SW.TaskSystem;
using Content.Server._SW.Targeting;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Shared._SW.Droids;
using Content.Shared._SW.TaskSystem;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chat;
using Content.Shared.Dataset;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._SW.Droids;

public sealed class SWDroidCommandSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SWAutoTargetingGunSystem _autoGun = default!;
    [Dependency] private readonly SWTargetingConfigTargetsSystem _targeting = default!;
    private const float UpdateDelay = 0.5f;
    private const float StayTime = 999999f;
    private const string DroidCommandTarget = "DroidCommandTarget";
    private const string DroidCommandTargetCoordinates = "DroidCommandTargetCoordinates";
    private const string DroidCommandTargetPathfind = "DroidCommandTargetPathfind";
    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidTakeCommandActionEvent>(OnTakeCommand);
        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidRelinquishCommandActionEvent>(OnRelinquishCommand);
        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidOrderActionEvent>(OnOrder);
        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidGoToActionEvent>(OnGoToOrder);
        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidAttackActionEvent>(OnAttackOrder);
        SubscribeLocalEvent<SWDroidCommanderComponent, MobStateChangedEvent>(OnCommanderMobStateChanged);
        SubscribeLocalEvent<SWDroidCommanderComponent, ComponentShutdown>(OnCommanderShutdown);
        SubscribeLocalEvent<SWDroidCommandableComponent, ComponentShutdown>(OnDroidShutdown);
        SubscribeLocalEvent<SWDroidCommandableComponent, MobStateChangedEvent>(OnDroidMobStateChanged);
        SubscribeLocalEvent<SWDroidCommandableComponent, SWAutoTargetingGunAttemptEvent>(OnAutoGunAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateDelay)
            return;

        _updateAccumulator = 0f;

        var query = EntityQueryEnumerator<SWFactionMemberComponent, ActionsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var factionMember, out var actions, out var transform))
        {
            if (!HasComp<ActorComponent>(uid) || !_mobState.IsAlive(uid))
                continue;

            var commander = CompOrNull<SWDroidCommanderComponent>(uid);
            var hasNearbyDroid = TryFindNearbyDroidForAction(
                factionMember,
                transform,
                out _);

            if (hasNearbyDroid)
            {
                commander ??= EnsureComp<SWDroidCommanderComponent>(uid);
                EnsureTakeCommandAction(uid, commander, actions);
            }
            else if (commander != null)
            {
                RemoveTakeCommandAction(uid, commander, actions);
            }

            if (commander == null)
                continue;

            CleanupInvalidDroids(uid, commander);

            if (TryFindNearbyCommandedDroidForAction(uid, commander, transform, out _))
                EnsureRelinquishCommandAction(uid, commander, actions);
            else
                RemoveRelinquishCommandAction(uid, commander, actions);

            if (commander.Droids.Count == 0)
            {
                RemoveOrderActions(uid, commander, actions);

                if (!hasNearbyDroid)
                    RemCompDeferred<SWDroidCommanderComponent>(uid);
            }
        }
    }

    private void OnTakeCommand(EntityUid uid, SWDroidCommanderComponent component, SWDroidTakeCommandActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!TryComp<SWFactionMemberComponent>(uid, out var factionMember) ||
            !TryComp(uid, out TransformComponent? transform) ||
            !TryFindCommandableDroid(uid, factionMember, transform, out var droid, out var droidComp, out var rank))
        {
            _popup.PopupEntity(Loc.GetString("sw-droid-command-no-droids-nearby"), uid, uid);
            return;
        }

        if (droidComp.ActiveCommander != null &&
            droidComp.ActiveCommander != uid &&
            TryComp<SWDroidCommanderComponent>(droidComp.ActiveCommander, out var previousCommander))
        {
            previousCommander.Droids.Remove(droid);
            UpdateOrderActions(droidComp.ActiveCommander.Value, previousCommander);
        }

        droidComp.ActiveCommander = uid;
        droidComp.ActiveCommanderRank = rank;
        Dirty(droid, droidComp);

        component.Droids.Add(droid);
        EnsureOrderActions(uid, component);
        SetOrder(droid, droidComp, uid, component.CurrentOrder, component.CurrentGoToTarget, component.CurrentAttackTarget);

        _popup.PopupEntity(Loc.GetString("sw-droid-command-accepted"), uid, uid);
    }

    private void OnRelinquishCommand(EntityUid uid, SWDroidCommanderComponent component, SWDroidRelinquishCommandActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!TryComp(uid, out TransformComponent? transform) ||
            !TryFindNearbyCommandedDroidForAction(uid, component, transform, out var droid) ||
            !TryComp<SWDroidCommandableComponent>(droid, out var droidComp))
        {
            _popup.PopupEntity(Loc.GetString("sw-droid-command-no-droids-nearby"), uid, uid);
            return;
        }

        RelinquishDroidCommand(uid, component, droid, droidComp);
        _popup.PopupEntity(Loc.GetString("sw-droid-command-relinquished"), uid, uid);
    }

    private void OnOrder(EntityUid uid, SWDroidCommanderComponent component, SWDroidOrderActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        IssueOrder(uid, component, args.Type);
    }

    private void OnGoToOrder(EntityUid uid, SWDroidCommanderComponent component, SWDroidGoToActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        IssueOrder(uid, component, SWDroidOrderType.GoTo, coordinates: args.Target);
    }

    private void OnAttackOrder(EntityUid uid, SWDroidCommanderComponent component, SWDroidAttackActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        IssueOrder(uid, component, SWDroidOrderType.Attack, target: args.Target);
    }

    private void OnCommanderMobStateChanged(EntityUid uid, SWDroidCommanderComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        ReleaseCommanderDroids(uid, component);

        if (TryComp<ActionsComponent>(uid, out var actions))
        {
            RemoveTakeCommandAction(uid, component, actions);
            RemoveRelinquishCommandAction(uid, component, actions);
            RemoveOrderActions(uid, component, actions);
        }

        RemCompDeferred<SWDroidCommanderComponent>(uid);
    }

    private void IssueOrder(
        EntityUid uid,
        SWDroidCommanderComponent component,
        SWDroidOrderType order,
        EntityCoordinates? coordinates = null,
        EntityUid? target = null)
    {
        component.CurrentOrder = order;
        component.CurrentGoToTarget = order == SWDroidOrderType.GoTo ? coordinates : null;
        component.CurrentAttackTarget = order == SWDroidOrderType.Attack ? target : null;
        DoCommandCallout(uid, component, order);

        foreach (var droid in component.Droids.ToArray())
        {
            if (!TryComp<SWDroidCommandableComponent>(droid, out var droidComp) ||
                droidComp.ActiveCommander != uid)
            {
                component.Droids.Remove(droid);
                continue;
            }

            SetOrder(droid, droidComp, uid, order, coordinates, target);
        }

        UpdateOrderActions(uid, component);
    }

    private void OnCommanderShutdown(EntityUid uid, SWDroidCommanderComponent component, ComponentShutdown args)
    {
        ReleaseCommanderDroids(uid, component);

        if (!TryComp<ActionsComponent>(uid, out var actions))
            return;

        RemoveTakeCommandAction(uid, component, actions);
        RemoveRelinquishCommandAction(uid, component, actions);
        RemoveOrderActions(uid, component, actions);
    }

    private void ReleaseCommanderDroids(EntityUid uid, SWDroidCommanderComponent component)
    {
        foreach (var droid in component.Droids.ToArray())
        {
            if (!TryComp<SWDroidCommandableComponent>(droid, out var droidComp) ||
                droidComp.ActiveCommander != uid)
            {
                continue;
            }

            droidComp.ActiveCommander = null;
            droidComp.ActiveCommanderRank = 0;
            Dirty(droid, droidComp);
            SetOrder(droid, droidComp, uid, SWDroidOrderType.Stay);
        }

        component.Droids.Clear();
    }

    private void OnDroidShutdown(EntityUid uid, SWDroidCommandableComponent component, ComponentShutdown args)
    {
        ReleaseDroidCommand(uid, component, false);
        _targeting.UnregisterTargetFromAll(uid);
    }

    private void OnDroidMobStateChanged(EntityUid uid, SWDroidCommandableComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ReleaseDroidCommand(uid, component, true);
    }

    private void OnAutoGunAttempt(Entity<SWDroidCommandableComponent> ent, ref SWAutoTargetingGunAttemptEvent args)
    {
        if (!IsAvailableDroid(ent.Owner))
        {
            args.Cancel();
            return;
        }

        if (!args.ForcedTarget && !HasComp<SWTargetingConfigComponent>(ent.Owner))
        {
            args.Cancel();
            return;
        }

        if (args.ForcedTarget)
            return;
    }

    private void ReleaseDroidCommand(EntityUid uid, SWDroidCommandableComponent component, bool dirty)
    {
        if (component.ActiveCommander == null)
            return;

        var commanderUid = component.ActiveCommander.Value;
        if (TryComp<SWDroidCommanderComponent>(commanderUid, out var commander))
        {
            commander.Droids.Remove(uid);
            UpdateOrderActions(commanderUid, commander);
        }

        component.ActiveCommander = null;
        component.ActiveCommanderRank = 0;

        if (dirty)
            Dirty(uid, component);
    }

    private void RelinquishDroidCommand(
        EntityUid commanderUid,
        SWDroidCommanderComponent commander,
        EntityUid droid,
        SWDroidCommandableComponent droidComp)
    {
        commander.Droids.Remove(droid);

        droidComp.ActiveCommander = null;
        droidComp.ActiveCommanderRank = 0;
        Dirty(droid, droidComp);

        SetOrder(droid, droidComp, commanderUid, SWDroidOrderType.Stay);

        if (!TryComp<ActionsComponent>(commanderUid, out var actions))
            return;

        if (commander.Droids.Count == 0)
        {
            RemoveOrderActions(commanderUid, commander, actions);
            RemoveRelinquishCommandAction(commanderUid, commander, actions);
            return;
        }

        UpdateOrderActions(commanderUid, commander);
    }

    private bool TryFindCommandableDroid(
        EntityUid commander,
        SWFactionMemberComponent factionMember,
        TransformComponent commanderTransform,
        out EntityUid droid,
        out SWDroidCommandableComponent droidComp,
        out int rank)
    {
        droid = default;
        droidComp = default!;
        rank = 0;
        var closestDistance = float.MaxValue;

        var query = EntityQueryEnumerator<SWDroidCommandableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var commandable, out var droidTransform))
        {
            if (!IsAvailableDroid(uid) ||
                !TryGetCommandRank(factionMember, commandable.Faction, out var candidateRank) ||
                !CanTakeCommand(commander, commandable, candidateRank) ||
                !commanderTransform.Coordinates.TryDistance(EntityManager, droidTransform.Coordinates, out var distance) ||
                distance > commandable.CommandRange ||
                distance >= closestDistance)
            {
                continue;
            }

            droid = uid;
            droidComp = commandable;
            rank = candidateRank;
            closestDistance = distance;
        }

        return droid != default;
    }

    private bool TryFindNearbyDroidForAction(
        SWFactionMemberComponent factionMember,
        TransformComponent commanderTransform,
        out int rank)
    {
        rank = 0;

        var query = EntityQueryEnumerator<SWDroidCommandableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var commandable, out var droidTransform))
        {
            if (!IsAvailableDroid(uid) ||
                !TryGetCommandRank(factionMember, commandable.Faction, out rank) ||
                !commanderTransform.Coordinates.TryDistance(EntityManager, droidTransform.Coordinates, out var distance) ||
                distance > commandable.CommandRange)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryFindNearbyCommandedDroidForAction(
        EntityUid commander,
        SWDroidCommanderComponent commanderComp,
        TransformComponent commanderTransform,
        out EntityUid droid)
    {
        droid = default;
        var closestDistance = float.MaxValue;

        foreach (var candidate in commanderComp.Droids)
        {
            if (!TryComp<SWDroidCommandableComponent>(candidate, out var commandable) ||
                commandable.ActiveCommander != commander ||
                !IsAvailableDroid(candidate) ||
                !TryComp(candidate, out TransformComponent? droidTransform) ||
                !commanderTransform.Coordinates.TryDistance(EntityManager, droidTransform.Coordinates, out var distance) ||
                distance > commandable.CommandRange ||
                distance >= closestDistance)
            {
                continue;
            }

            droid = candidate;
            closestDistance = distance;
        }

        return droid != default;
    }

    private bool TryGetCommandRank(SWFactionMemberComponent factionMember, string factionId, out int rank)
    {
        rank = 0;

        if (factionMember.Faction != factionId)
            return false;

        var factionEntity = _factions.GetFactionEntity(factionId);
        if (factionEntity == null)
            return false;

        var faction = factionEntity.Value.Comp;
        if (factionMember.IsLeader)
        {
            rank = int.MaxValue;
            return true;
        }

        if (factionMember.Job == null ||
            !faction.CommandRanks.TryGetValue(new ProtoId<JobPrototype>(factionMember.Job), out rank))
        {
            return false;
        }

        return true;
    }

    private bool CanTakeCommand(EntityUid commander, SWDroidCommandableComponent droid, int rank)
    {
        if (droid.ActiveCommander == null ||
            TerminatingOrDeleted(droid.ActiveCommander.Value))
        {
            return true;
        }

        return rank > droid.ActiveCommanderRank;
    }

    private bool IsAvailableDroid(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && _mobState.IsAlive(uid) && HasAICore(uid);
    }

    private bool HasAICore(EntityUid uid)
    {
        if (!TryComp<BorgChassisComponent>(uid, out var chassis))
            return false;

        foreach (var module in chassis.ModuleContainer.ContainedEntities)
        {
            if (HasComp<SWDroidAICoreModuleComponent>(module))
                return true;
        }

        return false;
    }

    private void SetOrder(
        EntityUid droid,
        SWDroidCommandableComponent droidComp,
        EntityUid commander,
        SWDroidOrderType order,
        EntityCoordinates? coordinates = null,
        EntityUid? target = null)
    {
        if (!TryComp<HTNComponent>(droid, out var htn))
            return;

        htn.PlanningToken?.Cancel();
        htn.PlanningToken = null;
        htn.PlanningJob = null;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        StopRangedCombat(droid);
        ClearCombatTarget(htn);

        var htnOrder = order == SWDroidOrderType.Dismiss
            ? SWDroidOrderType.Stay
            : order;

        if (htnOrder == SWDroidOrderType.Follow)
        {
            StopFollowingMovement(droid, htn, false);
            _npc.SetBlackboard(droid, NPCBlackboard.FollowTarget, new EntityCoordinates(commander, Vector2.Zero));
        }
        else if (htnOrder == SWDroidOrderType.GoTo && coordinates != null)
        {
            StopFollowingMovement(droid, htn, false);
            _npc.SetBlackboard(droid, NPCBlackboard.FollowTarget, coordinates.Value);
            _npc.SetBlackboard(droid, "DroidStayTime", StayTime);
        }
        else if (htnOrder == SWDroidOrderType.Attack && target != null)
        {
            StopFollowingMovement(droid, htn);
            _npc.SetBlackboard(droid, DroidCommandTarget, target.Value);
            _npc.SetBlackboard(droid, DroidCommandTargetCoordinates, new EntityCoordinates(target.Value, Vector2.Zero));
            _autoGun.SetForcedTarget(droid, target.Value);
        }
        else
        {
            StopFollowingMovement(droid, htn);
            _npc.SetBlackboard(droid, "DroidStayTime", StayTime);
        }

        if (htnOrder != SWDroidOrderType.Attack)
            _autoGun.SetForcedTarget(droid, null);

        _npc.SetBlackboard(droid, NPCBlackboard.CurrentOrders, htnOrder);
        _htn.Replan(htn);
        _npc.WakeNPC(droid, htn);
    }

    public bool TrySetFollowTarget(EntityUid droid, EntityUid target)
    {
        if (!IsAvailableDroid(droid) ||
            !TryComp<SWDroidCommandableComponent>(droid, out var droidComp))
        {
            return false;
        }

        SetOrder(droid, droidComp, target, SWDroidOrderType.Follow);
        return true;
    }

    public bool TryStopFollowingDatapad(EntityUid droid, EntityUid datapad)
    {
        if (!IsFollowingDatapad(droid, datapad) ||
            !IsAvailableDroid(droid) ||
            !TryComp<SWDroidCommandableComponent>(droid, out var droidComp))
        {
            return false;
        }

        SetOrder(droid, droidComp, datapad, SWDroidOrderType.Dismiss);
        return true;
    }

    public bool IsFollowingDatapad(EntityUid droid, EntityUid datapad)
    {
        if (!TryComp<HTNComponent>(droid, out var htn) ||
            !htn.Blackboard.TryGetValue<SWDroidOrderType>(NPCBlackboard.CurrentOrders, out var order, EntityManager) ||
            order != SWDroidOrderType.Follow ||
            !htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var followTarget, EntityManager))
        {
            return false;
        }

        return followTarget.EntityId == datapad;
    }

    private void StopFollowingMovement(EntityUid droid, HTNComponent htn, bool removeFollowTarget = true)
    {
        _steering.Unregister(droid);
        if (removeFollowTarget)
            htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        htn.Blackboard.Remove<EntityCoordinates>("FollowIdleTarget");
        htn.Blackboard.Remove<float>("IdleTime");
    }

    private void ClearCombatTarget(HTNComponent htn)
    {
        htn.Blackboard.Remove<EntityUid>("Target");
        htn.Blackboard.Remove<EntityUid>(DroidCommandTarget);
        htn.Blackboard.Remove<EntityCoordinates>(DroidCommandTargetCoordinates);
        htn.Blackboard.Remove<PathResultEvent>(DroidCommandTargetPathfind);
    }

    private void StopRangedCombat(EntityUid droid)
    {
        if (HasComp<NPCRangedCombatComponent>(droid))
            RemComp<NPCRangedCombatComponent>(droid);
    }

    private void DoCommandCallout(EntityUid uid, SWDroidCommanderComponent component, SWDroidOrderType order)
    {
        if (!component.OrderCallouts.TryGetValue(order, out var datasetId) ||
            !_prototype.TryIndex<LocalizedDatasetPrototype>(datasetId, out var datasetPrototype))
        {
            return;
        }

        var message = _random.Pick(datasetPrototype);
        _chat.TrySendInGameICMessage(uid, message, InGameICChatType.Speak, true);
    }

    private void EnsureTakeCommandAction(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        _actions.AddAction(uid, ref component.TakeCommandActionEntity, component.TakeCommandAction, component: actions);
    }

    private void EnsureRelinquishCommandAction(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        _actions.AddAction(uid, ref component.RelinquishCommandActionEntity, component.RelinquishCommandAction, component: actions);
    }

    private void EnsureOrderActions(EntityUid uid, SWDroidCommanderComponent component)
    {
        if (!TryComp<ActionsComponent>(uid, out var actions))
            return;

        _actions.AddAction(uid, ref component.CommandMenuActionEntity, component.CommandMenuAction, component: actions);
        _actions.AddAction(uid, ref component.OrderFollowActionEntity, component.OrderFollowAction, component: actions);
        _actions.AddAction(uid, ref component.OrderStayActionEntity, component.OrderStayAction, component: actions);
        _actions.AddAction(uid, ref component.OrderGoToActionEntity, component.OrderGoToAction, component: actions);
        _actions.AddAction(uid, ref component.OrderAttackActionEntity, component.OrderAttackAction, component: actions);
        _actions.AddAction(uid, ref component.OrderDismissActionEntity, component.OrderDismissAction, component: actions);
        UpdateOrderActions(uid, component);
    }

    private void UpdateOrderActions(EntityUid uid, SWDroidCommanderComponent component)
    {
        if (component.Droids.Count == 0)
        {
            if (TryComp<ActionsComponent>(uid, out var actions))
                RemoveOrderActions(uid, component, actions);

            return;
        }

        _actions.SetToggled(component.OrderFollowActionEntity, component.CurrentOrder == SWDroidOrderType.Follow);
        _actions.SetToggled(component.OrderStayActionEntity, component.CurrentOrder == SWDroidOrderType.Stay);
        _actions.SetToggled(component.OrderGoToActionEntity, component.CurrentOrder == SWDroidOrderType.GoTo);
        _actions.SetToggled(component.OrderAttackActionEntity, component.CurrentOrder == SWDroidOrderType.Attack);
        _actions.SetToggled(component.OrderDismissActionEntity, component.CurrentOrder == SWDroidOrderType.Dismiss);
        _actions.StartUseDelay(component.CommandMenuActionEntity);
        _actions.StartUseDelay(component.OrderFollowActionEntity);
        _actions.StartUseDelay(component.OrderStayActionEntity);
        _actions.StartUseDelay(component.OrderGoToActionEntity);
        _actions.StartUseDelay(component.OrderAttackActionEntity);
        _actions.StartUseDelay(component.OrderDismissActionEntity);
    }

    private void RemoveTakeCommandAction(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        if (component.TakeCommandActionEntity == null)
            return;

        var actionEntity = new Entity<ActionsComponent?>(uid, actions);
        _actions.RemoveAction(actionEntity, component.TakeCommandActionEntity);
        component.TakeCommandActionEntity = null;
    }

    private void RemoveRelinquishCommandAction(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        if (component.RelinquishCommandActionEntity == null)
            return;

        var actionEntity = new Entity<ActionsComponent?>(uid, actions);
        _actions.RemoveAction(actionEntity, component.RelinquishCommandActionEntity);
        component.RelinquishCommandActionEntity = null;
    }

    private void RemoveOrderActions(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        var actionEntity = new Entity<ActionsComponent?>(uid, actions);
        _actions.RemoveAction(actionEntity, component.CommandMenuActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderFollowActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderStayActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderGoToActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderAttackActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderDismissActionEntity);
        component.CommandMenuActionEntity = null;
        component.OrderFollowActionEntity = null;
        component.OrderStayActionEntity = null;
        component.OrderGoToActionEntity = null;
        component.OrderAttackActionEntity = null;
        component.OrderDismissActionEntity = null;
    }

    private void CleanupInvalidDroids(EntityUid commanderUid, SWDroidCommanderComponent component)
    {
        foreach (var droid in component.Droids.ToArray())
        {
            if (!TryComp<SWDroidCommandableComponent>(droid, out var droidComp) ||
                droidComp.ActiveCommander != commanderUid ||
                !IsAvailableDroid(droid))
            {
                component.Droids.Remove(droid);
            }
        }
    }
}
