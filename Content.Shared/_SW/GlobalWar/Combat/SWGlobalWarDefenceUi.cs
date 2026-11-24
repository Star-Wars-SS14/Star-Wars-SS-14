using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Combat;

[Serializable, NetSerializable]
public enum SWGlobalWarDefenceUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWGlobalWarDefenceCommand : byte { Hold, Objective, Retreat, Follow, Order }

/// <summary>
/// What the droid defence console tells one viewer: the droids of the viewer's side here and in all, and which
/// commands this viewer may give. Reason is a locale key, empty when the console works for the viewer.
/// </summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarDefenceView(string Reason, int Here, int HereLimit, int Side, int SideLimit,
    int Nearby, int Ordered, bool CanCommand, bool CanOrder, long Price, int BuildSeconds, string Result);

[Serializable, NetSerializable]
public sealed class SWGlobalWarDefenceOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarDefenceSnapshot(SWGlobalWarDefenceView view, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarDefenceView View = view;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarDefenceRequest(Guid subscription, SWGlobalWarDefenceCommand command)
    : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
    public readonly SWGlobalWarDefenceCommand Command = command;
}
