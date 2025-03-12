namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class IsSithGaspingComponent : Component
{
    [DataField]
    public float ActiveCooldown;

    [DataField]
    public int MinEyeDamage;

    [DataField]
    public float Cooldown = 5F;

    [DataField]
    public TimeSpan StunTime = TimeSpan.FromSeconds(10);
}
