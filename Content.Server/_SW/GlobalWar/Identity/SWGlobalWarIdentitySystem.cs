using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.TaskSystem;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._SW.GlobalWar.Identity;

/// <summary>Owns round-long faction locks independently of body and mind replacement.</summary>
public sealed class SWGlobalWarIdentitySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarGameruleSystem _rules = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;

    private readonly Dictionary<NetUserId, SWGlobalWarFaction> _memberships = new();
    private readonly Dictionary<NetUserId, Guid> _participationGenerations = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SWGlobalWarIdentityComponent, MindGotAddedEvent>(OnMindAdded);
        SubscribeLocalEvent<SWGlobalWarIdentityComponent, MindGotRemovedEvent>(OnMindRemoved);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _memberships.Clear();
            _participationGenerations.Clear();
        });
    }

    /// <summary>Checks a requested faction without consuming a slot or creating a mind.</summary>
    public bool CanJoin(NetUserId user, SWGlobalWarFaction faction)
    {
        return _rules.TryGetGlobalWarGamerule(out _) &&
               faction is SWGlobalWarFaction.Republic or SWGlobalWarFaction.Empire &&
               (!_memberships.TryGetValue(user, out var existing) || existing == faction);
    }

    /// <summary>Returns an existing round lock without selecting or committing a new faction.</summary>
    public bool TryGetMembership(NetUserId user, out SWGlobalWarFaction faction)
    {
        faction = default;
        return _rules.TryGetGlobalWarGamerule(out _) && _memberships.TryGetValue(user, out faction);
    }

    /// <summary>Commits the server-selected faction to the actual user's mind; stale preparations fail closed.</summary>
    public bool TryEnroll(Entity<MindComponent?> mind, SWGlobalWarFaction faction, Guid generation)
    {
        if (!Resolve(mind, ref mind.Comp, false) || mind.Comp.UserId is not { } user ||
            !_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation ||
            !CanJoin(user, faction) || !_minds.TryGetMind(user, out var actual) || actual.Value.Owner != mind.Owner)
            return false;

        var identity = EnsureComp<SWGlobalWarIdentityComponent>(mind.Owner);
        if (identity.Generation == generation && identity.Faction != faction)
            return false;
        _memberships[user] = faction;
        _participationGenerations[user] = generation;
        identity.Generation = generation;
        identity.Faction = faction;
        if (mind.Comp.OwnedEntity is { } body)
            UpdateBody(body, faction);
        return true;
    }

    /// <summary>Reads a body's current authoritative identity, including SSD bodies but excluding observers.</summary>
    public bool TryGetIdentity(EntityUid body, out EntityUid mindUid, out SWGlobalWarFaction faction)
    {
        return TryGetOwnedBodyIdentity(body, out mindUid, out faction) &&
               Comp<MindComponent>(mindUid).VisitingEntity == null;
    }

    /// <summary>
    /// Counts unique current participants for supply readiness, independently of office and life replacement.
    /// Connected dead members remain eligible; an offline member needs their current living or critical body.
    /// This does not grant equipment access or repeat the separately captured starting arsenal.
    /// </summary>
    public int CountSupplyParticipants(Guid generation, SWGlobalWarFaction faction)
    {
        if (!_rules.TryGetGlobalWarGamerule(out var rule) || rule.Comp.Generation != generation)
            return 0;
        var count = 0;
        foreach (var (user, memberFaction) in _memberships)
        {
            if (memberFaction != faction || _participationGenerations.GetValueOrDefault(user) != generation)
                continue;
            var connected = _players.TryGetSessionById(user, out var session) &&
                            session.Status is SessionStatus.Connected or SessionStatus.InGame;
            var livingBody = _minds.TryGetMind(user, out var mind) && mind.Value.Comp.OwnedEntity is { } body &&
                             TryGetOwnedBodyIdentity(body, out _, out var owner) && owner == faction &&
                             (_mobs.IsAlive(body) || _mobs.IsCritical(body));
            if (connected || livingBody)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Maintenance identity for the actual owned body, including SSD and temporary mind visits.
    /// This is not actor authorization; commands and ordinary travel must use TryGetIdentity/TryGetActor.
    /// </summary>
    public bool TryGetOwnedBodyIdentity(EntityUid body, out EntityUid mindUid, out SWGlobalWarFaction faction)
    {
        mindUid = default;
        faction = default;
        if (HasComp<GhostComponent>(body) || !_rules.TryGetGlobalWarGamerule(out var rule) ||
            !_minds.TryGetMind(body, out var mind, out var data) || data.OwnedEntity != body ||
            data.UserId is not { } user ||
            !_minds.TryGetMind(user, out var current) || current.Value.Owner != mind ||
            !TryComp<SWGlobalWarIdentityComponent>(mind, out var identity) ||
            identity.Generation != rule.Comp.Generation ||
            !_memberships.TryGetValue(user, out var locked) || locked != identity.Faction)
            return false;
        mindUid = mind;
        faction = identity.Faction;
        return true;
    }

    /// <summary>Validates a connected actor before a command; a visited body never grants political authority.</summary>
    public bool TryGetActor(ICommonSession session, EntityUid body, out EntityUid mind, out SWGlobalWarFaction faction)
    {
        mind = default;
        faction = default;
        return session.AttachedEntity == body && TryGetIdentity(body, out mind, out faction) &&
               Comp<MindComponent>(mind).UserId == session.UserId;
    }

    private void OnMindAdded(Entity<SWGlobalWarIdentityComponent> ent, ref MindGotAddedEvent args)
    {
        if (_rules.TryGetGlobalWarGamerule(out var rule) && ent.Comp.Generation == rule.Comp.Generation)
            UpdateBody(args.Container.Owner, ent.Comp.Faction);
    }

    private void OnMindRemoved(Entity<SWGlobalWarIdentityComponent> ent, ref MindGotRemovedEvent args)
    {
        var changed = new SWGlobalWarIdentityDetachedEvent(ent.Owner);
        RaiseLocalEvent(ref changed);
        if (TryComp<SWFactionMemberComponent>(args.Container.Owner, out var display))
        {
            display.IsLeader = false;
            Dirty(args.Container.Owner, display);
        }
    }

    private void UpdateBody(EntityUid body, SWGlobalWarFaction faction)
    {
        if (HasComp<GhostComponent>(body))
            return;
        var display = EnsureComp<SWFactionMemberComponent>(body);
        display.Faction = faction == SWGlobalWarFaction.Republic ? "RepublicFaction" : "EmpireFaction";
        display.IsLeader = false;
        Dirty(body, display);
    }
}

[ByRefEvent]
public readonly record struct SWGlobalWarIdentityDetachedEvent(EntityUid Mind);
