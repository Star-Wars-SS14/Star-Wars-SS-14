using System.Linq;
using Content.Server._SW._Senate.Core;
using Content.Server.GameTicking;
using Content.Shared.Coordinates;
using Content.Shared._SW._Senate.PressVideo;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.PowerCell;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._SW._Senate.PressVideo;

[RegisterComponent]
public sealed partial class SWPressCameraComponent : Component;
[RegisterComponent]
public sealed partial class SWPressViewerComponent : Component;

public sealed partial class SWPressVideoSystem : EntitySystem
{
    [Dependency] private readonly SWSenatePressSystem _press = default!;
    [Dependency] private readonly SWSenateTimeSystem _time = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ViewSubscriberSystem _views = default!;
    [Dependency] private readonly PowerCellSystem _power = default!;

    private EntityUid? _source;
    private EntityUid? _eye;
    private string _topic = string.Empty;
    private string _presenter = string.Empty;
    private string _edition = string.Empty;
    // Each actor may open multiple screens; this eye belongs exclusively to the feed.
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _screens = new();
    private float _chargeTimer;

    public override void Initialize()
    {
        InitializeAudio();
        SubscribeLocalEvent<SWPressCameraComponent, ComponentShutdown>(OnCameraShutdown);
        SubscribeLocalEvent<SWPressCameraComponent, BoundUIOpenedEvent>(OnCameraOpen);
        SubscribeLocalEvent<SWPressCameraComponent, PowerCellChangedEvent>(OnCellChanged);
        SubscribeLocalEvent<SWPressViewerComponent, BoundUIOpenedEvent>(OnViewerOpen);
        SubscribeLocalEvent<SWPressViewerComponent, BoundUIClosedEvent>(OnViewerClose);
        SubscribeLocalEvent<SWPressViewerComponent, ComponentShutdown>(OnViewerShutdown);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnd);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundFinished);
        Subs.BuiEvents<SWPressCameraComponent>(SWPressCameraUiKey.Key, subs =>
        {
            subs.Event<SWPressStartMessage>(OnStart);
            subs.Event<SWPressStopMessage>(OnStop);
        });
    }

    private void OnCameraOpen(EntityUid uid, SWPressCameraComponent component, BoundUIOpenedEvent args)
        => _ui.SetUiState(uid, SWPressCameraUiKey.Key, MakeState(uid));

    private void OnCellChanged(EntityUid uid, SWPressCameraComponent component, ref PowerCellChangedEvent args)
    {
        if (_source == uid && args.Ejected) Stop();
    }

    private void OnRoundFinished(RoundEndTextAppendEvent args) => Stop();

    private void OnStart(EntityUid uid, SWPressCameraComponent component, SWPressStartMessage args)
    {
        if (_source != null || !_press.TryGetEdition(args.Actor, out var edition)
            || !_power.HasCharge(uid, 1f))
            return;
        var topic = args.Topic?.Trim() ?? string.Empty;
        if (topic.Length is < 1 or > 120 || topic.Any(char.IsControl))
            return;
        _source = uid;
        _broadcaster = args.Actor;
        _chargeTimer = 0f;
        _topic = topic;
        _presenter = Identity.Name(args.Actor, EntityManager);
        _edition = edition switch
        {
            PressEdition.RepublicHerald => "Республиканский вестник",
            PressEdition.VoicesOfRepublic => "Голос миров",
            PressEdition.CoruscantTribune => "Корусантская трибуна",
            PressEdition.GalacticObserver => "Галактический обозреватель",
            _ => string.Empty,
        };
        _eye = SpawnAttachedTo("SWPressVideoEye", uid.ToCoordinates());
        foreach (var actor in _screens.Keys)
            Subscribe(actor);
        Refresh();
    }

    private void OnStop(EntityUid uid, SWPressCameraComponent component, SWPressStopMessage args)
    {
        if (_source == uid && _press.TryGetCorrespondent(args.Actor, out _))
            Stop();
    }

    private void OnCameraShutdown(EntityUid uid, SWPressCameraComponent component, ComponentShutdown args)
    {
        if (_source == uid)
            Stop();
    }

    private void OnViewerOpen(EntityUid uid, SWPressViewerComponent component, BoundUIOpenedEvent args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actor) || actor.PlayerSession.AttachedEntity != args.Actor)
            return;
        if (!_screens.ContainsKey(args.Actor) && _screens.Count >= 64)
        {
            _ui.CloseUi(uid, SWPressViewerUiKey.Key, args.Actor);
            return;
        }
        if (!_screens.TryGetValue(args.Actor, out var screens))
            _screens[args.Actor] = screens = new HashSet<EntityUid>();
        if (screens.Add(uid) && screens.Count == 1)
            Subscribe(args.Actor);
        RefreshViewer(uid);
    }

    private void OnViewerClose(EntityUid uid, SWPressViewerComponent component, BoundUIClosedEvent args)
        => RemoveViewer(uid, args.Actor);

    private void OnViewerShutdown(EntityUid uid, SWPressViewerComponent component, ComponentShutdown args)
    {
        foreach (var actor in _screens.Keys.ToArray())
            RemoveViewer(uid, actor);
    }

    private void OnDetached(PlayerDetachedEvent args)
    {
        RemoveAudioActor(args.Entity);
        if (!_screens.Remove(args.Entity))
            return;
        if (_eye is { } eye && !Deleted(eye))
            _views.RemoveViewSubscriber(eye, args.Player);
    }

    private void RemoveViewer(EntityUid screen, EntityUid actor)
    {
        RemoveAudioScreen(screen, actor);
        if (!_screens.TryGetValue(actor, out var screens) || !screens.Remove(screen) || screens.Count != 0)
            return;
        _screens.Remove(actor);
        if (_eye is { } eye && !Deleted(eye) && TryComp<ActorComponent>(actor, out var player))
            _views.RemoveViewSubscriber(eye, player.PlayerSession);
    }

    private void Subscribe(EntityUid actor)
    {
        if (_eye is { } eye && TryComp<ActorComponent>(actor, out var player))
            _views.AddViewSubscriber(eye, player.PlayerSession);
    }

    private void OnRoundEnd(RoundRestartCleanupEvent args)
    {
        Stop();
        _screens.Clear();
    }

    private void Stop()
    {
        StopAudio();
        var eye = _eye;
        if (eye is { } activeEye && !Deleted(activeEye))
            foreach (var actor in _screens.Keys)
                if (TryComp<ActorComponent>(actor, out var player))
                    _views.RemoveViewSubscriber(activeEye, player.PlayerSession);
        _source = null;
        _eye = null;
        _topic = _presenter = _edition = string.Empty;
        if (eye != null && !Deleted(eye.Value))
            QueueDel(eye.Value);
        Refresh();
    }

    public override void Update(float frameTime)
    {
        _audioCooldown = Math.Max(0f, _audioCooldown - frameTime);
        if (_activeSpeaker is { } speaker && (_source is not { } activeCamera
            || !AudioConfigured() || !SourceValid(speaker, _activeSpeakerUser, activeCamera)))
        {
            _activeSpeaker = null;
            InvalidateAudio();
        }
        if (_source is not { } source)
            return;
        if (!_time.ModeActive || Deleted(source) || _eye == null || Deleted(_eye.Value))
        {
            Stop();
            return;
        }
        _chargeTimer += frameTime;
        if (_chargeTimer < 1f)
            return;
        var status = AudioStatus();
        if (_lastAudioStatus != status)
        {
            _lastAudioStatus = status;
            Refresh();
        }
        var seconds = _chargeTimer;
        _chargeTimer = 0f;
        if (!_power.TryUseCharge(source, seconds))
            Stop();
    }

    private void Refresh()
    {
        var cameras = EntityQueryEnumerator<SWPressCameraComponent>();
        while (cameras.MoveNext(out var uid, out _))
            if (_ui.IsUiOpen(uid, SWPressCameraUiKey.Key))
                _ui.SetUiState(uid, SWPressCameraUiKey.Key, MakeState(uid));
        var viewers = EntityQueryEnumerator<SWPressViewerComponent>();
        while (viewers.MoveNext(out var uid, out _))
            RefreshViewer(uid);
    }

    private void RefreshViewer(EntityUid uid)
    {
        if (_ui.IsUiOpen(uid, SWPressViewerUiKey.Key))
            _ui.SetUiState(uid, SWPressViewerUiKey.Key, MakeState());
    }

    private SWPressVideoState MakeState(EntityUid? camera = null) => new()
    {
        SoundStatus = AudioStatus(), Camera = GetNetEntity(_eye), IsSource = _source != null && _source == camera, Topic = _topic, Presenter = _presenter, Edition = _edition,
    };
}
