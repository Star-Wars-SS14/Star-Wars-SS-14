using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._SW.TaskSystem;

[NetSerializable, Serializable]
public enum TaskConsoleUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class TaskConsoleBounderUserInterfaceState : BoundUserInterfaceState
{
    public List<TaskInfo> Tasklist;

    public TaskConsoleBounderUserInterfaceState(List<TaskInfo> list)
    {
        Tasklist = list;
    }
}

[Serializable]
public sealed class TaskInfo
{
    public string TaskName;
    public string TaskDesc;
    public FixedPoint2 Status;
    public TaskInfo(string tn, string td, FixedPoint2 s)
    {
        TaskName = tn;
        TaskDesc = td;
        Status = s;
    }
}

[Serializable, NetSerializable]
public sealed class UpdateTasksMessage : BoundUserInterfaceMessage
{

}
