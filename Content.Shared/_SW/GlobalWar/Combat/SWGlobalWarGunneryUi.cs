using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.GlobalWar.Combat;

[Serializable, NetSerializable]
public enum SWGlobalWarGunneryUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed record SWGlobalWarGunView(NetEntity Gun, string Name, Vector2 Position, float Bearing,
    float Arc, int Ammunition, int Capacity, bool Mine, bool Occupied, string Status);

[Serializable, NetSerializable]
public sealed record SWGlobalWarRadarHull(Vector2[] Corners, bool Friendly, bool Self);

[Serializable, NetSerializable]
public sealed class SWGlobalWarGunneryOpen(Guid subscription) : BoundUserInterfaceMessage
{
    public readonly Guid Subscription = subscription;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarGunnerySnapshot(Guid generation, long revision, Guid subscription, NetEntity viewer,
    NetEntity map, Vector2 center, bool authorized, SWGlobalWarGunView[] guns, SWGlobalWarRadarHull[] hulls)
    : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Viewer = viewer;
    public readonly NetEntity Map = map;
    public readonly Vector2 Center = center;
    public readonly bool Authorized = authorized;
    public readonly SWGlobalWarGunView[] Guns = guns;
    public readonly SWGlobalWarRadarHull[] Hulls = hulls;
}

[Serializable, NetSerializable]
public sealed class SWGlobalWarGunneryRequest(Guid generation, long revision, Guid subscription,
    NetEntity map, NetEntity[] guns, Vector2 point, bool firing) : BoundUserInterfaceMessage
{
    public readonly Guid Generation = generation;
    public readonly long Revision = revision;
    public readonly Guid Subscription = subscription;
    public readonly NetEntity Map = map;
    public readonly NetEntity[] Guns = guns;
    public readonly Vector2 Point = point;
    public readonly bool Firing = firing;
}
