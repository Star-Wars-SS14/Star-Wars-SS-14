using System.Linq;
using System.Numerics;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server.Destructible;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Projectiles;
using Content.Server.Shuttles.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared._SW.Vehicle;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction;
using Content.Shared.Gravity;
using Content.Shared.Slippery;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Power;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>Leased battery control. Aiming never bypasses the existing ammunition and physical damage pipeline.</summary>
public sealed class SWGlobalWarGunnerySystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarShipOwnershipSystem _ownership = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), Guid> _subscriptions = new();
    private TimeSpan _nextView;
    private TimeSpan _nextMountScan;
    private EntityUid? _remoteShooter;
    private static readonly EntProtoId CampaignLaser = "SWGlobalWarShuttleLaser";

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarGunneryConsoleComponent, GetVerbsEvent<ActivationVerb>>(OnVerb);
        SubscribeLocalEvent<SWGlobalWarGunneryConsoleComponent, BoundUIClosedEvent>(OnClosed);
        SubscribeLocalEvent<SWGlobalWarGunMountComponent, ShotAttemptedEvent>(OnShot);
        SubscribeLocalEvent<SWGlobalWarGunMountComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<SWGlobalWarGunOperatorComponent, SWGetFromCoordsShootEvent>(OnMuzzle);
        SubscribeLocalEvent<SWGlobalWarGunOperatorComponent, ShooterImpulseEvent>(OnShooterImpulse,
            after: [typeof(SharedGravitySystem), typeof(SlidingSystem)]);
        SubscribeLocalEvent<SWGlobalWarProjectileRangeComponent, StartCollideEvent>(OnCollision,
            before: [typeof(ProjectileSystem)]);
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent ev) =>
        {
            var query = AllEntityQuery<SWGlobalWarGunMountComponent>();
            while (query.MoveNext(out _, out var mount)) Release(mount);
            _subscriptions.Clear();
        });
        Subs.BuiEvents<SWGlobalWarGunneryConsoleComponent>(SWGlobalWarGunneryUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarGunneryOpen>(OnOpen);
            subs.Event<SWGlobalWarGunneryRequest>(OnRequest);
        });
    }

    /// <summary>Installs control on a newly loaded real hull, preserving every ammunition provider and stock.</summary>
    public void PrepareShip(EntityUid ship)
    {
        var weapons = EntityQueryEnumerator<GunComponent>();
        while (weapons.MoveNext(out var gun, out _))
        {
            if (Transform(gun).GridUid != ship ||
                MetaData(gun).EntityPrototype?.ID.StartsWith("SWShuttleGun", StringComparison.Ordinal) != true ||
                HasComp<SWGlobalWarGunMountComponent>(gun)) continue;
            var mount = AddComp<SWGlobalWarGunMountComponent>(gun);
            mount.BaseRotation = Transform(gun).LocalRotation;
            EnsureComp<ApcPowerReceiverComponent>(gun).Load = 100;
            EnsureComp<ExtensionCableReceiverComponent>(gun);
            if (MetaData(gun).EntityPrototype?.ID == "SWShuttleGunPerforator" &&
                TryComp<BatteryAmmoProviderComponent>(gun, out var battery)) battery.Prototype = CampaignLaser;
        }
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (consoles.MoveNext(out var console, out _))
        {
            if (Transform(console).GridUid != ship) continue;
            EnsureComp<SWGlobalWarGunneryConsoleComponent>(console);
            _ui.SetUi(console, SWGlobalWarGunneryUiKey.Key, new InterfaceData("SWGlobalWarGunneryBoundUserInterface"));
        }
    }

    private bool Context(EntityUid console, EntityUid body, Guid generation, long revision,
        out EntityUid ship, out SWGlobalWarAssignment assignment)
    {
        ship = default;
        assignment = default!;
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Generation != generation ||
            campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running ||
            TerminatingOrDeleted(console) || !HasComp<SWGlobalWarGunneryConsoleComponent>(console) ||
            !Transform(console).Anchored || !this.IsPowered(console, EntityManager) ||
            TryComp<DestructibleComponent>(console, out var damage) && damage.IsBroken ||
            !TryComp<ActorComponent>(body, out var actor) ||
            !_assignments.TryGetActor(actor.PlayerSession, generation, revision, out assignment) ||
            !_blocker.CanInteract(body, console) || !_interaction.InRangeUnobstructed(body, console, range: 2f) ||
            Transform(console).GridUid is not { } grid || Transform(body).GridUid != grid ||
            !_ownership.TryGetOwner(grid, generation, out var owner, out _) || assignment.Faction != owner ||
            !_planets.TryGetPhysicalLocation(console, out var planet, out _) ||
            !_politics.CanConductHostility(body, planet, generation)) return false;
        ship = grid;
        return true;
    }

    private bool CurrentContext(EntityUid console, EntityUid body, out EntityUid ship,
        out SWGlobalWarAssignment assignment)
    {
        ship = default;
        assignment = default!;
        return TryComp<ActorComponent>(body, out var actor) &&
               _assignments.TryGetAssignment(actor.PlayerSession.UserId, out var current) &&
               Context(console, body, current.Generation, current.Revision, out ship, out assignment);
    }

    private void OnVerb(EntityUid uid, SWGlobalWarGunneryConsoleComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess) return;
        var actor = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("sw-global-war-gunnery-open"),
            Act = () =>
            {
                if (!TerminatingOrDeleted(uid) && _blocker.CanInteract(actor, uid) &&
                    _interaction.InRangeUnobstructed(actor, uid)) _ui.TryOpenUi(uid, SWGlobalWarGunneryUiKey.Key, actor);
            },
        });
    }

    private void OnOpen(Entity<SWGlobalWarGunneryConsoleComponent> ent, ref SWGlobalWarGunneryOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarGunneryUiKey.Key, args.Actor)) return;
        _subscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        SendView(ent.Owner, args.Actor);
    }

    private void OnClosed(Entity<SWGlobalWarGunneryConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, SWGlobalWarGunneryUiKey.Key)) return;
        _subscriptions.Remove((ent.Owner, args.Actor));
        var query = EntityQueryEnumerator<SWGlobalWarGunMountComponent>();
        while (query.MoveNext(out _, out var mount))
            if (mount.Console == ent.Owner && mount.Operator == args.Actor) Release(mount);
    }

    private static void Release(SWGlobalWarGunMountComponent mount)
    {
        mount.Operator = null;
        mount.Console = null;
        mount.Firing = false;
    }

    private void OnRequest(Entity<SWGlobalWarGunneryConsoleComponent> ent, ref SWGlobalWarGunneryRequest args)
    {
        if (!_subscriptions.TryGetValue((ent.Owner, args.Actor), out var subscription) || subscription != args.Subscription ||
            !TryGetEntity(args.Map, out var map) || map == null || args.Guns.Length > 16) return;
        var guns = args.Guns.Select(g => TryGetEntity(g, out var uid) ? uid : null)
            .Where(g => g != null).Select(g => g!.Value).ToArray();
        TryControl(ent.Owner, args.Actor, args.Generation, args.Revision, map.Value, guns, args.Point, args.Firing);
    }

    /// <summary>Authoritative operator lease and target input; BUI subscriptions are checked by the network endpoint.</summary>
    public bool TryControl(EntityUid console, EntityUid actor, Guid generation, long revision, EntityUid map,
        EntityUid[] guns, Vector2 point, bool firing)
    {
        if (guns.Length > 16 || !float.IsFinite(point.X) || !float.IsFinite(point.Y) ||
            !Context(console, actor, generation, revision, out var ship, out _) || map != Transform(ship).MapUid)
            return false;
        var selected = new HashSet<EntityUid>();
        foreach (var gun in guns)
        {
            if (!TryComp<SWGlobalWarGunMountComponent>(gun, out var mount) || Transform(gun).GridUid != ship ||
                !Transform(gun).Anchored || mount.Operator != null && mount.Operator != actor) continue;
            selected.Add(gun);
            mount.Operator = actor;
            mount.Console = console;
            mount.Aim = point;
            mount.AimMap = map;
            mount.Firing = firing;
            mount.LastInput = _timing.CurTime;
        }
        EnsureComp<SWGlobalWarGunOperatorComponent>(actor);
        var query = EntityQueryEnumerator<SWGlobalWarGunMountComponent>();
        while (query.MoveNext(out var gun, out var mount))
            if (mount.Console == console && mount.Operator == actor && !selected.Contains(gun)) Release(mount);
        return selected.Count > 0 || guns.Length == 0;
    }

    private bool MountReady(EntityUid gun, SWGlobalWarGunMountComponent mount, out EntityUid ship)
    {
        ship = default;
        return mount.Operator is { } actor && mount.Console is { } console &&
               _timing.CurTime - mount.LastInput <= TimeSpan.FromSeconds(0.5) &&
               CurrentContext(console, actor, out ship, out _) && Transform(gun).GridUid == ship &&
               Transform(gun).Anchored && this.IsPowered(gun, EntityManager) &&
               !(TryComp<DestructibleComponent>(gun, out var damage) && damage.IsBroken) &&
               mount.AimMap == Transform(gun).MapUid;
    }

    private void OnShot(Entity<SWGlobalWarGunMountComponent> ent, ref ShotAttemptedEvent args)
    {
        // Legacy signal buttons and automatic fire cannot impersonate the leased operator.
        if (ent.Comp.Operator != args.User || !ent.Comp.Firing || !MountReady(ent.Owner, ent.Comp, out _)) args.Cancel();
    }

    private void OnShooterImpulse(Entity<SWGlobalWarGunOperatorComponent> ent, ref ShooterImpulseEvent args)
    {
        // A console operator does not physically hold the remote gun. Personal weapons retain normal recoil.
        if (_remoteShooter == ent.Owner) args.Push = false;
    }

    private void OnMuzzle(Entity<SWGlobalWarGunOperatorComponent> ent, ref SWGetFromCoordsShootEvent args)
    {
        if (!TryComp<SWGlobalWarGunMountComponent>(args.Gun, out var mount) || mount.Operator != ent.Owner ||
            !TryComp<GunComponent>(args.Gun, out var gun) || !MountReady(args.Gun, mount, out _)) return;
        args.Coords = new EntityCoordinates(args.Gun, gun.DefaultDirection.Normalized() * 0.5f);
        args.Handled = true;
    }

    private void OnAmmoShot(EntityUid uid, SWGlobalWarGunMountComponent component, AmmoShotEvent args)
    {
        foreach (var shot in args.FiredProjectiles)
        {
            if (!HasComp<ProjectileComponent>(shot)) continue;
            var range = EnsureComp<SWGlobalWarProjectileRangeComponent>(shot);
            range.Remaining = component.Range;
            range.Last = _transform.GetMapCoordinates(shot);
        }
    }

    private bool TrackProjectile(EntityUid shot, SWGlobalWarProjectileRangeComponent range)
    {
        var position = _transform.GetMapCoordinates(shot);
        range.Remaining -= Vector2.Distance(position.Position, range.Last.Position);
        var expired = position.MapId != range.Last.MapId || range.Remaining <= 0;
        range.Last = position;
        if (!expired) return true;
        if (TryComp<ProjectileComponent>(shot, out var projectile)) projectile.ProjectileSpent = true;
        QueueDel(shot);
        return false;
    }

    private void OnCollision(Entity<SWGlobalWarProjectileRangeComponent> ent, ref StartCollideEvent args) =>
        TrackProjectile(ent.Owner, ent.Comp);

    public override void Update(float frameTime)
    {
        if (_timing.CurTime >= _nextMountScan && _campaign.TryGetCampaign(out _))
        {
            _nextMountScan = _timing.CurTime + TimeSpan.FromSeconds(1);
            var ships = EntityQueryEnumerator<SWGlobalWarFleetShipComponent>();
            while (ships.MoveNext(out var ship, out _)) PrepareShip(ship);
        }
        var projectiles = EntityQueryEnumerator<SWGlobalWarProjectileRangeComponent>();
        while (projectiles.MoveNext(out var shot, out var range)) TrackProjectile(shot, range);
        var query = EntityQueryEnumerator<SWGlobalWarGunMountComponent, GunComponent>();
        while (query.MoveNext(out var gun, out var mount, out var weapon))
        {
            if (!MountReady(gun, mount, out var ship)) { Release(mount); continue; }
            var delta = mount.Aim - _transform.GetWorldPosition(gun);
            if (delta.LengthSquared() > mount.Range * mount.Range || delta.LengthSquared() < 0.25f)
            { mount.Status = "range"; continue; }
            var desired = delta.ToAngle() - _transform.GetWorldRotation(ship) - weapon.DefaultDirection.ToAngle();
            var fromBase = Angle.ShortestDistance(mount.BaseRotation, desired);
            if (Math.Abs(fromBase.Degrees) > mount.Arc / 2) { mount.Status = "arc"; continue; }
            var current = Transform(gun).LocalRotation;
            var difference = Angle.ShortestDistance(current, desired).Degrees;
            var step = Math.Clamp(difference, -mount.RotationSpeed * frameTime, mount.RotationSpeed * frameTime);
            _transform.SetLocalRotation(gun, current + Angle.FromDegrees(step));
            mount.Status = Math.Abs(difference) <= Math.Max(1, Math.Abs(step)) ? "ready" : "aiming";
            if (mount.Firing && Math.Abs(difference) <= Math.Max(1, Math.Abs(step)))
            {
                _remoteShooter = mount.Operator;
                try
                {
                    _guns.AttemptShoot(mount.Operator!.Value, gun, weapon,
                        new EntityCoordinates(mount.AimMap!.Value, mount.Aim));
                }
                finally
                {
                    _remoteShooter = null;
                }
            }
        }
        if (_timing.CurTime < _nextView) return;
        _nextView = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        foreach (var ((console, actor), _) in _subscriptions.ToArray()) SendView(console, actor);
    }

    private void SendView(EntityUid console, EntityUid body)
    {
        if (!_subscriptions.TryGetValue((console, body), out var subscription)) return;
        if (TerminatingOrDeleted(console) || !_ui.IsUiOpen(console, SWGlobalWarGunneryUiKey.Key, body))
        { _subscriptions.Remove((console, body)); return; }
        var authorized = CurrentContext(console, body, out var ship, out var assignment);
        var guns = new List<SWGlobalWarGunView>();
        var hulls = new List<SWGlobalWarRadarHull>();
        var center = _transform.GetWorldPosition(console);
        if (authorized)
        {
            var query = EntityQueryEnumerator<SWGlobalWarGunMountComponent>();
            while (query.MoveNext(out var gun, out var mount))
            {
                if (Transform(gun).GridUid != ship) continue;
                var ammo = new GetAmmoCountEvent();
                RaiseLocalEvent(gun, ref ammo);
                var status = !this.IsPowered(gun, EntityManager) ? "unpowered" :
                    TryComp<DestructibleComponent>(gun, out var damage) && damage.IsBroken ? "broken" :
                    ammo.Count == 0 ? "empty" : mount.Operator == body ? mount.Status : "ready";
                guns.Add(new SWGlobalWarGunView(GetNetEntity(gun), Name(gun), _transform.GetWorldPosition(gun),
                    (float) (mount.BaseRotation + _transform.GetWorldRotation(ship)).Degrees,
                    mount.Arc, ammo.Count, ammo.Capacity, mount.Operator == body,
                    mount.Operator != null && mount.Operator != body, status));
            }
            var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (grids.MoveNext(out var grid, out var data, out var transform))
            {
                if (hulls.Count >= 32 || transform.MapUid != Transform(ship).MapUid ||
                    Vector2.DistanceSquared(_transform.GetWorldPosition(grid), center) > 128 * 128) continue;
                var matrix = _transform.GetWorldMatrix(grid);
                var box = data.LocalAABB;
                var corners = new[] { box.BottomLeft, box.BottomRight, box.TopRight, box.TopLeft }
                    .Select(point => Vector2.Transform(point, matrix)).ToArray();
                var friendly = _ownership.TryGetOwner(grid, assignment.Generation, out var owner, out _) && owner == assignment.Faction;
                hulls.Add(new SWGlobalWarRadarHull(corners, friendly, grid == ship));
            }
        }
        _ui.ServerSendUiMessage(console, SWGlobalWarGunneryUiKey.Key, new SWGlobalWarGunnerySnapshot(
            authorized ? assignment.Generation : Guid.Empty, authorized ? assignment.Revision : 0, subscription,
            GetNetEntity(body), GetNetEntity(Transform(console).MapUid).GetValueOrDefault(), center,
            authorized, guns.ToArray(), hulls.ToArray()), body);
    }
}
