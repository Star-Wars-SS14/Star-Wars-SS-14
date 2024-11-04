using Robust.Shared.Audio;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public partial class ForceComponent : Component
{
    // Dash
    [DataField("DashSound")]
    public SoundSpecifier DashSound = new SoundPathSpecifier("/_SW/Abilities/dash.ogg");

    [DataField("DashSpeed")]
    public float DashSpeed = 2f;

    [DataField("DashImpulse")]
    public float DashImpulse = 1f;

    [DataField("DashTime")]
    public double DashTime = 0.5f;

    [DataField("DashCooldown")]
    public double DashCooldown = 1f;
}
