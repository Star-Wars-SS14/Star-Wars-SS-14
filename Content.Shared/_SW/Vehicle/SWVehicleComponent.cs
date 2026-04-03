using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[RegisterComponent]
public sealed partial class SWVehicleComponent : Component
{
    [DataField]
    public Dictionary<string, SWVehicleRoleInfo> RolesInfo = new();

    [DataField]
    public Dictionary<ProtoId<EntityPrototype>, SWVehiclePlacementInfo> VehicleParts = new();
}

[DataDefinition, Serializable]
public sealed partial class SWVehiclePlacementInfo
{
    [DataField]
    public Vector2 BuckleCoords = new Vector2(0.0f, 0.0f);
}

[DataDefinition, Serializable]
public sealed partial class SWVehicleRoleInfo
{
    [DataField]
    public ProtoId<SWVehicleRolePrototype> Role = default!;

    [DataField]
    public Vector2 BuckleCoords = new Vector2(0.0f, 0.0f);

    public bool Occupied = false;
}
