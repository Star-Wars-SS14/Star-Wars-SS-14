using Content.Shared.Random;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._SW.TaskSystem;

[RegisterComponent]
public sealed partial class SWFactionComponent : Component
{
    [DataField]
    public string Name;

    [DataField]
    public int StartScore = 0;

    public int CurrentScore = 0;

    [DataField]
    public bool IsMajor = false;

    [DataField]
    public List<ProtoId<JobPrototype>> RelatedJobs = new List<ProtoId<JobPrototype>>();

    [DataField]
    public int MinimumDroidCommandRank = 0;

    [DataField]
    public Dictionary<ProtoId<JobPrototype>, int> CommandRanks = new();
}
