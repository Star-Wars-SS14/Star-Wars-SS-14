using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.ParallelTaskProtect;

[RegisterComponent]
public sealed partial class SWParallelTaskProtectComponent : Component
{
    [DataField]
    public LocId DescriptionId = default;
}
