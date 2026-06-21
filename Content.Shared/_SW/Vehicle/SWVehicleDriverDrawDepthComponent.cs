using DrawDepthEnum = Content.Shared.DrawDepth.DrawDepth;
using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class SWVehicleDriverDrawDepthComponent : Component
{
    /// <summary>
    /// Offset applied to the vehicle sprite draw depth while this entity is operating the vehicle.
    /// Negative values draw the operator behind the vehicle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int VehicleDrawDepthOffset = -1;

    /// <summary>
    /// Draw depth used when the occupied vehicle has no sprite to read from.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int FallbackDrawDepth = (int) DrawDepthEnum.BelowMobs;

    /// <summary>
    /// Original sprite draw depth saved before this component changes it.
    /// </summary>
    [ViewVariables]
    public int? OriginalDrawDepth;
}
