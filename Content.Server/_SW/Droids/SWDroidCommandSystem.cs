using System.Linq;
using System.Numerics;
using Content.Server._SW.TaskSystem;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
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
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SWFactionGameruleSystem _factions = default!;
    [Dependency] private readonly SWDroidTargetingTabletSystem _targetingTablet = default!;

    private const float UpdateDelay = 0.5f;
    private const float StayTime = 999999f;
    private float _updateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidTakeCommandActionEvent>(OnTakeCommand);
        SubscribeLocalEvent<SWDroidCommanderComponent, SWDroidOrderActionEvent>(OnOrder);
        SubscribeLocalEvent<SWDroidCommanderComponent, ComponentShutdown>(OnCommanderShutdown);
        SubscribeLocalEvent<SWDroidCommandableComponent, ComponentShutdown>(OnDroidShutdown);
        SubscribeLocalEvent<SWDroidCommandableComponent, MobStateChangedEvent>(OnDroidMobStateChanged);
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
            if (!HasComp<ActorComponent>(uid))
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
                continue;
            }

            if (commander == null)
                continue;

            RemoveTakeCommandAction(uid, commander, actions);
            CleanupInvalidDroids(uid, commander);

            if (commander.Droids.Count == 0)
            {
                RemoveOrderActions(uid, commander, actions);
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
        _targetingTablet.TryApplyHeldTabletConfig(uid, droid);
        EnsureOrderActions(uid, component);
        SetOrder(droid, droidComp, uid, component.CurrentOrder);

        _popup.PopupEntity(Loc.GetString("sw-droid-command-accepted"), uid, uid);
    }

    private void OnOrder(EntityUid uid, SWDroidCommanderComponent component, SWDroidOrderActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        component.CurrentOrder = args.Type;
        DoCommandCallout(uid, component, args.Type);

        foreach (var droid in component.Droids.ToArray())
        {
            if (!TryComp<SWDroidCommandableComponent>(droid, out var droidComp) ||
                droidComp.ActiveCommander != uid)
            {
                component.Droids.Remove(droid);
                continue;
            }

            SetOrder(droid, droidComp, uid, args.Type);
        }

        UpdateOrderActions(uid, component);
    }

    private void OnCommanderShutdown(EntityUid uid, SWDroidCommanderComponent component, ComponentShutdown args)
    {
        foreach (var droid in component.Droids)
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

        if (!TryComp<ActionsComponent>(uid, out var actions))
            return;

        RemoveTakeCommandAction(uid, component, actions);
        RemoveOrderActions(uid, component, actions);
    }

    private void OnDroidShutdown(EntityUid uid, SWDroidCommandableComponent component, ComponentShutdown args)
    {
        ReleaseDroidCommand(uid, component, false);
    }

    private void OnDroidMobStateChanged(EntityUid uid, SWDroidCommandableComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ReleaseDroidCommand(uid, component, true);
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

        return rank >= faction.MinimumDroidCommandRank;
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
        return !TerminatingOrDeleted(uid) && _mobState.IsAlive(uid);
    }

    private void SetOrder(EntityUid droid, SWDroidCommandableComponent droidComp, EntityUid commander, SWDroidOrderType order)
    {
        if (!TryComp<HTNComponent>(droid, out var htn))
            return;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        if (order == SWDroidOrderType.Follow)
        {
            _npc.SetBlackboard(droid, NPCBlackboard.FollowTarget, new EntityCoordinates(commander, Vector2.Zero));
        }
        else
        {
            _npc.SetBlackboard(droid, "DroidStayTime", StayTime);
        }

        _npc.SetBlackboard(droid, NPCBlackboard.CurrentOrders, order);
        _htn.Replan(htn);
        _npc.WakeNPC(droid, htn);
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

    private void EnsureOrderActions(EntityUid uid, SWDroidCommanderComponent component)
    {
        if (!TryComp<ActionsComponent>(uid, out var actions))
            return;

        _actions.AddAction(uid, ref component.OrderFollowActionEntity, component.OrderFollowAction, component: actions);
        _actions.AddAction(uid, ref component.OrderStayActionEntity, component.OrderStayAction, component: actions);
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
        _actions.StartUseDelay(component.OrderFollowActionEntity);
        _actions.StartUseDelay(component.OrderStayActionEntity);
    }

    private void RemoveTakeCommandAction(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        var actionEntity = new Entity<ActionsComponent?>(uid, actions);
        _actions.RemoveAction(actionEntity, component.TakeCommandActionEntity);
        component.TakeCommandActionEntity = null;
    }

    private void RemoveOrderActions(EntityUid uid, SWDroidCommanderComponent component, ActionsComponent actions)
    {
        var actionEntity = new Entity<ActionsComponent?>(uid, actions);
        _actions.RemoveAction(actionEntity, component.OrderFollowActionEntity);
        _actions.RemoveAction(actionEntity, component.OrderStayActionEntity);
        component.OrderFollowActionEntity = null;
        component.OrderStayActionEntity = null;
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
