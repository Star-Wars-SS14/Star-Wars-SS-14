using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SWVehicleFaceForwardSystem))]
public sealed partial class SWVehicleFaceForwardComponent : Component
{
    /// <summary>
    /// Additional rotation applied on top of the occupied vehicle rotation.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Angle RotationOffset = Angle.Zero;
}
