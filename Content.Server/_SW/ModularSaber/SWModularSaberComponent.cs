using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Maths;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWModularSaberComponent : Component
{
    public ItemSlot? Crystal;
    public ItemSlot? Emitter;
    public ItemSlot? Focus;
}

[RegisterComponent]
public sealed partial class SWSaberCrystalComponent : Component
{
    [DataField(required: true)] public Color Color;
}
