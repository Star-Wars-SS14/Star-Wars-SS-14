using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWVehicleMobCollisionComponent : Component
{
    /// <summary>
    /// Vehicle speed required to paralyze a mob on collision.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinimumSpeed = 0.5f;

    /// <summary>
    /// How long the collided mob is paralyzed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ParalyzeTime = TimeSpan.FromSeconds(2f);
}
