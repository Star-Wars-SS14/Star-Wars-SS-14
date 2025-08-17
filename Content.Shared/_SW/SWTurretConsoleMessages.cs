using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW;

[NetSerializable, Serializable]
public enum SWTurretConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SWTurretConsoleBounderUserInterfaceState : BoundUserInterfaceState
{
    public SWTurretConsoleBounderUserInterfaceState()
    {
    }
}

//[Serializable, NetSerializable]
//public sealed class UpdateTasksMessage : BoundUserInterfaceMessage
//{

//}

