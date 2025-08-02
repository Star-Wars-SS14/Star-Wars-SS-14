using Content.Shared.Random;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWTaskContainerComponent : Component
{
    [DataField]
    public ProtoId<WeightedRandomPrototype>? AvailableTasks = null;

    public List<Entity<SWTaskComponent>> CurrentTasks = new();

    [DataField]
    public float AddIntervalInSeconds = 60 * 20;

    public TimeSpan NextAddTime = TimeSpan.FromSeconds(0);

    [DataField]
    public int StartAmountOfTasks = 0;

    [DataField]
    public int MaxAmountOfTasks = 0;
}

