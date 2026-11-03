using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Combat;

[Serializable, NetSerializable]
public enum SWGlobalWarCaptureUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWGlobalWarCaptureStatus : byte
{
    Idle,
    Capturing,
    Contested,
    Abandoned,
    Unpowered,
    Unavailable,
    Completed,
}

[Serializable, NetSerializable]
public sealed record SWGlobalWarCaptureView(Guid Generation, long AssignmentRevision,
    SWGlobalWarFaction Owner, SWGlobalWarFaction Attacker, SWGlobalWarPlanetLocation Location,
    SWGlobalWarCaptureStatus Status, float Progress, float Duration, float AbandonmentRemaining,
    bool CanStart, bool CanCancel, string Reason, bool Ship = false);

[Serializable, NetSerializable]
public sealed class SWGlobalWarCaptureOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarCaptureSnapshot(SWGlobalWarCaptureView view, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarCaptureView View = view;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarCaptureRequest(Guid generation, long revision, Guid request,
    Guid subscription, bool cancel) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly Guid Request = request;
    public readonly Guid Subscription = subscription;
    public readonly bool Cancel = cancel;
}
