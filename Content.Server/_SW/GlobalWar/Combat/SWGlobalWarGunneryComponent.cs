using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._SW.GlobalWar.Combat;

[RegisterComponent]
public sealed partial class SWGlobalWarGunneryConsoleComponent : Component;

/// <summary>A server-controlled physical installation; its base rotation defines its permanent firing sector.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarGunnerySystem))]
public sealed partial class SWGlobalWarGunMountComponent : Component
{
    /// <summary>Maximum aiming and actual projectile travel distance in metres.</summary>
    [DataField]
    public float Range = 64;
    /// <summary>Total firing sector in degrees around the original mapped forward direction.</summary>
    [DataField]
    public float Arc = 120;
    /// <summary>Maximum angular velocity of the powered mount in degrees per second.</summary>
    [DataField]
    public float RotationSpeed = 45;
    public Angle BaseRotation;
    public EntityUid? Operator;
    public EntityUid? Console;
    public Vector2 Aim;
    public EntityUid? AimMap;
    public bool Firing;
    public TimeSpan LastInput;
    public string Status = "ready";
}

/// <summary>Marks an operator for the existing muzzle-coordinate event without modifying the gun pipeline.</summary>
[RegisterComponent]
public sealed partial class SWGlobalWarGunOperatorComponent : Component;

/// <summary>Actual travelled distance, also checked before a collision can produce damage.</summary>
[RegisterComponent, Access(typeof(SWGlobalWarGunnerySystem))]
public sealed partial class SWGlobalWarProjectileRangeComponent : Component
{
    public float Remaining;
    public MapCoordinates Last;
}
