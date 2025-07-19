namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class ForcePulledComponent : Component
{
    [DataField]
    public EntityUid? ForceUser;

    [DataField]
    public float ActiveCooldown;

    [DataField]
    public float Cooldown = 0.2F;

    [DataField]
    public int Attempts;

    [DataField]
    public int MaxAttempts = 50;

    [DataField]
    public float ThrowForce = 10F;
}
