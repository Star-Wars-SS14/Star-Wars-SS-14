namespace Content.Shared._SW.ModularSaber;

// These directed events bracket only the synchronous drop/throw attempt in HandsSystem.
[ByRefEvent]
public readonly record struct SWSaberThrowAttemptStartedEvent(EntityUid User);

[ByRefEvent]
public readonly record struct SWSaberThrowAttemptFinishedEvent(EntityUid User);
