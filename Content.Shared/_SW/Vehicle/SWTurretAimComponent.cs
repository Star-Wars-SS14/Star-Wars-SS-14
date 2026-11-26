using System;
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
    /// </summary>
    [DataField]
    public float RotationSpeed = 120f;

    [DataField, AutoNetworkedField]
    public float FiringCone = 25f;

    /// <summary>
    /// Vehicle part id of the gun this aim component gates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string GunPartId = "cannon";

    /// <summary>
    /// Empirical one-time compensation added to the snapped barrel angle when
    /// computing the shot direction. Applied only at shot time in the gate,
    /// never per-tick (feeding it into Update would spin the turret forever).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle ShotAngleOffset = new(-Math.PI / 2);
}