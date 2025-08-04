using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.StealObjectTask;
[RegisterComponent]
public sealed partial class SWCollectObjectTaskComponent : Component
{
    [DataField("requirements", required: true)]
    public List<ObjectRequirement> ObjectsToCollect;

    public List<ObjectRequirement> ObjectsCollected = new();
}

[Serializable, DataDefinition]
public sealed partial class ObjectRequirement
{
    [DataField]
    public int Number = 0;

    [DataField("objects")]
    public List<string> ObjectsPrototypes = new List<string>();

    public ObjectRequirement()
    {
    }
}
