using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.StealObjectTask;
[RegisterComponent]
public sealed partial class CollectObjectTaskComponent : Component
{
    [DataField("objects", required: true)]
    public Dictionary<List<string>, int> ObjectsToCollect;

    public Dictionary<List<string>, int> ObjectsCollected = new();
}
