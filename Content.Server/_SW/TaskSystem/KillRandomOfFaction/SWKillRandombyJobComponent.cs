using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.KillRandomOfFaction;
[RegisterComponent]
public sealed partial class SWKillRandomByJobComponent : Component
{
    [DataField]
    public List<string> Factions = new();

    [DataField]
    public Dictionary<string, int> JobsToScores = new();

    public string? JobProtoOfKilled = null;
}
