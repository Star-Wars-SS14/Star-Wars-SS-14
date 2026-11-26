using System.Linq;
using Content.Server._SW.GlobalWar.Politics;
using Content.Shared._SW.GlobalWar;
using Content.Shared._SW.GlobalWar.Politics;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW.GlobalWar.Market;

/// <summary>Addressed work snapshots use a fresh subscription for each opening; window closure never cancels work.</summary>
public sealed class SWGlobalWarHubConsoleSystem : EntitySystem
{
    [Dependency] private readonly SWGlobalWarMarketSystem _markets = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<(EntityUid Hub, EntityUid Actor), Guid> _subscriptions = new();
    private TimeSpan _nextUpdate;
    private long _sequence;

    public override void Initialize()
    {
        SubscribeLocalEvent<SWGlobalWarHubComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SWGlobalWarHubComponent, BoundUIClosedEvent>(OnClosed);
        Subs.BuiEvents<SWGlobalWarHubComponent>(SWGlobalWarHubUiKey.Key, subs =>
        {
            subs.Event<SWGlobalWarHubOpen>(OnSubscribe);
            subs.Event<SWGlobalWarHubStartRequest>(OnStart);
            subs.Event<SWGlobalWarHubCancelRequest>(OnCancel);
            subs.Event<SWGlobalWarHubGrantRequest>(OnGrant);
            subs.Event<SWGlobalWarHubRevokeRequest>(OnRevoke);
        });
        SubscribeLocalEvent<SWGlobalWarAssignmentsChangedEvent>((ref SWGlobalWarAssignmentsChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarOwnershipChangedEvent>((ref SWGlobalWarOwnershipChangedEvent _) => Refresh());
        SubscribeLocalEvent<SWGlobalWarCampaignStoppedEvent>((ref SWGlobalWarCampaignStoppedEvent _) => Refresh());
    }

    private void OnOpened(Entity<SWGlobalWarHubComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarHubUiKey.Key))
            _subscriptions.Remove((ent.Owner, args.Actor));
    }

    private void OnClosed(Entity<SWGlobalWarHubComponent> ent, ref BoundUIClosedEvent args)
    {
        if (Equals(args.UiKey, SWGlobalWarHubUiKey.Key))
            _subscriptions.Remove((ent.Owner, args.Actor));
    }

    private bool Current(EntityUid hub, EntityUid actor, Guid subscription)
    {
        return subscription != Guid.Empty && _subscriptions.TryGetValue((hub, actor), out var current) &&
            current == subscription && _ui.IsUiOpen(hub, SWGlobalWarHubUiKey.Key, actor);
    }

    private void OnSubscribe(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubOpen args)
    {
        if (args.Subscription == Guid.Empty || !_ui.IsUiOpen(ent.Owner, SWGlobalWarHubUiKey.Key, args.Actor))
            return;
        _subscriptions[(ent.Owner, args.Actor)] = args.Subscription;
        SendView(ent.Owner, args.Actor);
    }

    private void OnStart(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubStartRequest args)
    {
        if (args.Command == null || !Current(ent, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryStartHubUpgrade(player.PlayerSession, ent, args.Command, out var result, out var failure);
        Reply(ent, args.Actor, args.Subscription, args.Command.Request, success, failure, result);
    }

    private void OnCancel(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubCancelRequest args)
    {
        if (!Current(ent, args.Actor, args.Subscription) || !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryCancelHubUpgrade(player.PlayerSession, ent, args.Generation, args.Revision,
            args.Attempt, out var result);
        Reply(ent, args.Actor, args.Subscription, args.Attempt, success,
            success ? SWGlobalWarHubFailure.None : SWGlobalWarHubFailure.Denied, result);
    }

    private void OnGrant(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubGrantRequest args)
    {
        if (args.Command == null || !Current(ent, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryGrantHubWork(player.PlayerSession, ent, args.Command, out _);
        Reply(ent, args.Actor, args.Subscription, args.Command.Request, success,
            success ? SWGlobalWarHubFailure.None : SWGlobalWarHubFailure.Denied, null);
    }

    private void OnRevoke(Entity<SWGlobalWarHubComponent> ent, ref SWGlobalWarHubRevokeRequest args)
    {
        if (args.Request == Guid.Empty || !Current(ent, args.Actor, args.Subscription) ||
            !TryComp<ActorComponent>(args.Actor, out var player))
            return;
        var success = _markets.TryRevokeHubWork(player.PlayerSession, ent, args);
        Reply(ent, args.Actor, args.Subscription, args.Request, success,
            success ? SWGlobalWarHubFailure.None : SWGlobalWarHubFailure.Denied, null);
    }

    private void Reply(EntityUid hub, EntityUid actor, Guid subscription, Guid request, bool success,
        SWGlobalWarHubFailure failure, SWGlobalWarHubResult? result)
    {
        _ui.ServerSendUiMessage(hub, SWGlobalWarHubUiKey.Key,
            new SWGlobalWarHubReply(request, success, failure, result, GetNetEntity(actor), subscription), actor);
        Refresh();
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (hub, actor) in _subscriptions.Keys.ToArray())
            SendView(hub, actor);
    }

    private void SendView(EntityUid hub, EntityUid actor)
    {
        // Ownership events are raised before the campaign transaction closes. Its temporary
        // denial is not an identity change and must not invalidate a client's pending request.
        if (_markets.IsClosingCampaignStep)
            return;
        if (TerminatingOrDeleted(hub) || !_subscriptions.TryGetValue((hub, actor), out var subscription) ||
            !_ui.IsUiOpen(hub, SWGlobalWarHubUiKey.Key, actor) || !TryComp<ActorComponent>(actor, out var player))
        {
            _subscriptions.Remove((hub, actor));
            return;
        }
        _ui.ServerSendUiMessage(hub, SWGlobalWarHubUiKey.Key,
            new SWGlobalWarHubSnapshot(_markets.GetHubView(player.PlayerSession, hub), GetNetEntity(actor), subscription,
                ++_sequence), actor);
    }
}
