using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Serialization;

namespace Content.Shared._SW;

[NetSerializable, Serializable]
public enum TaskConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class TaskConsoleUpdateState : BoundUserInterfaceState
{

}
