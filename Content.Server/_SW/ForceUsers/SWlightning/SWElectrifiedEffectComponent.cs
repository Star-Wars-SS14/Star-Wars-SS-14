using Robust.Shared.GameObjects;

namespace Content.Server._SW;

[RegisterComponent]
public sealed partial class ElectrifiedEffectComponent : Component
{
    [DataField]
    public EntityUid EffectEntity;
}