using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

public sealed class SWVehicleMenuBUIState : BoundUserInterfaceState
{
    public readonly Dictionary<ProtoId<SWVehicleRolePrototype>, bool> RolesInfo = new();

    public SWVehicleMenuBUIState (Dictionary<ProtoId<SWVehicleRolePrototype>, bool> rolesInfo)
    {
        RolesInfo = rolesInfo;
    }
}

public sealed class SWOccupyRoleMessage : BoundUserInterfaceMessage
{
    public ProtoId<SWVehicleRolePrototype> Role;
    public SWOccupyRoleMessage(ProtoId<SWVehicleRolePrototype> role)
    {
        Role = role;
    }
}

[NetSerializable, Serializable]
public enum SWVehicleMenuUiKey
{
    Key,
}
