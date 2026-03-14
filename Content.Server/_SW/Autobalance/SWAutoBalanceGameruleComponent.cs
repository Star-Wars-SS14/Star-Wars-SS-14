using Content.Server._SW.TaskSystem;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.Autobalance;

[RegisterComponent]
public sealed partial class SWAutoBalanceGameruleComponent : Component
{

    [DataField]
    public ProtoId<SWFactionRelatedJobsPrototype> FirstFactionRelatedJobsId = "SWRepublicRelatedJobs";

    [DataField]
    public ProtoId<SWFactionRelatedJobsPrototype> SecondRelatedJobsId = "SWEmpireRelatedJobs";

    /// <summary>
    /// The number of players in the second faction equal to 1 player in the first faction
    /// </summary>
    [DataField]
    public int Ratio = 2;

    public int FirstFactionSpawned = 0;
    public int SecondFactionSpawned = 0;

    public Dictionary<string, int> JobsSpawned = new Dictionary<string, int>();
}
