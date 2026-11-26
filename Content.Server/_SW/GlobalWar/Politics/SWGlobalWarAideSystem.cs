using Content.Server._SW.GlobalWar.Combat;
using Content.Server._SW.GlobalWar.Market;
using Content.Server._SW.GlobalWar.Production;
using Content.Server._SW.GlobalWar.Spawning;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Combat;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>What a liaison officer has something to say about. Each comes once a round for each player.</summary>
public enum SWGlobalWarAideTrigger : byte
{
    OtherWorld,
    Boarded,
    War,
    OwnCapture,
    EnemyCapture,
    SectorLost,
    Returned,
    ArsenalEmpty,
    Delivery,
    Truce,
}

/// <summary>
/// Decides when a line is due. A line waits for a quiet moment, and gives up after a minute so it does not arrive long
/// after it mattered. It counts as said only once it has been.
/// </summary>
public sealed class SWGlobalWarAideQueue
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan QuietAfterCombat = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    private readonly HashSet<SWGlobalWarAideTrigger> _said = new();
    private SWGlobalWarAideTrigger? _pending;
    private TimeSpan _pendingSince;
    private TimeSpan? _lastSaid;

    public void Offer(SWGlobalWarAideTrigger trigger, TimeSpan now)
    {
        if (_said.Contains(trigger) || trigger == _pending)
            return;

        // What changes the whole war goes ahead of a tip.
        if (_pending is { } pending && (IsUrgent(pending) || !IsUrgent(trigger)))
            return;

        _pending = trigger;
        _pendingSince = now;
    }

    public SWGlobalWarAideTrigger? Take(TimeSpan now, TimeSpan? lastCombat)
    {
        if (_pending is not { } pending)
            return null;

        if (now - _pendingSince > GiveUpAfter)
        {
            _pending = null;
            return null;
        }

        if (now - _lastSaid < Interval || now - lastCombat < QuietAfterCombat)
            return null;

        _pending = null;
        _lastSaid = now;
        _said.Add(pending);
        return pending;
    }

    private static bool IsUrgent(SWGlobalWarAideTrigger trigger)
    {
        return trigger is SWGlobalWarAideTrigger.War or SWGlobalWarAideTrigger.EnemyCapture
            or SWGlobalWarAideTrigger.SectorLost or SWGlobalWarAideTrigger.Truce;
    }
}

/// <summary>
/// A liaison officer of the player's own side comes up with one useful line when something first happens to them in a
/// campaign. It needs a living campaign role, so the tutorial and the lobby never see it.
/// </summary>
public sealed class SWGlobalWarAideSystem : EntitySystem
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly EntProtoId RepublicAide = "SWGlobalWarAideRepublic";
    private static readonly EntProtoId EmpireAide = "SWGlobalWarAideEmpire";
    private const float ShownSeconds = 6f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarCampaignSystem _campaign = default!;
    [Dependency] private readonly SWGlobalWarCaptureSystem _capture = default!;
    [Dependency] private readonly SWGlobalWarMarketSystem _market = default!;
    [Dependency] private readonly SWGlobalWarPlanetSystem _planets = default!;
    [Dependency] private readonly SWGlobalWarSpawnSystem _spawn = default!;

    private readonly Dictionary<NetUserId, SWGlobalWarAideQueue> _queues = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastHurt = new();
    private TimeSpan _nextPoll;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _queues.Clear();
            _lastHurt.Clear();
        });
        SubscribeLocalEvent<SWGlobalWarLifeComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>(OnOwnershipChanged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete, before: new[] { typeof(SWGlobalWarSpawnSystem) });
        SubscribeLocalEvent<SWGlobalWarFactoryComponent, BoundUIOpenedEvent>(OnFactoryOpened);
        SubscribeLocalEvent<SWGlobalWarDeliveryCompletedEvent>(OnDelivery);
    }

    private SWGlobalWarAideQueue Queue(NetUserId user)
    {
        if (!_queues.TryGetValue(user, out var queue))
            _queues[user] = queue = new SWGlobalWarAideQueue();
        return queue;
    }

    private void OnDamageChanged(Entity<SWGlobalWarLifeComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            _lastHurt[ent.Comp.User] = _timing.CurTime;
    }

    private void OnOwnershipChanged(ref SWGlobalWarOwnershipChangedEvent args)
    {
        foreach (var session in _player.Sessions)
        {
            if (_assignments.TryGetAssignment(session.UserId, out var assignment) && assignment.Faction == args.PreviousOwner)
                Queue(session.UserId).Offer(SWGlobalWarAideTrigger.SectorLost, _timing.CurTime);
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // This runs before the spawn system records the new life, so a life here is the one before it.
        if (_campaign.TryGetCampaign(out _) && _spawn.TryGetLife(args.Player.UserId, out _))
            Queue(args.Player.UserId).Offer(SWGlobalWarAideTrigger.Returned, _timing.CurTime);
    }

    private void OnFactoryOpened(Entity<SWGlobalWarFactoryComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!ent.Comp.Arsenal
            || !_campaign.TryGetCampaign(out var campaign)
            || !TryComp<ActorComponent>(args.Actor, out var actor)
            || !_assignments.TryGetAssignment(actor.PlayerSession.UserId, out var assignment)
            || _market.ArsenalHasKit(campaign.Comp.Generation, ent, assignment.Faction))
        {
            return;
        }

        Queue(assignment.User).Offer(SWGlobalWarAideTrigger.ArsenalEmpty, _timing.CurTime);
    }

    private void OnDelivery(ref SWGlobalWarDeliveryCompletedEvent args)
    {
        Queue(args.User).Offer(SWGlobalWarAideTrigger.Delivery, _timing.CurTime);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextPoll)
            return;

        _nextPoll = _timing.CurTime + PollInterval;

        if (!_campaign.TryGetCampaign(out var campaign) || campaign.Comp.Phase != SWGlobalWarCampaignPhase.Running)
            return;

        var relations = CurrentRelations(campaign.Comp.Generation);
        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame
                || !_assignments.TryGetAssignment(session.UserId, out var current)
                || !_assignments.TryGetActor(session, campaign.Comp.Generation, current.Revision, out var actor))
            {
                continue;
            }

            var now = _timing.CurTime;
            var queue = Queue(session.UserId);
            OfferWhatIsTrue(queue, session, actor, relations, now);

            TimeSpan? lastCombat = _combat.IsInCombatMode(actor.Body) ? now : null;
            if (lastCombat == null && _lastHurt.TryGetValue(session.UserId, out var hurt))
                lastCombat = hurt;

            if (queue.Take(now, lastCombat) is { } trigger)
                Say(session, actor.Faction, trigger);
        }
    }

    private void OfferWhatIsTrue(SWGlobalWarAideQueue queue, ICommonSession session, SWGlobalWarAssignment actor,
        SWGlobalWarRelations? relations, TimeSpan now)
    {
        if (relations == SWGlobalWarRelations.War)
            queue.Offer(SWGlobalWarAideTrigger.War, now);
        else if (relations == SWGlobalWarRelations.Truce)
            queue.Offer(SWGlobalWarAideTrigger.Truce, now);

        if (HasComp<SWGlobalWarFleetShipComponent>(Transform(actor.Body).GridUid))
            queue.Offer(SWGlobalWarAideTrigger.Boarded, now);

        if (_planets.TryGetPhysicalLocation(actor.Body, out var planet, out var location)
            && location == SWGlobalWarPlanetLocation.Surface
            && _prototypes.Index(Comp<SWGlobalWarPlanetComponent>(planet).Prototype) is var world
            && !(world.Kind == SWGlobalWarSectorKind.Capital && world.InitialOwner == actor.Faction))
        {
            queue.Offer(SWGlobalWarAideTrigger.OtherWorld, now);
        }

        var consoles = EntityQueryEnumerator<SWGlobalWarCaptureConsoleComponent>();
        while (consoles.MoveNext(out var console, out _))
        {
            var view = _capture.GetView(session, console);
            if (view.Status is SWGlobalWarCaptureStatus.Unavailable or SWGlobalWarCaptureStatus.Idle
                || view.Attacker == SWGlobalWarFaction.Neutral)
            {
                continue;
            }

            if (view.Owner == actor.Faction && view.Attacker != actor.Faction)
                queue.Offer(SWGlobalWarAideTrigger.EnemyCapture, now);
            else if (view.Attacker == actor.Faction
                     && _planets.TryGetPlanetForEntity(console, out var consolePlanet)
                     && consolePlanet == planet)
                queue.Offer(SWGlobalWarAideTrigger.OwnCapture, now);
        }
    }

    private void Say(ICommonSession session, SWGlobalWarFaction faction, SWGlobalWarAideTrigger trigger)
    {
        var republic = faction == SWGlobalWarFaction.Republic;
        var side = republic ? "republic" : "empire";
        var message = Loc.GetString($"sw-global-war-aide-{trigger.ToString().ToLowerInvariant()}-{side}");
        RaiseNetworkEvent(new SWGlobalWarAideEvent(message, republic ? RepublicAide : EmpireAide, faction, ShownSeconds), session);
    }

    private SWGlobalWarRelations? CurrentRelations(Guid generation)
    {
        var query = EntityQueryEnumerator<SWGlobalWarPoliticsComponent>();
        while (query.MoveNext(out var politics))
        {
            if (politics.Generation == generation)
                return politics.Relations;
        }

        return null;
    }
}

/// <summary>Raised when a player has handed over a delivery that was accepted at its destination.</summary>
[ByRefEvent]
public readonly record struct SWGlobalWarDeliveryCompletedEvent(NetUserId User);
