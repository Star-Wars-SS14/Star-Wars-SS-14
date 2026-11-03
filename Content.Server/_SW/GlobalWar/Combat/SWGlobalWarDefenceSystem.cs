using System.Linq;
using System.Numerics;
using Content.Server._SW.Droids;
using Content.Server._SW.GlobalWar.Mapping;
using Content.Server._SW.GlobalWar.Politics;
using Content.Server._SW.Targeting;
using Content.Server.Destructible;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Shared._SW.Droids;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Mapping;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.ActionBlocker;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell.Components;
using Content.Shared.Projectiles;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Combat;

/// <summary>
/// A real station device commanding faction droids and taking funded orders for finite new ones.
/// Its window gives the same commands as its context menu.
/// </summary>
[RegisterComponent]
public sealed partial class SWGlobalWarDefenceConsoleComponent : Component;

/// <summary>
/// Physical defence commands use current appointments and the existing droid AI/command arbitration.
/// An order pays from the war budget, and its droid stands at the capture console, so a location defends itself
/// without an operator. Steel and electronics are not part of the price: most worlds never hold any.
/// </summary>
public sealed class SWGlobalWarDefenceSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarMapAnchorSystem _anchors = default!;
    [Dependency] private readonly SWDroidCommandSystem _droids = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SWGlobalWarLedgerSystem _ledger = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const long DroidPrice = 8000;
    // Droids stop a small raid and buy time. Scripted, five armed soldiers beat three droids and die to four.
    private const int DroidsPerLocation = 3;
    private const int DroidsPerSide = 12;
    private const float PlacementRadius = 4f;
    private const float PlacementHalfSize = 0.35f;
    private static readonly TimeSpan DroidBuildTime = TimeSpan.FromSeconds(60);
    private static readonly EntProtoId RepublicDroid = "SWGlobalWarDroidRepublic";
    private static readonly EntProtoId EmpireDroid = "SWGlobalWarDroidEmpire";

    private readonly Dictionary<Guid, DroidOrder> _orders = new();
    private readonly Dictionary<(EntityUid Console, EntityUid Actor), (Guid Subscription, string Result)> _viewers = new();
    private Guid _generation;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarDefenceConsoleComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<SWGlobalWarDefenceConsoleComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<SWGlobalWarDefenceConsoleComponent, BoundUIClosedEvent>(OnClosed);
        Subs.BuiEvents<SWGlobalWarDefenceConsoleComponent>(SWGlobalWarDefenceUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarDefenceOpen>(OnOpen);
            subs.Event<SWGlobalWarDefenceRequest>(OnRequest);
        });
        SubscribeLocalEvent<SWDroidCommandableComponent, EntityTerminatingEvent>(OnDroidTerminating);
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent ev) => Reset(ev.Generation));
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Reset(Guid.Empty));
    }

    /// <summary>A wrecked droid is replaced by a new entity, and a bolt in flight must not name a deleted shooter.</summary>
    private void OnDroidTerminating(Entity<SWDroidCommandableComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_generation == Guid.Empty)
            return;
        var query = EntityQueryEnumerator<ProjectileComponent>();
        while (query.MoveNext(out var bolt, out var projectile))
        {
            if (projectile.Shooter == ent.Owner)
                QueueDel(bolt);
        }
    }

    private void Reset(Guid generation)
    {
        _orders.Clear();
        _viewers.Clear();
        _generation = generation;
        _nextUpdate = TimeSpan.Zero;
    }

    private bool Context(EntityUid console, EntityUid body, out SWGlobalWarAssignment assignment,
        out EntityUid planet, out SWGlobalWarPlanetLocation location)
    {
        assignment = default!;
        planet = default;
        location = default;
        return _campaign.TryGetCampaign(out var campaign) && campaign.Comp.Phase == SWGlobalWarCampaignPhase.Running &&
               !TerminatingOrDeleted(console) && HasComp<SWGlobalWarDefenceConsoleComponent>(console) &&
               Transform(console).Anchored && this.IsPowered(console, EntityManager) &&
               !(TryComp<DestructibleComponent>(console, out var damage) && damage.IsBroken) &&
               TryComp<ActorComponent>(body, out var actor) &&
               _assignments.TryGetAssignment(actor.PlayerSession.UserId, out assignment) &&
               _assignments.TryGetActor(actor.PlayerSession, campaign.Comp.Generation, assignment.Revision, out assignment) &&
               (assignment.Role is SWGlobalWarRole.Soldier or SWGlobalWarRole.Pilot or SWGlobalWarRole.ForceUser ||
                (_assignments.GetAuthority(assignment) & SWGlobalWarAuthority.MilitaryCommand) != 0) &&
               _blocker.CanInteract(body, console) && _interaction.InRangeUnobstructed(body, console, range: 2f) &&
               _planets.TryGetPhysicalLocation(console, out planet, out location) &&
               (location == SWGlobalWarPlanetLocation.Orbit ? Comp<SWGlobalWarPlanetComponent>(planet).OrbitOwner :
                   Comp<SWGlobalWarPlanetComponent>(planet).SurfaceOwner) == assignment.Faction;
    }

    private void OnVerbs(EntityUid uid, SWGlobalWarDefenceConsoleComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !Context(uid, args.User, out _, out _, out _)) return;
        foreach (var command in new[] { "hold", "objective", "retreat", "follow" })
        {
            var body = args.User;
            args.Verbs.Add(new ActivationVerb
            {
                Text = Loc.GetString($"sw-global-war-defence-{command}"),
                Act = () => Execute(uid, body, command),
            });
        }
        if (!Context(uid, args.User, out var assignment, out _, out _) ||
            (_assignments.GetAuthority(assignment) & SWGlobalWarAuthority.WarBudget) == 0)
            return;
        var buyer = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("sw-global-war-defence-order", ("price", DroidPrice),
                ("seconds", (int) DroidBuildTime.TotalSeconds)),
            Act = () => TryOrderDroid(uid, buyer, out _),
        });
    }

    private void OnExamined(EntityUid uid, SWGlobalWarDefenceConsoleComponent component, ExaminedEvent args)
    {
        if (!Context(uid, args.Examiner, out var assignment, out var planet, out var location))
            return;
        args.PushMarkup(Loc.GetString("sw-global-war-defence-status",
            ("here", CountDroids(assignment.Faction, planet, location)), ("hereLimit", DroidsPerLocation),
            ("side", CountDroids(assignment.Faction)), ("sideLimit", DroidsPerSide)));
    }

    private static string FactionName(SWGlobalWarFaction faction)
    {
        return faction == SWGlobalWarFaction.Republic ? "RepublicFaction" : "EmpireFaction";
    }

    /// <summary>Live droids plus those already paid for, on paused locations too, for the side or for one location.</summary>
    private int CountDroids(SWGlobalWarFaction faction, EntityUid? planet = null,
        SWGlobalWarPlanetLocation location = default)
    {
        var count = 0;
        var name = FactionName(faction);
        var query = EntityManager.AllEntityQueryEnumerator<SWDroidCommandableComponent>();
        while (query.MoveNext(out var droid, out var settings))
        {
            if (settings.Faction != name || !_mobs.IsAlive(droid))
                continue;
            if (planet == null ||
                _planets.TryGetPhysicalLocation(droid, out var droidPlanet, out var droidLocation) &&
                droidPlanet == planet && droidLocation == location)
                count++;
        }
        foreach (var order in _orders.Values)
        {
            if (order.Faction == faction && (planet == null || order.Planet == planet && order.Location == location))
                count++;
        }
        return count;
    }

    /// <summary>Reserves the money for one droid, spent when it arrives. A refusal changes nothing.</summary>
    public bool TryOrderDroid(EntityUid console, EntityUid body, out string failure)
    {
        failure = "sw-global-war-defence-order-denied";
        if (Context(console, body, out var assignment, out var planet, out var location) &&
            (_assignments.GetAuthority(assignment) & SWGlobalWarAuthority.WarBudget) != 0)
        {
            if (CountDroids(assignment.Faction, planet, location) >= DroidsPerLocation)
                failure = "sw-global-war-defence-order-location-limit";
            else if (CountDroids(assignment.Faction) >= DroidsPerSide)
                failure = "sw-global-war-defence-order-side-limit";
            else if (ReserveOrder(console, assignment, planet, location, out failure))
                failure = string.Empty;
        }
        var accepted = failure.Length == 0;
        _popup.PopupEntity(Loc.GetString(accepted ? "sw-global-war-defence-order-accepted" : failure,
            ("seconds", (int) DroidBuildTime.TotalSeconds)), console, body);
        return accepted;
    }

    private bool ReserveOrder(EntityUid console, SWGlobalWarAssignment assignment, EntityUid planet,
        SWGlobalWarPlanetLocation location, out string failure)
    {
        failure = "sw-global-war-defence-order-funds";
        using var notifications = _ledger.DeferNotifications();
        if (!_ledger.TryGetLedger(out var ledger) || !_ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(
                assignment.Generation, Guid.NewGuid(), ledger.Comp.Revision, SWGlobalWarLedgerAction.Reserve,
                assignment.Faction, SWGlobalWarAccount.War, DroidPrice, Purpose: "Defence droid"), out var reservation,
                assignment.User))
            return false;
        _orders.Add(Guid.NewGuid(), new DroidOrder(console, planet, location, assignment.Faction, reservation,
            _timing.CurTime + DroidBuildTime));
        return true;
    }

    /// <summary>
    /// Campaign droids guard on their own. Without a targeting config the droid's query rejects every target, and that
    /// config otherwise comes only from a tablet the campaign does not issue. A drained borg cell switches the chassis
    /// off for good, and no map has a charging pad, so a paid droid does not draw from its cell.
    /// </summary>
    private void PrepareDroids()
    {
        var query = EntityQueryEnumerator<SWDroidCommandableComponent>();
        while (query.MoveNext(out var droid, out var settings))
        {
            if (string.IsNullOrEmpty(settings.Faction) || HasComp<SWTargetingConfigComponent>(droid) ||
                !_planets.TryGetPhysicalLocation(droid, out _, out _))
                continue;
            AddComp<SWTargetingConfigComponent>(droid).TargetHumanoidsWithoutId = true;
            RemComp<PowerCellDrawComponent>(droid);
        }
    }

    public override void Update(float frameTime)
    {
        if (_generation == Guid.Empty || _timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        PrepareDroids();
        foreach (var (id, order) in _orders.ToArray())
        {
            if (!TryComp<SWGlobalWarPlanetComponent>(order.Planet, out var world))
            {
                _orders.Remove(id);
                continue;
            }
            var owner = order.Location == SWGlobalWarPlanetLocation.Orbit ? world.OrbitOwner : world.SurfaceOwner;
            if (TerminatingOrDeleted(order.Console) || owner != order.Faction ||
                !_ledger.TryGetReservation(order.Reservation, out _))
            {
                CancelOrder(id, order);
                continue;
            }
            if (_timing.CurTime < order.ReadyAt || !this.IsPowered(order.Console, EntityManager) ||
                !TryFindPost(order, out var post))
                continue;
            using var notifications = _ledger.DeferNotifications();
            if (!_ledger.TryGetLedger(out var ledger) || !_ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(_generation,
                    Guid.NewGuid(), ledger.Comp.Revision, SWGlobalWarLedgerAction.Commit, order.Faction,
                    SWGlobalWarAccount.War, DroidPrice, Reservation: order.Reservation), out _))
            {
                CancelOrder(id, order);
                continue;
            }
            _orders.Remove(id);
            Spawn(order.Faction == SWGlobalWarFaction.Republic ? RepublicDroid : EmpireDroid, post);
        }
        foreach (var (console, actor) in _viewers.Keys.ToArray())
            SendView(console, actor);
    }

    private void CancelOrder(Guid id, DroidOrder order)
    {
        _orders.Remove(id);
        using var notifications = _ledger.DeferNotifications();
        if (_ledger.TryGetReservation(order.Reservation, out _) && _ledger.TryGetLedger(out var ledger))
        {
            _ledger.TryExecuteSystem(new SWGlobalWarLedgerCommand(_generation, Guid.NewGuid(), ledger.Comp.Revision,
                SWGlobalWarLedgerAction.Release, order.Faction, SWGlobalWarAccount.War,
                Reservation: order.Reservation), out _);
        }
    }

    /// <summary>A free cell in sight of the capture console, or of this console if that one is gone.</summary>
    private bool TryFindPost(DroidOrder order, out EntityCoordinates post)
    {
        post = default;
        var address = new SWGlobalWarAnchorRef
        {
            Sector = Comp<SWGlobalWarPlanetComponent>(order.Planet).Prototype,
            Location = order.Location,
            AnchorId = "capturePrimary",
        };
        var center = _anchors.TryResolveAnchor(address, out var objective) ? objective.Value : order.Console;
        var transform = Transform(center);
        if (!TryComp<MapGridComponent>(transform.GridUid, out var grid))
            return false;
        for (var radius = 2; radius <= PlacementRadius; radius++)
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                continue;
            var candidate = transform.Coordinates.Offset(new Vector2(x, y));
            if (_maps.GetTileRef(transform.GridUid!.Value, grid, candidate).Tile.IsEmpty)
                continue;
            var position = _transform.ToMapCoordinates(candidate);
            var bounds = Box2.CenteredAround(position.Position, new Vector2(PlacementHalfSize * 2));
            if (_lookup.AnyEntitiesIntersecting(position.MapId, bounds, LookupFlags.Static | LookupFlags.Dynamic) ||
                !_interaction.InRangeUnobstructed(center, candidate, PlacementRadius * 1.5f))
                continue;
            post = candidate;
            return true;
        }
        return false;
    }

    /// <summary>Gives the order to every friendly droid within 64 m. Null when this body may not command here.</summary>
    private int? Execute(EntityUid console, EntityUid body, string command)
    {
        if (!Context(console, body, out var assignment, out var planet, out var location)) return null;
        var destination = Transform(console).Coordinates;
        if (command == "objective")
        {
            var address = new SWGlobalWarAnchorRef
            {
                Sector = Comp<SWGlobalWarPlanetComponent>(planet).Prototype,
                Location = location,
                AnchorId = "capturePrimary",
            };
            if (!_anchors.TryResolveAnchor(address, out var objective)) return null;
            destination = Transform(objective.Value).Coordinates;
        }
        var faction = FactionName(assignment.Faction);
        var rank = assignment.Role switch
        {
            SWGlobalWarRole.Head or SWGlobalWarRole.WarMinister => 100,
            SWGlobalWarRole.SeniorOfficer => 80,
            SWGlobalWarRole.JuniorOfficer => 60,
            _ => 40,
        };
        var order = command switch
        {
            "hold" => SWDroidOrderType.Stay,
            "follow" => SWDroidOrderType.Follow,
            _ => SWDroidOrderType.GoTo,
        };
        var count = 0;
        foreach (var droid in NearbyDroids(console, faction))
        {
            if (count >= 32)
                break;
            if (_droids.TrySetDefenceOrder(droid, body, faction, rank, order, destination)) count++;
        }
        _popup.PopupEntity(Loc.GetString("sw-global-war-defence-issued", ("count", count)), console, body);
        return count;
    }

    private List<EntityUid> NearbyDroids(EntityUid console, string faction)
    {
        var droids = new List<EntityUid>();
        var map = Transform(console).MapUid;
        var position = _transform.GetWorldPosition(console);
        var query = EntityQueryEnumerator<SWDroidCommandableComponent>();
        while (query.MoveNext(out var droid, out var settings))
        {
            if (settings.Faction == faction && Transform(droid).MapUid == map &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(droid), position) <= 64 * 64)
                droids.Add(droid);
        }
        return droids;
    }

    private void OnOpen(Entity<SWGlobalWarDefenceConsoleComponent> ent, ref SWGlobalWarDefenceOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarDefenceUiKey.Key, args.Actor))
            return;
        _viewers[(ent, args.Actor)] = (args.Subscription, string.Empty);
        SendView(ent, args.Actor);
    }

    private void OnClosed(Entity<SWGlobalWarDefenceConsoleComponent> ent, ref BoundUIClosedEvent args)
    {
        _viewers.Remove((ent, args.Actor));
    }

    private void OnRequest(Entity<SWGlobalWarDefenceConsoleComponent> ent, ref SWGlobalWarDefenceRequest args)
    {
        if (!_viewers.TryGetValue((ent, args.Actor), out var viewer) || viewer.Subscription != args.Subscription)
            return;
        string result;
        if (args.Command == SWGlobalWarDefenceCommand.Order)
        {
            result = TryOrderDroid(ent, args.Actor, out var failure)
                ? Loc.GetString("sw-global-war-defence-order-accepted", ("seconds", (int) DroidBuildTime.TotalSeconds))
                : Loc.GetString(failure);
        }
        else
        {
            var count = Execute(ent, args.Actor, args.Command.ToString().ToLowerInvariant());
            result = count is { } issued
                ? Loc.GetString("sw-global-war-defence-issued", ("count", issued))
                : Loc.GetString("sw-global-war-defence-ui-denied");
        }
        _viewers[(ent, args.Actor)] = (viewer.Subscription, result);
        SendView(ent, args.Actor);
    }

    private void SendView(EntityUid console, EntityUid actor)
    {
        if (!_viewers.TryGetValue((console, actor), out var viewer))
            return;
        if (TerminatingOrDeleted(console) || !_ui.IsUiOpen(console, SWGlobalWarDefenceUiKey.Key, actor))
        {
            _viewers.Remove((console, actor));
            return;
        }
        _ui.ServerSendUiMessage(console, SWGlobalWarDefenceUiKey.Key,
            new SWGlobalWarDefenceSnapshot(GetView(console, actor, viewer.Result), viewer.Subscription,
                GetNetEntity(actor)), actor);
    }

    /// <summary>Only this side's own droids are counted; anyone else sees why the console refuses them.</summary>
    public SWGlobalWarDefenceView GetView(EntityUid console, EntityUid body, string result = "")
    {
        var seconds = (int) DroidBuildTime.TotalSeconds;
        if (!Context(console, body, out var assignment, out var planet, out var location))
            return new(Denial(console, body), 0, DroidsPerLocation, 0, DroidsPerSide, 0, 0, false, false,
                DroidPrice, seconds, result);
        var ordered = _orders.Values.Count(o => o.Faction == assignment.Faction && o.Planet == planet && o.Location == location);
        return new(string.Empty, CountDroids(assignment.Faction, planet, location), DroidsPerLocation,
            CountDroids(assignment.Faction), DroidsPerSide, NearbyDroids(console, FactionName(assignment.Faction)).Count,
            ordered, true, (_assignments.GetAuthority(assignment) & SWGlobalWarAuthority.WarBudget) != 0,
            DroidPrice, seconds, result);
    }

    /// <summary>The first condition of <see cref="Context"/> that this body fails, as a locale key.</summary>
    private string Denial(EntityUid console, EntityUid body)
    {
        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running)
            return "sw-global-war-defence-ui-closed";
        if (!Transform(console).Anchored || !this.IsPowered(console, EntityManager) ||
            TryComp<DestructibleComponent>(console, out var damage) && damage.IsBroken)
            return "sw-global-war-defence-ui-unpowered";
        if (!TryComp<ActorComponent>(body, out var actor) ||
            !_assignments.TryGetAssignment(actor.PlayerSession.UserId, out var assignment))
            return "sw-global-war-defence-ui-no-side";
        if (!_planets.TryGetPhysicalLocation(console, out var planet, out var location) ||
            (location == SWGlobalWarPlanetLocation.Orbit ? Comp<SWGlobalWarPlanetComponent>(planet).OrbitOwner :
                Comp<SWGlobalWarPlanetComponent>(planet).SurfaceOwner) != assignment.Faction)
            return "sw-global-war-defence-ui-not-yours";
        if (assignment.Role is not (SWGlobalWarRole.Soldier or SWGlobalWarRole.Pilot or SWGlobalWarRole.ForceUser) &&
            (_assignments.GetAuthority(assignment) & SWGlobalWarAuthority.MilitaryCommand) == 0)
            return "sw-global-war-defence-ui-no-rank";
        return "sw-global-war-defence-ui-reach";
    }

    private sealed record DroidOrder(EntityUid Console, EntityUid Planet, SWGlobalWarPlanetLocation Location,
        SWGlobalWarFaction Faction, Guid Reservation, TimeSpan ReadyAt);
}
