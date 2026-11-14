using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWTurretAimComponent : Component
{
    /// <summary>
    /// Authoritative aiming angle. Written only by the server.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle TargetAngle = Angle.Zero;

    /// <summary>
    /// Additional rotation of the boom relative to the turret.
    /// </summary>
    [DataField]
    public Angle GunnerOffset = Angle.Zero;

    /// <summary>
    /// Rotation speed toward the cursor, deg/s.
    /// 120 = 180 / 1.5 (base turret MouseRotator = 180).
    /// </summary>
    [DataField]
    public float RotationSpeed = 120f;
}