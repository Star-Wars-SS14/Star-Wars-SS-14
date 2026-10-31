using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Examine;
using Content.Shared._SW._Senate.PressVideo;
using Content.Shared.Chat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._SW._Senate.PressVideo;

public sealed partial class SWPressVideoSystem
{
    [Dependency] private readonly ExamineSystem _examine = default!;
    [Dependency] private readonly SharedTransformSystem _speechTransform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private long _feedId;
    private TimeSpan _speechWindow;
    private int _speechCount;

    private void InitializeSpeech() => SubscribeLocalEvent<SWPressLocalSpeechEvent>(OnLocalSpeech);

    private bool InSpeechRange(EntityUid source, EntityUid camera)
    {
        var sourcePosition = _speechTransform.GetMapCoordinates(source);
        var cameraPosition = _speechTransform.GetMapCoordinates(camera);
        var range = SharedChatSystem.VoiceRange;
        if (sourcePosition.MapId != cameraPosition.MapId
            || (sourcePosition.Position - cameraPosition.Position).LengthSquared() >= range * range)
            return false;

        return _examine.InRangeUnOccluded(sourcePosition, cameraPosition, range,
            predicate: null, ignoreInsideBlocker: false);
    }

    private void OnLocalSpeech(SWPressLocalSpeechEvent ev)
    {
        if (_source is not { } camera || _eye == null || Deleted(camera) || Deleted(_eye.Value)
            || !_time.ModeActive || !_power.HasCharge(camera, 1f)
            || !TryComp<ActorComponent>(ev.Source, out var speaker)
            || speaker.PlayerSession.Status != SessionStatus.InGame
            || speaker.PlayerSession.AttachedEntity != ev.Source
            || speaker.PlayerSession.UserId != ev.SpeakerUserId
            || !TryComp<MobStateComponent>(ev.Source, out var mob) || mob.CurrentState != MobState.Alive
            || !ev.Recipients.Contains(ev.Source) || !InSpeechRange(ev.Source, camera)
            || string.IsNullOrWhiteSpace(ev.Message))
            return;

        if (_timing.CurTime - _speechWindow >= TimeSpan.FromSeconds(10))
        {
            _speechWindow = _timing.CurTime;
            _speechCount = 0;
        }
        if (_speechCount >= 20)
            return;
        _speechCount++;

        var text = ev.Message;
        if (text.Length > 512)
        {
            var length = char.IsHighSurrogate(text[511]) ? 511 : 512;
            text = text[..length] + "…";
        }

        // This is deliberately a neutral voice label, never the player's identity or a radio relay.
        var message = new SWPressVideoSpeechMessage(_feedId, text);
        foreach (var (actor, screens) in _screens)
        {
            if (!TryComp<ActorComponent>(actor, out var viewer)
                || viewer.PlayerSession.Status != SessionStatus.InGame
                || viewer.PlayerSession.AttachedEntity != actor)
                continue;
            foreach (var screen in screens)
                if (_ui.IsUiOpen(screen, SWPressViewerUiKey.Key, actor))
                    _ui.ServerSendUiMessage(screen, SWPressViewerUiKey.Key, message, actor);
        }
    }
}
