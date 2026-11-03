using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Combat;

[Serializable, NetSerializable]
public enum SWGlobalWarReconUiKey : byte { Key }

/// <summary>Detached observations. No mutable economy object or unauthorized live state is serialized.</summary>
[Serializable, NetSerializable]
public sealed record SWGlobalWarReconReport(Guid Id, string Sector, string Device, TimeSpan ObservedAt,
    string[] Observations);

[Serializable, NetSerializable]
public sealed class SWGlobalWarReconOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarReconSnapshot(Guid generation, Guid subscription, NetEntity viewer,
    TimeSpan now, bool authorized, SWGlobalWarReconReport[] reports) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
    public readonly TimeSpan Now = now;
    public readonly bool Authorized = authorized;
    public readonly SWGlobalWarReconReport[] Reports = reports;
}
