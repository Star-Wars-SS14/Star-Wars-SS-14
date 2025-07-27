using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.CapturePerson;

[RegisterComponent]
public sealed partial class SWCapturePersonComponent : Component
{
    public HashSet<EntityUid> CuffedTargets = new HashSet<EntityUid>();

    public Dictionary<string, int> JobsToScore = new Dictionary<string, int>();
}
