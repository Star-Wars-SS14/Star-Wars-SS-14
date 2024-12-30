using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Weapons.ReflectOnAttack;
[RegisterComponent]
public sealed partial class ReflectOnAttackComponent : Component
{
    [DataField("ReflectionChance")]
    public float ReflectionChance = 1f;
}


