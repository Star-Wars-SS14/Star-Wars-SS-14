using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Клиент → сервер: желаемый угол наведения турели (радианы).
/// </summary>
[Serializable, NetSerializable]
public sealed class SWTurretAimEvent : EntityEventArgs
{
    public float Angle;
}