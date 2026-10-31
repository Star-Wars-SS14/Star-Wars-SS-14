using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Chat.Systems;
using Content.Server.Corvax.TTS;
using Content.Server.Examine;
using Content.Shared._SW._Senate.PressVideo;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._SW._Senate.PressVideo;

public sealed partial class SWPressVideoSystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly TTSManager _tts = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;

    private readonly Dictionary<EntityUid, NetUserId> _speakers = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, long>> _audioScreens = new();
    private EntityUid? _broadcaster;
    private long _audioVersion;
    private bool _audioBusy;
    private bool _ttsFailed;
    private bool _ttsReady;
    private string _lastAudioStatus = string.Empty;
    private EntityUid? _activeSpeaker;
    private NetUserId _activeSpeakerUser;
    private float _audioCooldown;

    private void InitializeAudio()
    {
        SubscribeLocalEvent<SWPressLocalSpeechEvent>(OnLocalSpeech);
        Subs.BuiEvents<SWPressCameraComponent>(SWPressCameraUiKey.Key, subs =>
            subs.Event<SWPressSpeakerConsentMessage>(OnSpeakerConsent));
        Subs.BuiEvents<SWPressViewerComponent>(SWPressViewerUiKey.Key, subs =>
            subs.Event<SWPressViewerAudioMessage>(OnViewerAudio));
        SubscribeLocalEvent<SWPressCameraComponent, BoundUIClosedEvent>(OnCameraClosed);
    }

    private bool AudioConfigured() => _config.GetCVar(CCCVars.TTSEnabled)
        && !string.IsNullOrWhiteSpace(_config.GetCVar(CCCVars.TTSApiUrl))
        && _prototypes.TryIndex<TTSVoicePrototype>("Baya", out _);

    private string AudioStatus() => !AudioConfigured() || _ttsFailed
        ? "Звук недоступен"
        : _ttsReady ? "Звук доступен при согласии говорящего и зрителя" : "Звук проверяется при первой фразе";

    private bool LiveActor(EntityUid actor, out NetUserId user)
    {
        user = default;
        if (!TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame
            || player.PlayerSession.AttachedEntity != actor
            || !TryComp<MobStateComponent>(actor, out var mob)
            || mob.CurrentState != MobState.Alive)
            return false;
        user = player.PlayerSession.UserId;
        return true;
    }

    private bool ViewerActive(EntityUid actor, out NetUserId user)
    {
        user = default;
        if (!TryComp<ActorComponent>(actor, out var player)
            || player.PlayerSession.Status != SessionStatus.InGame
            || player.PlayerSession.AttachedEntity != actor)
            return false;
        user = player.PlayerSession.UserId;
        return true;
    }

    private bool SourceValid(EntityUid speaker, NetUserId user, EntityUid camera)
        => _source == camera && _eye != null && !Deleted(camera)
           && _power.HasCharge(camera, 1f)
           && _time.ModeActive
           && _broadcaster is { } broadcaster && _press.TryGetEdition(broadcaster, out _)
           && LiveActor(speaker, out var current) && current == user
           && _speakers.TryGetValue(speaker, out var consent) && consent == user
           && _ui.IsUiOpen(camera, SWPressCameraUiKey.Key, speaker)
           && _examine.InRangeUnOccluded(speaker, camera, 2f);

    private void OnSpeakerConsent(EntityUid uid, SWPressCameraComponent component, SWPressSpeakerConsentMessage args)
    {
        if (args.Enabled && _source == uid && (_speakers.Count < 64 || _speakers.ContainsKey(args.Actor)) && LiveActor(args.Actor, out var user)
            && _examine.InRangeUnOccluded(args.Actor, uid, 2f))
            _speakers[args.Actor] = user;
        else
            _speakers.Remove(args.Actor);
        InvalidateAudio();
        _ui.ServerSendUiMessage(uid, SWPressCameraUiKey.Key, new SWPressSpeakerConsentResult(args.Enabled, _speakers.ContainsKey(args.Actor)), args.Actor);
    }

    private void OnCameraClosed(EntityUid uid, SWPressCameraComponent component, BoundUIClosedEvent args)
    {
        if (_speakers.Remove(args.Actor))
            InvalidateAudio();
    }

    private void OnViewerAudio(EntityUid uid, SWPressViewerComponent component, SWPressViewerAudioMessage args)
    {
        if (args.Enabled && _source != null && _screens.TryGetValue(args.Actor, out var screens)
            && screens.Contains(uid) && ViewerActive(args.Actor, out _))
        {
            if (!_audioScreens.TryGetValue(args.Actor, out var enabled))
                _audioScreens[args.Actor] = enabled = new Dictionary<EntityUid, long>();
            enabled[uid] = args.Token;
        }
        else
            RemoveAudioScreen(uid, args.Actor);
        InvalidateAudio();
    }

    private void RemoveAudioScreen(EntityUid screen, EntityUid actor)
    {
        if (!_audioScreens.TryGetValue(actor, out var enabled) || !enabled.Remove(screen))
            return;
        if (enabled.Count == 0)
            _audioScreens.Remove(actor);
        InvalidateAudio();
    }

    private void RemoveAudioActor(EntityUid actor)
    {
        if (_speakers.Remove(actor) | _audioScreens.Remove(actor))
            InvalidateAudio();
    }

    private void InvalidateAudio()
    {
        _audioVersion++;
        foreach (var actor in _audioScreens.Keys)
            if (TryComp<ActorComponent>(actor, out var player))
                RaiseNetworkEvent(new SWPressAudioStopEvent(), player.PlayerSession);
    }

    private void StopAudio()
    {
        InvalidateAudio();
        _speakers.Clear();
        _audioScreens.Clear();
        _broadcaster = null;
        _ttsFailed = false;
        _ttsReady = false;
        _activeSpeaker = null;
    }

    private async void OnLocalSpeech(SWPressLocalSpeechEvent ev)
    {
        if (_source is not { } camera || _audioBusy || _audioCooldown > 0f
            || !AudioConfigured() || !SourceValid(ev.Source, ev.SpeakerUserId, camera)
            || !ev.Recipients.Contains(ev.Source)
            || ev.Message.Length is < 1 or > 160 || _audioScreens.Count == 0
            || !_prototypes.TryIndex<TTSVoicePrototype>("Baya", out var voice))
            return;

        // Fixed synthetic voice avoids transmitting the player's selected voice identity.
        var text = Regex.Replace(ev.Message, @"[^a-zA-Zа-яА-ЯёЁ0-9,\-?!. ]", "").Trim();
        if (text.Length == 0)
            return;

        var version = _audioVersion;
        var targets = _audioScreens.SelectMany(pair => pair.Value.Select(screen => (Actor: pair.Key, Screen: screen.Key, Token: screen.Value)))
            .Where(target => ViewerActive(target.Actor, out _)
                && _screens.TryGetValue(target.Actor, out var open) && open.Contains(target.Screen)
                && _ui.IsUiOpen(target.Screen, SWPressViewerUiKey.Key, target.Actor))
            .ToArray();
        if (targets.Length == 0)
            return;
        _audioBusy = true;
        _audioCooldown = 3f;
        byte[]? audio;
        try
        {
            audio = await _tts.ConvertTextToSpeech(voice.Speaker, text);
        }
        catch (Exception)
        {
            audio = null;
        }
        finally
        {
            _audioBusy = false;
        }

        if (version != _audioVersion || !SourceValid(ev.Source, ev.SpeakerUserId, camera) || !AudioConfigured())
            return;
        var failed = audio == null || audio.Length < 27 || audio.Length > 1024 * 1024
            || audio[0] != (byte) 'O' || audio[1] != (byte) 'g' || audio[2] != (byte) 'g' || audio[3] != (byte) 'S';
        if (_ttsFailed != failed || _ttsReady == failed)
        {
            _ttsFailed = failed;
            _ttsReady = !failed;
            Refresh();
        }
        if (failed)
            return;

        var sent = new HashSet<NetUserId>();
        foreach (var (actor, screen, token) in targets)
        {
            if (!ViewerActive(actor, out var user) || sent.Contains(user)
                || !_screens.TryGetValue(actor, out var screens) || !screens.Contains(screen)
                || !_audioScreens.TryGetValue(actor, out var enabled) || (!enabled.TryGetValue(screen, out var currentToken) || currentToken != token)
                || !_ui.IsUiOpen(screen, SWPressViewerUiKey.Key, actor)
                || !TryComp<ActorComponent>(actor, out var player))
                continue;
            _activeSpeaker = ev.Source;
            _activeSpeakerUser = ev.SpeakerUserId;
            sent.Add(user);
            RaiseNetworkEvent(new SWPressAudioPlayEvent(audio!, token), player.PlayerSession);
        }
    }
}
