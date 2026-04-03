using Robust.Shared.Prototypes;
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
