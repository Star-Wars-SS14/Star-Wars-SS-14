using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.DestroyObjectsTask;

[RegisterComponent]
public sealed partial class DestroyObjectsTaskComponent : Component
{
    [DataField("requirements", required: true)]
    public List<ObjectRequirement> ObjectsToDestroy;

    public List<ObjectRequirement> ObjectsDestroyed = new ();
}
