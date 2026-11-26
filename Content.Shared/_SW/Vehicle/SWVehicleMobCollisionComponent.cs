using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWVehicleMobCollisionComponent : Component
{
    /// <summary>
    /// Vehicle speed required to affect a mob on collision.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinimumSpeed = 0.5f;

    /// <summary>
    /// When set, knock down crawling mobs for this duration instead of paralyzing them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? KnockdownTime;

    /// <summary>
    /// How long the collided mob is paralyzed when knockdown is not configured.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ParalyzeTime = TimeSpan.FromSeconds(2f);
}
