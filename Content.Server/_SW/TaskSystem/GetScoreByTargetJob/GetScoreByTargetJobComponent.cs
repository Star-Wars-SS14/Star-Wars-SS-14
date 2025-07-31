using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem.GetScoreByTargetJob;

[RegisterComponent]
public sealed partial class SWGetScoreByTargetJobComponent : Component
{
    [DataField]
    public Dictionary<string, int> JobsToScores = new();
}
