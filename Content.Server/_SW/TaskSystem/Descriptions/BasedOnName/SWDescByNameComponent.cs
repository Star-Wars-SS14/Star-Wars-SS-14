using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.Descriptions.BasedOnName;

[RegisterComponent]
public sealed partial class SWDescByNameComponent : Component
{
    [DataField(required: true)]
    public LocId DescriptionId;
}
