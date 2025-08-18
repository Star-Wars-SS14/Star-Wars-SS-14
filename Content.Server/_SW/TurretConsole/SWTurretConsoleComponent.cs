using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TurretConsole;
[RegisterComponent]
public sealed partial class SWTurretConsoleComponent : Component
{
    [DataField]
    public List<string> StartJobsAllowed = new List<string>();

    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, bool> JobsAllowed = new Dictionary<string, bool>();
}
