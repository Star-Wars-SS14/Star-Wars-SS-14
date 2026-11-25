using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWModularSaberComponent : Component
{
    /// <summary>Added to the emitter's blade damage before the focus assembly modifies it.</summary>
    [DataField] public DamageSpecifier? ActivatedDamageBonus;

    /// <summary>Attack rate when lit, before applying the focus assembly multiplier.</summary>
    [DataField] public float BaseAttackRate = 1f;

    public ItemSlot? Crystal;
    public ItemSlot? Emitter;
    public ItemSlot? Focus;
}
