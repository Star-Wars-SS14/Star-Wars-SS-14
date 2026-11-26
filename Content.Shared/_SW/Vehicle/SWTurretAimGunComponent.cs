using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Marker on a vehicle gun part that is aimed/gated by SWTurretAimSystem.
/// Lets the aim system subscribe to shoot attempts without clashing with
/// other systems' GunComponent subscriptions.
/// </summary>
[RegisterComponent]
public sealed partial class SWTurretAimGunComponent : Component
{
}