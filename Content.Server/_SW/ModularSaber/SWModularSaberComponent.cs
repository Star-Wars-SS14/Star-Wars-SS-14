using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Robust.Shared.Maths;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWModularSaberComponent : Component
{
    [DataField] public ItemSlot Crystal = new();
    [DataField] public ItemSlot Emitter = new();
    [DataField] public ItemSlot Focus = new();
}

[RegisterComponent]
public sealed partial class SWSaberCrystalComponent : Component
{
    [DataField(required: true)] public Color Color;
}

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
