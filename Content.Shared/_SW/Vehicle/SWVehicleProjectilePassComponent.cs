using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Allows projectiles fired by this in-vehicle user or vehicle part to pass through that vehicle.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SWVehicleProjectilePassComponent : Component
{
}
