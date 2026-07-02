using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWExplosionThrowModifierComponent : Component
{
    /// <summary>
    /// Multiplier applied to explosion throw force before this entity is thrown.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float ThrowMultiplier = 1f;
}
