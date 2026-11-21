using Content.Shared.Damage;

namespace Content.Shared._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWSaberEmitterComponent : Component
{
    [DataField(required: true)] public DamageSpecifier Damage = default!;
}

[RegisterComponent]
public sealed partial class SWSaberFocusComponent : Component
{
    [DataField] public float DamageMultiplier = 1f;
    [DataField] public float AttackRateMultiplier = 1f;
}

[RegisterComponent]
public sealed partial class SWSaberCrystalComponent : Component
{
    [DataField(required: true)] public Color Color;
}
