using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent]
public sealed partial class SWInVehicleVisibilityComponent : Component
{
    /// <summary>
    /// Original sprite visibility saved before this component hides the vehicle occupant.
    /// </summary>
    [ViewVariables]
    public bool? OriginalVisible;

    /// <summary>
    /// Whether the occupant sprite is currently hidden by the client visual system.
    /// </summary>
    [ViewVariables]
    public bool Hidden;
}
