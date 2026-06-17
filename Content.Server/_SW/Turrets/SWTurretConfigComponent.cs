using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TurretConsole;
[RegisterComponent]
public sealed partial class SWTurretConfigComponent : Component
{
    [DataField]
    public Dictionary<string, bool> AllowedJobs = new Dictionary<string, bool>();

    [DataField]
    public bool TargetUnconfiguredJobs = true;
}
