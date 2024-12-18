using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Server._SW.Reinforcements.Components;

[RegisterComponent]
public sealed partial class ReinforcementsComponent : Component
{
    [DataField("roles", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<EntityPrototype>))]
    public HashSet<string> Channels;
}
