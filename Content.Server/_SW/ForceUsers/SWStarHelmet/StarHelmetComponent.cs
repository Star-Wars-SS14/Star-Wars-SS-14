using Content.Shared.Inventory;
using Content.Shared.Radio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class StarHelmetComponent : Component
{
    [DataField("channels", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<RadioChannelPrototype>))]
    public HashSet<string> Channels;

    //список слотов, в которые можно поместить предметы с этим компонентом
    //(не конкретно этот предмет, а вообще любые предметы с этим компонентом)
    [DataField("requiredSlot")]
    public static List<SlotFlags> AllowedSlots = new() { SlotFlags.EARS, SlotFlags.HEAD };
}
