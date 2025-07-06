using Content.Shared.Inventory;
using Content.Shared.Radio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Server._SW.Force.Components;

[RegisterComponent]
public sealed partial class StarHelmetComponent : Component
{
    [DataField("channels", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<RadioChannelPrototype>))]
    public HashSet<string> Channels;

    //shows in whicht inventory slot radio shuold be enabled.
    //If object with this component is equipped in any other slot, speaking through radio will be disabled
    [DataField("requiredSlot")]
    public SlotFlags RequiredSlot = SlotFlags.EARS;
}
