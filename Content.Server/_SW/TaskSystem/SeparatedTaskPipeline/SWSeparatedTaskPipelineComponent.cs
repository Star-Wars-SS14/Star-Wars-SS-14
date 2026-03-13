using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.SeparatedTaskPipeline;

[RegisterComponent]
public sealed partial class SWFactionSeparatedTaskPipelineComponent : Component
{
    [DataField(required: true)]
    public List<SWTaskPipelineInfo> Tasks = default!;
}

[Serializable, DataDefinition]
public sealed partial class SWTaskPipelineInfo
{
    [DataField]
    public TimeSpan TaskAddTime = default!;

    [DataField]
    public string TaskId = default!;

    [DataField]
    public bool TryAddUntilSuccess = false;

    public bool Added = false;

}
