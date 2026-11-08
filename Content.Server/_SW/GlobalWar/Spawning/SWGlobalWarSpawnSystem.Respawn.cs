using System.Linq;
using Content.Server._SW;
using Content.Server._SW.ForceUsers.SendMessageAbility;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._SW;
using Content.Shared._SW.Loadouts;
using Content.Shared._SW.ForceAbilities;
using Content.Shared._SW.ForceAbilities.SWHealAbility;
using Content.Shared._SW.ForceAbilities.SWStrangulatуAbility;
using Content.Shared._SW.GlobalWar;
using Content.Shared.Actions;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW.GlobalWar.Spawning;

public sealed partial class SWGlobalWarSpawnSystem
{
    private static readonly TimeSpan RespawnSyncPeriod = TimeSpan.FromSeconds(2);
    private static readonly ProtoId<TagPrototype> ForceUserTag = "ForceUser";

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly ForceAbilityDistanceInteractSystem _forceInteract = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    private readonly Dictionary<NetUserId, (Guid Life, TimeSpan ReadyAt, string? Civilian, bool Carrier, string? Force)>
        _lastRespawnStates = new();
    private readonly HashSet<NetUserId> _pendingJoins = new();
    private (NetUserId User, ProtoId<JobPrototype> Job, Guid Life)? _respawn;
    private bool _respawnCommitted;
    private string? _respawnRefusal;
    private TimeSpan _nextRespawnSync;

    private void InitializeRespawn()
    {
        SubscribeNetworkEvent<SWGlobalWarRespawnRequestEvent>(OnRespawnRequest);
        SubscribeLocalEvent<SWGlobalWarLifeComponent, EntityTerminatingEvent>(OnBodyTerminating);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RoundEndMessageEvent>(_ => CloseRespawnWindows());
    }

    /// <summary>Reads the moment a recorded death becomes eligible. Null while the life is alive or unknown.</summary>
    public TimeSpan? GetRespawnReadyAt(NetUserId user)
    {
        return _lives.TryGetValue(user, out var life) && life.Death is { } death &&
               _rules.TryGetGlobalWarGamerule(out var rule) &&
               TryComp<SWGlobalWarSpawnConfigurationComponent>(rule.Owner, out var configuration)
            ? death + configuration.RespawnDelay : null;
    }

    /// <summary>Reads the identifier of the player's current life, which a return request must name.</summary>
    public bool TryGetLife(NetUserId user, out Guid life)
    {
        life = default;
        if (!_lives.TryGetValue(user, out var record))
            return false;
        life = record.Id;
        return true;
    }

    /// <summary>
    /// Ends the named dead life and continues in a new one on the same side. Nothing is changed unless the whole
    /// operation succeeds, and a failure leaves the old life available for revival or another attempt.
    /// </summary>
    public bool TryRespawn(ICommonSession session, Guid lifeId, bool force, out string message)
    {
        message = Loc.GetString("sw-global-war-spawn-unavailable");
        var user = session.UserId;
        if (_respawn != null || !EnsureStations(out var rule, out var configuration))
            return false;
        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            message = Loc.GetString("sw-global-war-respawn-round-over");
            return false;
        }
        if (!_lives.TryGetValue(user, out var life) || life.Id != lifeId)
        {
            message = Loc.GetString("sw-global-war-respawn-stale");
            return false;
        }
        if (!CanReplaceLife(user, life, configuration, out message) ||
            !TryGetReturnRole(user, life, configuration, force, out var job, out var role, out message) ||
            !TryPickPoint(rule, role, out _, out message) ||
            !_roleStations.TryGetValue(life.Faction, out var station))
            return false;

        var oldMind = life.Mind;
        var oldCurrent = CompOrNull<MindComponent>(oldMind)?.CurrentEntity;
        // The place returns to the registry for the one synchronous call below; its result is decided before anyone
        // else can act, so no other player can observe it as free.
        _assignments.TryReleaseForceCarrier(user, life.Body, out var carrier);
        _respawn = (user, job, life.Id);
        _respawnCommitted = false;
        _respawnRefusal = null;
        try
        {
            _ticker.MakeJoinGame(session, station, job);
        }
        catch (Exception e)
        {
            Log.Error($"Campaign return of {session} failed: {e}");
            _respawnCommitted = false;
            RecoverMind(user, oldMind);
        }
        finally
        {
            _respawn = null;
        }

        if (!_respawnCommitted)
        {
            _lives[user] = life;
            if (carrier != null)
                _assignments.RestoreForceCarrier(carrier);
            message = _respawnRefusal ?? Loc.GetString("sw-global-war-spawn-unavailable");
            SendRespawnState(user, life);
            return false;
        }

        EndLife(life, oldMind, oldCurrent);
        SendRespawnState(user, _lives[user]);
        message = string.Empty;
        return true;
    }

    private void OnRespawnRequest(SWGlobalWarRespawnRequestEvent args, EntitySessionEventArgs session)
    {
        if (!_rules.TryGetGlobalWarGamerule(out _))
            return;
        var success = TryRespawn(session.SenderSession, args.Life, args.Force, out var message);
        RaiseNetworkEvent(new SWGlobalWarRespawnResultEvent(success, message), session.SenderSession);
    }

    /// <summary>Checks the term and that this exact body and mind are still the player's own.</summary>
    private bool CanReplaceLife(NetUserId user, SWGlobalWarLife life,
        SWGlobalWarSpawnConfigurationComponent configuration, out string reason)
    {
        reason = LifeRefusal(life, configuration);
        if (life.Death is not { } death || _timing.CurTime < death + configuration.RespawnDelay)
            return false;
        reason = Loc.GetString("sw-global-war-respawn-stale");
        if (!_minds.TryGetMind(user, out var mind) || mind.Value.Owner != life.Mind)
            return false;
        // Another mind took the old body after the player agreed to nothing of the sort: do not end their character.
        if (TryComp<MindContainerComponent>(life.Body, out var container) && container.Mind is { } occupant &&
            occupant != life.Mind)
            return false;
        var owned = mind.Value.Comp.OwnedEntity;
        if (owned != null && owned != life.Body && !HasComp<GhostComponent>(owned.Value))
            return false;
        reason = string.Empty;
        return true;
    }

    private bool TryGetReturnRole(NetUserId user, SWGlobalWarLife life,
        SWGlobalWarSpawnConfigurationComponent configuration, bool force,
        out ProtoId<JobPrototype> job, out SWGlobalWarSpawnRole role, out string reason)
    {
        role = default!;
        reason = Loc.GetString("sw-global-war-spawn-unavailable");
        if (!force)
            return TryGetCivilian(configuration, life.Faction, out job, out _) &&
                   configuration.Roles.TryGetValue(job, out role!);
        job = default;
        if (!_assignments.TryGetForceCarrier(user, out var carrier) || carrier.Body != life.Body)
        {
            reason = Loc.GetString("sw-global-war-respawn-force-not-carrier");
            return false;
        }
        foreach (var (id, entry) in configuration.Roles)
        {
            if (!entry.Force || entry.Faction != life.Faction)
                continue;
            job = id;
            role = entry;
            return true;
        }
        return false;
    }

    /// <summary>A spawn failed after the player was moved to a new mind: give them back their old character.</summary>
    private void RecoverMind(NetUserId user, EntityUid oldMind)
    {
        if (!_minds.TryGetMind(user, out var current) || current.Value.Owner == oldMind)
            return;
        var body = current.Value.Comp.OwnedEntity;
        _minds.WipeMind(current.Value.Owner, current.Value.Comp);
        if (body != null && !TerminatingOrDeleted(body.Value))
            QueueDel(body.Value);
        _minds.SetUserId(oldMind, user);
    }

    /// <summary>Keep the original kit on the old body; omit only the known kit from this return's spawn gear.</summary>
    private static void RemoveReturnSaberKit(SWMergedGear gear)
    {
        if (gear.Equipment.TryGetValue("pocket1", out var hilt) && hilt == "SWModularSaber")
            gear.Equipment.Remove("pocket1");

        if (!gear.Storage.TryGetValue("belt", out var items))
            return;

        items.Remove("SWSaberFocusStandard");
        items.Remove("SWSaberEmitterStandard");
        items.Remove("SWSaberEmitterHeat");
        items.Remove("SWSaberEmitterSlash");
        items.Remove("SWKyberCrystalBlue");
        items.Remove("SWKyberCrystalGreen");
        items.Remove("SWKyberCrystalYellow");
        items.Remove("SWKyberCrystalPurple");
        items.Remove("SWKyberCrystalRed");
    }

    private void EndLife(SWGlobalWarLife life, EntityUid oldMind, EntityUid? oldCurrent)
    {
        if (!TerminatingOrDeleted(life.Body))
        {
            EnsureComp<SWGlobalWarEndedLifeComponent>(life.Body);
            RemComp<SWGlobalWarLifeComponent>(life.Body);
            RemComp<GhostTakeoverAvailableComponent>(life.Body);
            RemComp<GhostRoleComponent>(life.Body);
            EndForceAbilities(life.Body);
        }
        _minds.UnVisit(oldMind);
        _minds.WipeMind(oldMind);
        if (oldCurrent != null && oldCurrent != life.Body && HasComp<GhostComponent>(oldCurrent.Value))
            QueueDel(oldCurrent.Value);
    }

    /// <summary>
    /// Force abilities live on the body as job components. Removing them with their actions ends the old carrier for
    /// good, whoever or whatever later revives this body; the 300 seconds of death already stopped running effects.
    /// </summary>
    private void EndForceAbilities(EntityUid body)
    {
        if (!_tags.HasTag(body, ForceUserTag))
            return;
        var actions = new HashSet<string>();
        if (TryComp<ForceAbilityDistanceInteractComponent>(body, out var interact))
        {
            actions.Add(interact.ActionAttractPrototype);
            _forceInteract.StopAttractingByForce(body, interact);
        }
        var moved = EntityQueryEnumerator<BeingMovedByForceComponent>();
        while (moved.MoveNext(out var item, out var movedComp))
        {
            if (movedComp.ForceUserMover == body)
                RaiseLocalEvent(item, new StopMovingByForceEvent(body));
        }
        if (TryComp<SWPushAbilityComponent>(body, out var push))
            actions.Add(push.ActionPrototype);
        if (TryComp<SWLightningAbilityComponent>(body, out var lightning))
            actions.Add(lightning.LigtningActionPrototype);
        if (TryComp<SWStrangulateAbilityComponent>(body, out var strangulate))
            actions.Add(strangulate.Action);
        if (TryComp<SWDashAbilityComponent>(body, out var dash) && dash.Action != null)
            actions.Add(dash.Action);
        if (TryComp<SWHealAbilityComponent>(body, out var heal))
            actions.Add(heal.HealAction);
        if (TryComp<SWSendMessageAbilityComponent>(body, out var influence))
            actions.Add(influence.Action);
        foreach (var action in _actions.GetActions(body).ToArray())
        {
            if (MetaData(action).EntityPrototype?.ID is { } id && actions.Contains(id))
                _actions.RemoveAction(action.AsNullable());
        }
        RemComp<ForceAbilityDistanceInteractComponent>(body);
        RemComp<SWForceUserBlockComponent>(body);
        RemComp<SWAbilityThrowSwordComponent>(body);
        RemComp<SWPushAbilityComponent>(body);
        RemComp<SWLightningAbilityComponent>(body);
        RemComp<SWStrangulateAbilityComponent>(body);
        RemComp<SWDashAbilityComponent>(body);
        RemComp<SWHealAbilityComponent>(body);
        RemComp<SWSendMessageAbilityComponent>(body);
        _tags.RemoveTag(body, ForceUserTag);
    }

    private string LifeRefusal(SWGlobalWarLife life, SWGlobalWarSpawnConfigurationComponent configuration)
    {
        if (life.Death is not { } death)
            return Loc.GetString("sw-global-war-spawn-life-active");
        var left = death + configuration.RespawnDelay - _timing.CurTime;
        return left > TimeSpan.Zero
            ? Loc.GetString("sw-global-war-spawn-life-unavailable", ("seconds", (int) Math.Ceiling(left.TotalSeconds)))
            : Loc.GetString("sw-global-war-spawn-use-return");
    }

    private void OnBodyTerminating(Entity<SWGlobalWarLifeComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_lives.TryGetValue(ent.Comp.User, out var life) && life.Body == ent.Owner && life.Death == null)
        {
            life.Death = _timing.CurTime;
            SendRespawnState(ent.Comp.User, life);
        }
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (_lives.TryGetValue(args.Player.UserId, out var life))
            SendRespawnState(args.Player.UserId, life);
    }

    /// <summary>Sends the current return options to their owner, and only to them.</summary>
    private void SendRespawnState(NetUserId user, SWGlobalWarLife life)
    {
        if (!_players.TryGetSessionById(user, out var session))
            return;
        if (life.Death is not { } death || !_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<SWGlobalWarSpawnConfigurationComponent>(rule.Owner, out var configuration) ||
            _ticker.RunLevel != GameRunLevel.InRound)
        {
            _lastRespawnStates.Remove(user);
            RaiseNetworkEvent(new SWGlobalWarRespawnStateEvent(Guid.Empty, TimeSpan.Zero, life.Faction, null, false, null),
                session);
            return;
        }
        var readyAt = death + configuration.RespawnDelay;
        var carrier = _assignments.TryGetForceCarrier(user, out var held) && held.Body == life.Body;
        string? civilianDenied = null;
        string? forceDenied = null;
        // The points are only worth checking once the player can act on the answer.
        if (_timing.CurTime >= readyAt)
        {
            civilianDenied = PreviewReturn(user, rule, life, configuration, false);
            forceDenied = carrier ? PreviewReturn(user, rule, life, configuration, true) : null;
        }
        _lastRespawnStates[user] = (life.Id, readyAt, civilianDenied, carrier, forceDenied);
        RaiseNetworkEvent(new SWGlobalWarRespawnStateEvent(life.Id, readyAt, life.Faction, civilianDenied, carrier,
            forceDenied), session);
    }

    private string? PreviewReturn(NetUserId user, Entity<SWGlobalWarGameruleComponent> rule, SWGlobalWarLife life,
        SWGlobalWarSpawnConfigurationComponent configuration, bool force)
    {
        return CanReplaceLife(user, life, configuration, out var reason) &&
               TryGetReturnRole(user, life, configuration, force, out _, out var role, out reason) &&
               TryPickPoint(rule, role, out _, out reason)
            ? null : reason;
    }

    /// <summary>Tells dead players when the term passes or a return point opens or closes.</summary>
    private void SyncRespawnStates()
    {
        if (_timing.CurTime < _nextRespawnSync)
            return;
        _nextRespawnSync = _timing.CurTime + RespawnSyncPeriod;
        if (!_rules.TryGetGlobalWarGamerule(out var rule) ||
            !TryComp<SWGlobalWarSpawnConfigurationComponent>(rule.Owner, out var configuration) ||
            _ticker.RunLevel != GameRunLevel.InRound)
            return;
        foreach (var (user, life) in _lives)
        {
            if (life.Death is not { } death || _timing.CurTime < death + configuration.RespawnDelay)
                continue;
            var carrier = _assignments.TryGetForceCarrier(user, out var held) && held.Body == life.Body;
            var current = (life.Id, death + configuration.RespawnDelay,
                PreviewReturn(user, rule, life, configuration, false), carrier,
                carrier ? PreviewReturn(user, rule, life, configuration, true) : null);
            if (!_lastRespawnStates.TryGetValue(user, out var last) || last != current)
                SendRespawnState(user, life);
        }
    }

    private void CloseRespawnWindows()
    {
        foreach (var (user, life) in _lives)
        {
            if (life.Death != null)
                SendRespawnState(user, life);
        }
    }

    /// <summary>Records why the admission was refused, for the waiting return or lobby window.</summary>
    internal void NoteRefusal(ICommonSession player, string reason)
    {
        if (_respawn is { } ticket && ticket.User == player.UserId)
        {
            _respawnRefusal = reason;
            return;
        }
        if (_pendingJoins.Remove(player.UserId))
        {
            RaiseNetworkEvent(new SWGlobalWarJoinResultEvent(false,
                string.IsNullOrEmpty(reason) ? Loc.GetString("sw-global-war-spawn-unavailable") : reason), player);
        }
    }

    /// <summary>A request that ended without a spawn or a stated reason still gets an answer.</summary>
    private void FlushPendingJoins()
    {
        if (_pendingJoins.Count == 0)
            return;
        foreach (var user in _pendingJoins)
        {
            if (_players.TryGetSessionById(user, out var session))
                RaiseNetworkEvent(new SWGlobalWarJoinResultEvent(false, Loc.GetString("sw-global-war-spawn-unavailable")),
                    session);
        }
        _pendingJoins.Clear();
    }

    /// <summary>States what the server actually accepted: side, role, place and the nearest thing to do.</summary>
    private void ConfirmEntry(ICommonSession player, SWGlobalWarSpawnPlan plan)
    {
        var returned = _respawn is { } ticket && ticket.User == player.UserId && ticket.Job == plan.Job;
        if (returned)
            _respawnCommitted = true;
        var configuration = _rules.TryGetGlobalWarGamerule(out var rule)
            ? CompOrNull<SWGlobalWarSpawnConfigurationComponent>(rule.Owner) : null;
        SWGlobalWarSpawnRole? role = null;
        configuration?.Roles.TryGetValue(plan.Job, out role);
        var kind = role?.Role?.ToString().ToLowerInvariant();
        var taskKey = kind == null ? "sw-global-war-first-task-default" : $"sw-global-war-first-task-{kind}";
        var faction = Loc.GetString(plan.Faction == SWGlobalWarFaction.Republic ? "sw-global-war-republic" : "sw-global-war-empire");
        var job = _prototypes.Index(plan.Job).LocalizedName;
        var planet = _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(plan.Planet).Prototype).Name;
        var message = Loc.GetString("sw-global-war-entry-confirmed", ("faction", faction), ("job", job), ("planet", planet));
        message += "\n" + Loc.GetString("sw-global-war-entry-task", ("task", Loc.GetString(taskKey)));
        if (returned)
            message += "\n" + Loc.GetString(role?.Force == true
                ? "sw-global-war-entry-returned-force" : "sw-global-war-entry-returned");
        // One line in the chat. The nearest task belongs to the entry window and the campaign hints.
        _chat.DispatchServerMessage(player, Loc.GetString("sw-global-war-entry-chat", ("faction", faction), ("job", job), ("planet", planet)));
        if (_pendingJoins.Remove(player.UserId))
            RaiseNetworkEvent(new SWGlobalWarJoinResultEvent(true, message), player);
    }
}
