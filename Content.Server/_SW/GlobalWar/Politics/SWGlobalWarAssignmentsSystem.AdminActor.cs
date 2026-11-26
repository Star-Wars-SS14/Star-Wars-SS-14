using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Popups;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>
/// Admin debugging: an active admin acts at every campaign console as the head of the side that owns the location,
/// or of the side picked with <see cref="SWGlobalWarAdminSideCommand"/>, from an aghost or from a body with no campaign
/// side, such as one taken by controlmob. In their own campaign body,
/// or as an ordinary dead ghost, an admin plays by the normal rules. The office is never written to the registry,
/// so it takes no slot, starts no succession and nobody else sees it.
/// </summary>
public sealed partial class SWGlobalWarAssignmentsSystem
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const long AdminActorRevision = 0;
    private static readonly TimeSpan AdminNoticeInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<(NetUserId User, SWGlobalWarFaction Faction), Guid> _adminActors = new();
    private readonly Dictionary<NetUserId, TimeSpan> _adminNotices = new();
    private readonly Dictionary<NetUserId, SWGlobalWarFaction> _adminSides = new();

    private void InitializeAdminActors()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _adminActors.Clear();
            _adminNotices.Clear();
        });
    }

    /// <summary>Null returns to the side that owns the location. The choice lasts until changed.</summary>
    public void SetAdminSide(NetUserId user, SWGlobalWarFaction? side)
    {
        if (side is { } chosen)
            _adminSides[user] = chosen;
        else
            _adminSides.Remove(user);
    }

    /// <summary>The admin debug office of whoever controls this body, if that body is the one attached right now.</summary>
    public bool TryGetAdminActor(EntityUid body, out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        return TryComp<ActorComponent>(body, out var actor) && actor.PlayerSession.AttachedEntity == body &&
               TryGetAdminActor(actor.PlayerSession.UserId, out assignment);
    }

    /// <summary>The mind and side of a campaign actor, or of an admin debugging in an aghost or a foreign body.</summary>
    public bool TryGetPersonnelActor(ICommonSession session, EntityUid body, out EntityUid mind, out SWGlobalWarFaction faction)
    {
        if (session.AttachedEntity == body && TryGetAdminActor(session.UserId, out var admin))
        {
            mind = admin.Mind;
            faction = admin.Faction;
            return true;
        }
        return _identity.TryGetActor(session, body, out mind, out faction);
    }

    private bool IsActiveAdmin(NetUserId user, out ICommonSession session)
    {
        return _players.TryGetSessionById(user, out session!) && _admins.HasAdminFlag(session, AdminFlags.Admin);
    }

    /// <summary>The head office an active admin holds where they stand while debugging.</summary>
    private bool TryGetAdminActor(NetUserId user, out SWGlobalWarAssignment assignment)
    {
        assignment = default!;
        if (!IsActiveAdmin(user, out var session) || session.AttachedEntity is not { } body || !IsDebugBody(body) ||
            !TryGetRegistry(out var registry) || !_minds.TryGetMind(user, out var mind))
            return false;

        var faction = SWGlobalWarFaction.Neutral;
        if (_adminSides.TryGetValue(user, out var chosen))
            faction = chosen;
        else if (_planets.TryGetPhysicalLocation(body, out var planet, out var location) &&
                 TryComp<SWGlobalWarPlanetComponent>(planet, out var world))
            faction = location == SWGlobalWarPlanetLocation.Orbit ? world.OrbitOwner : world.SurfaceOwner;
        if (faction == SWGlobalWarFaction.Neutral || !TryGetHeadJob(registry.Owner, faction, out var job))
        {
            NoticeNoOwner(session, body);
            return false;
        }

        if (!_adminActors.TryGetValue((user, faction), out var id))
            _adminActors[(user, faction)] = id = Guid.NewGuid();
        assignment = new SWGlobalWarAssignment(id, registry.Comp.Generation, user, mind.Value.Owner, body, faction,
            SWGlobalWarRole.Head, job, AdminActorRevision, false);
        return true;
    }

    /// <summary>An aghost, or a body outside the campaign; never a dead player's ghost or a campaign body.</summary>
    private bool IsDebugBody(EntityUid body)
    {
        return TryComp<GhostComponent>(body, out var ghost)
            ? ghost.CanGhostInteract
            : !_identity.TryGetOwnedBodyIdentity(body, out _, out _);
    }

    private bool IsAdminActor(SWGlobalWarAssignment assignment)
    {
        return _adminActors.TryGetValue((assignment.User, assignment.Faction), out var id) && id == assignment.Id;
    }

    private bool TryGetHeadJob(EntityUid registry, SWGlobalWarFaction faction, out ProtoId<JobPrototype> job)
    {
        job = default;
        if (!TryComp<SWGlobalWarSpawnConfigurationComponent>(registry, out var catalog))
            return false;
        foreach (var (id, role) in catalog.Roles)
        {
            if (role.Faction != faction || role.Role != SWGlobalWarRole.Head)
                continue;
            job = id;
            return true;
        }
        return false;
    }

    /// <summary>Said only while a window is open, so walking around a neutral world stays quiet.</summary>
    private void NoticeNoOwner(ICommonSession session, EntityUid body)
    {
        if (!_ui.GetActorUis(body).Any() ||
            _adminNotices.TryGetValue(session.UserId, out var next) && _timing.CurTime < next)
            return;
        _adminNotices[session.UserId] = _timing.CurTime + AdminNoticeInterval;
        _popup.PopupEntity(Loc.GetString("sw-global-war-admin-actor-no-owner"), body, session);
    }
}
