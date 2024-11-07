namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class ForcePulledComponent : Component
{
    [DataField]
    public EntityUid? ForceUser;

    [DataField]
    public float ActiveCooldown;

    [DataField]
    public float Cooldown = 0.3F;

    [DataField]
    public float ThrowForce = 9.5F;
}
