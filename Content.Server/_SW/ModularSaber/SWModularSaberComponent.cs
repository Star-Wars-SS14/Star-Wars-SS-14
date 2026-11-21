using Content.Shared.Containers.ItemSlots;

namespace Content.Server._SW.ModularSaber;

[RegisterComponent]
public sealed partial class SWModularSaberComponent : Component
{
    public ItemSlot? Crystal;
    public ItemSlot? Emitter;
    public ItemSlot? Focus;
}
