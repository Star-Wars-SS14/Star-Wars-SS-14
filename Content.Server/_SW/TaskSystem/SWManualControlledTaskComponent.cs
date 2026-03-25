using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWManualControlledTaskComponent : Component
{
    [DataField]
    public string Faction = string.Empty;
}
