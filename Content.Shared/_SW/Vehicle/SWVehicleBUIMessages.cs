using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.Vehicle;

[NetSerializable, Serializable]
public sealed class SWVehicleMenuBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly Dictionary<string, SWVehicleRoleInfo> RolesInfo = new();

    public SWVehicleMenuBoundUserInterfaceState(Dictionary<string, SWVehicleRoleInfo> rolesInfo)
    {
        RolesInfo = rolesInfo;
    }
}

[NetSerializable, Serializable]
public sealed class SWOccupyRoleMessage : BoundUserInterfaceMessage
{
    public string Role;
    public SWOccupyRoleMessage(string role)
    {
        Role = role;
    }
}

[NetSerializable, Serializable]
public enum SWVehicleMenuUiKey
{
    Key,
}
