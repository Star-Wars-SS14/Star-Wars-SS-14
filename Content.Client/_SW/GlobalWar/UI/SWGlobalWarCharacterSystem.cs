using System.Linq;
using System.Numerics;
using Content.Client.CharacterInfo;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Client.UserInterface.Systems.Character;
using Content.Client.UserInterface.Screens;
using Content.Shared._SW.GlobalWar.Politics;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._SW.GlobalWar.UI;

/// <summary>Attachment-scoped state survives character control recreation, but never a detach or new campaign.</summary>
public sealed class SWGlobalWarCharacterSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly CharacterInfoSystem _character = default!;
    private Guid _cookie;
    private EntityUid? _body;
    private long _sequence;
    private TimeSpan _nextWatch;
    private SWGlobalWarCharacterPanel? _panel;
    private SWGlobalWarServiceBadge? _badge;
    private SWGlobalWarServiceNotice? _notice;
    private TimeSpan _noticeUntil;
    private long _noticeOrder;
    private readonly HashSet<string> _seen = new();
    private readonly Dictionary<string, Notice> _unread = new();
    private Notice? _navigateTo;
    public sealed record Notice(string Key, string Kind, Guid Id, int Stage = -1, long Order = 0);
    public bool HasUnread => _unread.Count != 0;
    public SWGlobalWarCharacterView? View { get; private set; }
    public SWGlobalWarAppointmentRequest? Pending { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public string Search = string.Empty;
    public NetUserId? Target;
    public long TargetRevision;
    public bool TargetLost;
    public ProtoId<JobPrototype>? Job;
    public Guid? Offer;
    public bool OfferOpen;
    public SWGlobalWarAppointmentAction? Confirmation;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SWGlobalWarCharacterSnapshot>(OnSnapshot);
        SubscribeNetworkEvent<SWGlobalWarAppointmentResult>(OnResult);
        SubscribeNetworkEvent<SWGlobalWarServiceAvailable>((_, _) => BeginWatch());
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(_ => BeginWatch());
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => Clear());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnControls);
    }

    private void OnControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent args)
    {
        if (args.Entity != _players.LocalEntity)
            return;
        if (_body != args.Entity || _cookie == Guid.Empty || View?.Availability == SWGlobalWarViewAvailability.Forbidden)
            BeginWatch();
        if (View == null || View.Availability == SWGlobalWarViewAvailability.Unavailable)
            return;
        _panel?.Dispose();
        _panel = new(this);
        args.Controls.Add(_panel);
        Refresh();
        if (_navigateTo is { } notice)
        {
            _panel.ShowNotice(notice);
        }
    }

    private void BeginWatch()
    {
        Clear();
        if (_players.LocalEntity is not { } body)
            return;
        _body = body;
        _cookie = Guid.NewGuid();
        _nextWatch = _timing.CurTime + TimeSpan.FromSeconds(2);
        RaiseNetworkEvent(new SWGlobalWarCharacterWatch(_cookie, GetNetEntity(body)));
    }

    private void Clear()
    {
        if (_cookie != Guid.Empty && _players.LocalSession != null)
            RaiseNetworkEvent(new SWGlobalWarCharacterWatch(_cookie, null));
        _cookie = Guid.Empty;
        _body = null;
        _sequence = 0;
        View = null;
        Pending = null;
        Result = string.Empty;
        ResetEditing();
        _seen.Clear();
        _unread.Clear();
        _navigateTo = null;
        _noticeUntil = TimeSpan.Zero;
        _noticeOrder = 0;
        Refresh();
    }

    private void ResetEditing()
    {
        Search = string.Empty;
        Target = null;
        TargetRevision = 0;
        TargetLost = false;
        Job = null;
        Offer = null;
        OfferOpen = false;
        Confirmation = null;
    }

    private void OnSnapshot(SWGlobalWarCharacterSnapshot msg, EntitySessionEventArgs args)
    {
        if (_cookie == Guid.Empty || msg.Cookie != _cookie || _body is not { } body ||
            _players.LocalEntity != body || msg.View.Body != GetNetEntity(body) || msg.Sequence <= _sequence ||
            View != null && msg.View.Generation != View.Generation)
            return;
        _sequence = msg.Sequence;
        if (msg.View.Availability != SWGlobalWarViewAvailability.Ready)
        {
            View = msg.View;
            Pending = null;
            Result = string.Empty;
            ResetEditing();
            _unread.Clear();
            _navigateTo = null;
            Refresh();
            return;
        }
        if (Pending is { } pending && msg.View.RecentResults.FirstOrDefault(r => r.Request == pending.Request) is { } receipt)
            ReceiveResult(receipt);
        if (View != null && (View.Assignment != msg.View.Assignment || View.AssignmentRevision != msg.View.AssignmentRevision))
        {
            Pending = null;
            ResetEditing();
        }
        var firstReady = View?.Availability != SWGlobalWarViewAvailability.Ready;
        View = msg.View;
        if (Target is { } target && !View.Targets.Any(t => t.User == target && t.Revision == TargetRevision))
        {
            Target = null;
            Job = null;
            TargetLost = true;
            Confirmation = null;
        }
        if (Offer is { } offer && View.Offers.All(o => o.Id != offer))
        {
            Offer = null;
            OfferOpen = false;
        }
        foreach (var incoming in View.Offers)
            RegisterNotice(new("offer:" + incoming.Id, "offer", incoming.Id));
        if (View.Succession is { Eligible: true } succession)
            RegisterNotice(new($"succession:{succession.Vacancy}:{succession.Stage}", "succession", succession.Vacancy, succession.Stage));
        foreach (var outgoing in View.Outgoing.Where(o => o.Status != SWGlobalWarOfferStatus.Pending))
            RegisterNotice(new($"outcome:{outgoing.Id}:{outgoing.Status}", "outcome", outgoing.Id));
        if (View.Withdrawal is { } withdrawal)
            RegisterNotice(new($"withdrawal:{withdrawal.Id}:{withdrawal.Status}", "withdrawal", withdrawal.Id));
        foreach (var (key, notice) in _unread.ToArray())
            if (!NoticeExists(notice))
                _unread.Remove(key);
        if (firstReady)
            _character.RequestCharacterInfo();
        Refresh();
    }

    private void OnResult(SWGlobalWarAppointmentResult msg, EntitySessionEventArgs args)
    {
        if (msg.Cookie != _cookie || Pending is not { } pending || pending.Request != msg.Result.Request ||
            _body != _players.LocalEntity || View?.Generation != pending.Generation)
            return;
        ReceiveResult(msg.Result);
        Refresh();
    }

    private void ReceiveResult(SWGlobalWarAppointmentResultView result)
    {
        var name = View?.Targets.FirstOrDefault(t => t.User == Pending?.Target)?.Name ?? string.Empty;
        var role = View?.Offers.FirstOrDefault(o => o.Id == Pending?.Offer)?.Role;
        Result = result.Accepted ? Loc.GetString("sw-global-war-service-success-" + result.Action.ToString().ToLowerInvariant(),
                ("name", name), ("role", SWGlobalWarCharacterPanel.Role(role)))
            : SWGlobalWarCharacterPanel.Failure(result.Failure);
        Pending = null;
        Confirmation = null;
    }

    /// <summary>Freezes one exact request. Retrying cannot silently pick up a new target, stage or assignment.</summary>
    public void Request(SWGlobalWarAppointmentAction action)
    {
        if (Pending != null || View is not { Availability: SWGlobalWarViewAvailability.Ready, Living: true } view)
            return;
        if (action is SWGlobalWarAppointmentAction.Offer or SWGlobalWarAppointmentAction.Dismiss &&
            !view.Targets.Any(t => t.User == Target && t.Revision == TargetRevision &&
                (action == SWGlobalWarAppointmentAction.Dismiss ? t.CanDismiss : Job != null && t.Jobs.Contains(Job.Value))))
            return;
        Pending = new(_cookie, view.Generation, Guid.NewGuid(), action, view.AssignmentRevision,
            Target ?? default, TargetRevision, Job, Offer ?? Guid.Empty,
            view.Succession?.Vacancy ?? Guid.Empty, view.Succession?.Stage ?? -1);
        Result = string.Empty;
        RaiseNetworkEvent(Pending);
        Refresh();
    }

    public void Retry()
    {
        if (Pending != null)
            RaiseNetworkEvent(Pending);
    }

    private void RegisterNotice(Notice notice)
    {
        if (!_seen.Add(notice.Key))
            return;
        _unread[notice.Key] = notice with { Order = ++_noticeOrder };
        _noticeUntil = _timing.CurTime + TimeSpan.FromSeconds(8);
    }

    private bool NoticeExists(Notice notice)
    {
        return notice.Kind switch
        {
            "offer" => View?.Offers.Any(o => o.Id == notice.Id) == true,
            "succession" => View?.Succession is { Eligible: true } succession &&
                succession.Vacancy == notice.Id && succession.Stage == notice.Stage,
            "withdrawal" => View?.Withdrawal?.Id == notice.Id,
            "outcome" => View?.Outgoing.Any(o => o.Id == notice.Id) == true,
            _ => false,
        };
    }

    private Notice? NextNotice()
    {
        return _unread.Values.OrderByDescending(n => n.Kind == "succession")
            .ThenByDescending(n => n.Order).FirstOrDefault();
    }

    /// <summary>Only an explicit click requests navigation; snapshots never move the reader's viewport.</summary>
    public void OpenDetails()
    {
        var withdrawal = View?.Withdrawal;
        _navigateTo = withdrawal is { Active: true } &&
                      (!HasUnread || _timing.CurTime >= _noticeUntil)
            ? new Notice(string.Empty, "withdrawal", withdrawal.Id)
            : NextNotice() ?? (withdrawal != null ? new Notice(string.Empty, "withdrawal", withdrawal.Id) : null);
        _ui.GetUIController<CharacterUIController>().OpenServiceMenu();
    }

    /// <summary>The menu's unread action always follows an event, independently of the persistent withdrawal indicator.</summary>
    public void OpenNextNotice()
    {
        _navigateTo = NextNotice();
        _ui.GetUIController<CharacterUIController>().OpenServiceMenu();
    }

    public void MarkViewed(Notice notice)
    {
        foreach (var key in _unread.Where(p => p.Value.Kind == notice.Kind && p.Value.Id == notice.Id)
                     .Select(p => p.Key).ToArray())
            _unread.Remove(key);
        if (_navigateTo == notice)
            _navigateTo = null;
        Refresh();
    }

    public void MarkOfferViewed(Guid id)
    {
        _unread.Remove("offer:" + id);
        Refresh();
    }

    public void Refresh()
    {
        if (_panel is { Disposed: false })
            _panel.Refresh();
        if (_badge is { Disposed: false })
            _badge.Visible = HasUnread;
        RefreshNotice();
    }

    private void RefreshNotice()
    {
        if (_notice is not { Disposed: false })
            return;
        var active = View?.Withdrawal is { Active: true };
        var timed = HasUnread && _timing.CurTime < _noticeUntil;
        _notice.Visible = View?.Availability == SWGlobalWarViewAvailability.Ready &&
            (active || timed);
        if (_notice.Visible)
        {
            var text = active ? SWGlobalWarCharacterPanel.WithdrawalIndicator(View!.Withdrawal!) : string.Empty;
            if (timed && (!active || NextNotice()?.Kind != "withdrawal"))
            {
                var notification = NextNotice()?.Kind == "succession" && View?.Succession is { } succession
                    ? Loc.GetString(succession.Stage < 2 ? "sw-global-war-service-priority-notice" :
                        "sw-global-war-service-volunteer-notice", ("time", SWGlobalWarCharacterPanel.Clock(succession.Remaining)))
                    : Loc.GetString("sw-global-war-service-new");
                text = text.Length == 0 ? notification : text + "\n" + notification;
            }
            _notice.SetText(text);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_body == null || _players.LocalEntity != _body)
            return;
        if (View == null && _timing.CurTime >= _nextWatch)
        {
            _nextWatch = _timing.CurTime + TimeSpan.FromSeconds(2);
            RaiseNetworkEvent(new SWGlobalWarCharacterWatch(_cookie, GetNetEntity(_body.Value)));
        }
        var button = _ui.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.CharacterButton;
        if (button != null && (_badge == null || _badge.Disposed || _badge.Parent != button))
        {
            _badge?.Dispose();
            _badge = new();
            button.AddChild(_badge);
            Refresh();
        }
        if (button != null && _ui.ActiveScreen is InGameScreen screen &&
            (_notice == null || _notice.Disposed || _notice.Parent != screen))
        {
            _notice?.Dispose();
            _notice = new(OpenDetails);
            screen.AddChild(_notice);
            LayoutContainer.SetAnchorPreset(_notice, LayoutContainer.LayoutPreset.TopLeft);
        }
        if (_notice is { Disposed: false } && _ui.ActiveScreen is InGameScreen hud && button != null)
        {
            const float left = 80;
            var chat = hud.ChatBox;
            var freeWidth = chat.GlobalPosition.X - hud.GlobalPosition.X - left - 12;
            var top = button.GlobalPosition.Y + button.Size.Y - hud.GlobalPosition.Y + 8;
            if (freeWidth < 180)
            {
                top = chat.GlobalPosition.Y + chat.Size.Y - hud.GlobalPosition.Y + 8;
                freeWidth = hud.Size.X - left - 90;
            }
            _notice.SetWidth = Math.Clamp(freeWidth, 180, 340);
            LayoutContainer.SetPosition(_notice, new Vector2(left, top));
        }
        RefreshNotice();
    }
}
