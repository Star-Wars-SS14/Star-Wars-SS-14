using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SWInertialVehicleSystem))]
public sealed partial class SWInertialVehicleComponent : Component
{
    /// <summary>
    /// Forward acceleration while holding the forward input.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Acceleration = 10f;

    /// <summary>
    /// Backward acceleration while holding the reverse input and already moving backward.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float ReverseAcceleration = 6f;

    /// <summary>
    /// Deceleration used when the driver holds the opposite direction of current forward motion.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float BrakeAcceleration = 18f;

    /// <summary>
    /// Maximum speed along the vehicle's forward vector.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxForwardSpeed = 9f;

    /// <summary>
    /// Maximum speed opposite the vehicle's forward vector.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxReverseSpeed = 4f;

    /// <summary>
    /// How quickly steering input changes angular velocity.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float TurnAcceleration = 6f;

    /// <summary>
    /// Maximum rotational speed from steering.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxAngularVelocity = 2.4f;

    /// <summary>
    /// How quickly rotation slows down when there is no steering input.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float AngularDamping = 3f;

    /// <summary>
    /// How quickly forward or reverse speed decays when there is no throttle input.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float RollingFriction = 1.2f;

    /// <summary>
    /// How quickly sideways sliding is damped. Lower values allow more drifting.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float SidewaysFriction = 12f;

    /// <summary>
    /// Speed at which steering reaches full strength when turning in place is disabled.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float TurnSpeedReference = 3f;

    /// <summary>
    /// Minimum forward or reverse speed required before steering can add rotation when turning in place is disabled.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MinTurnSpeed = 2f;

    /// <summary>
    /// Whether steering works at full strength even when the vehicle has no forward or reverse speed.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool CanTurnInPlace = false;

    /// <summary>
    /// Local direction treated as the vehicle's nose for acceleration, braking, and drift calculations.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Vector2 ForwardVector = -Vector2.UnitY;
}
