using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.DynamicDescription;

[RegisterComponent]
public sealed partial class SWDescriptionBasedOnJobOfTargetComponent : Component
{
    [DataField]
    public Dictionary<string, LocId> JobsToDescriptions = new();
}
