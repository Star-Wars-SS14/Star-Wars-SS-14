using Robust.Shared.Network;

namespace Content.Server.Chat.Systems;

/// <summary>Authenticated living player's ordinary local speech, delivered in full to physically nearby recipients.
/// Whisper, radio and programmatic or relayed speech do not raise this event.</summary>
public sealed record SWPressLocalSpeechEvent(EntityUid Source, NetUserId SpeakerUserId, string DisplayName,
    string Message, IReadOnlyList<EntityUid> Recipients);
