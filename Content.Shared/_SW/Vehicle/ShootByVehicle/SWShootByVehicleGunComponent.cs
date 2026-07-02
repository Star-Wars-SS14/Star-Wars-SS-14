using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle.ShootByVehicle;

[RegisterComponent]
public sealed partial class SWShootByVehicleGunComponent : Component
{
    /// <summary>
    /// Angle in which this gun can shoot. Actually, it can shoot angle/2 to left of forward and angle/2 to right of forward
    /// </summary>
    [DataField("angle")]
    public double AngleInDegrees = 360f;

    [DataField]
    public Vector2 ForwardVector = new Vector2(0.0f, -1.0f);
}
