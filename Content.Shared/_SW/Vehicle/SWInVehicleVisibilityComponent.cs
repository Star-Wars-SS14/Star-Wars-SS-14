using Content.Shared.Eye;
using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWInVehicleVisibilityComponent : Component
{
    /// <summary>
    /// Visibility layer assigned while this entity is occupying a vehicle role.
    /// Normal players do not receive entities on this layer unless their eye mask also includes it.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public ushort HiddenLayer = (ushort) VisibilityFlags.Admin;

    /// <summary>
    /// Visibility layer that was present before the entity entered the vehicle.
    /// </summary>
    [ViewVariables]
    public ushort PreviousLayer;

    /// <summary>
    /// Whether this entity already had a visibility component before entering the vehicle.
    /// </summary>
    [ViewVariables]
    public bool HadVisibilityComponent;

    /// <summary>
    /// Whether the system has currently applied the hidden visibility layer.
    /// </summary>
    [ViewVariables]
    public bool Hidden;
}
