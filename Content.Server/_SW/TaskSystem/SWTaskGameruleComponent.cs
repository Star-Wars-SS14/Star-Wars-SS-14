using Content.Shared._SW.TaskSystem;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWTaskGameruleComponent : Component
{
    [DataField]
    public float DelayBeforeAddingStartTasksInSeconds = 60 * 3;

    public TimeSpan TimeToAddStartTasks;

    public bool StartTasksAdded = false;
}
