using System.Linq;
using Content.Server._SW.GlobalWar.Identity;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Politics;

/// <summary>Addressed service notifications scoped to an authenticated session, body, mind and campaign.</summary>
public sealed class SWGlobalWarCharacterUiSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarAssignmentsSystem _assignments = default!;
    [Dependency] private readonly SWGlobalWarIdentitySystem _identity = default!;
    [Dependency] private readonly SWGlobalWarPoliticsSystem _politics = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<NetUserId, Watch> _watches = new();
    private TimeSpan _nextUpdate;
    private bool _refreshing;
    private bool _executing;

    private sealed class Watch(ICommonSession session, Guid cookie, EntityUid body, EntityUid mind, Guid generation,
        NetEntity netBody)
    {
        public readonly ICommonSession Session = session;
        public readonly Guid Cookie = cookie;
        public readonly EntityUid Body = body;
        public readonly EntityUid Mind = mind;
        public readonly Guid Generation = generation;
        public readonly NetEntity NetBody = netBody;
        public long Sequence;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarCharacterWatch>(OnWatch);
        SubscribeNetworkEvent<SWGlobalWarAppointmentRequest>(OnRequest);
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarPoliticsChangedEvent>((ref SWGlobalWarPoliticsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => RevokeAll());
        SubscribeLocalEvent<SWGlobalWarCampaignStartedEvent>((ref SWGlobalWarCampaignStartedEvent _) =>
            RaiseNetworkEvent(new SWGlobalWarServiceAvailable()), after: [typeof(SWGlobalWarAssignmentsSystem)]);
        SubscribeLocalEvent<PlayerDetachedEvent>(ev => Revoke(ev.Player.UserId));
        SubscribeLocalEvent<SWGlobalWarIdentityDetachedEvent>(OnMindRemoved);
        _players.PlayerStatusChanged += OnStatus;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnStatus;
        _watches.Clear();
        base.Shutdown();
    }

    private void OnMindRemoved(ref SWGlobalWarIdentityDetachedEvent args)
    {
        foreach (var (user, watch) in _watches.ToArray())
            if (watch.Mind == args.Mind)
                Revoke(user);
    }

    private void OnStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            Revoke(args.Session.UserId);
    }

    private void OnWatch(SWGlobalWarCharacterWatch msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (msg.Body is not { } netBody)
        {
            if (_watches.TryGetValue(session.UserId, out var old) && ReferenceEquals(old.Session, session) &&
                old.Cookie == msg.Cookie)
                _watches.Remove(session.UserId);
            return;
        }
        if (msg.Cookie == Guid.Empty || session.Status != SessionStatus.InGame || session.AttachedEntity is not { } body ||
            GetNetEntity(body) != netBody)
            return;
        if (!_assignments.TryGetRegistry(out var registry))
        {
            RaiseNetworkEvent(new SWGlobalWarCharacterSnapshot(msg.Cookie, 1,
                SWGlobalWarAssignmentsSystem.EmptyCharacter(netBody, Guid.Empty, SWGlobalWarViewAvailability.Unavailable)), session);
            return;
        }
        if (!_identity.TryGetActor(session, body, out var mind, out _))
        {
            RaiseNetworkEvent(new SWGlobalWarCharacterSnapshot(msg.Cookie, 1,
                SWGlobalWarAssignmentsSystem.EmptyCharacter(netBody, registry.Comp.Generation,
                    SWGlobalWarViewAvailability.Forbidden)), session);
            return;
        }
        if (_watches.TryGetValue(session.UserId, out var current) && current.Cookie == msg.Cookie)
        {
            if (ReferenceEquals(current.Session, session) && Valid(current))
                Refresh();
            return;
        }
        _watches[session.UserId] = new(session, msg.Cookie, body, mind, registry.Comp.Generation, netBody);
        Refresh();
    }

    private bool Valid(Watch watch)
    {
        return watch.Session.Status == SessionStatus.InGame && watch.Session.AttachedEntity == watch.Body &&
            _identity.TryGetActor(watch.Session, watch.Body, out var mind, out _) && mind == watch.Mind &&
            _assignments.TryGetRegistry(out var registry) && registry.Comp.Generation == watch.Generation;
    }

    private void OnRequest(SWGlobalWarAppointmentRequest msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var success = false;
        var failure = SWGlobalWarAppointmentFailure.Denied;
        if (_watches.TryGetValue(session.UserId, out var watch) && ReferenceEquals(watch.Session, session) &&
            watch.Cookie == msg.Cookie && watch.Generation == msg.Generation && Valid(watch) && !_executing)
        {
            _executing = true;
            try
            {
                success = _assignments.TryExecute(session, new(msg.Generation, msg.Request, msg.Action,
                    msg.ActorRevision, msg.Target, msg.TargetRevision, msg.Job, msg.Offer, msg.Vacancy, msg.Stage),
                    out _, out failure);
            }
            finally
            {
                _executing = false;
            }
        }
        RaiseNetworkEvent(new SWGlobalWarAppointmentResult(msg.Cookie,
            new(msg.Request, msg.Action, success, failure)), session);
        Refresh();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        Refresh();
    }

    private void Refresh()
    {
        // Succession projection may synchronously publish Changed. No nested snapshot may overtake this one.
        if (_refreshing || _executing)
            return;
        _refreshing = true;
        try
        {
            foreach (var (user, watch) in _watches.ToArray())
            {
                if (!Valid(watch))
                {
                    Revoke(user);
                    continue;
                }
                var view = _assignments.GetCharacterView(watch.Session, watch.NetBody) with
                {
                    Withdrawal = _politics.GetWithdrawalView(watch.Session),
                };
                if (_watches.GetValueOrDefault(user) == watch && Valid(watch))
                    RaiseNetworkEvent(new SWGlobalWarCharacterSnapshot(watch.Cookie, ++watch.Sequence, view), watch.Session);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RevokeAll()
    {
        foreach (var user in _watches.Keys.ToArray())
            Revoke(user, SWGlobalWarViewAvailability.Unavailable);
    }

    private void Revoke(NetUserId user, SWGlobalWarViewAvailability availability = SWGlobalWarViewAvailability.Forbidden)
    {
        if (!_watches.Remove(user, out var watch) || watch.Session.Status == SessionStatus.Disconnected)
            return;
        RaiseNetworkEvent(new SWGlobalWarCharacterSnapshot(watch.Cookie, ++watch.Sequence,
            SWGlobalWarAssignmentsSystem.EmptyCharacter(watch.NetBody, watch.Generation,
                availability)), watch.Session);
    }
}
