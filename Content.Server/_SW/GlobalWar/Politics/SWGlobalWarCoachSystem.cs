using System.Numerics;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Production;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>
/// Tells a player in a running campaign the one next thing their role needs, as a short line and a pointer. The role
/// comes during the round, and each line goes away when the player has done what it says.
/// </summary>
public sealed class SWGlobalWarCoachSystem : EntitySystem
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private const float HelmRange = 3f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _market = default!;

    private readonly Dictionary<NetUserId, (SWGlobalWarCoachHint Hint, NetCoordinates? Focus)> _sent = new();
    private readonly HashSet<Guid> _routed = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _routed.Clear());
        SubscribeLocalEvent<SWGlobalWarRouteStartedEvent>(OnRouteStarted);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    private void OnRouteStarted(ref SWGlobalWarRouteStartedEvent args)
    {
        if (TryComp<ActorComponent>(args.User, out var actor)
            && _assignments.TryGetAssignment(actor.PlayerSession.UserId, out var assignment))
        {
            _routed.Add(assignment.Id);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        // A reconnected client starts with nothing on its screen.
        if (args.NewStatus != SessionStatus.InGame)
            _sent.Remove(args.Session.UserId);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + Interval;

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            var (hint, focus, warIn) = Compute(session);
            var known = _sent.TryGetValue(session.UserId, out var last);
            if (known ? last.Hint == hint && Nullable.Equals(last.Focus, focus) : hint == SWGlobalWarCoachHint.None)
                continue;

            _sent[session.UserId] = (hint, focus);
            RaiseNetworkEvent(new SWGlobalWarCoachEvent(hint, focus, warIn), session);
        }
    }

    private (SWGlobalWarCoachHint Hint, NetCoordinates? Focus, TimeSpan? WarIn) Compute(ICommonSession session)
    {
        if (!_campaign.TryGetCampaign(out var campaign)
            || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running
            || !_assignments.TryGetAssignment(session.UserId, out var current)
            || !_assignments.TryGetActor(session, campaign.Comp.Generation, current.Revision, out var actor))
        {
            return (SWGlobalWarCoachHint.None, null, null);
        }

        var body = actor.Body;
        var helm = FindHelm(body, actor.Role);
        var elapsed = _campaign.Elapsed(campaign);
        var generation = campaign.Comp.Generation;
        var fighter = actor.Role is SWGlobalWarRole.Soldier or SWGlobalWarRole.JuniorOfficer or SWGlobalWarRole.SeniorOfficer;
        var arsenal = fighter
            ? Nearest<SWGlobalWarFactoryComponent>(body, (uid, factory) =>
                factory.Arsenal && _market.ArsenalHasKit(generation, uid, actor.Faction))
            : null;
        var teleporter = fighter && OnOwnCapitalSurface(body, actor.Faction)
            ? Nearest<SWGlobalWarPlanetTeleporterComponent>(body, (_, _) => true)
            : null;

        var facts = new SWGlobalWarCoachFacts(
            actor.Role,
            AtPeace(generation),
            elapsed >= campaign.Comp.PeaceDuration,
            helm != null,
            _routed.Contains(actor.Id),
            _market.HasDrawnKit(actor.Id) || CarriesGun(body),
            arsenal != null,
            teleporter != null);

        var hint = SWGlobalWarCoachRules.Pick(facts);
        NetCoordinates? focus = hint switch
        {
            SWGlobalWarCoachHint.HeadDeclare => Nearest<SWGlobalWarGovernmentConsoleComponent>(body, (_, console) =>
                console.Kind == SWGlobalWarGovernmentConsoleKind.Diplomacy && console.Faction == actor.Faction),
            SWGlobalWarCoachHint.Route => helm is { } target ? GetNetCoordinates(Transform(target).Coordinates) : null,
            SWGlobalWarCoachHint.Kit => arsenal,
            SWGlobalWarCoachHint.Teleporter => teleporter,
            _ => null,
        };

        TimeSpan? warIn = hint is SWGlobalWarCoachHint.HeadWait or SWGlobalWarCoachHint.Countdown
            ? campaign.Comp.PeaceDuration - elapsed
            : null;

        return (hint, focus, warIn);
    }

    public bool IsAtPeace()
    {
        return _campaign.TryGetCampaign(out var campaign) && AtPeace(campaign.Comp.Generation);
    }

    /// <summary>A fighter has left once off the surface of their side's capital, the only place the line sends them from.</summary>
    private bool OnOwnCapitalSurface(EntityUid body, SWGlobalWarFaction faction)
    {
        return TryComp<SWGlobalWarPlanetMapComponent>(Transform(body).MapUid, out var map)
               && map.Location == SWGlobalWarPlanetLocation.Surface
               && TryComp<SWGlobalWarPlanetComponent>(map.Planet, out var planet)
               && _prototypes.Index(planet.Prototype) is { Kind: SWGlobalWarSectorKind.Capital } prototype
               && prototype.InitialOwner == faction;
    }

    private bool CarriesGun(EntityUid body)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(body))
        {
            if (HasComp<GunComponent>(item))
                return true;
        }

        return false;
    }

    private bool AtPeace(Guid generation)
    {
        var query = EntityQueryEnumerator<SWGlobalWarPoliticsComponent>();
        while (query.MoveNext(out var politics))
        {
            if (politics.Generation == generation)
                return politics.Relations == SWGlobalWarRelations.Peace;
        }

        return false;
    }

    /// <summary>
    /// The helm in use by anyone, or one a pilot stands at. The route is a verb on it, and nothing in its window says so.
    /// </summary>
    private EntityUid? FindHelm(EntityUid body, SWGlobalWarRole role)
    {
        if (TryComp<PilotComponent>(body, out var pilot)
            && pilot.Console is { } console
            && HasComp<SWGlobalWarTravelConsoleComponent>(console))
        {
            return console;
        }

        if (role != SWGlobalWarRole.Pilot)
            return null;

        foreach (var nearby in _lookup.GetEntitiesInRange<SWGlobalWarTravelConsoleComponent>(Transform(body).Coordinates, HelmRange))
        {
            return nearby;
        }

        return null;
    }

    private NetCoordinates? Nearest<T>(EntityUid body, Func<EntityUid, T, bool> wanted) where T : IComponent
    {
        var xform = Transform(body);
        var from = _transform.GetWorldPosition(xform);
        var best = float.MaxValue;
        NetCoordinates? found = null;

        var query = EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out var component, out var targetXform))
        {
            if (targetXform.MapID != xform.MapID || !wanted(uid, component))
                continue;

            var distance = Vector2.DistanceSquared(from, _transform.GetWorldPosition(targetXform));
            if (distance >= best)
                continue;

            best = distance;
            found = GetNetCoordinates(targetXform.Coordinates);
        }

        return found;
    }
}

/// <summary>Raised when a player's route has really put a ship into flight.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarRouteStartedEvent(EntityUid User);
