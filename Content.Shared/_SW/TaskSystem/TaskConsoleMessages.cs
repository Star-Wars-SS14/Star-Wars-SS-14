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

    // SetUiState skips a state equal to the last one, so polling sends only real changes.
    public override bool Equals(object? obj)
    {
        return obj is TaskConsoleBounderUserInterfaceState other && Tasklist.SequenceEqual(other.Tasklist);
    }

    public override int GetHashCode()
    {
        return Tasklist.Count;
    }
}

[Serializable]
public sealed class TaskInfo
{
    public string TaskName;
    public string TaskDesc;
    public FixedPoint2 Status;
    public List<string> Rewards;
    public string Score;
    public TaskInfo(string tn, string td, FixedPoint2 s, List<string> rewards, string score)
    {
        TaskName = tn;
        TaskDesc = td;
        Status = s;
        Rewards = rewards;
        Score = score;
    }

    public override bool Equals(object? obj)
    {
        return obj is TaskInfo other
            && TaskName == other.TaskName
            && TaskDesc == other.TaskDesc
            && Status == other.Status
            && Rewards.SequenceEqual(other.Rewards)
            && Score == other.Score;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TaskName, TaskDesc, Status);
    }
}
