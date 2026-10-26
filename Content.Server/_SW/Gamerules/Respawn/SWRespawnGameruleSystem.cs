using Content.Server.Actions;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared._SW;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.Gamerules.Respawn;
public sealed partial class SWRespawnGameruleSystem : GameRuleSystem<SWRespawnGameruleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWWaitingRespawnComponent, SWRequestRespawnActionEvent>(OnTryRespawn);
        SubscribeLocalEvent<SWWaitingRespawnComponent, ComponentShutdown>(OnWaitingShutdown);
        SubscribeLocalEvent<SWRespawnGameruleComponent, ComponentShutdown>(OnRuleShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    // Resolve the live rule instead of retaining a component from a previous round or server instance.
    private bool TryGetActiveRule(out Entity<SWRespawnGameruleComponent> rule)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            rule = (uid, comp);
            return true;
        }

        rule = default;
        return false;
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
        {
            return;
        }

        if (!TryGetActiveRule(out var rule))
        {
            return;
        }

        if (!_player.TryGetSessionByEntity(ev.Target, out var session))
        {
            return;
        }

        var message = Loc.GetString(rule.Comp.WaitMessage, ("count", rule.Comp.DelayBeforeCanRespawn));
        _chatManager.DispatchServerMessage(session, message);
    }

    protected override void Started(EntityUid uid, SWRespawnGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        ClearWaiting();
    }

    protected override void Ended(EntityUid uid, SWRespawnGameruleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        ClearWaiting();
    }

    private void OnRuleShutdown(Entity<SWRespawnGameruleComponent> ent, ref ComponentShutdown args) => ClearWaiting();

    private void OnRoundRestart(RoundRestartCleanupEvent args) => ClearWaiting();

    private void ClearWaiting()
    {
        var query = EntityQueryEnumerator<SWWaitingRespawnComponent>();
        while (query.MoveNext(out var uid, out _))
            RemComp<SWWaitingRespawnComponent>(uid);
    }

    private void OnWaitingShutdown(Entity<SWWaitingRespawnComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.RespawnActionId);
        if (ent.Comp.RespawnActionId is { } action && !TerminatingOrDeleted(action))
            QueueDel(action);
        ent.Comp.RespawnActionId = null;
    }

    private void OnTryRespawn(Entity<SWWaitingRespawnComponent> ent, ref SWRequestRespawnActionEvent args)
    {
        if (args.Handled
            || !TryGetActiveRule(out var rule)
            || !TryComp<GhostComponent>(ent, out var ghost)
            || _timing.CurTime < ghost.TimeOfDeath + TimeSpan.FromSeconds(rule.Comp.DelayBeforeCanRespawn))
            return;

        if (!_player.TryGetSessionByEntity(ent, out var session))
        {
            return;
        }

        _gameTicker.Respawn(session);
        args.Handled = true;
    }

    protected override void ActiveTick(EntityUid ruleUid, SWRespawnGameruleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(ruleUid, component, gameRule, frameTime);

        if (!TryGetActiveRule(out var active) || active.Owner != ruleUid)
        {
            return;
        }

        var query = EntityQueryEnumerator<GhostComponent>();
        while (query.MoveNext(out var uid, out var ghostComp))
        {
            if (!HasComp<SWWaitingRespawnComponent>(uid)
                && _timing.CurTime >= ghostComp.TimeOfDeath + TimeSpan.FromSeconds(component.DelayBeforeCanRespawn))
            {
                var comp = EnsureComp<SWWaitingRespawnComponent>(uid);
                _actions.AddAction(uid, ref comp.RespawnActionId, component.RespawnActionId);
            }
        }
    }
}


