namespace Content.Server.Chat.Systems;

/// <summary>Ordinary local speech actually delivered in full to nearby living listeners; excludes radio and whispers.</summary>
public sealed record SWPressLocalSpeechEvent(string DisplayName, string Message, IReadOnlyList<EntityUid> Recipients);
