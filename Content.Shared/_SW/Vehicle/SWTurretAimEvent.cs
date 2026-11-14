using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Client → server: desired turret orientation angle (radians).
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTurretAimEvent : EntityEventArgs
{
    public float Angle;
}