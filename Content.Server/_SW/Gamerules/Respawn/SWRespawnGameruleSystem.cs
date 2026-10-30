using Content.Server._SW.TaskSystem;
using Content.Server.Actions;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Echo.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared._SW;
using Content.Shared.Actions;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Roles.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Gamerules.Respawn;
public sealed partial class SWRespawnGameruleSystem : GameRuleSystem<SWRespawnGameruleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SWWaitingRespawnComponent, SWRequestRespawnActionEvent>(OnTryRespawn);
    }

    public static SWRespawnGameruleComponent? GameRuleSingleton;

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
        {
            return;
        }

        if (GameRuleSingleton == null)
        {
            return;
        }

        if (!_player.TryGetSessionByEntity(ev.Target, out var session))
        {
            return;
        }

        var message = Loc.GetString(GameRuleSingleton.WaitMessage, ("count", GameRuleSingleton.DelayBeforeCanRespawn));
        _chatManager.DispatchServerMessage(session, message);
    }

    protected override void Started(EntityUid uid, SWRespawnGameruleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (GameRuleSingleton == null)
        {
            GameRuleSingleton = component;
        }
    }


    private void OnTryRespawn(Entity<SWWaitingRespawnComponent> ent, ref SWRequestRespawnActionEvent args)
    {
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

        if (GameRuleSingleton == null)
        {
            return;
        }

        var query = EntityQueryEnumerator<GhostComponent>();
        while (query.MoveNext(out var uid, out var ghostComp))
        {
            if (HasComp<SWWaitingRespawnComponent>(uid))
            {
                continue;
            }

            if (_timing.CurTime > ghostComp.TimeOfDeath +
                TimeSpan.FromSeconds(GameRuleSingleton.DelayBeforeCanRespawn)
                && IsDeadPlayerGhost(uid))
            {
                var comp = EnsureComp<SWWaitingRespawnComponent>(uid);
                _actions.AddAction(uid, ref comp.RespawnActionId, component.RespawnActionId);
            }
        }
    }

    /// <summary>
    /// Observers and admin ghosts also have a GhostComponent, but they have nothing to respawn from.
    /// </summary>
    private bool IsDeadPlayerGhost(EntityUid ghost)
    {
        if (Prototype(ghost)?.ID == GameTicker.AdminObserverPrototypeName.Id)
        {
            return false;
        }

        return _mind.TryGetMind(ghost, out var mind, out _) && !_roles.MindHasRole<ObserverRoleComponent>(mind);
    }
}


