using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

/// <summary>
/// Vehicle collision behavior that knocks standing mobs down and flings them away.
/// Used by the landspeeder; other vehicles keep SWVehicleMobCollision untouched.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWVehicleKnockbackComponent : Component
{
    /// <summary>
    /// Vehicle speed required to affect a mob on collision.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinimumSpeed = 0.5f;

    /// <summary>
    /// When set, knock down standing mobs for this duration instead of paralyzing them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? KnockdownTime;

    /// <summary>
    /// How long the collided mob is paralyzed when knockdown is not configured.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ParalyzeTime = TimeSpan.FromSeconds(2f);

    /// <summary>
    /// Throw speed used to fling the collided mob away from the vehicle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ThrowSpeed = 10f;
}