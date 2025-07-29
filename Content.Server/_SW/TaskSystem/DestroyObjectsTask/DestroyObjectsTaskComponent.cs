using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.DestroyObjectsTask;

[RegisterComponent]
public sealed partial class DestroyObjectsTaskComponent : Component
{
    [DataField("objects", required: true)]
    public Dictionary<List<string>, int> ObjectsToDestroy;

    public Dictionary<List<string>, int> ObjectsDestroyed = new ();
}
