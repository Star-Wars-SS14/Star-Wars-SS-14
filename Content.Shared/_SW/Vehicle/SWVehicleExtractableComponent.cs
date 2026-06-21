using Robust.Shared.GameStates;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SWVehicleSystem))]
public sealed partial class SWVehicleExtractableComponent : Component
{
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float AliveDelay = 8f;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float CriticalDelay = 4f;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float DeadDelay = 2f;
}
