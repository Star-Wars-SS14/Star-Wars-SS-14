using Content.Shared.Random;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWFactionComponent : Component
{
    [DataField]
    public WeightedRandomPrototype AvailableTasks;

    public List<Entity<SWTaskComponent>> CurrentTasks;

    public float AddIntervalInSeconds = 60 * 20;

    public TimeSpan NextAddTime;
}
