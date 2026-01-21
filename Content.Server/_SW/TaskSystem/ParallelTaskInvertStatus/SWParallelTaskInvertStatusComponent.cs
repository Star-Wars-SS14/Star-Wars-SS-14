using Content.Shared._SW.TaskSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.ParallelTaskInvertStatus;

[RegisterComponent]
public sealed partial class SWParallelTaskInvertStatusComponent : Component
{
    public Entity<SWTaskComponent> TaskToInvert = default!;
}
