using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Combat;

[Serializable, NetSerializable]
public enum SWGlobalWarFleetUiKey : byte { Key }

[Serializable, NetSerializable]
public enum SWGlobalWarShipClass : byte { Transport, Fighter, Bomber, Frigate, Cruiser, Battleship }

[Serializable, NetSerializable]
public enum SWGlobalWarShipyardTier : byte { Transport, SmallMedium, Heavy }

[Serializable, NetSerializable]
public sealed record SWGlobalWarFleetOffer(SWGlobalWarShipClass Class, long Price, int Seconds, bool Available);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFleetOrderView(Guid Id, SWGlobalWarShipClass Class, int Seconds, string Status, bool CanCancel);

[Serializable, NetSerializable]
public sealed record SWGlobalWarFleetView(Guid Generation, long Revision, long Available, bool Authorized,
    bool Head, string Reason, SWGlobalWarFleetOffer[] Offers, SWGlobalWarFleetOrderView[] Orders);

[Serializable, NetSerializable]
public sealed class SWGlobalWarFleetOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFleetSnapshot(SWGlobalWarFleetView view, Guid subscription, NetEntity viewer)
    : BoundUserInterfaceMessage
{
    public readonly SWGlobalWarFleetView View = view;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarFleetRequest(Guid generation, long revision, Guid request, Guid subscription,
    SWGlobalWarShipClass shipClass, Guid cancel = default, bool headDecision = false) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly Guid Request = request;
    public readonly Guid Subscription = subscription;
    public readonly SWGlobalWarShipClass Class = shipClass;
    public readonly Guid Cancel = cancel;
    public readonly bool HeadDecision = headDecision;
}
