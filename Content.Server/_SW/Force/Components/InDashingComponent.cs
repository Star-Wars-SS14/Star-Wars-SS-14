using System.Numerics;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class InDashingComponent : Component
{
    [DataField]
    public float ActiveCooldown;

    [DataField]
    public float Cooldown = 0.4F;
}
