using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWInVehicleDamageProtectionComponent : Component
{
    /// <summary>
    /// Multiplier applied to positive damage while this entity is occupying a vehicle role.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float DamageMultiplier = 0f;
}
