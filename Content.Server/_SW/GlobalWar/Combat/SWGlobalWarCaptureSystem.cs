using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Identity;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Destructible;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared.Verbs;
using Content.Shared.Shuttles.Components;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Physical holding objectives; ownership commits only through the campaign's ordered arbitration.</summary>
public sealed class SWGlobalWarCaptureSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCampaignStepSystem _steps = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _ownership = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(EntityUid, SWGlobalWarPlanetLocation), Objective> _objectives = new();
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), Guid> _subscriptions = new();
    private readonly Dictionary<(EntityUid Actor, Guid Request), (EntityUid Console, bool Cancel)> _requests = new();
    private Guid _generation;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SWGlobalWarPoliticsSystem));
        UpdatesBefore.Add(typeof(SWGlobalWarCampaignStepSystem));
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent ev) => Reset(ev.Generation));
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Reset(Guid.Empty));
        SubscribeLocalEvent<SWGlobalWarCaptureConsoleComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<SWGlobalWarBoardingConsoleComponent, GetVerbsEvent<ActivationVerb>>(OnBoardingVerb);
        Subs.BuiEvents<SWGlobalWarCaptureConsoleComponent>(SWGlobalWarCaptureUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarCaptureOpen>(OnOpen);
            subs.Event<SWGlobalWarCaptureRequest>(OnRequest);
        });
    }

    private void Reset(Guid generation)
    {
        _generation = generation;
        _objectives.Clear();
        _subscriptions.Clear();
        _requests.Clear();
        _nextUpdate = TimeSpan.Zero;
    }

    private bool TryObjective(EntityUid console, out Objective objective)
    {
        objective = default!;
        if (_generation == Guid.Empty || !TryComp<SWGlobalWarCaptureConsoleComponent>(console, out var settings))
            return false;
        if (HasComp<SWGlobalWarBoardingConsoleComponent>(console))
            return TryBoardingObjective(console, settings, out objective);
        if (!_planets.TryGetPhysicalLocation(console, out var planet, out var location) ||
            !TryComp<SWGlobalWarMapAnchorComponent>(console, out var anchor) ||
            anchor.AnchorId != "capturePrimary" || anchor.Kind != SWGlobalWarAnchorKind.Capture ||
            !TryComp<SWGlobalWarPlanetComponent>(planet, out var world) ||
            !_anchors.TryResolveAnchor(new SWGlobalWarAnchorRef
            {
                Sector = world.Prototype, Location = location, AnchorId = anchor.AnchorId,
            }, out var resolved) || resolved != console || !Transform(console).Anchored)
            return false;
        var position = Transform(console).Coordinates;
        if (!_objectives.TryGetValue((planet, location), out objective!))
        {
            objective = new Objective(planet, location, position, settings.ZoneHalfExtent,
                settings.Duration, settings.AbandonmentGrace, _timing.CurTime);
            _objectives.Add((planet, location), objective);
        }
        // A rebuilt terminal may resume the same objective, but moving a console cannot move the holding zone.
        if (position.EntityId != objective.Position.EntityId ||
            Vector2.DistanceSquared(position.Position, objective.Position.Position) > 0.01f)
            return false;
        objective.Console = console;
        return true;
    }

    private bool TryBoardingObjective(EntityUid console, SWGlobalWarCaptureConsoleComponent settings,
        out Objective objective)
    {
        objective = default!;
        if (!Transform(console).Anchored || Transform(console).GridUid is not { } ship ||
            !_ownership.TryGetOwner(ship, _generation, out _, out _) || HasComp<FTLComponent>(ship) ||
            !_planets.TryGetPhysicalLocation(console, out var planet, out _)) return false;
        var position = Transform(console).Coordinates;
        if (!_objectives.TryGetValue((ship, SWGlobalWarPlanetLocation.Orbit), out objective!))
        {
            objective = new Objective(planet, SWGlobalWarPlanetLocation.Orbit, position, settings.ZoneHalfExtent,
                settings.Duration, settings.AbandonmentGrace, _timing.CurTime) { Ship = ship };
            _objectives.Add((ship, SWGlobalWarPlanetLocation.Orbit), objective);
        }
        // Another nav console cannot move a previously established bridge objective.
        if (position.EntityId != objective.Position.EntityId ||
            Vector2.DistanceSquared(position.Position, objective.Position.Position) > 0.01f) return false;
        if (objective.Planet != planet)
        {
            Stop(objective);
            objective.Planet = planet;
        }
        objective.Console = console;
        return true;
    }

    private bool CanCapture(EntityUid body, SWGlobalWarFaction faction, Objective objective)
    {
        return objective.Ship is { } ship
            ? _ownership.TryGetOwner(ship, _generation, out var owner, out _) && owner != faction &&
              !HasComp<FTLComponent>(ship) && _politics.CanConductHostility(body, objective.Planet, _generation)
            : _politics.CanCapture(body, objective.Planet, objective.Location, _generation);
    }

    private bool CanCancel(SWGlobalWarAssignment actor, Objective objective)
    {
        return actor.Body == objective.Initiator ||
               (_assignments.GetAuthority(actor) & SWGlobalWarAuthority.MilitaryCommand) != 0;
    }

    private bool CanMaintain(Objective objective)
    {
        return objective.Ship is { } ship
            ? _ownership.TryGetOwner(ship, _generation, out var owner, out var revision) &&
              owner != objective.Attacker && revision == objective.OwnershipRevision &&
              !HasComp<FTLComponent>(ship) && _politics.CanConductNpcHostility(objective.Planet, _generation)
            : _politics.CanMaintainCapture(objective.Attacker, objective.Planet, objective.Location, _generation);
    }

    private void OnBoardingVerb(EntityUid uid, SWGlobalWarBoardingConsoleComponent component,
        GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess) return;
        var actor = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("sw-global-war-boarding-open"),
            Act = () =>
            {
                if (!TerminatingOrDeleted(uid) && _blocker.CanInteract(actor, uid) &&
                    _interaction.InRangeUnobstructed(actor, uid, range: 2f))
                    _ui.TryOpenUi(uid, SWGlobalWarCaptureUiKey.Key, actor);
            },
        });
    }

    private bool Working(Objective objective)
    {
        return objective.Console is { } console && !TerminatingOrDeleted(console) &&
               TryObjective(console, out var current) && current == objective && this.IsPowered(console, EntityManager) &&
               !(TryComp<DestructibleComponent>(console, out var destructible) && destructible.IsBroken);
    }

    private bool InZone(EntityUid body, Objective objective)
    {
        if (TerminatingOrDeleted(objective.Position.EntityId) ||
            Transform(body).MapUid != Transform(objective.Position.EntityId).MapUid)
            return false;
        var local = _transform.ToCoordinates(objective.Position.EntityId, _transform.GetMapCoordinates(body)).Position;
        var offset = Vector2.Abs(local - objective.Position.Position);
        return offset.X <= objective.HalfExtent && offset.Y <= objective.HalfExtent;
    }

    private (bool Attackers, bool Defenders) Presence(Objective objective)
    {
        var attackers = false;
        var defenders = false;
        var players = EntityQueryEnumerator<ActorComponent>();
        while (players.MoveNext(out var body, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame || actor.PlayerSession.AttachedEntity != body ||
                !InZone(body, objective) || !_mobs.IsAlive(body) || !_blocker.CanInteract(body, null) ||
                !_identity.TryGetIdentity(body, out _, out var faction))
                continue;
            if (faction == objective.Attacker)
                attackers |= CanCapture(body, faction, objective);
            else if (faction != SWGlobalWarFaction.Neutral &&
                     _politics.CanConductHostility(body, objective.Planet, _generation))
                defenders = true;
        }
        var droids = EntityQueryEnumerator<SWDroidCommandableComponent>();
        while (droids.MoveNext(out var body, out var droid))
        {
            if (!InZone(body, objective) || !_mobs.IsAlive(body) || !_blocker.CanInteract(body, null))
                continue;
            var faction = droid.Faction switch
            {
                "RepublicFaction" => SWGlobalWarFaction.Republic,
                "EmpireFaction" => SWGlobalWarFaction.Empire,
                _ => SWGlobalWarFaction.Neutral,
            };
            if (faction != SWGlobalWarFaction.Neutral && faction != objective.Attacker)
                defenders = true;
        }
        return (attackers, defenders);
    }

    /// <summary>Starts or cancels only an authenticated actor's current physical objective.</summary>
    public bool TryExecute(ICommonSession session, EntityUid console, Guid generation, long revision,
        Guid request, bool cancel)
    {
        if (request == Guid.Empty || generation != _generation || _steps.IsClosing ||
            !_assignments.TryGetActor(session, generation, revision, out var actor) ||
            !TryObjective(console, out var objective) || !Working(objective) ||
            !_blocker.CanInteract(actor.Body, console) || !_interaction.InRangeUnobstructed(actor.Body, console, range: 2f))
            return false;
        if (_requests.TryGetValue((actor.Body, request), out var receipt))
            return receipt == (console, cancel);
        if (cancel)
        {
            if (objective.Operation == Guid.Empty || objective.Attacker != actor.Faction ||
                !CanCancel(actor, objective) || !CanCapture(actor.Body, actor.Faction, objective))
                return false;
            Stop(objective);
        }
        else
        {
            if (objective.Operation != Guid.Empty ||
                !CanCapture(actor.Body, actor.Faction, objective))
                return false;
            objective.Attacker = actor.Faction;
            objective.Initiator = actor.Body;
            if (objective.Ship is { } ship)
                _ownership.TryGetOwner(ship, generation, out _, out objective.OwnershipRevision);
            objective.Operation = Guid.NewGuid();
            objective.Progress = TimeSpan.Zero;
            objective.MissingSince = null;
            objective.UpdatedAt = _timing.CurTime;
            objective.Status = SWGlobalWarCaptureStatus.Capturing;
            if (objective.Ship == null)
                _politics.SetNeutralCapture(actor.Body, Comp<SWGlobalWarPlanetComponent>(objective.Planet).Prototype,
                    generation, objective.Operation, true);
        }
        _requests.Add((actor.Body, request), (console, cancel));
        return true;
    }

    private void Stop(Objective objective)
    {
        if (objective.Operation != Guid.Empty && objective.Ship == null)
            _politics.SetNeutralCapture(objective.Initiator, Comp<SWGlobalWarPlanetComponent>(objective.Planet).Prototype,
                _generation, objective.Operation, false);
        objective.Operation = Guid.Empty;
        objective.Progress = TimeSpan.Zero;
        objective.MissingSince = null;
        objective.Queued = false;
        objective.Attacker = SWGlobalWarFaction.Neutral;
        objective.Status = SWGlobalWarCaptureStatus.Idle;
    }

    public override void Update(float frameTime)
    {
        if (_generation == Guid.Empty || _timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var bridges = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (bridges.MoveNext(out var bridge, out _))
        {
            if (HasComp<SWGlobalWarBoardingConsoleComponent>(bridge) || Transform(bridge).GridUid is not { } ship ||
                !_ownership.TryGetOwner(ship, _generation, out _, out _)) continue;
            EnsureComp<SWGlobalWarBoardingConsoleComponent>(bridge);
            EnsureComp<SWGlobalWarCaptureConsoleComponent>(bridge);
            _ui.SetUi(bridge, SWGlobalWarCaptureUiKey.Key, new InterfaceData("SWGlobalWarCaptureBoundUserInterface"));
        }
        var consoles = EntityQueryEnumerator<SWGlobalWarCaptureConsoleComponent>();
        while (consoles.MoveNext(out var console, out _))
            TryObjective(console, out _);
        foreach (var objective in _objectives.Values.ToArray())
            Advance(objective);
        foreach (var (console, actor) in _subscriptions.Keys.ToArray())
            SendView(console, actor);
    }

    private void Advance(Objective objective)
    {
        var now = _timing.CurTime;
        var elapsed = now - objective.UpdatedAt;
        objective.UpdatedAt = now;
        if (objective.Operation == Guid.Empty || objective.Queued)
            return;
        if (!CanMaintain(objective))
        {
            Stop(objective);
            return;
        }
        var (attackers, defenders) = Presence(objective);
        if (!attackers)
        {
            objective.MissingSince ??= now;
            objective.Status = SWGlobalWarCaptureStatus.Abandoned;
            if (now - objective.MissingSince >= objective.Grace)
                Stop(objective);
            return;
        }
        objective.MissingSince = null;
        if (!Working(objective))
        {
            objective.Status = SWGlobalWarCaptureStatus.Unpowered;
            return;
        }
        if (defenders)
        {
            objective.Status = SWGlobalWarCaptureStatus.Contested;
            return;
        }
        objective.Status = SWGlobalWarCaptureStatus.Capturing;
        if (elapsed > TimeSpan.Zero)
            objective.Progress += elapsed;
        if (objective.Progress < objective.Duration)
            return;
        var operation = objective.Operation;
        if (objective.Ship is { } ship)
        {
            if (_ownership.TryGetOwner(ship, _generation, out _, out var shipRevision) &&
                shipRevision == objective.OwnershipRevision && CanMaintain(objective) && Working(objective) &&
                Presence(objective) is (true, false) &&
                _ownership.CommitOwnership(ship, _generation, shipRevision, operation, objective.Attacker))
            {
                objective.Operation = Guid.Empty;
                objective.Status = SWGlobalWarCaptureStatus.Completed;
                objective.Progress = objective.Duration;
            }
            return;
        }
        var generation = _generation;
        var revision = Comp<SWGlobalWarPlanetComponent>(objective.Planet).OwnershipRevision;
        objective.Queued = _steps.TryQueueOwnership(objective.Planet, objective.Location, objective.Attacker,
            revision, generation, operation, success =>
            {
                if (_generation != generation || objective.Operation != operation)
                    return;
                objective.Queued = false;
                if (!success)
                    return;
                objective.Operation = Guid.Empty;
                objective.Status = SWGlobalWarCaptureStatus.Completed;
                objective.Progress = objective.Duration;
            }, () => _generation == generation && objective.Operation == operation && Working(objective) &&
                     _politics.CanMaintainCapture(objective.Attacker, objective.Planet, objective.Location, generation) &&
                     Presence(objective) is (true, false));
    }

    private void OnOpen(Entity<SWGlobalWarCaptureConsoleComponent> ent, ref SWGlobalWarCaptureOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarCaptureUiKey.Key, args.Actor))
            return;
        _subscriptions[(ent, args.Actor)] = args.Subscription;
        SendView(ent, args.Actor);
    }

    private void OnClosed(Entity<SWGlobalWarCaptureConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        _subscriptions.Remove((ent, args.Actor));
    }

    private void OnRequest(Entity<SWGlobalWarCaptureConsoleComponent> ent, ref SWGlobalWarCaptureRequest args)
    {
        if (!_subscriptions.TryGetValue((ent, args.Actor), out var subscription) || subscription != args.Subscription ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        TryExecute(player.PlayerSession, ent, args.Generation, args.Revision, args.Request, args.Cancel);
        SendView(ent, args.Actor);
    }

    private void SendView(EntityUid console, EntityUid actor)
    {
        if (!_subscriptions.TryGetValue((console, actor), out var subscription))
            return;
        if (TerminatingOrDeleted(console) || !_ui.IsUiOpen(console, SWGlobalWarCaptureUiKey.Key, actor) ||
            !TryComp<ActorComponent>(actor, out var player))
        {
            _subscriptions.Remove((console, actor));
            return;
        }
        var view = GetView(player.PlayerSession, console);
        _ui.ServerSendUiMessage(console, SWGlobalWarCaptureUiKey.Key,
            new SWGlobalWarCaptureSnapshot(view, subscription, GetNetEntity(actor)), actor);
    }

    /// <summary>Only local capture facts are exposed, never economic or private command information.</summary>
    public SWGlobalWarCaptureView GetView(ICommonSession session, EntityUid console)
    {
        if (!TryObjective(console, out var objective) ||
            !_assignments.TryGetAssignment(session.UserId, out var actor) ||
            !_assignments.TryGetActor(session, _generation, actor.Revision, out actor))
            return new(Guid.Empty, 0, default, default, default, SWGlobalWarCaptureStatus.Unavailable,
                0, 300, 0, false, false, "sw-global-war-capture-unavailable");
        var world = Comp<SWGlobalWarPlanetComponent>(objective.Planet);
        var owner = objective.Location == SWGlobalWarPlanetLocation.Orbit ? world.OrbitOwner : world.SurfaceOwner;
        if (objective.Ship is { } ship) _ownership.TryGetOwner(ship, _generation, out owner, out _);
        var allowed = Working(objective) && CanCapture(actor.Body, actor.Faction, objective);
        var grace = objective.MissingSince is { } since ? Math.Max(0, (objective.Grace - (_timing.CurTime - since)).TotalSeconds) : 0;
        return new(_generation, actor.Revision,
            owner,
            objective.Attacker, objective.Location, objective.Status, (float) objective.Progress.TotalSeconds,
            (float) objective.Duration.TotalSeconds, (float) grace,
            allowed && objective.Operation == Guid.Empty,
            allowed && objective.Operation != Guid.Empty && objective.Attacker == actor.Faction && CanCancel(actor, objective),
            allowed ? "sw-global-war-capture-hold" : "sw-global-war-capture-denied", objective.Ship != null);
    }

    private sealed class Objective(EntityUid planet, SWGlobalWarPlanetLocation location, EntityCoordinates position,
        float halfExtent, TimeSpan duration, TimeSpan grace, TimeSpan now)
    {
        public EntityUid Planet = planet;
        public EntityUid? Ship;
        public long OwnershipRevision;
        public readonly SWGlobalWarPlanetLocation Location = location;
        public readonly EntityCoordinates Position = position;
        public readonly float HalfExtent = Math.Clamp(halfExtent, 1, 12);
        public readonly TimeSpan Duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(300);
        public readonly TimeSpan Grace = grace > TimeSpan.Zero ? grace : TimeSpan.FromSeconds(30);
        public EntityUid? Console;
        public EntityUid Initiator;
        public Guid Operation;
        public SWGlobalWarFaction Attacker;
        public TimeSpan Progress;
        public TimeSpan UpdatedAt = now;
        public TimeSpan? MissingSince;
        public bool Queued;
        public SWGlobalWarCaptureStatus Status;
    }
}
