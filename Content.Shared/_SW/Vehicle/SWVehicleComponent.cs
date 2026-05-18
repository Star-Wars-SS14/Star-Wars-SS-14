using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SWVehicleComponent : Component
{
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, SWVehicleRoleInfo> RolesInfo = new();

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, SWVehiclePartInfo> VehicleParts = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class SWVehiclePartInfo
{
    [DataField]
    public EntProtoId EntProto = default!;

    [DataField]
    public Vector2 BuckleCoords = new Vector2(0.0f, 0.0f);
    [ViewVariables(VVAccess.ReadWrite)]
    public NetEntity SpawnedPart = default!;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class SWVehicleRoleInfo
{
    [DataField]
    public ProtoId<SWVehicleRolePrototype> Role = default!;

    public NetEntity? Occupant = null;
}
