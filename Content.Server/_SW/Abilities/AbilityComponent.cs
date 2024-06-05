using System.Numerics;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._SW.Abilities;

/// This is all jedi and sith abilities in game
[RegisterComponent]
public sealed partial class DashComponent : Component
{
    [DataField] public EntityUid? Action;

    /// <summary>
    /// The velocity of the throw
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public Vector2 Velocity;

    /// <summary>
    /// Dash Ability.
    /// </summary>
    [DataField("Dash")]
    public bool Dash = false;

    [DataField("DashSound")]
    public string DashSound = String.Empty;

    [DataField("DashSpeed")]
    public float DashSpeed = 2f;

    [DataField("DashImpulse")]
    public float DashImpulse = 1f;

    [DataField("DashTime")]
    public double DashTime = 0.5f;

    [DataField("DashCooldown")]
    public double DashCooldown = 1f;
}

[RegisterComponent]
public sealed partial class DashEventComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan DashEndTime;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan DashEndCooldown;
}

[RegisterComponent]
public sealed partial class LightningAbilityComponent : Component
{
    [DataField] public EntityUid? Action;

    [DataField("LightningRange")]
    public float LightningRange = 3f;

    [DataField("BoltCount")]
    public int BoltCount = 2;

    [DataField("ArcDepth")]
    public int ArcDepth = 1;

    [DataField("Cooldown")]
    public int Cooldown = 20;
}

[RegisterComponent]
public sealed partial class LightningEventComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField]
    public TimeSpan LightningEndCooldown;

}
