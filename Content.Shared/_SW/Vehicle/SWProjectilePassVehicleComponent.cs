using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Marks a projectile as allowed to pass through one vehicle and its attached parts.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SWVehicleProjectilePassSystem))]
public sealed partial class SWProjectilePassVehicleComponent : Component
{
    /// <summary>
    /// Vehicle entity that this projectile should ignore while colliding.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Vehicle;
}
